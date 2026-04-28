from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class Stage:
    number: int
    name: str
    status: str
    next_gate: str


@dataclass(frozen=True)
class Layer:
    name: str
    owner: str
    responsibility: str
    output_contract: str


MASTER_STAGES: tuple[Stage, ...] = (
    Stage(0, "Execution Lock", "base_closed_continuous_alignment", "governance"),
    Stage(1, "Domain Event Contracts", "closed_operational_contract", "events"),
    Stage(2, "Autonomous Cycle Orchestrator", "implemented_central_cycle_base", "cycle"),
    Stage(3, "Case Manager", "closed_operational_base", "cases"),
    Stage(4, "Channel Routing Brain", "closed_operational_base", "routing"),
    Stage(5, "Accounting Core evidence-first", "closed_evidence_first_base", "accounting"),
    Stage(6, "Product Shell", "closed_operational_shell", "operator_experience"),
    Stage(7, "Cabin Authority", "closed_cabin_authority", "workspace"),
    Stage(8, "Safe Eyes / Reader Core", "closed_reader_core_base", "reader_core"),
    Stage(9, "Living Memory", "next_pending", "memory"),
    Stage(10, "Business Brain", "partial", "business_reasoning"),
    Stage(11, "Trust & Safety + Input Arbiter", "advanced_base", "safety"),
    Stage(12, "Hands & Verification", "advanced_base", "hands"),
    Stage(13, "Tool Registry", "pending", "tools"),
    Stage(14, "Cloud Sync / ariadgsm.com", "partial", "cloud"),
    Stage(15, "Evaluation + Release", "pending_final", "release"),
)


LAYERS: tuple[Layer, ...] = (
    Layer("Stage 0 Product Foundation", "Python + Docs", "Validate product foundation, execution lock, versioning and release readiness.", "stage_zero_readiness"),
    Layer("Domain Contract Governance", "Python", "Validate domain event registry, adapter coverage, human corrections and event-first memory readiness.", "domain_contracts_final_readiness"),
    Layer("Cabin Authority", "C#/.NET", "Own Edge/Chrome/Firefox WhatsApp preparation, window authority and safe browser launch without closing operator sessions.", "cabin_authority_state"),
    Layer("Vision Engine", "C#/.NET", "Live desktop capture, change detection and temporary local visual evidence.", "vision_event"),
    Layer("Perception Engine", "C#/.NET + Python", "Convert pixels, OCR and accessibility into business-visible objects.", "perception_event"),
    Layer("Safe Eyes / Reader Core", "Python + browser adapters", "Accept only WhatsApp Web sources, compare DOM/accessibility/UIA/OCR, and emit reliable visible messages.", "reader_core_state"),
    Layer("Timeline Engine", "Python", "Unify live and historical messages into one deduplicated conversation timeline.", "conversation_event"),
    Layer("Domain Event Contracts", "Python", "Translate engine events into validated business events with evidence, risk, privacy and traceability.", "domain_event"),
    Layer("Case Manager", "Python", "Project validated domain events into durable customer/business cases with audit and next actions.", "case_manager_state"),
    Layer("Channel Routing Brain", "Python", "Decide whether a case stays, transfers or merges across WhatsApp channels with evidence and approval gates.", "channel_routing_state"),
    Layer("Memory Core", "Python", "Store clients, facts, procedures, slang, failures and conversation memory.", "learning_event"),
    Layer("Operating Core", "Python", "Track business state, cases, tasks, priorities and queues.", "decision_event"),
    Layer("Accounting Core", "Python", "Create payment, debt and refund records with evidence-first confirmation gates.", "accounting_core_state"),
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
    "cabin_authority_state",
    "vision_event",
    "perception_event",
    "visible_message",
    "reader_core_state",
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
    "accounting_core_state",
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


def describe_master_stages() -> list[dict[str, str | int]]:
    return [
        {
            "number": stage.number,
            "name": stage.name,
            "status": stage.status,
            "nextGate": stage.next_gate,
        }
        for stage in MASTER_STAGES
    ]
