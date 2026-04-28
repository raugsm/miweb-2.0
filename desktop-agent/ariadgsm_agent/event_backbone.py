from __future__ import annotations

import json
import os
import shutil
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable


VERSION = "0.9.9"
ENGINE = "ariadgsm_event_timeline_durable_backbone"
DEFAULT_MAX_READ_BYTES = 8 * 1024 * 1024
DEFAULT_BOOTSTRAP_BYTES = 8 * 1024 * 1024
DEFAULT_COMPACT_MAX_BYTES = 128 * 1024 * 1024
DEFAULT_COMPACT_KEEP_BYTES = 32 * 1024 * 1024


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def read_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig", errors="replace"))
    except Exception:
        return {}
    return value if isinstance(value, dict) else {}


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    temp.replace(path)


@dataclass(frozen=True)
class SourceReadResult:
    path: str
    exists: bool
    file_size: int = 0
    previous_offset: int = 0
    start_offset: int = 0
    end_offset: int = 0
    bytes_read: int = 0
    backlog_bytes: int = 0
    skipped_backlog_bytes: int = 0
    lines_read: int = 0
    lines_returned: int = 0
    partial_line_held: bool = False
    reset_reason: str = ""
    duration_ms: int = 0

    def to_state(self) -> dict[str, Any]:
        return {
            "path": self.path,
            "exists": self.exists,
            "fileSize": self.file_size,
            "previousOffset": self.previous_offset,
            "startOffset": self.start_offset,
            "endOffset": self.end_offset,
            "bytesRead": self.bytes_read,
            "backlogBytes": self.backlog_bytes,
            "skippedBacklogBytes": self.skipped_backlog_bytes,
            "linesRead": self.lines_read,
            "linesReturned": self.lines_returned,
            "partialLineHeld": self.partial_line_held,
            "resetReason": self.reset_reason,
            "durationMs": self.duration_ms,
        }


@dataclass(frozen=True)
class JsonlReadBatch:
    events: list[dict[str, Any]]
    source_results: list[SourceReadResult]
    cycle_ms: int
    checkpoint_file: Path
    namespace: str
    limit: int
    mode: str = "incremental_offset_tail"

    @property
    def bytes_read(self) -> int:
        return sum(item.bytes_read for item in self.source_results)

    @property
    def backlog_bytes(self) -> int:
        return sum(item.backlog_bytes for item in self.source_results)

    @property
    def skipped_backlog_bytes(self) -> int:
        return sum(item.skipped_backlog_bytes for item in self.source_results)

    @property
    def lines_returned(self) -> int:
        return sum(item.lines_returned for item in self.source_results)

    def telemetry(self) -> dict[str, Any]:
        return {
            "engine": ENGINE,
            "version": VERSION,
            "mode": self.mode,
            "namespace": self.namespace,
            "checkpointFile": str(self.checkpoint_file),
            "cycleMs": self.cycle_ms,
            "limit": self.limit,
            "sources": [item.to_state() for item in self.source_results],
            "summary": {
                "events": len(self.events),
                "linesReturned": self.lines_returned,
                "bytesRead": self.bytes_read,
                "backlogBytes": self.backlog_bytes,
                "skippedBacklogBytes": self.skipped_backlog_bytes,
            },
            "policy": {
                "jsonlIsProjection": True,
                "sqliteIsDurableTruth": True,
                "activeSourceLogsAreNotRewritten": True,
                "largeBacklogPolicy": "tail_then_checkpoint_to_end",
            },
        }


def _namespace_key(namespace: str, path: Path) -> str:
    return f"{namespace}:{str(path.resolve()).lower()}"


def _state_sources(state: dict[str, Any]) -> dict[str, Any]:
    sources = state.get("sources")
    return sources if isinstance(sources, dict) else {}


def _decode_lines(data: bytes) -> list[str]:
    if not data:
        return []
    return [
        line.decode("utf-8-sig", errors="replace").strip()
        for line in data.splitlines()
        if line.strip()
    ]


def _read_source_incremental(
    path: Path,
    previous: dict[str, Any],
    *,
    limit: int,
    max_bytes: int,
    bootstrap_bytes: int,
) -> tuple[list[str], SourceReadResult]:
    started = time.perf_counter()
    resolved = path.resolve()
    if not path.exists():
        return [], SourceReadResult(path=str(resolved), exists=False, duration_ms=int((time.perf_counter() - started) * 1000))

    file_size = path.stat().st_size
    previous_offset = int(previous.get("offset") or 0) if previous else 0
    reset_reason = ""
    skipped = 0
    start_offset = previous_offset
    align_after_seek = False

    if not previous:
        if file_size > bootstrap_bytes:
            start_offset = max(0, file_size - bootstrap_bytes)
            skipped = start_offset
            align_after_seek = start_offset > 0
            reset_reason = "bootstrap_tail"
        else:
            start_offset = 0
            reset_reason = "first_read"
    elif previous_offset > file_size:
        if file_size > bootstrap_bytes:
            start_offset = max(0, file_size - bootstrap_bytes)
            skipped = start_offset
            align_after_seek = start_offset > 0
        else:
            start_offset = 0
        reset_reason = "source_truncated_or_rotated"

    available = max(0, file_size - start_offset)
    if available > max_bytes:
        new_start = max(0, file_size - max_bytes)
        skipped += max(0, new_start - start_offset)
        start_offset = new_start
        align_after_seek = start_offset > 0
        reset_reason = reset_reason or "backlog_tail"

    data = b""
    end_offset = file_size
    partial_line_held = False
    with path.open("rb") as handle:
        handle.seek(start_offset)
        if align_after_seek:
            discarded = handle.readline()
            skipped += len(discarded)
            start_offset = handle.tell()
        data = handle.read(max(0, file_size - start_offset))
        end_offset = handle.tell()

    if data and not data.endswith((b"\n", b"\r")):
        parts = data.splitlines(keepends=True)
        if parts:
            partial = parts[-1]
            data = b"".join(parts[:-1])
            end_offset -= len(partial)
            partial_line_held = True

    lines = _decode_lines(data)
    lines_read = len(lines)
    if len(lines) > limit:
        lines = lines[-limit:]

    duration_ms = int((time.perf_counter() - started) * 1000)
    result = SourceReadResult(
        path=str(resolved),
        exists=True,
        file_size=file_size,
        previous_offset=previous_offset,
        start_offset=start_offset,
        end_offset=end_offset,
        bytes_read=len(data),
        backlog_bytes=max(0, file_size - end_offset),
        skipped_backlog_bytes=skipped,
        lines_read=lines_read,
        lines_returned=len(lines),
        partial_line_held=partial_line_held,
        reset_reason=reset_reason,
        duration_ms=duration_ms,
    )
    return lines, result


def read_jsonl_incremental(
    source_files: Iterable[Path],
    checkpoint_file: Path,
    *,
    namespace: str,
    limit: int = 500,
    max_bytes: int = DEFAULT_MAX_READ_BYTES,
    bootstrap_bytes: int = DEFAULT_BOOTSTRAP_BYTES,
    event_type: str | None = None,
) -> JsonlReadBatch:
    started = time.perf_counter()
    checkpoint_file.parent.mkdir(parents=True, exist_ok=True)
    state = read_json(checkpoint_file)
    sources = _state_sources(state)
    events: list[dict[str, Any]] = []
    results: list[SourceReadResult] = []

    for path in source_files:
        key = _namespace_key(namespace, path)
        previous = sources.get(key) if isinstance(sources.get(key), dict) else {}
        lines, result = _read_source_incremental(
            path,
            previous,
            limit=limit,
            max_bytes=max(1, max_bytes),
            bootstrap_bytes=max(1, bootstrap_bytes),
        )
        results.append(result)
        for line in lines:
            try:
                item = json.loads(line)
            except json.JSONDecodeError:
                continue
            if not isinstance(item, dict):
                continue
            if event_type and item.get("eventType") != event_type:
                continue
            events.append(item)

        sources[key] = {
            "path": str(path.resolve()),
            "namespace": namespace,
            "offset": result.end_offset,
            "fileSize": result.file_size,
            "lastReadAt": utc_now(),
            "linesReturned": result.lines_returned,
            "bytesRead": result.bytes_read,
            "skippedBacklogBytes": result.skipped_backlog_bytes,
            "resetReason": result.reset_reason,
            "partialLineHeld": result.partial_line_held,
        }

    batch = JsonlReadBatch(
        events=events[-limit:],
        source_results=results,
        cycle_ms=int((time.perf_counter() - started) * 1000),
        checkpoint_file=checkpoint_file,
        namespace=namespace,
        limit=limit,
    )

    next_state = {
        "status": "ok",
        "engine": ENGINE,
        "version": VERSION,
        "updatedAt": utc_now(),
        "sources": sources,
        "latestBatch": batch.telemetry(),
    }
    write_json(checkpoint_file, next_state)
    return batch


def append_jsonl(path: Path, events: list[dict[str, Any]]) -> None:
    if not events:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        for event in events:
            handle.write(json.dumps(event, ensure_ascii=False, separators=(",", ":")) + "\n")


def read_jsonl_tail(path: Path, *, limit: int = 500, max_bytes: int = DEFAULT_MAX_READ_BYTES, event_type: str | None = None) -> list[dict[str, Any]]:
    batch = read_jsonl_incremental(
        [path],
        path.parent / ".transient-tail-checkpoint.json",
        namespace=f"tail:{path.name}:{time.time_ns()}",
        limit=limit,
        max_bytes=max_bytes,
        bootstrap_bytes=max_bytes,
        event_type=event_type,
    )
    try:
        (path.parent / ".transient-tail-checkpoint.json").unlink(missing_ok=True)
    except Exception:
        pass
    return batch.events


def compact_jsonl_tail(path: Path, *, max_bytes: int = DEFAULT_COMPACT_MAX_BYTES, keep_bytes: int = DEFAULT_COMPACT_KEEP_BYTES) -> dict[str, Any]:
    if not path.exists():
        return {"path": str(path), "compacted": False, "reason": "missing"}
    file_size = path.stat().st_size
    if file_size <= max_bytes:
        return {"path": str(path), "compacted": False, "reason": "under_limit", "fileSize": file_size}

    keep_bytes = max(1024, min(keep_bytes, file_size))
    archive_dir = path.parent / "archive"
    archive_dir.mkdir(parents=True, exist_ok=True)
    archive_path = archive_dir / f"{path.stem}-{datetime.now(timezone.utc).strftime('%Y%m%d%H%M%S')}{path.suffix}.bak"
    shutil.copy2(path, archive_path)

    with path.open("rb") as handle:
        handle.seek(max(0, file_size - keep_bytes))
        if handle.tell() > 0:
            handle.readline()
        data = handle.read()

    temp = path.with_suffix(path.suffix + ".compact.tmp")
    with temp.open("wb") as handle:
        handle.write(data)
        if data and not data.endswith((b"\n", b"\r")):
            handle.write(b"\n")
    os.replace(temp, path)
    return {
        "path": str(path),
        "compacted": True,
        "fileSizeBefore": file_size,
        "fileSizeAfter": path.stat().st_size,
        "archive": str(archive_path),
        "keptBytes": keep_bytes,
    }
