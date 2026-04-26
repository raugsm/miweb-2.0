#!/usr/bin/env python
"""Reader Core for AriadGSM visual agent.

Priority order:
1. Structured visible text from browser DOM or Windows accessibility.
2. OCR fallback.
3. Later: AI verifier for doubtful readings.

This module does not read browser cookies, tokens, storage, or hidden session data.
It only accepts visible-message observations written by a local trusted reader.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


SCRIPT_DIR = Path(__file__).resolve().parent
RUNTIME_DIR = SCRIPT_DIR / "runtime"
READER_DIR = RUNTIME_DIR / "reader-core"
OBSERVATIONS_FILE = READER_DIR / "structured-observations.jsonl"
STATE_FILE = READER_DIR / "reader-core.state.json"

STRUCTURED_SOURCES = {
    "browser_extension": 0.96,
    "dom": 0.94,
    "accessibility": 0.9,
}
OCR_BASE_CONFIDENCE = 0.58


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def parse_time(value: Any) -> float:
    if not value:
        return 0.0
    try:
        text = str(value).replace("Z", "+00:00")
        return datetime.fromisoformat(text).timestamp()
    except ValueError:
        return 0.0


def clean_text(value: Any) -> str:
    return re.sub(r"\s+", " ", str(value or "")).strip()


def valid_message(message: dict[str, Any]) -> bool:
    text = clean_text(message.get("text"))
    if len(text) < 2:
        return False
    return bool(re.search(r"\w", text, flags=re.UNICODE))


def normalize_message(message: dict[str, Any], index: int) -> dict[str, Any]:
    return {
        "messageId": str(message.get("messageId") or message.get("id") or f"visible-{index}"),
        "text": clean_text(message.get("text")),
        "senderName": clean_text(message.get("senderName") or message.get("sender") or ""),
        "direction": clean_text(message.get("direction") or "unknown"),
        "sentAt": clean_text(message.get("sentAt") or message.get("time") or ""),
    }


def normalize_observation(payload: dict[str, Any]) -> dict[str, Any] | None:
    channel_id = clean_text(payload.get("channelId"))
    source = clean_text(payload.get("source") or payload.get("readerSource") or "dom")
    if not channel_id or source not in STRUCTURED_SOURCES:
        return None

    messages = [
        normalize_message(message, index)
        for index, message in enumerate(payload.get("messages") or [])
        if isinstance(message, dict) and valid_message(message)
    ]
    if not messages:
        return None

    captured_at = clean_text(payload.get("capturedAt") or now_iso())
    confidence = float(payload.get("confidence") or STRUCTURED_SOURCES[source])
    confidence = max(0.0, min(1.0, confidence))
    return {
        "channelId": channel_id,
        "source": source,
        "confidence": confidence,
        "capturedAt": captured_at,
        "conversationTitle": clean_text(payload.get("conversationTitle") or payload.get("name") or channel_id),
        "visibleOnly": bool(payload.get("visibleOnly", True)),
        "url": clean_text(payload.get("url")),
        "messages": messages,
        "raw": payload,
    }


def append_structured_observation(payload: dict[str, Any]) -> dict[str, Any]:
    observation = normalize_observation(payload)
    if not observation:
        raise ValueError("La observacion estructurada no contiene mensajes visibles validos.")
    READER_DIR.mkdir(parents=True, exist_ok=True)
    with OBSERVATIONS_FILE.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(observation, ensure_ascii=False, separators=(",", ":")) + "\n")
    write_state({"lastStructuredObservation": observation})
    return observation


def read_structured_observations(max_age_seconds: float = 10.0, limit: int = 300) -> list[dict[str, Any]]:
    if not OBSERVATIONS_FILE.exists():
        return []
    try:
        lines = OBSERVATIONS_FILE.read_text(encoding="utf-8").splitlines()[-limit:]
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
        if parse_time(observation.get("capturedAt")) < cutoff:
            continue
        observations.append(observation)
    return observations


def latest_by_channel(observations: list[dict[str, Any]]) -> dict[str, dict[str, Any]]:
    latest: dict[str, dict[str, Any]] = {}
    for observation in observations:
        channel_id = str(observation.get("channelId") or "")
        current = latest.get(channel_id)
        if not current or parse_time(observation.get("capturedAt")) >= parse_time(current.get("capturedAt")):
            latest[channel_id] = observation
    return latest


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


def ocr_confidence(event: dict[str, Any]) -> float:
    accepted = [clean_text(line) for line in event.get("acceptedLines") or [] if clean_text(line)]
    ignored = event.get("ignoredLines") or []
    if not accepted:
        return 0.0
    useful_ratio = len(accepted) / max(1, len(accepted) + len(ignored))
    length_bonus = min(0.12, sum(len(line) for line in accepted) / 1200.0)
    confidence = OCR_BASE_CONFIDENCE + (useful_ratio * 0.18) + length_bonus
    return max(0.0, min(0.82, confidence))


def observation_to_event(observation: dict[str, Any], base_event: dict[str, Any]) -> dict[str, Any]:
    messages = observation.get("messages") or []
    lines = [message["text"] for message in messages if message.get("text")]
    event = dict(base_event)
    event["rawLines"] = lines
    event["acceptedLines"] = lines
    event["ignoredLines"] = []
    event["name"] = observation.get("conversationTitle") or event.get("name")
    event["structuredMessages"] = messages
    event["readerCore"] = {
        "selectedSource": observation.get("source"),
        "confidence": observation.get("confidence"),
        "observationKey": observation_key(observation),
        "fallbackSource": "ocr",
        "visibleOnly": observation.get("visibleOnly"),
        "conversationTitle": observation.get("conversationTitle"),
        "capturedAt": observation.get("capturedAt"),
        "reason": "structured_reader_preferred",
    }
    return event


def select_reading(
    ocr_event: dict[str, Any],
    observations_by_channel: dict[str, dict[str, Any]],
    min_structured_confidence: float = 0.86,
) -> dict[str, Any]:
    channel_id = str(ocr_event.get("channelId") or "")
    structured = observations_by_channel.get(channel_id)
    ocr_score = ocr_confidence(ocr_event)
    if structured and float(structured.get("confidence") or 0.0) >= min_structured_confidence:
        return observation_to_event(structured, ocr_event)

    event = dict(ocr_event)
    event["readerCore"] = {
        "selectedSource": "ocr",
        "confidence": ocr_score,
        "fallbackSource": None,
        "reason": "structured_reader_unavailable",
    }
    return event


def write_state(extra: dict[str, Any] | None = None) -> dict[str, Any]:
    READER_DIR.mkdir(parents=True, exist_ok=True)
    observations = read_structured_observations(max_age_seconds=60.0)
    state = {
        "updatedAt": now_iso(),
        "structuredObservationCount": len(observations),
        "channels": sorted(latest_by_channel(observations).keys()),
        "observationFile": str(OBSERVATIONS_FILE),
    }
    if extra:
        state.update(extra)
    STATE_FILE.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
    return state


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM Reader Core")
    parser.add_argument("--status", action="store_true")
    parser.add_argument("--write-sample", action="store_true")
    parser.add_argument("--channel-id", default="wa-1")
    parser.add_argument("--text", default="Cuanto vale liberar un Samsung por IMEI?")
    parser.add_argument("--source", choices=sorted(STRUCTURED_SOURCES), default="dom")
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    if args.write_sample:
        observation = append_structured_observation(
            {
                "channelId": args.channel_id,
                "source": args.source,
                "confidence": STRUCTURED_SOURCES[args.source],
                "capturedAt": now_iso(),
                "conversationTitle": f"Sample {args.channel_id}",
                "visibleOnly": True,
                "messages": [{"text": args.text, "senderName": "Cliente", "direction": "client"}],
            }
        )
        print(json.dumps({"Status": "written", "observation": observation}, ensure_ascii=False, indent=2))
        return 0
    print(json.dumps(write_state(), ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(__import__("sys").argv[1:]))
