from __future__ import annotations

import json
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.accounting_evidence import run_accounting_evidence_once
from ariadgsm_agent.case_manager import run_case_manager_once
from ariadgsm_agent.contracts import validate_contract
from ariadgsm_agent.domain_events import make_domain_event, run_domain_events_once


def write_jsonl(path: Path, *events: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("".join(json.dumps(event, ensure_ascii=False) + "\n" for event in events), encoding="utf-8")


def read_jsonl(path: Path) -> list[dict[str, object]]:
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def source(accounting_id: str, channel_id: str, conversation_id: str, case_id: str, customer_id: str) -> dict[str, object]:
    return {
        "eventType": "accounting_event",
        "accountingId": accounting_id,
        "createdAt": "2026-04-28T12:00:00Z",
        "status": "draft",
        "confidence": 0.9,
        "clientName": customer_id,
        "conversationId": conversation_id,
        "channelId": channel_id,
        "caseId": case_id,
        "customerId": customer_id,
        "kind": "payment",
        "evidence": [accounting_id],
    }


def accounting_domain_event(
    event_type: str,
    accounting_id: str,
    kind: str,
    amount: float | None,
    currency: str,
    *,
    evidence_level: str = "B",
) -> dict[str, object]:
    source_event = source(accounting_id, "wa-2", f"conv-{accounting_id}", f"case-{accounting_id}", f"customer-{accounting_id}")
    data = {
        "accountingId": accounting_id,
        "kind": kind,
        "status": "confirmed" if event_type in {"PaymentConfirmed", "AccountingRecordConfirmed"} else "draft",
        "clientName": f"Cliente {accounting_id}",
        "amount": amount,
        "currency": currency,
        "method": "USDT" if currency == "USDT" else "unknown",
    }
    event = make_domain_event(
        event_type,
        source_event,
        subject_type="accounting_record",
        subject_id=accounting_id,
        data=data,
        confidence=0.92 if evidence_level == "A" else 0.78,
        summary=f"{event_type} fixture {accounting_id}",
        source_domain="AccountingBrain",
        autonomy_level=3,
    )
    event["eventId"] = f"fixture-{event_type}-{accounting_id}"
    event["idempotencyKey"] = f"fixture:{event_type}:{accounting_id}"
    event["evidence"][0]["evidenceLevel"] = evidence_level
    event["evidence"][0]["summary"] = "Banco/comprobante confirmado." if evidence_level == "A" else "Texto o comprobante visible."
    errors = validate_contract(event, "domain_event")
    assert not errors, errors
    return event


def main() -> int:
    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        domain_file = root / "domain-events.jsonl"
        case_events_file = root / "case-events.jsonl"
        case_state_file = root / "case-manager-state.json"
        case_report_file = root / "case-manager-report.json"
        case_db = root / "case-manager.sqlite"
        accounting_events_file = root / "accounting-core-events.jsonl"
        accounting_state_file = root / "accounting-core-state.json"
        accounting_report_file = root / "accounting-core-report.json"
        accounting_db = root / "accounting-core.sqlite"
        domain_state_file = root / "domain-events-state.json"
        domain_db = root / "domain-events.sqlite"

        payment_draft = accounting_domain_event("PaymentDrafted", "draft-1", "payment", 25.0, "USDT", evidence_level="B")
        debt_missing_amount = accounting_domain_event("DebtDetected", "debt-1", "debt", None, "", evidence_level="C")
        refund_candidate = accounting_domain_event("RefundCandidate", "refund-1", "refund", 10.0, "USD", evidence_level="C")
        payment_confirmed = accounting_domain_event("PaymentConfirmed", "confirmed-1", "payment", 80.0, "USDT", evidence_level="A")
        write_jsonl(domain_file, payment_draft, debt_missing_amount, refund_candidate, payment_confirmed)

        case_state = run_case_manager_once(
            domain_file,
            case_events_file,
            case_state_file,
            case_db,
            report_file=case_report_file,
            limit=100,
        )
        assert case_state["summary"]["accountingCases"] >= 4, case_state

        state = run_accounting_evidence_once(
            domain_file,
            case_db,
            accounting_events_file,
            accounting_state_file,
            accounting_db,
            report_file=accounting_report_file,
            limit=100,
        )
        assert state["status"] == "attention", state
        assert not validate_contract(state, "accounting_core_state"), state
        assert state["summary"]["accountingRecords"] == 4, state
        assert state["summary"]["confirmedRecords"] == 1, state
        assert state["summary"]["needsEvidence"] >= 1, state
        assert state["summary"]["needsHuman"] >= 1, state
        assert state["summary"]["payments"] >= 2, state
        assert state["summary"]["debts"] >= 1, state
        assert state["summary"]["refunds"] >= 1, state
        assert accounting_report_file.exists()
        assert accounting_events_file.exists()

        accounting_events = read_jsonl(accounting_events_file)
        accounting_types = {event["eventType"] for event in accounting_events}
        assert "AccountingEvidenceAttached" in accounting_types, accounting_types
        assert "AccountingRecordConfirmed" in accounting_types, accounting_types
        assert "HumanApprovalRequired" in accounting_types, accounting_types
        for event in accounting_events:
            assert not validate_contract(event, "domain_event"), event
            assert event["data"]["accountingCoreEvent"] is True
            if event["eventType"] == "AccountingRecordConfirmed":
                levels = {item["evidenceLevel"] for item in event["evidence"]}
                assert "A" in levels, event

        repeated = run_accounting_evidence_once(
            domain_file,
            case_db,
            accounting_events_file,
            accounting_state_file,
            accounting_db,
            report_file=accounting_report_file,
            limit=100,
        )
        assert repeated["ingested"]["records"] == 0, repeated
        assert repeated["ingested"]["duplicates"] >= 4, repeated

        domain_state = run_domain_events_once(
            [accounting_events_file],
            domain_file,
            domain_state_file,
            domain_db,
            limit=100,
        )
        assert domain_state["ingested"]["invalid"] == 0, domain_state
        assert "AccountingEvidenceAttached" in domain_state["summary"]["byType"], domain_state
        assert "AccountingRecordConfirmed" in domain_state["summary"]["byType"], domain_state

    print("accounting core evidence OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
