from __future__ import annotations

import json
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.autonomous_cycle import run_autonomous_cycle_once
from ariadgsm_agent.contracts import sample_event, validate_contract
from ariadgsm_agent.domain_events import adapt_engine_event


def write_json(path: Path, value: dict) -> None:
    path.write_text(json.dumps(value, ensure_ascii=False), encoding="utf-8")


def seed_runtime(root: Path, *, operator_control: bool = False, critical: bool = False) -> None:
    write_json(root / "vision-health.json", {"status": "ok", "visibleWindowCount": 3, "framesCaptured": 4})
    write_json(root / "perception-health.json", {"status": "ok", "messagesExtracted": 18, "conversationEventsWritten": 3})
    write_json(root / "interaction-state.json", {"status": "ok", "actionableTargets": 2, "targetsObserved": 5})
    write_json(
        root / "orchestrator-state.json",
        {
            "status": "ok",
            "phase": "ready",
            "summary": "3/3 canales listos.",
            "metrics": {
                "expectedChannels": 3,
                "cabinReadyChannels": 3,
                "visionWhatsAppWindows": 3,
                "perceptionChannels": 3,
                "actionableTargets": 2,
            },
            "recommendations": [],
            "blockers": [],
        },
    )
    write_json(
        root / "timeline-state.json",
        {
            "status": "ok",
            "historyLimitDays": 30,
            "ingested": {
                "messages": 18,
                "timelines": 3,
                "completeTimelines": 0,
                "rejectedEvents": 0,
            },
        },
    )
    write_json(
        root / "cognitive-state.json",
        {
            "status": "ok",
            "summary": {
                "decisions": 2,
                "clientProfiles": 1,
                "learningEvents": 2,
                "latestDecision": {
                    "intent": "price_request",
                    "confidence": 0.88,
                    "proposedAction": "open_chat",
                },
            },
        },
    )
    write_json(
        root / "operating-state.json",
        {"status": "ok", "summary": {"cases": 1, "openTasks": 1, "accountingDrafts": 1}},
    )
    case_manager_state = sample_event("case_manager_state")
    case_manager_state["status"] = "ok"
    case_manager_state["summary"]["needsHuman"] = 0
    case_manager_state["summary"]["openCases"] = 1
    case_manager_state["humanReport"]["necesitanBryams"] = []
    write_json(root / "case-manager-state.json", case_manager_state)
    channel_routing_state = sample_event("channel_routing_state")
    channel_routing_state["status"] = "ok"
    channel_routing_state["summary"]["proposedRoutes"] = 0
    channel_routing_state["summary"]["needsHuman"] = 0
    channel_routing_state["humanReport"]["rutasPropuestas"] = []
    channel_routing_state["humanReport"]["necesitanBryams"] = []
    write_json(root / "channel-routing-state.json", channel_routing_state)
    accounting_core_state = sample_event("accounting_core_state")
    accounting_core_state["status"] = "ok"
    accounting_core_state["summary"]["needsEvidence"] = 0
    accounting_core_state["summary"]["needsHuman"] = 0
    accounting_core_state["humanReport"]["necesitanBryams"] = []
    write_json(root / "accounting-core-state.json", accounting_core_state)
    write_json(
        root / "memory-state.json",
        {
            "status": "ok",
            "summary": {
                "memoryMessages": 18,
                "signals": 9,
                "learningEvents": 2,
                "knowledgeItems": 1,
                "accountingEvents": 1,
            },
        },
    )
    business_brain_state = sample_event("business_brain_state")
    business_brain_state["status"] = "ok"
    business_brain_state["summary"]["requiresHuman"] = 0
    business_brain_state["summary"]["recommendations"] = 1
    business_brain_state["humanReport"]["queNecesitoDeBryams"] = []
    write_json(root / "business-brain-state.json", business_brain_state)
    tool_registry_state = sample_event("tool_registry_state")
    tool_registry_state["status"] = "ok"
    tool_registry_state["summary"]["requestsRead"] = 1
    tool_registry_state["summary"]["matchedRequests"] = 1
    tool_registry_state["summary"]["plansNeedHuman"] = 0
    tool_registry_state["humanReport"]["queNecesitaBryams"] = []
    write_json(root / "tool-registry-state.json", tool_registry_state)
    trust_safety_state = sample_event("trust_safety_state")
    trust_safety_state["status"] = "blocked" if critical else "ok"
    trust_safety_state["permissionGate"]["decision"] = "BLOCK" if critical else "ALLOW"
    trust_safety_state["permissionGate"]["canHandsRun"] = not critical
    trust_safety_state["summary"]["blocked"] = 1 if critical else 0
    trust_safety_state["summary"]["critical"] = 1 if critical else 0
    trust_safety_state["summary"]["requiresHumanConfirmation"] = 0
    write_json(root / "trust-safety-state.json", trust_safety_state)
    write_json(
        root / "supervisor-state.json",
        {
            "status": "blocked" if critical else "ok",
            "summary": {
                "findings": 1 if critical else 0,
                "requiresHumanConfirmation": 0,
                "blocked": 1 if critical else 0,
                "critical": 1 if critical else 0,
                "safeNextActions": 1,
            },
            "latestFindings": [
                {
                    "severity": "critical",
                    "allowed": False,
                    "sourceId": "test-critical",
                    "reason": "Riesgo critico de prueba.",
                }
            ]
            if critical
            else [],
        },
    )
    write_json(
        root / "hands-state.json",
        {
            "status": "ok",
            "actionsPlanned": 1,
            "actionsWritten": 1,
            "actionsBlocked": 0,
            "actionsExecuted": 1,
            "actionsVerified": 1,
            "actionsSkipped": 0,
            "lastSummary": "open_chat: verified.",
        },
    )
    write_json(
        root / "input-arbiter-state.json",
        {
            "status": "ok",
            "phase": "operator_control" if operator_control else "ai_control",
            "summary": "Bryams esta usando el mouse." if operator_control else "Mouse disponible.",
            "operatorHasPriority": operator_control,
            "operatorIdleMs": 0 if operator_control else 2500,
            "requiredIdleMs": 1200,
        },
    )
    write_json(root / "life-controller-state.json", {"status": "ok", "phase": "engines_running", "summary": "Motores encendidos.", "desiredRunning": True, "isRunning": True})
    write_json(root / "status-bus-state.json", {"status": "ok", "phase": "ready", "summary": "IA lista."})
    write_json(root / "agent-supervisor-state.json", {"status": "ok", "restartCount": 0, "lastSummary": "Sin reinicios."})
    write_json(root / "update-state.json", {"status": "ok", "currentVersion": "0.8.0", "latestVersion": "0.8.0"})
    write_json(root / "workspace-setup-state.json", {"status": "ok", "phase": "ready", "summary": "Cabina lista."})
    write_json(
        root / "cabin-authority-state.json",
        {
            "status": "ok",
            "engine": "ariadgsm_cabin_authority",
            "phase": "ready",
            "summary": "Cabin Authority: las 3 columnas WhatsApp estan visibles y libres.",
            "reason": "test",
            "updatedAt": "2026-04-27T00:00:00Z",
            "exclusiveWindowControl": True,
            "handsMayFocus": True,
            "handsMayRecoverWindows": False,
            "handsMayArrangeWindows": False,
            "launchPolicy": {
                "mode": "explicit_browser_executable_only",
                "url": "https://web.whatsapp.com/",
                "profilePinningDefault": False,
                "shellUrlLaunchAllowed": False,
                "tabSelectionAllowedControlTypes": ["TabItem"],
            },
            "policy": [
                "Solo Cabin Authority puede acomodar o restaurar ventanas de navegador.",
                "El monitor en bucle solo observa; no minimiza ventanas del operador.",
                "Hands puede clicar solo en canales ready, visibles y sin bloqueadores.",
                "Si una ventana cubre WhatsApp, se reporta al operador en vez de cerrarla.",
            ],
            "channels": [
                {
                    "channelId": "wa-1",
                    "browserProcess": "msedge",
                    "status": "ready",
                    "handsMayAct": True,
                    "expectedBounds": {"left": 0, "top": 0, "width": 640, "height": 900},
                    "remainingBlockers": 0,
                },
                {
                    "channelId": "wa-2",
                    "browserProcess": "chrome",
                    "status": "ready",
                    "handsMayAct": True,
                    "expectedBounds": {"left": 640, "top": 0, "width": 640, "height": 900},
                    "remainingBlockers": 0,
                },
                {
                    "channelId": "wa-3",
                    "browserProcess": "firefox",
                    "status": "ready",
                    "handsMayAct": True,
                    "expectedBounds": {"left": 1280, "top": 0, "width": 640, "height": 900},
                    "remainingBlockers": 0,
                },
            ],
            "blockers": [],
            "actions": [],
        },
    )
    write_json(
        root / "cabin-manager-state.json",
        {
            "status": "ok",
            "phase": "ready",
            "summary": "3/3 WhatsApps listos.",
            "channels": [
                {"channelId": "wa-1", "status": "READY"},
                {"channelId": "wa-2", "status": "READY"},
                {"channelId": "wa-3", "status": "READY"},
            ],
        },
    )
    write_json(root / "workspace-guardian-state.json", {"status": "ok", "summary": "Cabina bajo control."})
    write_json(root / "cabin-readiness.json", {"status": "ok", "summary": "Cabina lista."})


def latest_jsonl(path: Path) -> dict:
    lines = path.read_text(encoding="utf-8").splitlines()
    return json.loads(lines[-1])


def test_autonomous_cycle_allow_gate() -> None:
    with TemporaryDirectory() as temporary:
        root = Path(temporary)
        seed_runtime(root)
        state = run_autonomous_cycle_once(root, trigger="start")
        assert state["status"] == "ok"
        assert state["phase"] == "start"
        assert state["loopContract"]["order"] == [
            "observe",
            "understand",
            "plan",
            "request_permission",
            "act",
            "verify",
            "learn",
            "report",
        ]
        assert [step["stepId"] for step in state["steps"]] == state["loopContract"]["order"]
        assert state["permissionGate"]["decision"] == "ALLOW"
        assert state["directives"]["allowedEngines"]["hands"] is True
        assert (root / "autonomous-cycle-directives.json").exists()
        assert (root / "autonomous-cycle-report.json").exists()
        event = latest_jsonl(root / "autonomous-cycle-events.jsonl")
        assert not validate_contract(event, "autonomous_cycle_event")
        domain_events = adapt_engine_event(event)
        assert domain_events and domain_events[0]["eventType"] == "CycleStarted"


def test_autonomous_cycle_pauses_for_operator_input() -> None:
    with TemporaryDirectory() as temporary:
        root = Path(temporary)
        seed_runtime(root, operator_control=True)
        state = run_autonomous_cycle_once(root)
        assert state["status"] == "attention"
        assert state["phase"] == "operator_control"
        assert state["permissionGate"]["decision"] == "PAUSE_FOR_OPERATOR"
        assert state["directives"]["allowedEngines"]["hands"] is False
        act_step = next(step for step in state["steps"] if step["stepId"] == "act")
        assert act_step["status"] == "attention"
        assert "Bryams" in state["humanReport"]["whatINeedFromBryams"][0]


def test_autonomous_cycle_blocks_critical_supervisor_findings() -> None:
    with TemporaryDirectory() as temporary:
        root = Path(temporary)
        seed_runtime(root, critical=True)
        state = run_autonomous_cycle_once(root)
        assert state["status"] == "blocked"
        assert state["phase"] == "blocked"
        assert state["permissionGate"]["decision"] == "BLOCK"
        assert state["directives"]["allowedEngines"]["hands"] is False
        event = latest_jsonl(root / "autonomous-cycle-events.jsonl")
        assert not validate_contract(event, "autonomous_cycle_event")
        domain_events = adapt_engine_event(event)
        assert domain_events and domain_events[0]["eventType"] == "CycleBlocked"


def main() -> int:
    test_autonomous_cycle_allow_gate()
    test_autonomous_cycle_pauses_for_operator_input()
    test_autonomous_cycle_blocks_critical_supervisor_findings()
    print("autonomous cycle orchestrator OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
