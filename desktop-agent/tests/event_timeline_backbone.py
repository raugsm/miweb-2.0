from __future__ import annotations

import json
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.contracts import sample_event, validate_contract
from ariadgsm_agent.reader_core import run_reader_core_once
from ariadgsm_agent.timeline import run_timeline_once


def append_jsonl(path: Path, events: list[dict]) -> None:
    with path.open("a", encoding="utf-8") as handle:
        for event in events:
            handle.write(json.dumps(event, ensure_ascii=False) + "\n")


def make_perception_event(channel_id: str, browser: str, conversation_id: str, title: str, message_key: str, text: str) -> dict:
    return {
        "eventType": "perception_event",
        "perceptionEventId": f"perception-{channel_id}-{message_key}",
        "observedAt": "2026-04-28T22:00:00Z",
        "sourceVisionEventId": f"vision-{channel_id}-{message_key}",
        "objects": [
            {
                "objectType": "window",
                "confidence": 0.95,
                "text": f"WhatsApp Business - {browser}",
                "role": "whatsapp_web",
                "metadata": {
                    "channelId": channel_id,
                    "browserProcess": browser,
                    "windowTitle": f"WhatsApp Business - {browser}",
                },
            },
            {
                "objectType": "conversation",
                "confidence": 0.92,
                "text": title,
                "role": "active_conversation",
                "metadata": {
                    "channelId": channel_id,
                    "browserProcess": browser,
                    "conversationId": conversation_id,
                    "conversationTitle": title,
                    "windowTitle": f"WhatsApp Business - {browser}",
                },
            },
            {
                "objectType": "message_bubble",
                "confidence": 0.9,
                "text": text,
                "role": "client",
                "metadata": {
                    "channelId": channel_id,
                    "browserProcess": browser,
                    "conversationId": conversation_id,
                    "conversationTitle": title,
                    "messageKey": message_key,
                    "windowTitle": f"WhatsApp Business - {browser}",
                    "sourceKind": "windows_accessibility",
                },
            },
        ],
    }


def main() -> int:
    assert not validate_contract(sample_event("event_backbone_state"), "event_backbone_state")
    assert not validate_contract(sample_event("timeline_state"), "timeline_state")

    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        source_file = root / "perception-events.jsonl"
        visible_file = root / "reader-visible-messages.jsonl"
        conversation_file = root / "conversation-events.jsonl"
        timeline_file = root / "timeline-events.jsonl"
        reader_state_file = root / "reader-core-state.json"
        timeline_state_file = root / "timeline-state.json"
        report_file = root / "reader-core-report.json"
        reader_db = root / "reader-core.sqlite"
        timeline_db = root / "timeline.sqlite"
        checkpoint = root / "event-backbone-state.json"

        filler = {
            "eventType": "perception_event",
            "perceptionEventId": "old-noise",
            "observedAt": "2026-04-28T21:00:00Z",
            "objects": [{"objectType": "label", "text": "Buscar", "confidence": 0.9}],
        }
        # Make a source bigger than the read window. The accepted messages live at the tail.
        append_jsonl(source_file, [filler] * 2500)
        append_jsonl(
            source_file,
            [
                make_perception_event("wa-1", "msedge", "cliente-1", "Cliente Uno", "msg-1", "Ya pague 25 usdt"),
                make_perception_event("wa-2", "chrome", "cliente-2", "Cliente Dos", "msg-2", "Precio para Xiaomi?"),
                make_perception_event("wa-3", "firefox", "cliente-3", "Cliente Tres", "msg-3", "Necesito liberar Samsung"),
            ],
        )

        state = run_reader_core_once(
            [source_file],
            visible_file,
            conversation_file,
            reader_state_file,
            report_file,
            reader_db,
            checkpoint_file=checkpoint,
            max_read_bytes=16 * 1024,
            limit=500,
        )
        assert state["status"] == "ok"
        assert state["ingested"]["sourceBytesRead"] < source_file.stat().st_size
        assert state["ingested"]["sourceSkippedBacklogBytes"] > 0
        assert state["ingested"]["newMessages"] == 3
        assert all(channel["readiness"]["freshRead"] for channel in state["channels"])
        assert not validate_contract(state, "reader_core_state")

        timeline_state = run_timeline_once(
            conversation_file,
            timeline_file,
            timeline_state_file,
            db_path=timeline_db,
            checkpoint_file=checkpoint,
            max_read_bytes=16 * 1024,
        )
        assert timeline_state["status"] == "ok"
        assert timeline_state["durable"]["sqliteIsTruth"] is True
        assert timeline_state["durable"]["storedMessages"] == 3
        assert timeline_state["ingested"]["timelines"] == 3
        assert not validate_contract(timeline_state, "timeline_state")

        repeated = run_reader_core_once(
            [source_file],
            visible_file,
            conversation_file,
            reader_state_file,
            report_file,
            reader_db,
            checkpoint_file=checkpoint,
            max_read_bytes=16 * 1024,
            limit=500,
        )
        assert repeated["ingested"]["sourceBytesRead"] == 0
        assert repeated["ingested"]["newMessages"] == 0

    print("event timeline backbone OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
