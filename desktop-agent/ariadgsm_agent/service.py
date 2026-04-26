from __future__ import annotations

import json
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from .classifier import classify_messages
from .config import AgentConfig
from .memory import AgentMemory
from .reader_core_bus import read_recent_observations


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


class DesktopAgentService:
    def __init__(self, config: AgentConfig):
        self.config = config
        self.config.runtime_dir.mkdir(parents=True, exist_ok=True)
        self.memory = AgentMemory(config.memory_db)

    def close(self) -> None:
        self.memory.close()

    def run_once(self) -> dict[str, Any]:
        observations = read_recent_observations(
            self.config.reader_core.observations_file,
            self.config.reader_core.max_age_seconds,
            self.config.reader_core.limit,
        )
        ingested = {"observations": 0, "messages": 0, "decisions": 0}
        decisions: list[dict[str, Any]] = []
        for observation in observations:
            decision = classify_messages(observation.get("messages") or [])
            result = self.memory.ingest_observation(observation, decision)
            ingested = {key: ingested[key] + result.get(key, 0) for key in ingested}
            if result.get("observations"):
                decisions.append(
                    {
                        "channelId": observation.get("channelId"),
                        "source": observation.get("source"),
                        "conversationTitle": observation.get("conversationTitle"),
                        "decision": decision.to_dict(),
                    }
                )
        state = {
            "Status": "ok",
            "Engine": "desktop_agent_core",
            "updatedAt": utc_now(),
            "readerCoreFile": str(self.config.reader_core.observations_file),
            "observationsSeen": len(observations),
            "ingested": ingested,
            "decisions": decisions[-10:],
            "memory": self.memory.summary(),
            "cloud": {
                "url": self.config.cloud_url,
                "enabled": self.config.cloud_enabled,
            },
        }
        self.write_state(state)
        return state

    def write_state(self, state: dict[str, Any]) -> None:
        self.config.state_file.parent.mkdir(parents=True, exist_ok=True)
        temp = self.config.state_file.with_suffix(".tmp")
        temp.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
        temp.replace(self.config.state_file)

    def watch(self, max_cycles: int = 0, interval_seconds: float | None = None) -> dict[str, Any]:
        interval = self.config.service.interval_seconds if interval_seconds is None else interval_seconds
        cycles = 0
        last_state: dict[str, Any] = {}
        while True:
            cycles += 1
            last_state = self.run_once()
            last_state["Mode"] = "watch"
            last_state["Cycle"] = cycles
            self.write_state(last_state)
            if max_cycles and cycles >= max_cycles:
                break
            time.sleep(max(0.1, interval))
        return last_state


def run_service(config: AgentConfig, once: bool, watch: bool, max_cycles: int = 0, interval_seconds: float | None = None) -> dict[str, Any]:
    service = DesktopAgentService(config)
    try:
        if watch:
            return service.watch(max_cycles=max_cycles or config.service.max_cycles, interval_seconds=interval_seconds)
        return service.run_once()
    finally:
        service.close()
