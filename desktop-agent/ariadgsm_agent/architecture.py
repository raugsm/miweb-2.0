from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class Layer:
    name: str
    owner: str
    responsibility: str
    output_contract: str


LAYERS: tuple[Layer, ...] = (
    Layer("Stage 0 Product Foundation", "Python + Docs", "Validate product foundation, execution lock, versioning and release readiness.", "stage_zero_readiness"),
    Layer("Domain Contract Governance", "Python", "Validate domain event registry, adapter coverage, human corrections and event-first memory readiness.", "domain_contracts_final_readiness"),
    Layer("Vision Engine", "C#/.NET", "Live desktop capture, change detection and temporary local visual evidence.", "vision_event"),
    Layer("Perception Engine", "C#/.NET + Python", "Convert pixels, OCR and accessibility into business-visible objects.", "perception_event"),
    Layer("Timeline Engine", "Python", "Unify live and historical messages into one deduplicated conversation timeline.", "conversation_event"),
    Layer("Domain Event Contracts", "Python", "Translate engine events into validated business events with evidence, risk, privacy and traceability.", "domain_event"),
    Layer("Case Manager", "Python", "Project validated domain events into durable customer/business cases with audit and next actions.", "case_manager_state"),
    Layer("Channel Routing Brain", "Python", "Decide whether a case stays, transfers or merges across WhatsApp channels with evidence and approval gates.", "channel_routing_state"),
    Layer("Memory Core", "Python", "Store clients, facts, procedures, slang, failures and conversation memory.", "learning_event"),
    Layer("Operating Core", "Python", "Track business state, cases, tasks, priorities and queues.", "decision_event"),
    Layer("Accounting Core", "Python", "Create payment, debt and quote records with evidence and confidence.", "accounting_event"),
    Layer("Cognitive Core", "Python", "Reason, plan, learn, ask for confirmation and choose the next action.", "decision_event"),
    Layer("Hands Engine", "C#/.NET", "Move mouse, keyboard, windows and scroll, then verify the action.", "action_event"),
    Layer("Supervisor", "C#/.NET + Python", "Enforce autonomy, confidence, permissions, audit and safety policy.", "action_event"),
    Layer("Autonomous Cycle", "Python + C#/.NET", "Unify eyes, timeline, reasoning, memory, safety and hands into one auditable operating loop.", "autonomous_cycle_event"),
)


AUTONOMY_LEVELS: dict[int, str] = {
    1: "observe",
    2: "suggest",
    3: "navigate",
    4: "record",
    5: "prepare",
    6: "execute",
}


CONTRACT_NAMES: tuple[str, ...] = (
    "stage_zero_readiness",
    "domain_contracts_final_readiness",
    "vision_event",
    "perception_event",
    "conversation_event",
    "decision_event",
    "action_event",
    "accounting_event",
    "learning_event",
    "autonomous_cycle_event",
    "human_feedback_event",
    "domain_event",
    "case_manager_state",
    "channel_routing_state",
)


def describe_pipeline() -> list[dict[str, str]]:
    return [
        {
            "name": layer.name,
            "owner": layer.owner,
            "responsibility": layer.responsibility,
            "outputContract": layer.output_contract,
        }
        for layer in LAYERS
    ]
