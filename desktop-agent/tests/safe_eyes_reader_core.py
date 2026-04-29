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
        csharp_perception_event = {
            "eventType": "perception_event",
            "perceptionEventId": "perception-csharp-001",
            "observedAt": "2026-04-27T18:00:02Z",
            "sourceVisionEventId": "vision-002",
            "objects": [
                {
                    "objectType": "window",
                    "confidence": 0.95,
                    "text": "(12) WhatsApp Business - Microsoft Edge",
                    "role": "whatsapp_web",
                    "metadata": {
                        "channelId": "wa-1",
                        "processName": "msedge",
                        "browserProcess": "msedge",
                        "windowTitle": "(12) WhatsApp Business - Microsoft Edge",
                    },
                },
                {
                    "objectType": "conversation",
                    "confidence": 0.91,
                    "text": "Cliente Uno",
                    "role": "active_conversation",
                    "metadata": {
                        "channelId": "wa-1",
                        "conversationId": "cliente-pe-1",
                        "conversationTitle": "Cliente Uno",
                        "browserProcess": "msedge",
                        "windowTitle": "(12) WhatsApp Business - Microsoft Edge",
                    },
                },
                {
                    "objectType": "message_bubble",
                    "confidence": 0.88,
                    "text": "Ya pague 25 usdt para liberar Samsung",
                    "role": "client",
                    "metadata": {
                        "channelId": "wa-1",
                        "messageKey": "msg-payment-csharp-1",
                        "conversationId": "cliente-pe-1",
                        "conversationTitle": "Cliente Uno",
                        "browserProcess": "msedge",
                        "windowTitle": "(12) WhatsApp Business - Microsoft Edge",
                        "sourceKind": "windows_accessibility",
                    },
                },
            ],
        }
        write_jsonl(perception_file, [perception_event, csharp_perception_event])

        state = run_reader_core_once(
            [source_file, perception_file],
            visible_file,
            conversation_file,
            state_file,
            report_file,
            db_file,
        )

        assert state["status"] == "ok"
        assert state["ingested"]["candidateMessages"] == 4
        assert state["ingested"]["newMessages"] == 2
        assert state["ingested"]["rejected"] == 2
        assert state["summary"]["structuredSourceMessages"] == 2
        assert state["summary"]["ocrFallbackMessages"] == 0
        assert state["summary"]["latestRunDisagreements"] == 1
        assert state["channels"][0]["channelId"] == "wa-1"
        assert state["channels"][0]["readiness"]["freshRead"] is True
        assert state["channels"][1]["channelId"] == "wa-2"
        assert state["channels"][1]["readiness"]["freshRead"] is True
        assert state["channels"][2]["channelId"] == "wa-3"
        assert state["channels"][2]["readiness"]["freshRead"] is False
        assert not validate_contract(state, "reader_core_state")

        visible = [json.loads(line) for line in visible_file.read_text(encoding="utf-8").splitlines()]
        assert len(visible) == 2
        message = next(item for item in visible if item["channelId"] == "wa-2")
        assert message["channelId"] == "wa-2"
        assert message["browserProcess"] == "chrome"
        assert message["source"]["kind"] == "dom"
        assert message["identity"]["isWhatsAppWeb"]
        assert message["disagreements"]
        assert any(signal["kind"] == "price_request" for signal in message["signals"])
        assert any(signal["kind"] == "country" and signal["value"] == "MX" for signal in message["signals"])
        assert not validate_contract(message, "visible_message")
        csharp_message = next(item for item in visible if item["channelId"] == "wa-1")
        assert csharp_message["browserProcess"] == "msedge"
        assert csharp_message["source"]["kind"] == "accessibility"
        assert csharp_message["identity"]["identitySource"] == "window_title:whatsapp_browser"

        conversations = [json.loads(line) for line in conversation_file.read_text(encoding="utf-8").splitlines()]
        assert len(conversations) == 2
        assert all(event["eventType"] == "conversation_event" for event in conversations)
        assert any(event["messages"][0]["sourceKind"] == "dom" for event in conversations)
        assert any(event["messages"][0]["sourceKind"] == "accessibility" for event in conversations)
        assert all(not validate_contract(event, "conversation_event") for event in conversations)

        repeated = run_reader_core_once(
            [source_file, perception_file],
            visible_file,
            conversation_file,
            state_file,
            report_file,
            db_file,
        )
        assert repeated["ingested"]["newMessages"] == 0
        assert repeated["ingested"]["duplicates"] == 0
        assert repeated["ingested"]["sourceBytesRead"] == 0
        assert repeated["summary"]["storedMessages"] == 2

    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        perception_file = root / "perception-events.jsonl"
        visible_file = root / "reader-visible-messages.jsonl"
        conversation_file = root / "conversation-events.jsonl"
        state_file = root / "reader-core-state.json"
        report_file = root / "reader-core-report.json"
        db_file = root / "reader-core.sqlite"
        objects = []
        for channel_id, browser, title in [
            ("wa-1", "msedge", "Cliente Edge"),
            ("wa-2", "chrome", "Cliente Chrome"),
            ("wa-3", "firefox", "Cliente Firefox"),
        ]:
            objects.append(
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
                }
            )
            objects.append(
                {
                    "objectType": "conversation",
                    "confidence": 0.95,
                    "text": title,
                    "role": "active_conversation",
                    "metadata": {
                        "channelId": channel_id,
                        "conversationId": f"{channel_id}-cliente",
                        "conversationTitle": title,
                        "browserProcess": browser,
                        "windowTitle": f"WhatsApp Business - {browser}",
                    },
                }
            )
            for index in range(80):
                objects.append(
                    {
                        "objectType": "message_bubble",
                        "confidence": 0.91,
                        "text": f"{title} mensaje util {index}",
                        "role": "client",
                        "metadata": {
                            "channelId": channel_id,
                            "messageKey": f"{channel_id}-msg-{index}",
                            "conversationId": f"{channel_id}-cliente",
                            "conversationTitle": title,
                            "browserProcess": browser,
                            "windowTitle": f"WhatsApp Business - {browser}",
                            "sourceKind": "windows_accessibility",
                        },
                    }
                )
        write_jsonl(
            perception_file,
            [
                {
                    "eventType": "perception_event",
                    "perceptionEventId": "perception-three-channel-heavy",
                    "observedAt": "2026-04-27T18:05:00Z",
                    "sourceVisionEventId": "vision-three-channel-heavy",
                    "objects": objects,
                }
            ],
        )
        state = run_reader_core_once(
            [perception_file],
            visible_file,
            conversation_file,
            state_file,
            report_file,
            db_file,
        )
        assert state["channels"][0]["readiness"]["freshRead"] is True, state
        assert state["channels"][1]["readiness"]["freshRead"] is True, state
        assert state["channels"][2]["readiness"]["freshRead"] is True, state
        assert state["ingested"]["candidateMessages"] == 120, state

    print("safe eyes reader core OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
