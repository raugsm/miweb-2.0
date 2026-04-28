from __future__ import annotations

import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


AGENT_ROOT = Path(__file__).resolve().parents[1]
RUNTIME_DIR = AGENT_ROOT / "runtime"


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def read_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        return {}
    return value if isinstance(value, dict) else {}


def write_json_atomic(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary.replace(path)


def append_jsonl(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n")


def read_jsonl_tail(path: Path, limit: int = 100) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    events: list[dict[str, Any]] = []
    try:
        lines = path.read_text(encoding="utf-8-sig", errors="replace").splitlines()[-limit:]
    except OSError:
        return []
    for line in lines:
        if not line.strip():
            continue
        try:
            item = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(item, dict):
            events.append(item)
    return events


def as_int(value: Any, default: int = 0) -> int:
    try:
        if value is None or value == "":
            return default
        return int(float(value))
    except (TypeError, ValueError):
        return default


def as_float(value: Any, default: float = 0.0) -> float:
    try:
        if value is None or value == "":
            return default
        return float(value)
    except (TypeError, ValueError):
        return default


def text(value: Any, default: str = "") -> str:
    if value is None:
        return default
    raw = str(value).strip()
    return raw if raw else default


def nested(document: dict[str, Any], *keys: str) -> Any:
    value: Any = document
    for key in keys:
        if not isinstance(value, dict):
            return None
        value = value.get(key)
    return value


def normalize_status(value: Any) -> str:
    raw = text(value, "attention").lower()
    if raw in {"ok", "ready", "listo", "current", "idle", "running", "activo"}:
        return "ok"
    if raw in {"error", "critical", "blocked", "bloqueado", "fatal"}:
        return "blocked"
    return "attention"


def worst_status(values: list[str]) -> str:
    if "blocked" in values:
        return "blocked"
    if "attention" in values:
        return "attention"
    return "ok"


def make_stage(
    stage_id: str,
    name: str,
    status: str,
    detail: str,
    metrics: dict[str, Any] | None = None,
    evidence: list[str] | None = None,
) -> dict[str, Any]:
    return {
        "stageId": stage_id,
        "name": name,
        "status": normalize_status(status),
        "detail": detail,
        "metrics": metrics or {},
        "evidence": evidence or [],
    }


def cycle_id(created_at: str, stages: list[dict[str, Any]]) -> str:
    digest = hashlib.sha1(
        json.dumps({"createdAt": created_at, "stages": stages}, sort_keys=True, ensure_ascii=False).encode("utf-8")
    ).hexdigest()[:16]
    return f"cycle-{digest}"


def latest_action(runtime_dir: Path, limit: int) -> dict[str, Any]:
    events = read_jsonl_tail(runtime_dir / "action-events.jsonl", limit=limit)
    if not events:
        return {}
    item = events[-1]
    target = item.get("target") if isinstance(item.get("target"), dict) else {}
    verification = item.get("verification") if isinstance(item.get("verification"), dict) else {}
    return {
        "actionId": item.get("actionId"),
        "actionType": item.get("actionType"),
        "status": item.get("status"),
        "channelId": target.get("channelId"),
        "conversationId": target.get("conversationId"),
        "conversationTitle": target.get("conversationTitle") or target.get("chatRowTitle"),
        "verified": verification.get("verified"),
        "verificationSummary": verification.get("summary"),
        "confidence": verification.get("confidence"),
    }


def latest_decision(cognitive: dict[str, Any], memory: dict[str, Any]) -> dict[str, Any]:
    decision = nested(cognitive, "summary", "latestDecision")
    if isinstance(decision, dict) and decision:
        return decision
    memory_decision = nested(memory, "summary", "latestDecision")
    return memory_decision if isinstance(memory_decision, dict) else {}


def build_eyes_stage(states: dict[str, dict[str, Any]]) -> dict[str, Any]:
    vision = states["vision"]
    perception = states["perception"]
    interaction = states["interaction"]
    orchestrator = states["orchestrator"]
    metrics = orchestrator.get("metrics") if isinstance(orchestrator.get("metrics"), dict) else {}
    expected = as_int(metrics.get("expectedChannels"), 3)
    ready = as_int(metrics.get("cabinReadyChannels"))
    visible = as_int(metrics.get("visionWhatsAppWindows") or vision.get("visibleWindowCount"))
    perceived = as_int(metrics.get("perceptionChannels"))
    actionable = as_int(metrics.get("actionableTargets") or interaction.get("actionableTargets"))
    statuses = [
        normalize_status(orchestrator.get("status") if orchestrator else "attention"),
        normalize_status(vision.get("status") if vision else "attention"),
        normalize_status(perception.get("status") if perception else "attention"),
    ]
    status = worst_status(statuses)
    if expected and (visible < expected or perceived < expected):
        status = worst_status([status, "attention"])
    detail = text(
        orchestrator.get("summary"),
        f"Canales listos={ready}/{expected}; vision={visible}; lectura={perceived}; accionables={actionable}.",
    )
    return make_stage(
        "eyes",
        "Ojos",
        status,
        detail,
        {
            "expectedChannels": expected,
            "cabinReadyChannels": ready,
            "visionWhatsAppWindows": visible,
            "perceptionChannels": perceived,
            "actionableTargets": actionable,
            "framesCaptured": as_int(vision.get("framesCaptured") or vision.get("eventsWritten")),
            "messagesExtracted": as_int(perception.get("messagesExtracted")),
        },
        ["vision-health.json", "perception-health.json", "interaction-state.json", "orchestrator-state.json"],
    )


def build_timeline_stage(timeline: dict[str, Any]) -> dict[str, Any]:
    ingested = timeline.get("ingested") if isinstance(timeline.get("ingested"), dict) else {}
    messages = as_int(ingested.get("messages"))
    timelines = as_int(ingested.get("timelines"))
    rejected = as_int(ingested.get("rejectedEvents"))
    complete = as_int(ingested.get("completeTimelines"))
    status = normalize_status(timeline.get("status") if timeline else "attention")
    if messages <= 0 or timelines <= 0:
        status = worst_status([status, "attention"])
    detail = f"Unio {messages} mensajes en {timelines} historias; completas={complete}; rechazadas={rejected}."
    return make_stage(
        "timeline",
        "Timeline vivo + aprendizaje",
        status,
        detail,
        {
            "messages": messages,
            "timelines": timelines,
            "completeTimelines": complete,
            "rejectedEvents": rejected,
            "historyLimitDays": as_int(timeline.get("historyLimitDays"), 30),
        },
        ["timeline-state.json", "timeline-events.jsonl"],
    )


def build_brain_stage(cognitive: dict[str, Any], operating: dict[str, Any]) -> dict[str, Any]:
    cognitive_summary = cognitive.get("summary") if isinstance(cognitive.get("summary"), dict) else {}
    operating_summary = operating.get("summary") if isinstance(operating.get("summary"), dict) else {}
    decisions = as_int(cognitive_summary.get("decisions"))
    cases = as_int(operating_summary.get("cases"))
    tasks = as_int(operating_summary.get("openTasks"))
    status = worst_status([
        normalize_status(cognitive.get("status") if cognitive else "attention"),
        normalize_status(operating.get("status") if operating else "attention"),
    ])
    if decisions <= 0 and cases <= 0:
        status = worst_status([status, "attention"])
    latest = cognitive_summary.get("latestDecision") if isinstance(cognitive_summary.get("latestDecision"), dict) else {}
    latest_intent = text(latest.get("intent") if latest else "", "sin decision reciente")
    detail = f"Decisiones={decisions}; casos={cases}; tareas={tasks}; ultima intencion={latest_intent}."
    return make_stage(
        "brain",
        "Cerebro operativo",
        status,
        detail,
        {
            "decisions": decisions,
            "clientProfiles": as_int(cognitive_summary.get("clientProfiles")),
            "learningEvents": as_int(cognitive_summary.get("learningEvents")),
            "cases": cases,
            "openTasks": tasks,
        },
        ["cognitive-state.json", "operating-state.json", "cognitive-decision-events.jsonl", "decision-events.jsonl"],
    )


def build_memory_accounting_stage(memory: dict[str, Any], operating: dict[str, Any]) -> dict[str, Any]:
    memory_summary = memory.get("summary") if isinstance(memory.get("summary"), dict) else {}
    operating_summary = operating.get("summary") if isinstance(operating.get("summary"), dict) else {}
    memory_messages = as_int(memory_summary.get("memoryMessages"))
    learning = as_int(memory_summary.get("learningEvents"))
    accounting = as_int(memory_summary.get("accountingEvents"))
    drafts = as_int(operating_summary.get("accountingDrafts"))
    status = normalize_status(memory.get("status") if memory else "attention")
    if memory_messages <= 0:
        status = worst_status([status, "attention"])
    detail = f"Memoria={memory_messages} mensajes; aprendizaje={learning}; contabilidad={accounting}; borradores={drafts}."
    return make_stage(
        "memory_accounting",
        "Memoria y contabilidad",
        status,
        detail,
        {
            "memoryMessages": memory_messages,
            "signals": as_int(memory_summary.get("signals")),
            "learningEvents": learning,
            "knowledgeItems": as_int(memory_summary.get("knowledgeItems")),
            "accountingEvents": accounting,
            "accountingDrafts": drafts,
        },
        ["memory-state.json", "memory-core.sqlite", "accounting-events.jsonl", "learning-events.jsonl"],
    )


def build_supervisor_stage(supervisor: dict[str, Any]) -> dict[str, Any]:
    summary = supervisor.get("summary") if isinstance(supervisor.get("summary"), dict) else {}
    critical = as_int(summary.get("critical"))
    blocked = as_int(summary.get("blocked"))
    requires_human = as_int(summary.get("requiresHumanConfirmation"))
    safe = as_int(summary.get("safeNextActions"))
    status = normalize_status(supervisor.get("status") if supervisor else "attention")
    if critical > 0:
        status = "blocked"
    elif blocked > 0 or requires_human > 0:
        status = worst_status([status, "attention"])
    detail = f"Hallazgos={as_int(summary.get('findings'))}; humano={requires_human}; bloqueadas={blocked}; seguras={safe}."
    return make_stage(
        "supervisor",
        "Supervisor y seguridad",
        status,
        detail,
        {
            "findings": as_int(summary.get("findings")),
            "requiresHumanConfirmation": requires_human,
            "blocked": blocked,
            "critical": critical,
            "safeNextActions": safe,
        },
        ["supervisor-state.json"],
    )


def build_hands_stage(hands: dict[str, Any]) -> dict[str, Any]:
    executed = as_int(hands.get("actionsExecuted"))
    verified = as_int(hands.get("actionsVerified"))
    blocked = as_int(hands.get("actionsBlocked"))
    skipped = as_int(hands.get("actionsSkipped"))
    written = as_int(hands.get("actionsWritten"))
    status = normalize_status(hands.get("status") if hands else "attention")
    if skipped > max(1000, written * 10):
        status = worst_status([status, "attention"])
    detail = text(
        hands.get("lastSummary"),
        f"Ejecutadas={executed}; verificadas={verified}; bloqueadas={blocked}; saltadas={skipped}.",
    )
    return make_stage(
        "hands",
        "Manos y verificacion",
        status,
        detail,
        {
            "actionsPlanned": as_int(hands.get("actionsPlanned")),
            "actionsWritten": written,
            "actionsExecuted": executed,
            "actionsVerified": verified,
            "actionsBlocked": blocked,
            "actionsSkipped": skipped,
        },
        ["hands-state.json", "action-events.jsonl"],
    )


def build_life_controller_stage(life: dict[str, Any], agent_supervisor: dict[str, Any], update: dict[str, Any]) -> dict[str, Any]:
    status = worst_status([
        normalize_status(life.get("status") if life else "attention"),
        normalize_status(agent_supervisor.get("status") if agent_supervisor else "attention"),
    ])
    phase = text(life.get("phase"), "sin fase")
    summary = text(life.get("summary"), text(agent_supervisor.get("lastSummary"), "Control de vida sin estado reciente."))
    return make_stage(
        "life_controller",
        "AriadGSM Life Controller",
        status,
        f"{phase}: {summary}",
        {
            "desiredRunning": bool(life.get("desiredRunning")),
            "isRunning": bool(life.get("isRunning")),
            "restartCount": as_int(agent_supervisor.get("restartCount")),
            "currentVersion": text(update.get("currentVersion") or life.get("version")),
            "latestVersion": text(update.get("latestVersion")),
        },
        ["life-controller-state.json", "agent-supervisor-state.json", "update-state.json"],
    )


def build_workspace_guardian_stage(
    workspace_setup: dict[str, Any],
    cabin_manager: dict[str, Any],
    workspace_guardian: dict[str, Any],
    cabin: dict[str, Any],
    orchestrator: dict[str, Any],
) -> dict[str, Any]:
    setup_status = normalize_status(workspace_setup.get("status") if workspace_setup else "attention")
    manager_status = normalize_status(cabin_manager.get("status") if cabin_manager else "attention")
    guardian_status = normalize_status(workspace_guardian.get("status") if workspace_guardian else "attention")
    cabin_status = normalize_status(cabin.get("status") if cabin else "attention")
    status = worst_status([setup_status, manager_status, guardian_status, cabin_status])
    channels = cabin_manager.get("channels") if isinstance(cabin_manager.get("channels"), list) else []
    if not channels:
        channels = workspace_guardian.get("channels") if isinstance(workspace_guardian.get("channels"), list) else []
    ready = sum(1 for item in channels if isinstance(item, dict) and text(item.get("status")).lower() == "ready")
    blockers = cabin_manager.get("blockers") if isinstance(cabin_manager.get("blockers"), list) else []
    if not blockers:
        blockers = workspace_guardian.get("blockers") if isinstance(workspace_guardian.get("blockers"), list) else []
    detail = text(
        cabin_manager.get("summary"),
        text(
            workspace_guardian.get("summary"),
            text(workspace_setup.get("summary"), "Cabina pendiente de diagnostico."),
        ),
    )
    return make_stage(
        "workspace_guardian",
        "AriadGSM Workspace Guardian",
        status,
        detail,
        {
            "readyChannels": ready,
            "expectedChannels": max(3, len(channels)),
            "canStartDegraded": bool(cabin_manager.get("canStartDegraded")),
            "blockers": len(blockers),
            "orchestratorPhase": text(orchestrator.get("phase")),
        },
        ["cabin-manager-state.json", "workspace-setup-state.json", "workspace-guardian-state.json", "cabin-readiness.json"],
    )


def build_input_arbiter_stage(input_arbiter: dict[str, Any]) -> dict[str, Any]:
    phase = text(input_arbiter.get("phase"), "idle")
    status = normalize_status(input_arbiter.get("status") if input_arbiter else "ok")
    if phase in {"operator_control", "operator_cooldown"}:
        status = "ok"
    detail = text(input_arbiter.get("summary"), "Mouse disponible; manos esperan acciones verificadas.")
    return make_stage(
        "input_arbiter",
        "AriadGSM Input Arbiter",
        status,
        detail,
        {
            "phase": phase,
            "operatorIdleMs": as_int(input_arbiter.get("operatorIdleMs")),
            "requiredIdleMs": as_int(input_arbiter.get("requiredIdleMs"), 1200),
            "operatorHasPriority": bool(input_arbiter.get("operatorHasPriority")),
            "handsPausedOnly": bool(input_arbiter.get("handsPausedOnly")),
        },
        ["input-arbiter-state.json", "action-events.jsonl"],
    )


def build_reader_core_stage(states: dict[str, dict[str, Any]]) -> dict[str, Any]:
    stage = build_eyes_stage(states)
    stage["stageId"] = "reader_core"
    stage["name"] = "AriadGSM Reader Core"
    return stage


def build_verified_hands_stage(hands: dict[str, Any], input_arbiter: dict[str, Any]) -> dict[str, Any]:
    stage = build_hands_stage(hands)
    stage["stageId"] = "verified_hands"
    stage["name"] = "AriadGSM Verified Hands"
    stage["metrics"]["inputArbiterPhase"] = text(input_arbiter.get("phase"))
    stage["metrics"]["operatorHasPriority"] = bool(input_arbiter.get("operatorHasPriority"))
    if text(input_arbiter.get("phase")) == "operator_control":
        stage["detail"] = "Manos cedidas al operador; la IA sigue mirando y aprendiendo."
    return stage


def build_business_memory_stage(memory: dict[str, Any], timeline: dict[str, Any], cognitive: dict[str, Any], operating: dict[str, Any]) -> dict[str, Any]:
    stage = build_memory_accounting_stage(memory, operating)
    stage["stageId"] = "business_memory"
    stage["name"] = "AriadGSM Business Memory"
    timeline_ingested = timeline.get("ingested") if isinstance(timeline.get("ingested"), dict) else {}
    cognitive_summary = cognitive.get("summary") if isinstance(cognitive.get("summary"), dict) else {}
    stage["metrics"]["timelineMessages"] = as_int(timeline_ingested.get("messages"))
    stage["metrics"]["timelines"] = as_int(timeline_ingested.get("timelines"))
    stage["metrics"]["decisions"] = as_int(cognitive_summary.get("decisions"))
    stage["detail"] = (
        f"Memoria={stage['metrics']['memoryMessages']} mensajes; "
        f"historias={stage['metrics']['timelines']}; "
        f"decisiones={stage['metrics']['decisions']}; "
        f"contabilidad={stage['metrics']['accountingEvents']}."
    )
    return stage


def build_case_manager_stage(case_manager: dict[str, Any]) -> dict[str, Any]:
    summary = case_manager.get("summary") if isinstance(case_manager.get("summary"), dict) else {}
    open_cases = as_int(summary.get("openCases"))
    needs_human = as_int(summary.get("needsHuman"))
    ignored = as_int(summary.get("ignoredCases"))
    emitted = as_int(summary.get("emittedCaseEvents"))
    status = normalize_status(case_manager.get("status") if case_manager else "attention")
    if needs_human > 0:
        status = worst_status([status, "attention"])
    detail = text(
        nested(case_manager, "humanReport", "quePaso"),
        f"Casos abiertos={open_cases}; necesitan Bryams={needs_human}; ignorados={ignored}; eventos={emitted}.",
    )
    return make_stage(
        "case_manager",
        "AriadGSM Case Manager",
        status,
        detail,
        {
            "cases": as_int(summary.get("cases")),
            "openCases": open_cases,
            "needsHuman": needs_human,
            "ignoredCases": ignored,
            "accountingCases": as_int(summary.get("accountingCases")),
            "priceCases": as_int(summary.get("priceCases")),
            "serviceCases": as_int(summary.get("serviceCases")),
            "emittedCaseEvents": emitted,
        },
        ["case-manager-state.json", "case-manager.sqlite", "case-events.jsonl"],
    )


def build_channel_routing_stage(channel_routing: dict[str, Any]) -> dict[str, Any]:
    summary = channel_routing.get("summary") if isinstance(channel_routing.get("summary"), dict) else {}
    proposed = as_int(summary.get("proposedRoutes"))
    approved = as_int(summary.get("approvedRoutes"))
    needs_human = as_int(summary.get("needsHuman"))
    duplicates = as_int(summary.get("duplicateGroups"))
    status = normalize_status(channel_routing.get("status") if channel_routing else "attention")
    if needs_human > 0 or proposed > 0:
        status = worst_status([status, "attention"])
    detail = text(
        nested(channel_routing, "humanReport", "quePaso"),
        f"Rutas propuestas={proposed}; aprobadas={approved}; duplicados={duplicates}; humano={needs_human}.",
    )
    return make_stage(
        "channel_routing",
        "AriadGSM Channel Routing Brain",
        status,
        detail,
        {
            "casesRead": as_int(summary.get("casesRead")),
            "proposedRoutes": proposed,
            "approvedRoutes": approved,
            "rejectedRoutes": as_int(summary.get("rejectedRoutes")),
            "needsHuman": needs_human,
            "duplicateGroups": duplicates,
            "crossChannelCandidates": as_int(summary.get("crossChannelCandidates")),
            "emittedRouteEvents": as_int(summary.get("emittedRouteEvents")),
        },
        ["channel-routing-state.json", "channel-routing.sqlite", "route-events.jsonl"],
    )


def build_accounting_core_stage(accounting_core: dict[str, Any]) -> dict[str, Any]:
    summary = accounting_core.get("summary") if isinstance(accounting_core.get("summary"), dict) else {}
    records = as_int(summary.get("accountingRecords"))
    confirmed = as_int(summary.get("confirmedRecords"))
    needs_evidence = as_int(summary.get("needsEvidence"))
    needs_human = as_int(summary.get("needsHuman"))
    status = normalize_status(accounting_core.get("status") if accounting_core else "attention")
    if needs_evidence > 0 or needs_human > 0:
        status = worst_status([status, "attention"])
    detail = text(
        nested(accounting_core, "humanReport", "quePaso"),
        f"Registros={records}; confirmados={confirmed}; falta evidencia={needs_evidence}; humano={needs_human}.",
    )
    return make_stage(
        "accounting_core",
        "Contabilidad con evidencia",
        status,
        detail,
        {
            "accountingRecords": records,
            "drafts": as_int(summary.get("drafts")),
            "confirmedRecords": confirmed,
            "needsEvidence": needs_evidence,
            "needsHuman": needs_human,
            "payments": as_int(summary.get("payments")),
            "debts": as_int(summary.get("debts")),
            "refunds": as_int(summary.get("refunds")),
            "emittedAccountingEvents": as_int(summary.get("emittedAccountingEvents")),
        },
        ["accounting-core-state.json", "accounting-core.sqlite", "accounting-core-events.jsonl"],
    )


def make_step(
    step_id: str,
    name: str,
    status: str,
    objective: str,
    detail: str,
    inputs: list[str],
    outputs: list[str],
    metrics: dict[str, Any] | None = None,
    gate: dict[str, Any] | None = None,
) -> dict[str, Any]:
    return {
        "stepId": step_id,
        "name": name,
        "status": normalize_status(status),
        "objective": objective,
        "detail": detail,
        "inputs": inputs,
        "outputs": outputs,
        "metrics": metrics or {},
        "gate": gate or {},
    }


def step_status(values: list[str]) -> str:
    return worst_status([normalize_status(value) for value in values])


def permission_gate(states: dict[str, dict[str, Any]]) -> dict[str, Any]:
    supervisor = states["supervisor"]
    input_arbiter = states["input_arbiter"]
    summary = supervisor.get("summary") if isinstance(supervisor.get("summary"), dict) else {}
    operator_has_priority = bool(input_arbiter.get("operatorHasPriority"))
    input_phase = text(input_arbiter.get("phase"), "idle")
    critical = as_int(summary.get("critical"))
    blocked = as_int(summary.get("blocked"))
    requires_human = as_int(summary.get("requiresHumanConfirmation"))

    if operator_has_priority or input_phase in {"operator_control", "operator_cooldown"}:
        decision = "PAUSE_FOR_OPERATOR"
        reason = text(input_arbiter.get("summary"), "Bryams tiene prioridad de mouse/teclado.")
        can_hands_run = False
    elif critical > 0:
        decision = "BLOCK"
        reason = "Supervisor reporto hallazgos criticos."
        can_hands_run = False
    elif blocked > 0:
        decision = "BLOCK"
        reason = "Supervisor bloqueo acciones pendientes."
        can_hands_run = False
    elif requires_human > 0:
        decision = "ASK_HUMAN"
        reason = "Hay acciones que necesitan confirmacion humana."
        can_hands_run = False
    else:
        decision = "ALLOW"
        reason = "Permisos operativos suficientes para continuar el ciclo."
        can_hands_run = True

    return {
        "decision": decision,
        "reason": reason,
        "canHandsRun": can_hands_run,
        "canObserve": True,
        "canUnderstand": True,
        "canLearn": decision != "BLOCK",
        "operatorHasPriority": operator_has_priority,
        "inputPhase": input_phase,
        "requiresHumanConfirmation": requires_human,
        "blockedActions": blocked,
        "criticalFindings": critical,
    }


def build_operating_steps(
    states: dict[str, dict[str, Any]],
    stages: list[dict[str, Any]],
    gate: dict[str, Any],
) -> list[dict[str, Any]]:
    stage_map = {stage["stageId"]: stage for stage in stages}
    reader = stage_map.get("reader_core", {})
    memory_stage = stage_map.get("business_memory", {})
    hands_stage = stage_map.get("verified_hands", {})
    supervisor = states["supervisor"]
    timeline = states["timeline"]
    cognitive = states["cognitive"]
    operating = states["operating"]
    case_manager = states["case_manager"]
    channel_routing = states["channel_routing"]
    accounting_core = states["accounting_core"]
    memory = states["memory"]

    timeline_ingested = timeline.get("ingested") if isinstance(timeline.get("ingested"), dict) else {}
    cognitive_summary = cognitive.get("summary") if isinstance(cognitive.get("summary"), dict) else {}
    operating_summary = operating.get("summary") if isinstance(operating.get("summary"), dict) else {}
    case_summary = case_manager.get("summary") if isinstance(case_manager.get("summary"), dict) else {}
    route_summary = channel_routing.get("summary") if isinstance(channel_routing.get("summary"), dict) else {}
    accounting_core_summary = accounting_core.get("summary") if isinstance(accounting_core.get("summary"), dict) else {}
    memory_summary = memory.get("summary") if isinstance(memory.get("summary"), dict) else {}
    supervisor_summary = supervisor.get("summary") if isinstance(supervisor.get("summary"), dict) else {}

    messages = as_int(timeline_ingested.get("messages"))
    timelines = as_int(timeline_ingested.get("timelines"))
    decisions = as_int(cognitive_summary.get("decisions"))
    cases = as_int(operating_summary.get("cases"))
    case_manager_cases = as_int(case_summary.get("openCases"))
    route_decisions = as_int(route_summary.get("routeDecisions"))
    accounting_records = as_int(accounting_core_summary.get("accountingRecords"))
    actions_written = as_int(hands_stage.get("metrics", {}).get("actionsWritten"))
    actions_executed = as_int(hands_stage.get("metrics", {}).get("actionsExecuted"))
    actions_verified = as_int(hands_stage.get("metrics", {}).get("actionsVerified"))
    learning = as_int(memory_summary.get("learningEvents"))
    accounting = as_int(memory_summary.get("accountingEvents"))
    memory_messages = as_int(memory_summary.get("memoryMessages"))

    observe_status = normalize_status(reader.get("status"))
    understand_status = step_status([observe_status, "ok" if messages > 0 and timelines > 0 else "attention"])
    plan_status = "ok" if decisions > 0 or cases > 0 or case_manager_cases > 0 or route_decisions > 0 or accounting_records > 0 else "attention"
    if observe_status == "blocked":
        plan_status = "blocked"

    gate_decision = text(gate.get("decision"), "ALLOW")
    permission_status = "ok"
    if gate_decision in {"ASK_HUMAN", "PAUSE_FOR_OPERATOR"}:
        permission_status = "attention"
    if gate_decision == "BLOCK":
        permission_status = "blocked"

    if gate_decision == "BLOCK":
        act_status = "blocked"
        act_detail = "No actuo porque el gate del ciclo bloqueo acciones."
    elif gate_decision == "PAUSE_FOR_OPERATOR":
        act_status = "attention"
        act_detail = "No muevo mouse porque Bryams tiene prioridad de control."
    elif gate_decision == "ASK_HUMAN":
        act_status = "attention"
        act_detail = "Accion pendiente de confirmacion humana."
    elif actions_executed > 0:
        act_status = "ok"
        act_detail = f"Hands ejecuto {actions_executed} acciones autorizadas."
    elif actions_written > 0:
        act_status = "attention"
        act_detail = f"Hay {actions_written} acciones escritas pero no ejecutadas aun."
    else:
        act_status = "ok"
        act_detail = "No habia accion de mouse necesaria en este ciclo."

    if actions_executed > 0 and actions_verified >= actions_executed:
        verify_status = "ok"
        verify_detail = f"Verifique {actions_verified}/{actions_executed} acciones ejecutadas."
    elif actions_executed > 0:
        verify_status = "attention"
        verify_detail = f"Falta verificar {actions_executed - actions_verified} acciones."
    else:
        verify_status = "ok"
        verify_detail = "No hubo accion fisica que verificar."

    learn_status = "ok" if memory_messages > 0 or learning > 0 or accounting > 0 or accounting_records > 0 else "attention"
    if gate_decision == "BLOCK":
        learn_status = step_status([learn_status, "attention"])

    return [
        make_step(
            "observe",
            "Observar",
            observe_status,
            "Saber que ventanas/canales estan disponibles antes de razonar.",
            text(reader.get("detail"), "Lectura pendiente."),
            ["vision-health.json", "perception-health.json", "cabin-readiness.json"],
            ["canales observados", "mensajes visibles", "objetivos accionables"],
            reader.get("metrics") if isinstance(reader.get("metrics"), dict) else {},
        ),
        make_step(
            "understand",
            "Entender",
            understand_status,
            "Convertir observaciones en conversaciones y senales de negocio.",
            f"Timeline unio {messages} mensajes en {timelines} historias.",
            ["timeline-state.json", "conversation-events.jsonl"],
            ["historias", "senales utiles", "eventos rechazados"],
            {"messages": messages, "timelines": timelines, "rejectedEvents": as_int(timeline_ingested.get("rejectedEvents"))},
        ),
        make_step(
            "plan",
            "Planear",
            plan_status,
            "Elegir la siguiente mejor accion de negocio sin tocar aun la PC.",
            f"Decisiones={decisions}; casos={cases}; tareas={as_int(operating_summary.get('openTasks'))}.",
            ["cognitive-state.json", "operating-state.json", "memory-state.json"],
            ["decision propuesta", "caso actualizado", "siguiente accion"],
            {
                "decisions": decisions,
                "cases": cases,
                "openTasks": as_int(operating_summary.get("openTasks")),
                "accountingDrafts": as_int(operating_summary.get("accountingDrafts")),
            },
        ),
        make_step(
            "request_permission",
            "Pedir permiso",
            permission_status,
            "Autorizar, limitar, pausar o bloquear antes de actuar.",
            text(gate.get("reason"), "Gate evaluado."),
            ["supervisor-state.json", "input-arbiter-state.json"],
            ["permissionGate", "allowedEngines", "humanRequired"],
            {
                "requiresHumanConfirmation": as_int(supervisor_summary.get("requiresHumanConfirmation")),
                "blocked": as_int(supervisor_summary.get("blocked")),
                "critical": as_int(supervisor_summary.get("critical")),
            },
            gate,
        ),
        make_step(
            "act",
            "Actuar",
            act_status,
            "Ejecutar solo acciones autorizadas por el gate.",
            act_detail,
            ["hands-state.json", "action-events.jsonl", "autonomous-cycle-directives.json"],
            ["accion ejecutada", "accion cedida", "accion bloqueada"],
            {
                "actionsWritten": actions_written,
                "actionsExecuted": actions_executed,
                "actionsBlocked": as_int(hands_stage.get("metrics", {}).get("actionsBlocked")),
                "actionsSkipped": as_int(hands_stage.get("metrics", {}).get("actionsSkipped")),
            },
        ),
        make_step(
            "verify",
            "Verificar",
            verify_status,
            "Confirmar que la accion logro el objetivo correcto.",
            verify_detail,
            ["hands-state.json", "perception-health.json"],
            ["accion verificada", "fallo explicado", "recuperacion requerida"],
            {"actionsExecuted": actions_executed, "actionsVerified": actions_verified},
        ),
        make_step(
            "learn",
            "Aprender",
            learn_status,
            "Guardar experiencia como hecho, sospecha, aprendizaje o borrador.",
            f"Memoria={memory_messages}; aprendizajes={learning}; contabilidad={accounting}.",
            ["memory-state.json", "learning-events.jsonl", "accounting-events.jsonl"],
            ["memoria actualizada", "aprendizaje candidato", "borrador contable"],
            {"memoryMessages": memory_messages, "learningEvents": learning, "accountingEvents": accounting},
        ),
        make_step(
            "report",
            "Reportar",
            "ok",
            "Explicar el estado a Bryams en lenguaje humano.",
            "Reporte humano generado para la interfaz.",
            ["autonomous-cycle-state.json"],
            ["autonomous-cycle-report.json", "resumen humano"],
            {},
        ),
    ]


def build_directives(
    cycle_identifier: str,
    created_at: str,
    gate: dict[str, Any],
    steps: list[dict[str, Any]],
    next_actions: list[dict[str, Any]],
) -> dict[str, Any]:
    gate_decision = text(gate.get("decision"), "ALLOW")
    hands_allowed = bool(gate.get("canHandsRun"))
    return {
        "engine": "ariadgsm_autonomous_cycle",
        "cycleId": cycle_identifier,
        "updatedAt": created_at,
        "gateDecision": gate_decision,
        "gateReason": text(gate.get("reason")),
        "allowedEngines": {
            "vision": True,
            "perception": True,
            "timeline": True,
            "cognitive": gate_decision != "BLOCK",
            "memory": bool(gate.get("canLearn")),
            "supervisor": True,
            "hands": hands_allowed,
            "domainEvents": True,
        },
        "handsPolicy": {
            "mode": "run" if hands_allowed else "pause",
            "reason": text(gate.get("reason")),
            "operatorHasPriority": bool(gate.get("operatorHasPriority")),
        },
        "nextStep": next((step["stepId"] for step in steps if step["status"] != "ok"), "observe"),
        "nextActions": next_actions,
    }


def build_human_report(
    status: str,
    phase: str,
    summary: str,
    gate: dict[str, Any],
    steps: list[dict[str, Any]],
    blockers: list[dict[str, Any]],
    next_actions: list[dict[str, Any]],
) -> dict[str, Any]:
    attention_steps = [step for step in steps if step.get("status") != "ok"]
    headline = "Estoy trabajando contigo"
    if status == "blocked":
        headline = "Necesito tu ayuda para seguir"
    elif attention_steps:
        headline = "Estoy lista, pero estoy cuidando un punto"

    what_happened = [
        f"{step['name']}: {step['detail']}"
        for step in steps
        if step["stepId"] in {"observe", "understand", "plan", "learn"}
    ]
    needs = []
    gate_decision = text(gate.get("decision"), "ALLOW")
    if gate_decision != "ALLOW":
        needs.append(text(gate.get("reason"), "Necesito revision antes de actuar."))
    needs.extend(text(item.get("detail")) for item in blockers if text(item.get("detail")))
    if not needs:
        needs.append("No necesito intervencion inmediata.")

    return {
        "headline": headline,
        "phase": phase,
        "summary": summary,
        "gateDecision": gate_decision,
        "whatHappened": what_happened[:6],
        "whatINeedFromBryams": needs[:6],
        "nextActions": next_actions[:6],
        "safeToContinue": status != "blocked" and gate_decision in {"ALLOW", "ALLOW_WITH_LIMIT"},
    }


def collect_blockers(states: dict[str, dict[str, Any]], stages: list[dict[str, Any]]) -> list[dict[str, Any]]:
    blockers: list[dict[str, Any]] = []
    orchestrator = states["orchestrator"]
    orchestrator_blockers = orchestrator.get("blockers") if isinstance(orchestrator.get("blockers"), list) else []
    for item in orchestrator_blockers:
        if isinstance(item, dict):
            blockers.append(
                {
                    "source": "orchestrator",
                    "code": item.get("code", "orchestrator_blocker"),
                    "severity": item.get("severity", "warning"),
                    "channelId": item.get("channelId", ""),
                    "detail": item.get("detail", ""),
                }
            )
    supervisor = states["supervisor"]
    latest_findings = supervisor.get("latestFindings") if isinstance(supervisor.get("latestFindings"), list) else []
    for item in latest_findings:
        if not isinstance(item, dict):
            continue
        reason = text(item.get("reason"))
        if "allowed for execution" in reason.lower():
            continue
        if item.get("severity") == "critical" or item.get("allowed") is False:
            blockers.append(
                {
                    "source": "supervisor",
                    "code": item.get("sourceId", "supervisor_finding"),
                    "severity": item.get("severity", "review"),
                    "channelId": "",
                    "detail": reason,
                }
            )
    for stage in stages:
        if stage.get("status") == "blocked":
            blockers.append(
                {
                    "source": "autonomous_cycle",
                    "code": f"{stage.get('stageId')}_blocked",
                    "severity": "error",
                    "channelId": "",
                    "detail": stage.get("detail", ""),
                }
            )
    return blockers[:12]


def collect_next_actions(states: dict[str, dict[str, Any]], hands_stage: dict[str, Any]) -> list[dict[str, Any]]:
    actions: list[dict[str, Any]] = []
    orchestrator = states["orchestrator"]
    recommendations = orchestrator.get("recommendations") if isinstance(orchestrator.get("recommendations"), list) else []
    for item in recommendations:
        if isinstance(item, dict):
            actions.append(
                {
                    "source": "orchestrator",
                    "kind": item.get("code", "recommendation"),
                    "channelId": item.get("channelId", ""),
                    "detail": item.get("detail", ""),
                    "priority": "high",
                }
            )
    supervisor = states["supervisor"]
    safe_next_actions = supervisor.get("safeNextActions") if isinstance(supervisor.get("safeNextActions"), list) else []
    for item in safe_next_actions:
        if isinstance(item, dict):
            actions.append(
                {
                    "source": "supervisor",
                    "kind": item.get("proposedAction", "safe_action"),
                    "sourceId": item.get("sourceId", ""),
                    "detail": item.get("reason", ""),
                    "confidence": as_float(item.get("confidence")),
                    "priority": "normal",
                }
            )
    skipped = as_int(nested(hands_stage, "metrics", "actionsSkipped"))
    written = as_int(nested(hands_stage, "metrics", "actionsWritten"))
    if skipped > max(1000, written * 10):
        actions.insert(
            0,
            {
                "source": "autonomous_cycle",
                "kind": "compact_action_queue",
                "channelId": "",
                "detail": "Compactar eventos antiguos para que Hands no repase cola vieja.",
                "priority": "high",
            },
        )
    return actions[:10]


def derive_phase(status: str, stages: list[dict[str, Any]], steps: list[dict[str, Any]], gate: dict[str, Any]) -> str:
    if status == "blocked":
        return "blocked"
    if bool(gate.get("operatorHasPriority")):
        return "operator_control"
    gate_decision = text(gate.get("decision"), "ALLOW")
    if gate_decision == "ASK_HUMAN":
        return "permission_wait"
    for step in steps:
        if step.get("status") != "ok":
            return text(step.get("stepId"), "attention")
    stage_map = {stage["stageId"]: stage for stage in stages}
    if as_int(nested(stage_map.get("verified_hands", {}), "metrics", "actionsExecuted")):
        return "verify"
    if as_int(nested(stage_map.get("business_memory", {}), "metrics", "learningEvents")) > 0:
        return "learn"
    return "waiting"


def build_summary(status: str, states: dict[str, dict[str, Any]], stages: list[dict[str, Any]], steps: list[dict[str, Any]], gate: dict[str, Any]) -> str:
    stage_map = {stage["stageId"]: stage for stage in stages}
    expected = as_int(nested(stage_map.get("reader_core", {}), "metrics", "expectedChannels"), 3)
    perceived = as_int(nested(stage_map.get("reader_core", {}), "metrics", "perceptionChannels"))
    messages = as_int(nested(stage_map.get("business_memory", {}), "metrics", "timelineMessages"))
    decisions = as_int(nested(stage_map.get("business_memory", {}), "metrics", "decisions"))
    learning = as_int(nested(stage_map.get("business_memory", {}), "metrics", "learningEvents"))
    accounting = as_int(nested(stage_map.get("business_memory", {}), "metrics", "accountingEvents"))
    routes = as_int(nested(stage_map.get("channel_routing", {}), "metrics", "proposedRoutes"))
    accounting_confirmed = as_int(nested(stage_map.get("accounting_core", {}), "metrics", "confirmedRecords"))
    executed = as_int(nested(stage_map.get("verified_hands", {}), "metrics", "actionsExecuted"))
    label = {"ok": "estable", "attention": "con avisos", "blocked": "bloqueado"}[status]
    gate_decision = text(gate.get("decision"), "ALLOW")
    next_step = next((step["name"] for step in steps if step.get("status") != "ok"), "observar de nuevo")
    return (
        f"Ciclo autonomo {label}: WhatsApp {perceived}/{expected}, "
        f"mensajes={messages}, decisiones={decisions}, aprendizaje={learning}, "
        f"contabilidad={accounting}, confirmados={accounting_confirmed}, rutas={routes}, acciones={executed}, gate={gate_decision}, siguiente={next_step}."
    )


def run_autonomous_cycle_once(
    runtime_dir: Path | str = RUNTIME_DIR,
    state_file: Path | str | None = None,
    event_file: Path | str | None = None,
    directives_file: Path | str | None = None,
    report_file: Path | str | None = None,
    limit: int = 250,
    append_event: bool = True,
    trigger: str = "checkpoint",
) -> dict[str, Any]:
    runtime = Path(runtime_dir)
    state_path = Path(state_file) if state_file is not None else runtime / "autonomous-cycle-state.json"
    event_path = Path(event_file) if event_file is not None else runtime / "autonomous-cycle-events.jsonl"
    directives_path = Path(directives_file) if directives_file is not None else runtime / "autonomous-cycle-directives.json"
    report_path = Path(report_file) if report_file is not None else runtime / "autonomous-cycle-report.json"
    states = {
        "vision": read_json(runtime / "vision-health.json"),
        "perception": read_json(runtime / "perception-health.json"),
        "interaction": read_json(runtime / "interaction-state.json"),
        "orchestrator": read_json(runtime / "orchestrator-state.json"),
        "timeline": read_json(runtime / "timeline-state.json"),
        "cognitive": read_json(runtime / "cognitive-state.json"),
        "operating": read_json(runtime / "operating-state.json"),
        "case_manager": read_json(runtime / "case-manager-state.json"),
        "channel_routing": read_json(runtime / "channel-routing-state.json"),
        "accounting_core": read_json(runtime / "accounting-core-state.json"),
        "memory": read_json(runtime / "memory-state.json"),
        "supervisor": read_json(runtime / "supervisor-state.json"),
        "hands": read_json(runtime / "hands-state.json"),
        "input_arbiter": read_json(runtime / "input-arbiter-state.json"),
        "life": read_json(runtime / "life-controller-state.json"),
        "status_bus": read_json(runtime / "status-bus-state.json"),
        "agent_supervisor": read_json(runtime / "agent-supervisor-state.json"),
        "update": read_json(runtime / "update-state.json"),
        "workspace_setup": read_json(runtime / "workspace-setup-state.json"),
        "cabin_manager": read_json(runtime / "cabin-manager-state.json"),
        "workspace_guardian": read_json(runtime / "workspace-guardian-state.json"),
        "cabin": read_json(runtime / "cabin-readiness.json"),
    }

    stages = [
        build_life_controller_stage(states["life"], states["agent_supervisor"], states["update"]),
        build_workspace_guardian_stage(states["workspace_setup"], states["cabin_manager"], states["workspace_guardian"], states["cabin"], states["orchestrator"]),
        build_input_arbiter_stage(states["input_arbiter"]),
        build_reader_core_stage(states),
        build_case_manager_stage(states["case_manager"]),
        build_channel_routing_stage(states["channel_routing"]),
        build_accounting_core_stage(states["accounting_core"]),
        build_business_memory_stage(states["memory"], states["timeline"], states["cognitive"], states["operating"]),
        build_verified_hands_stage(states["hands"], states["input_arbiter"]),
    ]
    gate = permission_gate(states)
    steps = build_operating_steps(states, stages, gate)
    status = worst_status([stage["status"] for stage in stages] + [step["status"] for step in steps])
    focus = {
        "decision": latest_decision(states["cognitive"], states["memory"]),
        "lastAction": latest_action(runtime, limit),
        "input": {
            "phase": text(states["input_arbiter"].get("phase")),
            "summary": text(states["input_arbiter"].get("summary")),
        },
        "statusBus": {
            "phase": text(states["status_bus"].get("phase")),
            "summary": text(states["status_bus"].get("summary")),
        },
    }
    phase = "start" if trigger == "start" and status != "blocked" else derive_phase(status, stages, steps, gate)
    blockers = collect_blockers(states, stages)
    next_actions = collect_next_actions(states, stages[-1])
    created_at = utc_now()
    initial_cycle_id = cycle_id(created_at, stages + steps)
    directives = build_directives(initial_cycle_id, created_at, gate, steps, next_actions)
    summary = build_summary(status, states, stages, steps, gate)
    human_report = build_human_report(status, phase, summary, gate, steps, blockers, next_actions)
    cycle = {
        "engine": "ariadgsm_autonomous_cycle",
        "status": status,
        "updatedAt": created_at,
        "cycleId": initial_cycle_id,
        "trigger": trigger,
        "phase": phase,
        "summary": summary,
        "loopContract": {
            "version": "0.8.0",
            "order": ["observe", "understand", "plan", "request_permission", "act", "verify", "learn", "report"],
            "sourceOfTruth": "autonomous-cycle-state.json",
            "eventContract": "autonomous_cycle_event",
        },
        "steps": steps,
        "stages": stages,
        "permissionGate": gate,
        "directives": directives,
        "humanReport": human_report,
        "currentFocus": focus,
        "blockers": blockers,
        "nextActions": next_actions,
        "metrics": {
            "stagesOk": sum(1 for stage in stages if stage["status"] == "ok"),
            "stagesAttention": sum(1 for stage in stages if stage["status"] == "attention"),
            "stagesBlocked": sum(1 for stage in stages if stage["status"] == "blocked"),
            "stepsOk": sum(1 for step in steps if step["status"] == "ok"),
            "stepsAttention": sum(1 for step in steps if step["status"] == "attention"),
            "stepsBlocked": sum(1 for step in steps if step["status"] == "blocked"),
            "blockers": len(blockers),
            "nextActions": len(next_actions),
        },
        "sourceFiles": {
            "runtimeDir": str(runtime),
            "eventFile": str(event_path),
            "stateFile": str(state_path),
            "directivesFile": str(directives_path),
            "reportFile": str(report_path),
        },
    }
    event = {
        "eventType": "autonomous_cycle_event",
        "cycleId": cycle["cycleId"],
        "createdAt": created_at,
        "status": status,
        "phase": phase,
        "summary": cycle["summary"],
        "trigger": trigger,
        "steps": steps,
        "stages": stages,
        "permissionGate": gate,
        "directives": directives,
        "humanReport": human_report,
        "currentFocus": focus,
        "blockers": blockers,
        "nextActions": next_actions,
        "metrics": cycle["metrics"],
    }
    write_json_atomic(state_path, cycle)
    write_json_atomic(directives_path, directives)
    write_json_atomic(report_path, human_report)
    if append_event:
        append_jsonl(event_path, event)
    return cycle


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="AriadGSM Autonomous Cycle")
    parser.add_argument("--runtime-dir", type=Path, default=RUNTIME_DIR)
    parser.add_argument("--state-file", type=Path, default=None)
    parser.add_argument("--event-file", type=Path, default=None)
    parser.add_argument("--directives-file", type=Path, default=None)
    parser.add_argument("--report-file", type=Path, default=None)
    parser.add_argument("--limit", type=int, default=250)
    parser.add_argument("--trigger", choices=["start", "checkpoint", "recovery"], default="checkpoint")
    parser.add_argument("--no-event", action="store_true")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)

    state = run_autonomous_cycle_once(
        args.runtime_dir,
        state_file=args.state_file,
        event_file=args.event_file,
        directives_file=args.directives_file,
        report_file=args.report_file,
        limit=args.limit,
        append_event=not args.no_event,
        trigger=args.trigger,
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=True))
    else:
        print(state["summary"])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
