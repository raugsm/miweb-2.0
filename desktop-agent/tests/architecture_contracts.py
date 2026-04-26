from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.architecture import CONTRACT_NAMES, LAYERS
from ariadgsm_agent.contracts import sample_event, validate_contract
from ariadgsm_agent.cognitive import decision_event_from_conversation
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
    print("architecture contracts OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

