from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any


def find_repo_root(start: Path) -> Path:
    current = start.resolve()
    if current.is_file():
        current = current.parent
    for item in [current, *current.parents]:
        if (item / ".git").exists():
            return item
    return current


def resolve_path(repo_root: Path, value: str | Path) -> Path:
    path = Path(value).expanduser()
    if path.is_absolute():
        return path
    return (repo_root / path).resolve()


@dataclass(frozen=True)
class ReaderCoreConfig:
    observations_file: Path
    max_age_seconds: float = 45.0
    limit: int = 800


@dataclass(frozen=True)
class ServiceConfig:
    interval_seconds: float = 0.75
    max_cycles: int = 0


@dataclass(frozen=True)
class AgentConfig:
    repo_root: Path
    runtime_dir: Path
    memory_db: Path
    state_file: Path
    reader_core: ReaderCoreConfig
    service: ServiceConfig
    cloud_url: str
    cloud_enabled: bool


def load_config(config_path: str | Path) -> AgentConfig:
    config_file = Path(config_path).expanduser().resolve()
    repo_root = find_repo_root(config_file)
    data: dict[str, Any] = json.loads(config_file.read_text(encoding="utf-8-sig"))

    reader = data.get("readerCore") or {}
    service = data.get("service") or {}
    cloud = data.get("cloud") or {}

    runtime_dir = resolve_path(repo_root, data.get("runtimeDir", "./desktop-agent/runtime"))
    memory_db = resolve_path(repo_root, data.get("memoryDb", runtime_dir / "agent-memory.sqlite"))
    state_file = resolve_path(repo_root, data.get("stateFile", runtime_dir / "core.state.json"))
    observations_file = resolve_path(
        repo_root,
        reader.get("observationsFile", "./scripts/visual-agent/runtime/reader-core/structured-observations.jsonl"),
    )

    return AgentConfig(
        repo_root=repo_root,
        runtime_dir=runtime_dir,
        memory_db=memory_db,
        state_file=state_file,
        reader_core=ReaderCoreConfig(
            observations_file=observations_file,
            max_age_seconds=float(reader.get("maxAgeSeconds", 45.0)),
            limit=int(reader.get("limit", 800)),
        ),
        service=ServiceConfig(
            interval_seconds=max(0.1, float(service.get("intervalSeconds", 0.75))),
            max_cycles=max(0, int(service.get("maxCycles", 0))),
        ),
        cloud_url=str(cloud.get("url", "https://ariadgsm.com")),
        cloud_enabled=bool(cloud.get("enabled", False)),
    )
