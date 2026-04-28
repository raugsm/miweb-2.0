from __future__ import annotations

from pathlib import Path

REPO = Path(__file__).resolve().parents[2]


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def main() -> int:
    doc = read_text(REPO / "docs/ARIADGSM_HANDS_ENGINE_ADVANCED_DESIGN.md")
    pipeline = read_text(REPO / "desktop-agent/hands-engine/src/AriadGSM.Hands.Core/Pipeline/HandsPipeline.cs")
    executor = read_text(REPO / "desktop-agent/hands-engine/src/AriadGSM.Hands.Core/Execution/Win32HandsExecutor.cs")
    verifier = read_text(REPO / "desktop-agent/hands-engine/src/AriadGSM.Hands.Core/Verification/ActionVerifier.cs")
    arbiter = read_text(REPO / "desktop-agent/hands-engine/src/AriadGSM.Hands.Core/Input/InputArbiter.cs")
    options = read_text(REPO / "desktop-agent/hands-engine/src/AriadGSM.Hands.Core/Config/HandsOptions.cs")

    assert "abrir un chat visible" in doc.lower()
    assert "ceder el mouse" in doc.lower()
    assert "antes de continuar" in doc.lower()
    assert "sendinput" in doc.lower()
    assert "getlastinputinfo" in doc.lower()

    assert "SendInput" in executor
    assert "mouse_event" not in executor
    assert "SetForegroundWindow" in executor

    assert "VerifyAfterExecutionAsync" in pipeline
    assert "ShouldWaitForFreshOpenChatVerification" in pipeline
    assert "FinalActionStatus" in pipeline
    assert 'plan.ActionType.Equals("open_chat"' in pipeline
    assert 'return "failed";' in pipeline
    assert "verifiedBeforeContinue" in pipeline
    assert "verificationPerceptionEventId" in pipeline

    assert "VerifyOpenChat" in verifier
    assert "TitlesMatch" in verifier
    assert "No continuo con acciones dependientes" in verifier

    assert "OpenChatVerificationTimeoutMs" in options
    assert "OpenChatVerificationPollMs" in options

    assert "GetLastInputInfo" in arbiter
    assert "handsPausedOnly" in arbiter
    assert "eyesContinue" in arbiter
    assert "memoryContinue" in arbiter
    assert "cognitiveContinue" in arbiter

    print("hands engine advanced OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
