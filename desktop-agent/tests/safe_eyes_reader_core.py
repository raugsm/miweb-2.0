from __future__ import annotations

import json
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.contracts import sample_event, validate_contract
from ariadgsm_agent.reader_core import run_reader_core_once


def write_jsonl(path: Path, events: list[dict]) -> None:
    path.write_text(
        "\n".join(json.dumps(event, ensure_ascii=False) for event in events) + "\n",
        encoding="utf-8",
    )


def main() -> int:
    assert not validate_contract(sample_event("visible_message"), "visible_message")
    assert not validate_contract(sample_event("reader_core_state"), "reader_core_state")

    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        source_file = root / "reader-source-events.jsonl"
        perception_file = root / "perception-events.jsonl"
        visible_file = root / "reader-visible-messages.jsonl"
        conversation_file = root / "conversation-events.jsonl"
        state_file = root / "reader-core-state.json"
        report_file = root / "reader-core-report.json"
        db_file = root / "reader-core.sqlite"

        structured_event = {
            "sourceEventId": "chrome-dom-001",
            "sourceKind": "dom",
            "browserProcess": "chrome",
            "url": "https://web.whatsapp.com/",
            "windowTitle": "WhatsApp - Google Chrome",
            "conversationId": "cliente-mx-1",
            "conversationTitle": "Omar Torres Colombia",
            "observedAt": "2026-04-27T18:00:00Z",
            "messages": [
                {
                    "messageKey": "msg-price-1",
                    "text": "Cuanto vale liberar Samsung en Mexico?",
                    "direction": "client",
                    "senderName": "Omar",
                    "sentAt": "2026-04-27T18:00:00Z",
                    "confidence": 0.96,
                    "selector": "[data-pre-plain-text]",
                },
                {
                    "messageKey": "noise-1",
                    "text": "Foto",
                    "direction": "unknown",
                    "confidence": 0.9,
                },
            ],
        }
        accessibility_event = {
            "sourceEventId": "chrome-ax-001",
            "sourceKind": "accessibility",
            "browserProcess": "chrome",
            "url": "https://web.whatsapp.com/",
            "windowTitle": "WhatsApp - Google Chrome",
            "conversationId": "cliente-mx-1",
            "conversationTitle": "Omar Torres Colombia",
            "messages": [
                {
                    "messageKey": "msg-price-1",
                    "text": "Cuanto vale liberar Samsung en Mexico?",
                    "direction": "client",
                    "confidence": 0.92,
                    "automationId": "ax-message-1",
                }
            ],
        }
        non_whatsapp_event = {
            "sourceEventId": "codex-ui-001",
            "sourceKind": "uia",
            "browserProcess": "chrome",
            "url": "https://chatgpt.com/",
            "windowTitle": "Codex",
            "conversationTitle": "Codex",
            "messages": [{"messageKey": "bad-1", "text": "Buscar", "confidence": 0.8}],
        }
        write_jsonl(source_file, [structured_event, accessibility_event, non_whatsapp_event])

        perception_event = {
            "eventType": "perception_event",
            "perceptionEventId": "perception-ocr-001",
            "observedAt": "2026-04-27T18:00:01Z",
            "sourceVisionEventId": "vision-001",
            "channelId": "wa-2",
            "browserProcess": "chrome",
            "url": "https://web.whatsapp.com/",
            "windowTitle": "WhatsApp - Google Chrome",
            "conversationId": "cliente-mx-1",
            "conversationTitle": "Omar Torres Colombia",
            "objects": [
                {
                    "objectType": "message_bubble",
                    "confidence": 0.72,
                    "text": "Cuanto vale liberar Samsun en Mexico?",
                    "role": "client",
                    "metadata": {
                        "messageKey": "msg-price-1",
                        "browserProcess": "chrome",
                        "url": "https://web.whatsapp.com/",
                        "windowTitle": "WhatsApp - Google Chrome",
                    },
                }
            ],
        }
        write_jsonl(perception_file, [perception_event])

        state = run_reader_core_once(
            [source_file, perception_file],
            visible_file,
            conversation_file,
            state_file,
            report_file,
            db_file,
        )

        assert state["status"] == "ok"
        assert state["ingested"]["candidateMessages"] == 3
        assert state["ingested"]["newMessages"] == 1
        assert state["ingested"]["rejected"] == 2
        assert state["summary"]["structuredSourceMessages"] == 1
        assert state["summary"]["ocrFallbackMessages"] == 0
        assert state["summary"]["latestRunDisagreements"] == 1
        assert not validate_contract(state, "reader_core_state")

        visible = [json.loads(line) for line in visible_file.read_text(encoding="utf-8").splitlines()]
        assert len(visible) == 1
        message = visible[0]
        assert message["channelId"] == "wa-2"
        assert message["browserProcess"] == "chrome"
        assert message["source"]["kind"] == "dom"
        assert message["identity"]["isWhatsAppWeb"]
        assert message["disagreements"]
        assert any(signal["kind"] == "price_request" for signal in message["signals"])
        assert any(signal["kind"] == "country" and signal["value"] == "MX" for signal in message["signals"])
        assert not validate_contract(message, "visible_message")

        conversations = [json.loads(line) for line in conversation_file.read_text(encoding="utf-8").splitlines()]
        assert len(conversations) == 1
        assert conversations[0]["eventType"] == "conversation_event"
        assert conversations[0]["messages"][0]["sourceKind"] == "dom"
        assert not validate_contract(conversations[0], "conversation_event")

        repeated = run_reader_core_once(
            [source_file, perception_file],
            visible_file,
            conversation_file,
            state_file,
            report_file,
            db_file,
        )
        assert repeated["ingested"]["newMessages"] == 0
        assert repeated["ingested"]["duplicates"] == 1

    print("safe eyes reader core OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
