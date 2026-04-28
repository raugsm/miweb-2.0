from __future__ import annotations

import json
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.contracts import sample_event, validate_contract
from ariadgsm_agent.supervisor import run_supervisor_once
from ariadgsm_agent.trust_safety import run_trust_safety_once


def write_jsonl(path: Path, *events: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("".join(json.dumps(event, ensure_ascii=False) + "\n" for event in events), encoding="utf-8")


def write_json(path: Path, value: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False), encoding="utf-8")


def decision_event(decision_id: str, proposed_action: str, confidence: float = 0.95) -> dict[str, object]:
    return {
        "eventType": "decision_event",
        "decisionId": decision_id,
        "createdAt": "2026-04-28T12:00:00Z",
        "goal": "operate",
        "intent": proposed_action,
        "confidence": confidence,
        "autonomyLevel": 3,
        "proposedAction": proposed_action,
        "requiresHumanConfirmation": False,
        "reasoningSummary": proposed_action,
        "evidence": ["msg-wa-1-test"],
        "channelId": "wa-1",
        "conversationTitle": "Cliente prueba",
    }


def approval_event(target_source_id: str) -> dict[str, object]:
    return {
        "eventType": "safety_approval_event",
        "approvalId": f"approval-{target_source_id}",
        "createdAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
        "expiresAt": (datetime.now(timezone.utc) + timedelta(minutes=5)).isoformat().replace("+00:00", "Z"),
        "targetSourceId": target_source_id,
        "decision": "APPROVE",
        "approvedBy": "bryams",
        "scope": "single_decision",
        "reason": "Aprobado para prueba.",
        "constraints": {"maxUses": 1},
    }


def verified_open_chat(action_id: str) -> dict[str, object]:
    return {
        "eventType": "action_event",
        "actionId": action_id,
        "createdAt": "2026-04-28T12:00:01Z",
        "actionType": "open_chat",
        "target": {
            "channelId": "wa-1",
            "conversationTitle": "Cliente prueba",
            "requiredAutonomyLevel": 3,
            "verifiedBeforeContinue": True,
        },
        "status": "verified",
        "verification": {
            "verified": True,
            "summary": "Perception confirmo chat correcto.",
            "confidence": 0.96,
        },
    }


def accounting_domain_event(event_id: str, event_type: str, evidence_level: str = "B") -> dict[str, object]:
    return {
        "eventId": event_id,
        "eventType": event_type,
        "schemaVersion": "0.8.2",
        "createdAt": "2026-04-28T12:00:02Z",
        "sourceDomain": "AccountingBrain",
        "sourceSystem": "ariadgsm-local-agent",
        "actor": {"type": "ai", "id": "ariadgsm-business-brain"},
        "subject": {"type": "accounting_record", "id": event_id},
        "correlationId": f"case-{event_id}",
        "causationId": f"source-{event_id}",
        "idempotencyKey": f"{event_type}:{event_id}",
        "traceId": f"trace-{event_id}",
        "channelId": "wa-2",
        "conversationId": f"conv-{event_id}",
        "caseId": f"case-{event_id}",
        "customerId": f"customer-{event_id}",
        "confidence": 0.93,
        "evidence": [
            {
                "evidenceId": f"ev-{event_id}",
                "source": "accounting_event",
                "evidenceLevel": evidence_level,
                "observedAt": "2026-04-28T12:00:02Z",
                "summary": "Comprobante visible." if evidence_level == "A" else "Texto de chat sin comprobante final.",
                "rawReference": f"local://{event_id}",
                "confidence": 0.93,
                "redactionState": "safe_summary",
                "limitations": [],
            }
        ],
        "privacy": {
            "classification": "internal",
            "cloudAllowed": True,
            "redactionRequired": False,
            "retentionPolicy": "case_lifetime",
            "contains": [],
            "reason": "Accounting summary.",
        },
        "risk": {
            "riskLevel": "critical" if "Confirmed" in event_type else "medium",
            "riskReasons": ["accounting"],
            "autonomyLevel": 3,
            "allowedActions": ["record_audit"],
            "blockedActions": ["confirm_payment"],
        },
        "requiresHumanReview": True,
        "data": {"amount": 35, "currency": "USDT"},
    }


def run_core(root: Path, *, autonomy_level: int = 3, permissions: dict[str, object] | None = None) -> dict[str, object]:
    return run_trust_safety_once(
        root / "cognitive-decision-events.jsonl",
        root / "decision-events.jsonl",
        root / "action-events.jsonl",
        root / "domain-events.jsonl",
        root / "trust-safety-state.json",
        business_decision_events_file=root / "business-decision-events.jsonl",
        approval_events_file=root / "safety-approval-events.jsonl",
        input_arbiter_state_file=root / "input-arbiter-state.json",
        permissions=permissions,
        autonomy_level=autonomy_level,
        limit=100,
    )


def test_contract_sample() -> None:
    state = sample_event("trust_safety_state")
    assert not validate_contract(state, "trust_safety_state"), state
    input_state = sample_event("input_arbiter_state")
    assert not validate_contract(input_state, "input_arbiter_state"), input_state
    approval = sample_event("safety_approval_event")
    assert not validate_contract(approval, "safety_approval_event"), approval


def test_local_verified_chat_is_allowed_with_limits() -> None:
    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        write_jsonl(root / "cognitive-decision-events.jsonl")
        write_jsonl(root / "decision-events.jsonl")
        write_jsonl(root / "action-events.jsonl", verified_open_chat("action-open-1"))
        write_jsonl(root / "domain-events.jsonl")
        state = run_core(root, autonomy_level=3)
        assert not validate_contract(state, "trust_safety_state"), state
        assert state["permissionGate"]["decision"] == "ALLOW_WITH_LIMIT", state
        assert state["permissionGate"]["allowedEngines"]["hands"] is True
        assert state["summary"]["blocked"] == 0, state


def test_send_message_without_explicit_permission_is_blocked() -> None:
    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        write_jsonl(root / "cognitive-decision-events.jsonl", decision_event("decision-send-1", "send_message", 0.99))
        write_jsonl(root / "decision-events.jsonl")
        write_jsonl(root / "action-events.jsonl")
        write_jsonl(root / "domain-events.jsonl")
        state = run_core(root, autonomy_level=6)
        assert state["status"] == "blocked", state
        assert state["permissionGate"]["decision"] == "BLOCK", state
        assert state["summary"]["irreversibleBlocked"] == 1, state
        assert any("allowMessageSend" in item["reason"] for item in state["blockedActions"]), state


def test_send_message_with_permission_still_requires_per_action_approval() -> None:
    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        write_jsonl(root / "cognitive-decision-events.jsonl", decision_event("decision-send-2", "send_message", 0.99))
        write_jsonl(root / "decision-events.jsonl")
        write_jsonl(root / "action-events.jsonl")
        write_jsonl(root / "domain-events.jsonl")
        state = run_core(root, autonomy_level=6, permissions={"allowMessageSend": True})
        assert state["permissionGate"]["decision"] == "ASK_HUMAN", state
        assert state["summary"]["blocked"] == 0, state
        assert state["summary"]["requiresHumanConfirmation"] == 1, state

        write_jsonl(root / "safety-approval-events.jsonl", approval_event("decision-send-2"))
        approved = run_core(root, autonomy_level=6, permissions={"allowMessageSend": True})
        assert approved["permissionGate"]["decision"] == "ALLOW", approved
        assert approved["summary"]["approvalsApplied"] == 1, approved


def test_accounting_confirmation_requires_evidence_a_and_permission() -> None:
    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        write_jsonl(root / "cognitive-decision-events.jsonl")
        write_jsonl(root / "decision-events.jsonl")
        write_jsonl(root / "action-events.jsonl")
        write_jsonl(root / "domain-events.jsonl", accounting_domain_event("pay-1", "PaymentConfirmed", "B"))
        state = run_core(root, autonomy_level=6, permissions={"allowAccountingConfirmation": True})
        assert state["permissionGate"]["decision"] == "BLOCK", state
        assert any("evidencia nivel A" in item["reason"] for item in state["blockedActions"]), state

        write_jsonl(root / "domain-events.jsonl", accounting_domain_event("pay-2", "PaymentConfirmed", "A"))
        state_ok = run_core(root, autonomy_level=6, permissions={"allowAccountingConfirmation": True})
        assert state_ok["summary"]["blocked"] == 0, state_ok
        assert state_ok["permissionGate"]["decision"] in {"ALLOW", "ASK_HUMAN"}, state_ok


def test_operator_control_pauses_hands_only() -> None:
    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        write_jsonl(root / "cognitive-decision-events.jsonl")
        write_jsonl(root / "decision-events.jsonl")
        write_jsonl(root / "action-events.jsonl", verified_open_chat("action-open-operator"))
        write_jsonl(root / "domain-events.jsonl")
        write_json(root / "input-arbiter-state.json", {"phase": "operator_control", "operatorHasPriority": True, "summary": "Bryams esta usando el mouse."})
        state = run_core(root, autonomy_level=3)
        assert state["permissionGate"]["decision"] == "PAUSE_FOR_OPERATOR", state
        assert state["permissionGate"]["allowedEngines"]["hands"] is False
        assert state["permissionGate"]["allowedEngines"]["vision"] is True
        assert state["permissionGate"]["allowedEngines"]["memory"] is True


def test_supervisor_reads_business_brain_decisions() -> None:
    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        write_jsonl(root / "cognitive-decision-events.jsonl")
        write_jsonl(root / "decision-events.jsonl")
        write_jsonl(root / "business-decision-events.jsonl", decision_event("business-decision-send-1", "send_message", 0.99))
        write_jsonl(root / "action-events.jsonl")
        write_jsonl(root / "domain-events.jsonl")
        state = run_supervisor_once(
            root / "cognitive-decision-events.jsonl",
            root / "decision-events.jsonl",
            root / "action-events.jsonl",
            root / "supervisor-state.json",
            domain_events_file=root / "domain-events.jsonl",
            business_decision_events_file=root / "business-decision-events.jsonl",
            input_arbiter_state_file=root / "input-arbiter-state.json",
            trust_safety_state_file=root / "trust-safety-state.json",
            autonomy_level=6,
            limit=100,
        )
        assert state["summary"]["decisionsRead"] == 1, state
        assert state["permissionGate"]["decision"] == "BLOCK", state
        assert state["latestFindings"][0]["sourceId"] == "business-decision-send-1", state


def test_supervisor_writes_compatible_trust_state() -> None:
    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        write_jsonl(root / "cognitive-decision-events.jsonl", decision_event("decision-send-supervisor", "send_message", 0.99))
        write_jsonl(root / "decision-events.jsonl")
        write_jsonl(root / "action-events.jsonl")
        write_jsonl(root / "domain-events.jsonl")
        state = run_supervisor_once(
            root / "cognitive-decision-events.jsonl",
            root / "decision-events.jsonl",
            root / "action-events.jsonl",
            root / "supervisor-state.json",
            domain_events_file=root / "domain-events.jsonl",
            input_arbiter_state_file=root / "input-arbiter-state.json",
            trust_safety_state_file=root / "trust-safety-state.json",
            autonomy_level=6,
            limit=100,
        )
        assert state["engine"] == "ariadgsm_supervisor_core", state
        assert state["permissionGate"]["decision"] == "BLOCK", state
        assert (root / "trust-safety-state.json").exists()
        assert (root / "supervisor-state.json").exists()


def main() -> int:
    test_contract_sample()
    test_local_verified_chat_is_allowed_with_limits()
    test_send_message_without_explicit_permission_is_blocked()
    test_send_message_with_permission_still_requires_per_action_approval()
    test_accounting_confirmation_requires_evidence_a_and_permission()
    test_operator_control_pauses_hands_only()
    test_supervisor_reads_business_brain_decisions()
    test_supervisor_writes_compatible_trust_state()
    print("trust safety core OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
