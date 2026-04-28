from __future__ import annotations

import json
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.contracts import sample_event, validate_contract
from ariadgsm_agent.runtime_kernel import build_runtime_kernel_state, write_state


def write_json(path: Path, value: dict) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False), encoding="utf-8")


def main() -> int:
    sample = sample_event("runtime_kernel_state")
    assert not validate_contract(sample, "runtime_kernel_state")

    with TemporaryDirectory() as tmp:
        runtime = Path(tmp)
        write_json(
            runtime / "life-controller-state.json",
            {
                "status": "running",
                "desiredRunning": True,
                "isRunning": True,
                "updatedAt": "2026-04-28T10:00:00Z",
            },
        )
        write_json(
            runtime / "agent-supervisor-state.json",
            {
                "status": "ok",
                "supervisorRunning": True,
                "coreLoopRunning": True,
                "restartCount": 1,
                "workers": [
                    {"name": "Vision", "running": True},
                    {"name": "Perception", "running": True},
                    {"name": "Hands", "running": True},
                ],
            },
        )
        write_json(
            runtime / "vision-health.json",
            {
                "status": "ok",
                "updatedAt": "2026-04-28T10:00:00Z",
                "framesCaptured": 3,
                "summary": "Vision sigue capturando.",
            },
        )
        write_json(
            runtime / "cabin-authority-state.json",
            {"status": "ok", "updatedAt": "2026-04-28T10:00:00Z", "summary": "Cabina lista."},
        )
        write_json(
            runtime / "input-arbiter-state.json",
            {
                "status": "attention",
                "phase": "operator_control",
                "operatorHasPriority": True,
                "summary": "Bryams esta usando mouse.",
            },
        )
        write_json(
            runtime / "trust-safety-state.json",
            {"status": "ok", "permissionGate": {"decision": "ALLOW"}},
        )
        (runtime / "windows-app.log").write_text(
            "2026-04-28 Vision error: System.UnauthorizedAccessException: Access to the path is denied.\n"
            "2026-04-28 Vision was not running. Restarting from reliability supervisor.\n",
            encoding="utf-8",
        )

        state = build_runtime_kernel_state(runtime, desired_running=True, is_running=True)
        errors = validate_contract(state, "runtime_kernel_state")
        assert not errors, errors
        assert state["engine"] == "ariadgsm_runtime_kernel"
        assert state["authority"]["operatorHasPriority"] is True
        assert state["authority"]["canAct"] is False
        assert state["summary"]["incidentsOpen"] >= 2
        assert {incident["code"] for incident in state["incidents"]} >= {
            "state_write_denied",
            "engine_restart",
        }

        write_state(runtime, state)
        assert (runtime / "runtime-kernel-state.json").exists()
        assert (runtime / "runtime-kernel-report.json").exists()

    print("runtime kernel OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
