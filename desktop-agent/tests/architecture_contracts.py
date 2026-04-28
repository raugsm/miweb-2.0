from __future__ import annotations

import json
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.architecture import CONTRACT_NAMES, LAYERS
from ariadgsm_agent.contracts import sample_event, validate_contract
from ariadgsm_agent.cognitive import CognitiveCore, CognitiveStore, decision_event_from_conversation, run_cognitive_once
from ariadgsm_agent.memory import MemoryStore, run_memory_once
from ariadgsm_agent.operating import OperatingCore, OperatingStore, run_operating_once
from ariadgsm_agent.supervisor import SupervisorCore, SupervisorPolicy, run_supervisor_once
from ariadgsm_agent.timeline import ConversationTimeline, run_timeline_once
from ariadgsm_agent.autonomous_cycle import run_autonomous_cycle_once


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

    signal_conversation = dict(conversation)
    signal_conversation["conversationEventId"] = "conversation-cognitive-test"
    signal_conversation["conversationId"] = "wa-1-cliente-cognitive"
    signal_conversation["conversationTitle"] = "Cliente Cognitive"
    signal_conversation["messages"] = [
        {
            "messageId": "cog-1",
            "text": "Ya hice pago de 25 usdt para liberar Samsung en Mexico",
            "direction": "client",
            "signals": [
                {"kind": "payment", "value": "pago", "confidence": 0.94},
                {"kind": "amount", "value": "25 USDT", "confidence": 0.84},
                {"kind": "service", "value": "samsung", "confidence": 0.8},
                {"kind": "country", "value": "MX", "confidence": 0.72},
            ],
        }
    ]
    cognitive = CognitiveCore()
    assessment = cognitive.assess_conversation(signal_conversation, SupervisorPolicy(autonomy_level=2))
    assert assessment.intent == "accounting_payment"
    assert assessment.requires_human_confirmation
    assert assessment.learning_events
    assert not validate_contract(assessment.decision_event, "decision_event")
    for learning_event in assessment.learning_events:
        assert not validate_contract(learning_event, "learning_event")

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

    supervisor = SupervisorCore(SupervisorPolicy(autonomy_level=3))
    supervisor_state = supervisor.assess([decision], [])
    assert supervisor_state["engine"] == "ariadgsm_supervisor_core"
    assert supervisor_state["summary"]["decisionsRead"] == 1

    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        conversation_file = root / "conversation-events.jsonl"
        history_file = root / "history-conversation-events.jsonl"
        timeline_file = root / "timeline-events.jsonl"
        decision_file = root / "decision-events.jsonl"
        cognitive_decision_file = root / "cognitive-decision-events.jsonl"
        learning_file = root / "learning-events.jsonl"
        accounting_file = root / "accounting-events.jsonl"
        state_file = root / "operating-state.json"
        timeline_state_file = root / "timeline-state.json"
        cognitive_state_file = root / "cognitive-state.json"
        memory_state_file = root / "memory-state.json"
        supervisor_state_file = root / "supervisor-state.json"
        trust_safety_state_file = root / "trust-safety-state.json"
        autonomous_cycle_state_file = root / "autonomous-cycle-state.json"
        autonomous_cycle_events_file = root / "autonomous-cycle-events.jsonl"
        action_file = root / "action-events.jsonl"
        db_file = root / "operating.sqlite"
        cognitive_db_file = root / "cognitive.sqlite"
        memory_db_file = root / "memory.sqlite"
        payment_conversation = dict(conversation)
        payment_conversation["conversationEventId"] = "conversation-payment-test"
        payment_conversation["conversationId"] = "wa-1-cliente-pago"
        payment_conversation["conversationTitle"] = "Cliente Pago"
        payment_conversation["messages"] = [
            {
                "messageId": "pay-1",
                "text": "Ya hice el pago de 25 usdt para Samsung en Mexico",
                "direction": "client",
                "signals": [
                    {"kind": "payment", "value": "pago", "confidence": 0.94},
                    {"kind": "amount", "value": "25 USDT", "confidence": 0.84},
                    {"kind": "service", "value": "samsung", "confidence": 0.8},
                    {"kind": "country", "value": "MX", "confidence": 0.72},
                ],
            }
        ]
        conversation_file.write_text(
            json.dumps(conversation, ensure_ascii=False) + "\n"
            + json.dumps(payment_conversation, ensure_ascii=False) + "\n",
            encoding="utf-8",
        )
        history_payment = dict(payment_conversation)
        history_payment["conversationEventId"] = "conversation-payment-history-test"
        history_payment["source"] = "history"
        history_payment["timeline"] = {
            "historyLimitDays": 30,
            "complete": True,
            "oldestLoadedAt": "2026-04-01T00:00:00Z",
            "dedupeStrategy": "test",
        }
        history_payment["messages"] = [
            {
                "messageId": "pay-history-1",
                "text": "Servicio Samsung acordado antes del pago",
                "direction": "client",
                "sentAt": "2026-04-20T12:00:00Z",
                "signals": [{"kind": "service", "value": "samsung", "confidence": 0.8}],
            },
            payment_conversation["messages"][0],
        ]
        history_file.write_text(json.dumps(history_payment, ensure_ascii=False) + "\n", encoding="utf-8")
        timeline_state = run_timeline_once(
            conversation_file,
            timeline_file,
            timeline_state_file,
            history_events_file=history_file,
            history_limit_days=30,
        )
        assert timeline_state["status"] == "ok"
        assert timeline_state["ingested"]["timelines"] == 2
        assert timeline_state["ingested"]["messages"] == 3
        assert timeline_file.exists()

        state = run_operating_once(
            timeline_file,
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
            timeline_file,
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

        cognitive_state = run_cognitive_once(
            timeline_file,
            cognitive_decision_file,
            learning_file,
            cognitive_state_file,
            cognitive_db_file,
            autonomy_level=2,
        )
        assert cognitive_state["status"] == "ok"
        assert cognitive_state["summary"]["decisions"] == 2
        assert cognitive_state["summary"]["clientProfiles"] == 2
        assert cognitive_decision_file.exists()
        assert learning_file.exists()

        repeated_cognitive = run_cognitive_once(
            timeline_file,
            cognitive_decision_file,
            learning_file,
            cognitive_state_file,
            cognitive_db_file,
            autonomy_level=2,
        )
        assert repeated_cognitive["ingested"]["events"] == 0
        assert repeated_cognitive["ingested"]["duplicates"] == 2

        cognitive_store = CognitiveStore(cognitive_db_file)
        try:
            summary = cognitive_store.summary()
            assert summary["decisions"] == 2
            assert summary["learningEvents"] >= 1
        finally:
            cognitive_store.close()

        memory_state = run_memory_once(
            timeline_file,
            cognitive_decision_file,
            decision_file,
            learning_file,
            accounting_file,
            memory_state_file,
            memory_db_file,
        )
        assert memory_state["status"] == "ok"
        assert memory_state["summary"]["conversations"] == 2
        assert memory_state["summary"]["memoryMessages"] == 3
        assert memory_state["summary"]["signals"] >= 4
        assert memory_state["summary"]["memoryDecisions"] >= 4
        assert memory_state["summary"]["learningEvents"] >= 1
        assert memory_state["summary"]["accountingEvents"] == 1

        repeated_memory = run_memory_once(
            timeline_file,
            cognitive_decision_file,
            decision_file,
            learning_file,
            accounting_file,
            memory_state_file,
            memory_db_file,
        )
        assert repeated_memory["ingested"]["events"] == 0
        assert repeated_memory["ingested"]["duplicates"] >= 5

        memory_store = MemoryStore(memory_db_file)
        try:
            profile = memory_store.customer_profile("wa-1-cliente-pago")
            assert profile is not None
            assert "MX" in profile["countries"]
            assert "samsung" in profile["services"]
        finally:
            memory_store.close()

        action_file.write_text(
            json.dumps(
                {
                    "eventType": "action_event",
                    "actionId": "action-supervisor-test",
                    "createdAt": "2026-04-26T00:00:00Z",
                    "actionType": "open_chat",
                    "target": {
                        "channelId": "wa-1",
                        "requiredAutonomyLevel": 3,
                        "requiresHumanConfirmation": False,
                    },
                    "status": "planned",
                    "verification": {"verified": False, "confidence": 0.4},
                },
                ensure_ascii=False,
            )
            + "\n",
            encoding="utf-8",
        )
        supervisor_run = run_supervisor_once(
            cognitive_decision_file,
            decision_file,
            action_file,
            supervisor_state_file,
            trust_safety_state_file=trust_safety_state_file,
            autonomy_level=3,
        )
        assert supervisor_run["status"] in {"ok", "attention"}
        assert supervisor_run["summary"]["actionsRead"] == 1
        assert supervisor_state_file.exists()

        (root / "vision-health.json").write_text(
            json.dumps(
                {
                    "status": "ok",
                    "framesCaptured": 5,
                    "visibleWindowCount": 3,
                    "captureIntervalMs": 750,
                },
                ensure_ascii=False,
            ),
            encoding="utf-8",
        )
        (root / "perception-health.json").write_text(
            json.dumps(
                {
                    "status": "ok",
                    "messagesExtracted": 3,
                    "conversationEventsWritten": 2,
                    "lastReaderStatus": "ok",
                },
                ensure_ascii=False,
            ),
            encoding="utf-8",
        )
        (root / "interaction-state.json").write_text(
            json.dumps(
                {
                    "status": "ok",
                    "targetsObserved": 3,
                    "actionableTargets": 2,
                    "targetsRejected": 0,
                },
                ensure_ascii=False,
            ),
            encoding="utf-8",
        )
        (root / "orchestrator-state.json").write_text(
            json.dumps(
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
                ensure_ascii=False,
            ),
            encoding="utf-8",
        )
        (root / "hands-state.json").write_text(
            json.dumps(
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
                ensure_ascii=False,
            ),
            encoding="utf-8",
        )
        (root / "window-reality-state.json").write_text(
            json.dumps(sample_event("window_reality_state"), ensure_ascii=False),
            encoding="utf-8",
        )
        (root / "case-manager-state.json").write_text(
            json.dumps(sample_event("case_manager_state"), ensure_ascii=False),
            encoding="utf-8",
        )
        (root / "channel-routing-state.json").write_text(
            json.dumps(sample_event("channel_routing_state"), ensure_ascii=False),
            encoding="utf-8",
        )
        (root / "accounting-core-state.json").write_text(
            json.dumps(sample_event("accounting_core_state"), ensure_ascii=False),
            encoding="utf-8",
        )
        tool_registry_state = sample_event("tool_registry_state")
        tool_registry_state["status"] = "ok"
        tool_registry_state["summary"]["plansNeedHuman"] = 0
        tool_registry_state["humanReport"]["queNecesitaBryams"] = []
        (root / "tool-registry-state.json").write_text(
            json.dumps(tool_registry_state, ensure_ascii=False),
            encoding="utf-8",
        )
        cycle_state = run_autonomous_cycle_once(
            root,
            autonomous_cycle_state_file,
            autonomous_cycle_events_file,
            append_event=True,
        )
        assert cycle_state["engine"] == "ariadgsm_autonomous_cycle"
        assert cycle_state["status"] in {"ok", "attention", "blocked"}
        assert len(cycle_state["stages"]) == 13
        assert any(stage["stageId"] == "window_reality" for stage in cycle_state["stages"])
        assert any(stage["stageId"] == "business_brain" for stage in cycle_state["stages"])
        assert any(stage["stageId"] == "tool_registry" for stage in cycle_state["stages"])
        assert any(stage["stageId"] == "trust_safety" for stage in cycle_state["stages"])
        assert [step["stepId"] for step in cycle_state["steps"]] == [
            "observe",
            "understand",
            "plan",
            "request_permission",
            "act",
            "verify",
            "learn",
            "report",
        ]
        assert cycle_state["permissionGate"]["decision"] in {
            "ALLOW",
            "ALLOW_WITH_LIMIT",
            "ASK_HUMAN",
            "PAUSE_FOR_OPERATOR",
            "BLOCK",
        }
        assert (root / "autonomous-cycle-directives.json").exists()
        assert (root / "autonomous-cycle-report.json").exists()
        cycle_event = json.loads(autonomous_cycle_events_file.read_text(encoding="utf-8").splitlines()[-1])
        assert not validate_contract(cycle_event, "autonomous_cycle_event")
    print("architecture contracts OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
