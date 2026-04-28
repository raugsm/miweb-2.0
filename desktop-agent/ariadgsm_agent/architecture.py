from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class Stage:
    number: str
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
    Stage("0", "Execution Lock", "base_closed_continuous_alignment", "governance"),
    Stage("0.5", "Runtime Kernel", "closed_runtime_kernel_final", "runtime_kernel"),
    Stage("1", "Domain Event Contracts", "closed_operational_contract", "events"),
    Stage("2", "Autonomous Cycle Orchestrator", "implemented_central_cycle_base", "cycle"),
    Stage("3", "Case Manager", "closed_operational_base", "cases"),
    Stage("4", "Channel Routing Brain", "closed_operational_base", "routing"),
    Stage("5", "Accounting Core evidence-first", "closed_evidence_first_base", "accounting"),
    Stage("6", "Product Shell", "closed_operational_shell", "operator_experience"),
    Stage("7", "Cabin Authority", "closed_cabin_authority", "workspace"),
    Stage("8", "Safe Eyes / Reader Core", "closed_reader_core_base", "reader_core"),
    Stage("9", "Living Memory", "closed_living_memory_base", "memory"),
    Stage("10", "Business Brain", "closed_business_brain_base", "business_reasoning"),
    Stage("11", "Trust & Safety + Input Arbiter", "closed_trust_safety_input_arbiter_final", "safety"),
    Stage("12", "Hands & Verification", "closed_hands_verification_final", "hands"),
    Stage("13", "Tool Registry", "closed_tool_registry_final", "tools"),
    Stage("14", "Cloud Sync / ariadgsm.com", "closed_cloud_sync_final", "cloud"),
    Stage("15", "Evaluation + Release", "closed_release_candidate", "release_candidate"),
)


LAYERS: tuple[Layer, ...] = (
    Layer("Stage 0 Product Foundation", "Python + Docs", "Validate product foundation, execution lock, versioning and release readiness.", "stage_zero_readiness"),
    Layer("Runtime Kernel", "C#/.NET + Python", "Own the single operational truth for lifecycle, incidents, recovery and human state before cloud sync.", "runtime_kernel_state"),
    Layer("Runtime Governor", "C#/.NET + Python", "Own process lifetime, Windows Job Object grouping, process ownership, shutdown verification and desired-vs-real reconciliation.", "runtime_governor_state"),
    Layer("Domain Contract Governance", "Python", "Validate domain event registry, adapter coverage, human corrections and event-first memory readiness.", "domain_contracts_final_readiness"),
    Layer("Cabin Authority", "C#/.NET", "Own Edge/Chrome/Firefox WhatsApp preparation, window authority and safe browser launch without closing operator sessions.", "cabin_authority_state"),
    Layer("Vision Engine", "C#/.NET", "Live desktop capture, change detection and temporary local visual evidence.", "vision_event"),
    Layer("Perception Engine", "C#/.NET + Python", "Convert pixels, OCR and accessibility into business-visible objects.", "perception_event"),
    Layer("Safe Eyes / Reader Core", "Python + browser adapters", "Accept only WhatsApp Web sources, compare DOM/accessibility/UIA/OCR, and emit reliable visible messages.", "reader_core_state"),
    Layer("Window Reality Resolver", "C#/.NET + Python", "Fuse Windows identity, screen geometry, Reader Core semantics, state freshness and actionability before any channel is considered ready.", "window_reality_state"),
    Layer("Timeline Engine", "Python", "Unify live and historical messages into one deduplicated conversation timeline.", "conversation_event"),
    Layer("Domain Event Contracts", "Python", "Translate engine events into validated business events with evidence, risk, privacy and traceability.", "domain_event"),
    Layer("Case Manager", "Python", "Project validated domain events into durable customer/business cases with audit and next actions.", "case_manager_state"),
    Layer("Channel Routing Brain", "Python", "Decide whether a case stays, transfers or merges across WhatsApp channels with evidence and approval gates.", "channel_routing_state"),
    Layer("Memory Core", "Python", "Store episodic, semantic, procedural, accounting, style and correction memories with evidence and uncertainty.", "living_memory_state"),
    Layer("Business Brain", "Python", "Use Living Memory, cases, routing and accounting evidence to propose business decisions without physical action.", "business_brain_state"),
    Layer("Tool Registry", "Python", "Resolve GSM capabilities into registered tools, risk, inputs, outputs, verifiers and fallback plans without direct execution.", "tool_registry_state"),
    Layer("Cloud Sync", "Python + Node/Railway", "Publish only understood business events and health reports to ariadgsm.com with idempotency, bounded retries, local ledger and no raw screen uploads.", "cloud_sync_state"),
    Layer("Evaluation + Release", "Python + .NET", "Run release gates for runtime ownership, durable checkpoints, evals, observability, updater rollback, long-run simulation and release candidate packaging.", "evaluation_release_state"),
    Layer("Trust & Safety", "Python", "Evaluate Cognitive, Operating and Business Brain decisions, enforce approvals, evidence, autonomy and permission gates.", "trust_safety_state"),
    Layer("Input Arbiter", "C#/.NET", "Own mouse and keyboard leases so the AI never fights the operator while eyes, memory and brain continue.", "input_arbiter_state"),
    Layer("Operating Core", "Python", "Track business state, cases, tasks, priorities and queues.", "decision_event"),
    Layer("Accounting Core", "Python", "Create payment, debt and refund records with evidence-first confirmation gates.", "accounting_core_state"),
    Layer("Cognitive Core", "Python", "Reason, plan, learn, ask for confirmation and choose the next action.", "decision_event"),
    Layer("Hands Engine", "C#/.NET", "Move mouse, keyboard, windows and scroll only after permission, then verify every physical action before continuing.", "hands_verification_state"),
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
    "runtime_kernel_state",
    "runtime_governor_state",
    "domain_contracts_final_readiness",
    "cabin_authority_state",
    "vision_event",
    "perception_event",
    "visible_message",
    "reader_core_state",
    "window_reality_state",
    "conversation_event",
    "decision_event",
    "action_event",
    "accounting_event",
    "learning_event",
    "living_memory_state",
    "business_brain_state",
    "tool_registry_state",
    "cloud_sync_state",
    "evaluation_release_state",
    "autonomous_cycle_event",
    "human_feedback_event",
    "safety_approval_event",
    "domain_event",
    "case_manager_state",
    "channel_routing_state",
    "accounting_core_state",
    "input_arbiter_state",
    "trust_safety_state",
    "hands_verification_state",
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


def describe_master_stages() -> list[dict[str, str]]:
    return [
        {
            "number": stage.number,
            "name": stage.name,
            "status": stage.status,
            "nextGate": stage.next_gate,
        }
        for stage in MASTER_STAGES
    ]
