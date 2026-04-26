from __future__ import annotations

import hashlib
import json
import time
from datetime import datetime
from pathlib import Path
from typing import Any

from .text import clean_text


def parse_time(value: Any) -> float:
    if not value:
        return 0.0
    try:
        return datetime.fromisoformat(str(value).replace("Z", "+00:00")).timestamp()
    except ValueError:
        return 0.0


def observation_key(observation: dict[str, Any]) -> str:
    messages = observation.get("messages") or []
    message_parts = [
        str(message.get("messageId") or message.get("text") or "")
        for message in messages
        if isinstance(message, dict)
    ]
    raw = json.dumps(
        {
            "channelId": observation.get("channelId"),
            "source": observation.get("source"),
            "capturedAt": observation.get("capturedAt"),
            "messages": message_parts,
        },
        ensure_ascii=False,
        sort_keys=True,
    )
    return hashlib.sha1(raw.encode("utf-8")).hexdigest()


def normalize_observation(payload: dict[str, Any]) -> dict[str, Any] | None:
    channel_id = clean_text(payload.get("channelId"))
    source = clean_text(payload.get("source") or "unknown")
    if not channel_id:
        return None
    messages: list[dict[str, Any]] = []
    for index, message in enumerate(payload.get("messages") or []):
        if not isinstance(message, dict):
            continue
        text = clean_text(message.get("text"))
        if not text:
            continue
        messages.append(
            {
                "messageId": str(message.get("messageId") or message.get("id") or f"visible-{index}"),
                "text": text,
                "senderName": clean_text(message.get("senderName") or message.get("sender")),
                "direction": clean_text(message.get("direction") or "unknown"),
                "sentAt": clean_text(message.get("sentAt") or message.get("time")),
            }
        )
    if not messages:
        return None
    observation = {
        "channelId": channel_id,
        "source": source,
        "confidence": max(0.0, min(1.0, float(payload.get("confidence") or 0.0))),
        "capturedAt": clean_text(payload.get("capturedAt")),
        "conversationTitle": clean_text(payload.get("conversationTitle") or payload.get("name") or channel_id),
        "visibleOnly": bool(payload.get("visibleOnly", True)),
        "browser": clean_text(payload.get("browser")),
        "messages": messages,
        "raw": payload,
    }
    observation["observationKey"] = observation_key(observation)
    return observation


def read_recent_observations(path: Path, max_age_seconds: float, limit: int) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    try:
        lines = path.read_text(encoding="utf-8").splitlines()[-limit:]
    except OSError:
        return []
    cutoff = time.time() - max_age_seconds
    observations: list[dict[str, Any]] = []
    for line in lines:
        try:
            payload = json.loads(line)
        except json.JSONDecodeError:
            continue
        observation = normalize_observation(payload)
        if not observation:
            continue
        captured_ts = parse_time(observation.get("capturedAt"))
        if captured_ts and captured_ts < cutoff:
            continue
        observations.append(observation)
    observations.sort(key=lambda item: parse_time(item.get("capturedAt")))
    return observations
