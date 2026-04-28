from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.contracts import sample_event, validate_contract


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def main() -> int:
    sample = sample_event("control_plane_state")
    errors = validate_contract(sample, "control_plane_state")
    assert not errors, errors

    control_plane_source = read_text(
        REPO / "desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/AgentRuntime.ControlPlane.cs"
    )
    runtime_source = read_text(REPO / "desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/AgentRuntime.cs")
    ui_source = read_text(REPO / "desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/MainForm.cs")
    kernel_source = read_text(
        REPO / "desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/AgentRuntime.RuntimeKernel.cs"
    )

    for phase in [
        "operator_authorized",
        "update_check",
        "workspace_bootstrap",
        "preflight",
        "runtime_governor",
        "workspace_guardian",
        "workers",
        "python_core",
        "supervisor",
        "readiness",
    ]:
        assert phase in control_plane_source, phase

    assert "runSessionId" in control_plane_source
    assert "control-plane-command-ledger.jsonl" in control_plane_source
    assert "control-plane-checkpoints.jsonl" in control_plane_source
    assert "RequestControlPlaneStart" in control_plane_source
    assert "BuildControlPlaneReadiness" in control_plane_source
    assert '"read"' in control_plane_source
    assert '"think"' in control_plane_source
    assert '"act"' in control_plane_source
    assert "lastStopCause" in control_plane_source

    assert 'Stop("operator_button", "ui.pause_button")' in ui_source
    assert 'Stop("app_closing", "ui.form_closing")' in ui_source
    assert "RequestControlPlaneStart" in ui_source
    assert 'Stop("dispose", "runtime.dispose")' in runtime_source
    assert 'Stop("prepare_whatsapps", "ui.prepare_whatsapps_button")' in ui_source
    assert "MarkBootPhase" in runtime_source
    assert "EnsureStartSession" in runtime_source
    assert '"sessionTruthSource"] = "control-plane-state.json"' in kernel_source
    assert '"canRead"] = canRead' in kernel_source

    print("ai runtime control plane OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
