from __future__ import annotations

import json
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.architecture import CONTRACT_NAMES, LAYERS
from ariadgsm_agent.contracts import sample_event, validate_contract
from ariadgsm_agent.cognitive import decision_event_from_conversation
from ariadgsm_agent.operating import OperatingCore, OperatingStore, run_operating_once
from ariadgsm_agent.supervisor import SupervisorPolicy
from ariadgsm_agent.timeline import ConversationTimeline


def main() -> int:
    assert len(LAYERS) >= 9
    for contract_name in CONTRACT_NAMES:
        event = sample_event(contract_name)
        errors = validate_contract(event, contract_name)
        assert not errors, (contract_name, errors)

    timeline = ConversationTimeline("wa-1-cliente", "wa-1", "Cliente")
    timeline.merge(
        [
            {"messageId": "1", "text": "Cuanto vale liberar Samsung?", "direction": "client"},
            {"messageId": "1", "text": "Cuanto vale liberar Samsung?", "direction": "client"},
        ]
    )
    conversation = timeline.to_event()
    assert len(conversation["messages"]) == 1
    assert not validate_contract(conversation, "conversation_event")

    decision = decision_event_from_conversation(conversation, SupervisorPolicy(autonomy_level=2))
    assert decision["eventType"] == "decision_event"
    assert not validate_contract(decision, "decision_event")

    operating = OperatingCore()
    update = operating.process_conversation(conversation, autonomy_level=2)
    assert update.case.status == "customer_waiting"
    assert update.tasks
    assert update.decision_event
    assert not validate_contract(update.decision_event, "decision_event")

    ignored_group = dict(conversation)
    ignored_group["conversationTitle"] = "Pagos Mexico"
    ignored_update = operating.process_conversation(ignored_group, autonomy_level=2)
    assert ignored_update.ignored
    assert not ignored_update.tasks
    assert ignored_update.decision_event is None

    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        conversation_file = root / "conversation-events.jsonl"
        decision_file = root / "decision-events.jsonl"
        accounting_file = root / "accounting-events.jsonl"
        state_file = root / "operating-state.json"
        db_file = root / "operating.sqlite"
        payment_conversation = dict(conversation)
        payment_conversation["conversationEventId"] = "conversation-payment-test"
        payment_conversation["conversationId"] = "wa-1-cliente-pago"
        payment_conversation["conversationTitle"] = "Cliente Pago"
        payment_conversation["messages"] = [
            {"messageId": "pay-1", "text": "Ya hice el pago de 25 usdt", "direction": "client"}
        ]
        conversation_file.write_text(
            json.dumps(conversation, ensure_ascii=False) + "\n"
            + json.dumps(payment_conversation, ensure_ascii=False) + "\n",
            encoding="utf-8",
        )
        state = run_operating_once(
            conversation_file,
            decision_file,
            state_file,
            db_file,
            autonomy_level=2,
            accounting_events_file=accounting_file,
        )
        assert state["status"] == "ok"
        assert state["summary"]["cases"] == 2
        assert state["summary"]["openTasks"] == 2
        assert state["summary"]["accountingDrafts"] == 1
        assert state["ingested"]["duplicates"] == 0
        assert decision_file.exists()
        assert accounting_file.exists()

        repeated = run_operating_once(
            conversation_file,
            decision_file,
            state_file,
            db_file,
            autonomy_level=2,
            accounting_events_file=accounting_file,
        )
        assert repeated["ingested"]["events"] == 0
        assert repeated["ingested"]["duplicates"] == 2
        assert repeated["summary"]["cases"] == 2
        assert repeated["summary"]["openTasks"] == 2

        store = OperatingStore(db_file)
        try:
            summary = store.summary()
            assert summary["cases"] == 2
            assert summary["accountingDrafts"] == 1
        finally:
            store.close()
    print("architecture contracts OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
