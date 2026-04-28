from __future__ import annotations

import argparse
import hashlib
import json
import re
import sqlite3
import sys
import time
import unicodedata
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any

from .event_backbone import compact_jsonl_tail, read_jsonl_incremental


AGENT_ROOT = Path(__file__).resolve().parents[1]
VERSION = "0.9.9"


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def parse_datetime(value: Any) -> datetime | None:
    if not value:
        return None
    raw = str(value).strip()
    if not raw:
        return None
    if raw.endswith("Z"):
        raw = raw[:-1] + "+00:00"
    try:
        parsed = datetime.fromisoformat(raw)
    except ValueError:
        return None
    if parsed.tzinfo is None:
        parsed = parsed.replace(tzinfo=timezone.utc)
    return parsed.astimezone(timezone.utc)


def normalize_text(value: str) -> str:
    return re.sub(r"\s+", " ", str(value or "")).strip().lower()


def normalize_identity_text(value: Any) -> str:
    normalized = unicodedata.normalize("NFKD", str(value or "")).encode("ascii", "ignore").decode("ascii")
    normalized = re.sub(r"[^a-zA-Z0-9\s]+", " ", normalized.lower())
    return re.sub(r"\s+", " ", normalized).strip()


def unreliable_conversation_reasons(event: dict[str, Any]) -> list[str]:
    quality = event.get("quality") if isinstance(event.get("quality"), dict) else {}
    if quality and quality.get("isReliable") is False:
        reasons = quality.get("rejectionReasons")
        if isinstance(reasons, list) and reasons:
            return [str(reason) for reason in reasons]
        return ["quality_not_reliable"]

    title = normalize_identity_text(event.get("conversationTitle") or event.get("conversationId"))
    blocked = (
        "whatsapp",
        "whatsapp business",
        "paginas mas",
        "perfil 1",
        "anadir esta pagina a marcadores",
        "anadir pestana a la barra de tareas",
        "editar marcador",
        "editar favorito",
        "configuracion y mas",
        "extensiones",
        "ver estado",
        "leer en voz alta",
        "informacion del sitio",
        "ver informacion del sitio",
        "ctrl d",
        "google chrome",
        "microsoft edge",
        "mozilla firefox",
        "http",
        "drive google",
    )
    if not title:
        return ["missing_title"]
    if any(token == title or token in title for token in blocked):
        return ["browser_or_generic_title"]
    return []


def message_fingerprint(channel_id: str, conversation_id: str, message: dict[str, Any]) -> str:
    raw = "|".join(
        [
            channel_id,
            conversation_id,
            str(message.get("direction") or "unknown"),
            str(message.get("sentAt") or ""),
            normalize_text(str(message.get("text") or "")),
        ]
    )
    return hashlib.sha1(raw.encode("utf-8")).hexdigest()


@dataclass
class ConversationTimeline:
    conversation_id: str
    channel_id: str
    title: str = ""
    history_limit_days: int = 30
    complete: bool = False
    messages: list[dict[str, Any]] = field(default_factory=list)
    source_counts: dict[str, int] = field(default_factory=dict)
    oldest_loaded_at: str | None = None
    latest_observed_at: str | None = None
    _seen: set[str] = field(default_factory=set)

    def add_message(
        self,
        message: dict[str, Any],
        *,
        event_observed_at: str | None = None,
        source: str = "live",
        cutoff: datetime | None = None,
    ) -> bool:
        text = str(message.get("text") or "").strip()
        if not text:
            return False
        timestamp = parse_datetime(message.get("sentAt")) or parse_datetime(message.get("observedAt")) or parse_datetime(event_observed_at)
        if cutoff is not None and timestamp is not None and timestamp < cutoff:
            return False
        fingerprint = str(message.get("messageId") or "") or message_fingerprint(self.channel_id, self.conversation_id, message)
        if fingerprint in self._seen:
            return False
        self._seen.add(fingerprint)
        stored = dict(message)
        stored.setdefault("messageId", fingerprint)
        stored.setdefault("direction", "unknown")
        stored.setdefault("observedAt", event_observed_at or utc_now())
        stored.setdefault("source", source)
        self.messages.append(stored)
        self.source_counts[source] = self.source_counts.get(source, 0) + 1
        if timestamp is not None:
            timestamp_raw = timestamp.isoformat().replace("+00:00", "Z")
            if self.oldest_loaded_at is None or timestamp < (parse_datetime(self.oldest_loaded_at) or timestamp):
                self.oldest_loaded_at = timestamp_raw
            if self.latest_observed_at is None or timestamp > (parse_datetime(self.latest_observed_at) or timestamp):
                self.latest_observed_at = timestamp_raw
        return True

    def merge(
        self,
        messages: list[dict[str, Any]],
        *,
        event_observed_at: str | None = None,
        source: str = "live",
        cutoff: datetime | None = None,
    ) -> int:
        return sum(
            1
            for message in messages
            if self.add_message(message, event_observed_at=event_observed_at, source=source, cutoff=cutoff)
        )

    def sort_messages(self) -> None:
        self.messages.sort(key=message_sort_key)

    def to_event(self, source: str = "timeline") -> dict[str, Any]:
        self.sort_messages()
        message_digest = hashlib.sha1(
            "|".join(str(message.get("messageId") or "") for message in self.messages).encode("utf-8")
        ).hexdigest()[:16]
        timeline_payload: dict[str, Any] = {
            "historyLimitDays": self.history_limit_days,
            "complete": self.complete,
            "dedupeStrategy": "timeline_channel_conversation_message_fingerprint",
            "sourceCounts": dict(sorted(self.source_counts.items())),
        }
        if self.oldest_loaded_at:
            timeline_payload["oldestLoadedAt"] = self.oldest_loaded_at
        if self.latest_observed_at:
            timeline_payload["latestObservedAt"] = self.latest_observed_at
        return {
            "eventType": "conversation_event",
            "conversationEventId": f"timeline-{self.channel_id}-{hashlib.sha1(self.conversation_id.encode('utf-8')).hexdigest()[:12]}-{message_digest}",
            "conversationId": self.conversation_id,
            "channelId": self.channel_id,
            "observedAt": utc_now(),
            "conversationTitle": self.title,
            "source": source,
            "messages": list(self.messages),
            "timeline": timeline_payload,
        }


def message_sort_key(message: dict[str, Any]) -> tuple[str, str]:
    timestamp = parse_datetime(message.get("sentAt")) or parse_datetime(message.get("observedAt"))
    return ((timestamp or datetime.max.replace(tzinfo=timezone.utc)).isoformat(), str(message.get("messageId") or ""))


def event_source(event: dict[str, Any]) -> str:
    source = str(event.get("source") or "live").strip().lower()
    return source if source in {"live", "history", "replay", "manual", "timeline"} else "live"


def build_timelines(events: list[dict[str, Any]], history_limit_days: int = 30) -> list[ConversationTimeline]:
    cutoff = datetime.now(timezone.utc) - timedelta(days=max(1, history_limit_days))
    timelines: dict[tuple[str, str], ConversationTimeline] = {}
    for event in events:
        if event.get("eventType") != "conversation_event":
            continue
        if unreliable_conversation_reasons(event):
            continue
        channel_id = str(event.get("channelId") or "unknown").strip()
        conversation_id = str(event.get("conversationId") or f"{channel_id}-unknown").strip()
        key = (channel_id, conversation_id)
        title = str(event.get("conversationTitle") or conversation_id).strip()
        timeline = timelines.get(key)
        if timeline is None:
            timeline = ConversationTimeline(
                conversation_id=conversation_id,
                channel_id=channel_id,
                title=title,
                history_limit_days=history_limit_days,
            )
            timelines[key] = timeline
        elif title and (not timeline.title or timeline.title == timeline.conversation_id):
            timeline.title = title

        source = event_source(event)
        input_timeline = event.get("timeline") if isinstance(event.get("timeline"), dict) else {}
        timeline.complete = timeline.complete or bool(input_timeline.get("complete")) or source == "history"
        oldest = input_timeline.get("oldestLoadedAt")
        if oldest and (timeline.oldest_loaded_at is None or (parse_datetime(oldest) or datetime.max.replace(tzinfo=timezone.utc)) < (parse_datetime(timeline.oldest_loaded_at) or datetime.max.replace(tzinfo=timezone.utc))):
            timeline.oldest_loaded_at = str(oldest)
        messages = [message for message in event.get("messages") or [] if isinstance(message, dict)]
        timeline.merge(
            messages,
            event_observed_at=str(event.get("observedAt") or utc_now()),
            source=source,
            cutoff=cutoff,
        )

    return sorted(timelines.values(), key=lambda item: (item.channel_id, item.title.lower(), item.conversation_id))


def read_jsonl_events(path: Path, event_type: str, limit: int = 1000) -> list[dict[str, Any]]:
    checkpoint = path.parent / ".timeline-tail-checkpoint.json"
    batch = read_jsonl_incremental(
        [path],
        checkpoint,
        namespace=f"timeline_tail:{path.name}:{time.time_ns()}",
        limit=limit,
        max_bytes=8 * 1024 * 1024,
        bootstrap_bytes=8 * 1024 * 1024,
        event_type=event_type,
    )
    try:
        checkpoint.unlink(missing_ok=True)
    except Exception:
        pass
    return batch.events


def write_jsonl(path: Path, events: list[dict[str, Any]], replace_output: bool) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    mode = "w" if replace_output else "a"
    with path.open(mode, encoding="utf-8") as handle:
        for event in events:
            handle.write(json.dumps(event, ensure_ascii=False, separators=(",", ":")) + "\n")


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    temp.replace(path)


class TimelineStore:
    def __init__(self, db_path: Path, history_limit_days: int = 30):
        self.db_path = db_path
        self.history_limit_days = history_limit_days
        self.db_path.parent.mkdir(parents=True, exist_ok=True)
        self.conn = sqlite3.connect(self.db_path)
        self.conn.row_factory = sqlite3.Row
        self.init_schema()

    def close(self) -> None:
        self.conn.close()

    def init_schema(self) -> None:
        self.conn.execute("pragma journal_mode=WAL")
        self.conn.execute("pragma synchronous=NORMAL")
        self.conn.executescript(
            """
            create table if not exists timeline_messages (
              message_id text primary key,
              channel_id text not null,
              conversation_id text not null,
              conversation_title text not null,
              direction text not null,
              text text not null,
              sent_at text,
              observed_at text not null,
              source text not null,
              message_json text not null,
              stored_at text not null
            );
            create index if not exists idx_timeline_conversation
              on timeline_messages(channel_id, conversation_id);
            create index if not exists idx_timeline_observed
              on timeline_messages(observed_at);
            """
        )
        self.conn.commit()

    def save_conversation_event(self, event: dict[str, Any]) -> tuple[int, int]:
        channel_id = str(event.get("channelId") or "unknown").strip()
        conversation_id = str(event.get("conversationId") or f"{channel_id}-unknown").strip()
        title = str(event.get("conversationTitle") or conversation_id).strip()
        observed_at = str(event.get("observedAt") or utc_now())
        source = event_source(event)
        inserted = 0
        duplicates = 0
        messages = [message for message in event.get("messages") or [] if isinstance(message, dict)]
        with self.conn:
            for message in messages:
                text = str(message.get("text") or "").strip()
                if not text:
                    continue
                message_id = str(message.get("messageId") or message_fingerprint(channel_id, conversation_id, message))
                payload = dict(message)
                payload.setdefault("messageId", message_id)
                payload.setdefault("observedAt", observed_at)
                payload.setdefault("source", source)
                try:
                    self.conn.execute(
                        """
                        insert into timeline_messages (
                          message_id, channel_id, conversation_id, conversation_title,
                          direction, text, sent_at, observed_at, source, message_json, stored_at
                        ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                        """,
                        (
                            message_id,
                            channel_id,
                            conversation_id,
                            title,
                            str(message.get("direction") or "unknown"),
                            text,
                            str(message.get("sentAt") or ""),
                            str(message.get("observedAt") or observed_at),
                            source,
                            json.dumps(payload, ensure_ascii=False, separators=(",", ":")),
                            utc_now(),
                        ),
                    )
                    inserted += 1
                except sqlite3.IntegrityError:
                    duplicates += 1
        return inserted, duplicates

    def load_timelines(self) -> list[ConversationTimeline]:
        cutoff = datetime.now(timezone.utc) - timedelta(days=max(1, self.history_limit_days))
        timelines: dict[tuple[str, str], ConversationTimeline] = {}
        rows = self.conn.execute(
            """
            select channel_id, conversation_id, conversation_title, observed_at, source, message_json
            from timeline_messages
            order by channel_id, conversation_id, coalesce(sent_at, observed_at), message_id
            """
        ).fetchall()
        for row in rows:
            try:
                message = json.loads(str(row["message_json"]))
            except json.JSONDecodeError:
                continue
            if not isinstance(message, dict):
                continue
            channel_id = str(row["channel_id"])
            conversation_id = str(row["conversation_id"])
            key = (channel_id, conversation_id)
            timeline = timelines.get(key)
            if timeline is None:
                timeline = ConversationTimeline(
                    conversation_id=conversation_id,
                    channel_id=channel_id,
                    title=str(row["conversation_title"]),
                    history_limit_days=self.history_limit_days,
                )
                timelines[key] = timeline
            timeline.add_message(
                message,
                event_observed_at=str(row["observed_at"]),
                source=str(row["source"] or "live"),
                cutoff=cutoff,
            )
        return sorted(timelines.values(), key=lambda item: (item.channel_id, item.title.lower(), item.conversation_id))

    def summary(self) -> dict[str, Any]:
        def scalar(sql: str) -> int:
            row = self.conn.execute(sql).fetchone()
            return int(row[0] if row else 0)

        return {
            "storedMessages": scalar("select count(*) from timeline_messages"),
            "storedTimelines": scalar("select count(distinct channel_id || ':' || conversation_id) from timeline_messages"),
        }


def run_timeline_once(
    conversation_events_file: Path,
    timeline_events_file: Path,
    state_file: Path,
    history_events_file: Path | None = None,
    history_limit_days: int = 30,
    limit: int = 1000,
    replace_output: bool = True,
    db_path: Path | None = None,
    checkpoint_file: Path | None = None,
    max_read_bytes: int = 8 * 1024 * 1024,
) -> dict[str, Any]:
    cycle_started = time.perf_counter()
    checkpoint = checkpoint_file or state_file.with_name("event-backbone-state.json")
    source_files = [conversation_events_file]
    if history_events_file:
        source_files.append(history_events_file)
    batch = read_jsonl_incremental(
        source_files,
        checkpoint,
        namespace="timeline_sources",
        limit=limit,
        max_bytes=max_read_bytes,
        bootstrap_bytes=max_read_bytes,
        event_type="conversation_event",
    )
    live_events = list(batch.events)
    history_events = []
    rejected_events = sum(1 for event in [*live_events, *history_events] if unreliable_conversation_reasons(event))
    valid_events = [event for event in batch.events if not unreliable_conversation_reasons(event)]
    db = db_path or state_file.with_name("timeline.sqlite")
    store = TimelineStore(db, history_limit_days=history_limit_days)
    inserted_messages = 0
    duplicate_messages = 0
    try:
        for event in valid_events:
            inserted, duplicates = store.save_conversation_event(event)
            inserted_messages += inserted
            duplicate_messages += duplicates
        timelines = store.load_timelines()
        durable_summary = store.summary()
    finally:
        store.close()
    output_events = [timeline.to_event("timeline") for timeline in timelines if timeline.messages]
    write_jsonl(timeline_events_file, output_events, replace_output=replace_output)
    compactions = [compact_jsonl_tail(timeline_events_file)]
    cycle_ms = int((time.perf_counter() - cycle_started) * 1000)
    state = {
        "status": "ok",
        "engine": "ariadgsm_timeline_engine",
        "version": VERSION,
        "updatedAt": utc_now(),
        "conversationEventsFile": str(conversation_events_file),
        "historyEventsFile": str(history_events_file) if history_events_file else "",
        "timelineEventsFile": str(timeline_events_file),
        "db": str(db),
        "checkpointFile": str(checkpoint),
        "historyLimitDays": history_limit_days,
        "replaceOutput": replace_output,
        "ingested": {
            "liveEvents": len(batch.events),
            "historyEvents": len(history_events),
            "rejectedEvents": rejected_events,
            "newMessages": inserted_messages,
            "duplicateMessages": duplicate_messages,
            "timelines": len(output_events),
            "messages": sum(len(event.get("messages") or []) for event in output_events),
            "completeTimelines": sum(1 for event in output_events if (event.get("timeline") or {}).get("complete")),
            "sourceBytesRead": batch.bytes_read,
            "sourceBacklogBytes": batch.backlog_bytes,
            "sourceSkippedBacklogBytes": batch.skipped_backlog_bytes,
            "cycleDurationMs": cycle_ms,
        },
        "durable": {
            **durable_summary,
            "sqliteIsTruth": True,
            "jsonIsProjection": True,
        },
        "backbone": {
            **batch.telemetry(),
            "cycleDurationMs": cycle_ms,
            "compactions": compactions,
            "humanSummary": (
                f"Timeline uso SQLite durable; leyo {batch.bytes_read} bytes incrementales; "
                f"mensajes nuevos={inserted_messages}; duplicados={duplicate_messages}; ciclo={cycle_ms}ms."
            ),
        },
        "latestTimelines": [
            {
                "conversationEventId": event.get("conversationEventId"),
                "conversationId": event.get("conversationId"),
                "channelId": event.get("channelId"),
                "conversationTitle": event.get("conversationTitle"),
                "messages": len(event.get("messages") or []),
                "complete": bool((event.get("timeline") or {}).get("complete")),
                "sourceCounts": (event.get("timeline") or {}).get("sourceCounts") or {},
            }
            for event in output_events[-20:]
        ],
    }
    write_json(state_file, state)
    return state


def resolve_runtime_path(value: str | Path) -> Path:
    path = Path(value).expanduser()
    if path.is_absolute():
        return path
    return (AGENT_ROOT / path).resolve()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM Timeline Engine")
    parser.add_argument("--conversation-events", default="runtime/conversation-events.jsonl")
    parser.add_argument("--history-events", default="runtime/history-conversation-events.jsonl")
    parser.add_argument("--timeline-events", default="runtime/timeline-events.jsonl")
    parser.add_argument("--state-file", default="runtime/timeline-state.json")
    parser.add_argument("--db", default="runtime/timeline.sqlite")
    parser.add_argument("--checkpoint-file", default="runtime/event-backbone-state.json")
    parser.add_argument("--history-limit-days", type=int, default=30)
    parser.add_argument("--limit", type=int, default=1000)
    parser.add_argument("--max-read-bytes", type=int, default=8 * 1024 * 1024)
    parser.add_argument("--append", action="store_true")
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def main() -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    args = parse_args()
    history_file = resolve_runtime_path(args.history_events)
    state = run_timeline_once(
        resolve_runtime_path(args.conversation_events),
        resolve_runtime_path(args.timeline_events),
        resolve_runtime_path(args.state_file),
        history_events_file=history_file if history_file.exists() else None,
        history_limit_days=args.history_limit_days,
        limit=args.limit,
        replace_output=not args.append,
        db_path=resolve_runtime_path(args.db),
        checkpoint_file=resolve_runtime_path(args.checkpoint_file),
        max_read_bytes=max(4096, args.max_read_bytes),
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=False, indent=2))
    else:
        ingested = state["ingested"]
        print(
            "AriadGSM Timeline Engine: "
            f"live={ingested['liveEvents']} "
            f"history={ingested['historyEvents']} "
            f"timelines={ingested['timelines']} "
            f"messages={ingested['messages']} "
            f"file={state['timelineEventsFile']}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
