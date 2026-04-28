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
    state = sample_event("cabin_authority_state")
    errors = validate_contract(state, "cabin_authority_state")
    assert not errors, errors

    runtime = read_text(REPO / "desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/AgentRuntime.cs")
    locator = read_text(REPO / "desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/AgentRuntime.SessionLocator.cs")
    guardian = read_text(REPO / "desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/AgentRuntime.WorkspaceGuardian.cs")
    config = read_text(REPO / "desktop-agent/perception-engine/config/perception.example.json")

    assert "UseShellExecute = false" in runtime
    assert "WhatsAppWebUrl" in runtime
    assert "UseShellExecute = true" not in runtime[runtime.index("private bool LaunchWhatsAppWindow") : runtime.index("private ChannelReadiness EvaluateChannelReadiness")]
    assert "BrowserProfilePinningEnabled()" in runtime
    assert '"profileDirectory": "Profile 1"' not in config
    assert '"profileDirectory": "Profile 2"' not in config

    assert "ControlType.TabItem" in locator
    assert "ControlType.Button" not in locator
    assert "ControlType.ListItem" not in locator
    assert "ControlType.Custom" not in locator
    assert "cerrar" in locator and "close" in locator

    assert 'CabinAuthorityCanModifyWindows("loop")' not in guardian
    assert 'reason.Equals("loop"' not in guardian
    assert '"handsMayArrangeWindows"] = false' in guardian
    assert '"shellUrlLaunchAllowed"] = false' in guardian
    assert '"TabItem"' in guardian

    assert "process.Kill(entireProcessTree: true)" in runtime
    assert '"msedge"' not in runtime[runtime.index("private void StopExternalWorkerProcesses") : runtime.index("private void RecoverExpectedWorkers")]
    assert '"chrome"' not in runtime[runtime.index("private void StopExternalWorkerProcesses") : runtime.index("private void RecoverExpectedWorkers")]
    assert '"firefox"' not in runtime[runtime.index("private void StopExternalWorkerProcesses") : runtime.index("private void RecoverExpectedWorkers")]

    print("cabin authority final OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
