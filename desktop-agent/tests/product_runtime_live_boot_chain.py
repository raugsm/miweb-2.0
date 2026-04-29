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
    assert not validate_contract(sample_event("window_lifecycle_event"), "window_lifecycle_event")

    runtime = read_text(REPO / "desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/AgentRuntime.cs")
    guardian = read_text(REPO / "desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/AgentRuntime.WorkspaceGuardian.cs")
    hands = read_text(REPO / "desktop-agent/hands-engine/src/AriadGSM.Hands.Core/Pipeline/HandsPipeline.cs")
    governor = read_text(REPO / "desktop-agent/ariadgsm_agent/runtime_governor.py")
    docs = read_text(REPO / "docs/ARIADGSM_PRODUCT_RUNTIME_PACKAGING_LIVE_BOOT_CHAIN.md")

    assert "StartTrustSafetyHeartbeatLoop" in runtime
    assert "StopTrustSafetyHeartbeatLoop" in runtime
    assert "TrustSafetyHeartbeatInterval" in runtime
    assert "TrustSafetyHeartbeat" in runtime
    assert "RunTrustSafetyProcessToExitAsync" in runtime
    assert "StartLiveReadinessLoop" in runtime
    assert "PrimeLiveReadinessAsync" in runtime
    assert "RunLiveReadinessSequenceAsync" in runtime
    assert "RunLiveReadinessProcessToExitAsync" in runtime
    assert "LiveReadinessInterval" in runtime
    assert "_trustSafetyProcessGate" in runtime
    assert "_liveReadinessProcessGate" in runtime
    assert "WaitForLoopStop(_liveReadinessTask" in runtime
    assert "WaitForLoopStop(_coreLoopTask" in runtime
    assert 'StopExternalWorkerProcesses("shutdown")' in runtime
    assert "StopProcessTree(process, name, \"cancelled\")" in runtime
    assert "process.Kill(entireProcessTree: true)" in runtime
    assert '"msedge"' not in runtime[runtime.index("private void StopExternalWorkerProcesses") : runtime.index("private void RecoverExpectedWorkers")]
    assert '"chrome"' not in runtime[runtime.index("private void StopExternalWorkerProcesses") : runtime.index("private void RecoverExpectedWorkers")]
    assert '"firefox"' not in runtime[runtime.index("private void StopExternalWorkerProcesses") : runtime.index("private void RecoverExpectedWorkers")]

    assert "window-lifecycle-events.jsonl" in guardian
    assert "TrackWindowLifecycle" in guardian
    for event_type in [
        "channel_window_lost",
        "channel_window_restored",
        "channel_window_changed",
        "channel_window_covered",
        "channel_window_uncovered",
    ]:
        assert event_type in guardian

    assert "UnauthorizedAccessException" in hands
    assert "TryDeleteTempFile" in hands
    assert "Console.Error.WriteLine" in hands
    assert "File.Delete(tempPath)" not in hands[hands.index("private static async ValueTask WriteTextAtomicAsync") :]

    assert 'encoding="utf-8"' in governor
    assert 'errors="replace"' in governor

    for phrase in [
        "Safe State Writer",
        "Process Ownership Shutdown",
        "Window Lifecycle Tracking",
        "Trust & Safety Heartbeat",
        "End-to-End Boot/Shutdown Evaluation",
    ]:
        assert phrase in docs, phrase

    print("product runtime live boot chain OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
