from __future__ import annotations

import json
import sys
from datetime import datetime, timezone, timedelta
from pathlib import Path
from tempfile import TemporaryDirectory


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.contracts import sample_event, validate_contract
from ariadgsm_agent.window_reality import run_window_reality_once


def utc_now(offset_seconds: int = 0) -> str:
    return (datetime.now(timezone.utc) + timedelta(seconds=offset_seconds)).isoformat().replace("+00:00", "Z")


def write_json(path: Path, value: dict) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")


def cabin_state(status: str = "ready") -> dict:
    now = utc_now()
    channels = []
    for channel_id, browser in (("wa-1", "msedge"), ("wa-2", "chrome"), ("wa-3", "firefox")):
        channels.append(
            {
                "channelId": channel_id,
                "browser": browser,
                "status": status.upper(),
                "isReady": status == "ready",
                "requiresHuman": False,
                "canLaunch": False,
                "detail": f"{browser}: WhatsApp Web visible y utilizable.",
                "evidence": [f"{browser}: WhatsApp"],
                "window": {
                    "processId": 100,
                    "processName": browser,
                    "title": "WhatsApp",
                    "zOrder": 1,
                    "bounds": {"left": 0, "top": 0, "width": 800, "height": 900},
                },
            }
        )
    return {
        "identityVersion": "cabin-window-identity-v1",
        "status": status,
        "summary": "wa-1:READY | wa-2:READY | wa-3:READY",
        "updatedAt": now,
        "ready": status == "ready",
        "requiresHuman": False,
        "canStartDegraded": True,
        "readyChannels": 3 if status == "ready" else 0,
        "expectedChannels": 3,
        "channels": channels,
    }


def reader_state() -> dict:
    return {
        "status": "ok",
        "engine": "ariadgsm_reader_core",
        "version": "0.9.8",
        "updatedAt": utc_now(),
        "ingested": {"newMessages": 3},
        "summary": {"latestRunMessages": 3},
        "channels": [
            {
                "channelId": "wa-1",
                "status": "fresh_messages_confirmed",
                "messageConfirmed": True,
                "latestAcceptedMessages": 1,
                "readiness": {"freshRead": True, "canRead": True, "canUnlockHands": True, "reason": "ok"},
                "latestMessages": [{"text": "Hola wa1"}],
            },
            {
                "channelId": "wa-2",
                "status": "fresh_messages_confirmed",
                "messageConfirmed": True,
                "latestAcceptedMessages": 1,
                "readiness": {"freshRead": True, "canRead": True, "canUnlockHands": True, "reason": "ok"},
                "latestMessages": [{"text": "Hola wa2"}],
            },
            {
                "channelId": "wa-3",
                "status": "fresh_messages_confirmed",
                "messageConfirmed": True,
                "latestAcceptedMessages": 1,
                "readiness": {"freshRead": True, "canRead": True, "canUnlockHands": True, "reason": "ok"},
                "latestMessages": [{"text": "Hola wa3"}],
            },
        ],
        "latestMessages": [
            {"channelId": "wa-1", "text": "Hola wa1"},
            {"channelId": "wa-2", "text": "Hola wa2"},
            {"channelId": "wa-3", "text": "Hola wa3"},
        ],
    }


def input_state(operator: bool = False) -> dict:
    return {
        "status": "ok" if not operator else "attention",
        "phase": "idle" if not operator else "operator_control",
        "operatorHasPriority": operator,
        "operatorIdleMs": 2000,
        "summary": "Mouse disponible." if not operator else "Bryams esta usando mouse o teclado.",
        "updatedAt": utc_now(),
    }


def hands_state() -> dict:
    return {
        "status": "ok",
        "actionsExecuted": 0,
        "actionsVerified": 0,
        "lastSummary": "Hands listo.",
        "updatedAt": utc_now(),
    }


def main() -> int:
    assert not validate_contract(sample_event("window_reality_state"), "window_reality_state")

    with TemporaryDirectory() as tmp:
        runtime = Path(tmp)
        write_json(runtime / "cabin-readiness.json", cabin_state())
        write_json(runtime / "reader-core-state.json", reader_state())
        write_json(runtime / "input-arbiter-state.json", input_state())
        write_json(runtime / "hands-state.json", hands_state())
        state = run_window_reality_once(runtime)
        assert state["status"] == "ok"
        assert state["summary"]["operationalChannels"] == 3
        assert state["summary"]["structuralReadyChannels"] == 3
        assert state["summary"]["actionReadyChannels"] == 3
        assert state["summary"]["handsMayActChannels"] == 3
        assert all(item["status"] == "READY" for item in state["channels"])
        assert all(item["actionReady"] is True for item in state["channels"])
        assert not validate_contract(state, "window_reality_state")

    with TemporaryDirectory() as tmp:
        runtime = Path(tmp)
        stale_hands = hands_state()
        stale_hands["updatedAt"] = utc_now(-300)
        write_json(runtime / "cabin-readiness.json", cabin_state())
        write_json(runtime / "reader-core-state.json", reader_state())
        write_json(runtime / "input-arbiter-state.json", input_state())
        write_json(runtime / "hands-state.json", stale_hands)
        state = run_window_reality_once(runtime)
        assert state["status"] == "ok"
        assert state["summary"]["staleInputs"] == 0
        assert state["summary"]["operationalChannels"] == 3
        assert state["summary"]["handsMayActChannels"] == 3
        assert all(item["status"] == "READY" for item in state["channels"])
        hands_input = next(item for item in state["inputs"] if item["file"] == "hands-state.json")
        assert hands_input["role"] == "telemetry_only"
        assert hands_input["blocksReality"] is False
        assert hands_input["freshness"]["fresh"] is False
        assert not validate_contract(state, "window_reality_state")

    with TemporaryDirectory() as tmp:
        runtime = Path(tmp)
        stale = cabin_state()
        stale["updatedAt"] = utc_now(-120)
        write_json(runtime / "cabin-readiness.json", stale)
        write_json(runtime / "reader-core-state.json", reader_state())
        write_json(runtime / "input-arbiter-state.json", input_state())
        write_json(runtime / "hands-state.json", hands_state())
        state = run_window_reality_once(runtime)
        assert state["status"] == "blocked"
        assert state["summary"]["staleInputs"] >= 1
        assert all(item["status"] == "STALE_STATE" for item in state["channels"])
        assert not validate_contract(state, "window_reality_state")

    with TemporaryDirectory() as tmp:
        runtime = Path(tmp)
        covered = cabin_state()
        covered["status"] = "degraded"
        covered["channels"][1]["status"] = "COVERED_BY_WINDOW"
        covered["channels"][1]["isReady"] = False
        covered["channels"][1]["requiresHuman"] = True
        covered["channels"][1]["detail"] = "chrome WhatsApp cubierto por otra ventana."
        write_json(runtime / "cabin-readiness.json", covered)
        write_json(runtime / "reader-core-state.json", reader_state())
        write_json(runtime / "input-arbiter-state.json", input_state())
        write_json(runtime / "hands-state.json", hands_state())
        state = run_window_reality_once(runtime)
        wa2 = next(item for item in state["channels"] if item["channelId"] == "wa-2")
        assert wa2["status"] == "READY_WITH_CONFLICT"
        assert wa2["isOperational"] is True
        assert wa2["handsMayAct"] is False
        assert state["status"] == "attention"
        assert not validate_contract(state, "window_reality_state")

    with TemporaryDirectory() as tmp:
        runtime = Path(tmp)
        write_json(runtime / "cabin-readiness.json", cabin_state())
        write_json(runtime / "reader-core-state.json", reader_state())
        write_json(runtime / "input-arbiter-state.json", input_state(operator=True))
        write_json(runtime / "hands-state.json", hands_state())
        state = run_window_reality_once(runtime)
        assert state["status"] == "ok"
        assert all(item["status"] == "READY_OPERATOR_BUSY" for item in state["channels"])
        assert all(item["structuralReady"] is True for item in state["channels"])
        assert all(item["actionReady"] is False for item in state["channels"])
        assert state["summary"]["handsMayActChannels"] == 0
        assert not validate_contract(state, "window_reality_state")

    with TemporaryDirectory() as tmp:
        runtime = Path(tmp)
        reader = reader_state()
        reader["channels"][1]["status"] = "no_fresh_read"
        reader["channels"][1]["messageConfirmed"] = False
        reader["channels"][1]["latestAcceptedMessages"] = 0
        reader["channels"][1]["readiness"] = {
            "freshRead": False,
            "canRead": False,
            "canUnlockHands": False,
            "reason": "Tengo ventana, pero no mensajes frescos.",
        }
        reader["channels"][1]["latestMessages"] = []
        reader["latestMessages"] = [
            {"channelId": "wa-1", "text": "Hola wa1"},
            {"channelId": "wa-3", "text": "Hola wa3"},
        ]
        write_json(runtime / "cabin-readiness.json", cabin_state())
        write_json(runtime / "reader-core-state.json", reader)
        write_json(runtime / "input-arbiter-state.json", input_state())
        write_json(runtime / "hands-state.json", hands_state())
        state = run_window_reality_once(runtime)
        wa2 = next(item for item in state["channels"] if item["channelId"] == "wa-2")
        assert wa2["status"] == "READY_PENDING_READER"
        assert wa2["structuralReady"] is True
        assert wa2["semanticFresh"] is False
        assert wa2["handsMayAct"] is False
        assert state["summary"]["handsMayActChannels"] == 2
        assert not validate_contract(state, "window_reality_state")

    print("window reality resolver OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
