from __future__ import annotations

import json
import sys
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.cloud_sync import run_cloud_sync_once
from ariadgsm_agent.contracts import sample_event, validate_contract


def write_json(path: Path, value: dict) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False), encoding="utf-8")


def write_jsonl(path: Path, rows: list[dict]) -> None:
    path.write_text(
        "".join(json.dumps(row, ensure_ascii=False) + "\n" for row in rows),
        encoding="utf-8",
    )


def prepare_runtime(runtime: Path) -> None:
    write_json(
        runtime / "runtime-kernel-state.json",
        {
            "status": "ok",
            "updatedAt": "2026-04-28T10:00:00Z",
            "authority": {
                "canObserve": True,
                "canThink": True,
                "canAct": False,
                "canSync": True,
                "operatorHasPriority": False,
                "mainBlocker": "",
            },
            "summary": {"enginesRunning": 20, "enginesTotal": 22, "incidentsOpen": 0},
        },
    )
    write_json(
        runtime / "cabin-readiness.json",
        {
            "updatedAt": "2026-04-28T10:00:00Z",
            "channels": [
                {"channelId": "wa-1", "status": "ready", "detail": "Edge listo."},
                {"channelId": "wa-2", "status": "ready", "detail": "Chrome listo."},
                {"channelId": "wa-3", "status": "ready", "detail": "Firefox listo."},
            ],
        },
    )
    write_jsonl(
        runtime / "timeline-events.jsonl",
        [
            {
                "conversationEventId": "timeline-wa-2-001",
                "conversationId": "wa-2-cliente-precio",
                "channelId": "wa-2",
                "conversationTitle": "Cliente Precio",
                "messages": [
                    {
                        "messageId": "msg-1",
                        "text": "Cuanto vale liberar Samsung en Mexico?",
                        "direction": "client",
                        "sentAt": "2026-04-28T09:59:00Z",
                    },
                    {
                        "messageId": "msg-ui",
                        "text": "Anadir esta pagina a marcadores Ctrl+D",
                        "direction": "unknown",
                        "sentAt": "2026-04-28T10:00:00Z",
                    },
                ],
            }
        ],
    )
    write_jsonl(
        runtime / "domain-events.jsonl",
        [
            {
                "eventId": "domain-review-1",
                "eventType": "PaymentDrafted",
                "summary": "Cliente menciona pago sin comprobante visible.",
                "requiresHumanReview": True,
                "confidence": 0.82,
                "privacy": {
                    "cloudAllowed": True,
                    "redactionRequired": False,
                    "classification": "internal",
                },
                "risk": {"riskLevel": "medium"},
            }
        ],
    )


class CaptureHandler(BaseHTTPRequestHandler):
    requests: list[dict] = []

    def log_message(self, format: str, *args: object) -> None:
        return

    def do_POST(self) -> None:
        length = int(self.headers.get("Content-Length", "0"))
        body = self.rfile.read(length).decode("utf-8")
        CaptureHandler.requests.append(
            {
                "path": self.path,
                "authorization": self.headers.get("Authorization", ""),
                "idempotency": self.headers.get("Idempotency-Key", ""),
                "payload": json.loads(body),
            }
        )
        response = json.dumps({"ok": True, "batch": {"id": "server-batch"}}).encode("utf-8")
        self.send_response(201)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(response)))
        self.end_headers()
        self.wfile.write(response)


def main() -> int:
    sample = sample_event("cloud_sync_state")
    assert not validate_contract(sample, "cloud_sync_state")

    with TemporaryDirectory() as tmp:
        runtime = Path(tmp)
        prepare_runtime(runtime)
        state = run_cloud_sync_once(
            runtime,
            repo_root=REPO,
            cloud_url="http://127.0.0.1:9",
            token="test-token",
            token_source="test",
            enabled=True,
            dry_run=True,
        )
        errors = validate_contract(state, "cloud_sync_state")
        assert not errors, errors
        assert state["status"] == "dry_run"
        assert state["policy"]["rawFramesUploaded"] is False
        payload = json.loads((runtime / "cloud-sync-payload.json").read_text(encoding="utf-8"))
        event_types = [event["type"] for event in payload["events"]]
        assert "agent_status" in event_types
        assert "checkpoint" in event_types
        assert "whatsapp_message" in event_types
        assert "review_item" in event_types
        message_texts = [
            event["data"].get("text", "")
            for event in payload["events"]
            if event["type"] == "whatsapp_message"
        ]
        assert "Cuanto vale liberar Samsung en Mexico?" in message_texts
        assert all("marcadores" not in text.lower() for text in message_texts)

        CaptureHandler.requests = []
        server = HTTPServer(("127.0.0.1", 0), CaptureHandler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            url = f"http://127.0.0.1:{server.server_address[1]}"
            posted = run_cloud_sync_once(
                runtime,
                repo_root=REPO,
                cloud_url=url,
                token="test-token",
                token_source="test",
                enabled=True,
                dry_run=False,
                timeout=3,
            )
        finally:
            server.shutdown()
            thread.join(timeout=3)

        assert posted["status"] == "ok"
        assert posted["batch"]["responseStatusCode"] == 201
        assert CaptureHandler.requests
        captured = CaptureHandler.requests[0]
        assert captured["path"] == "/api/operativa-v2/cloud/sync"
        assert captured["authorization"] == "Bearer test-token"
        assert captured["idempotency"].startswith("cloudsync-")
        assert captured["payload"]["security"]["rawFramesUploaded"] is False
        assert (runtime / "cloud-sync-ledger.json").exists()

    print("cloud sync OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
