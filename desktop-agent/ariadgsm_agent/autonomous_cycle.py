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
        if item.get("severity") == "critical" or item.get("allowed") is False:
            blockers.append(
                {
                    "source": "supervisor",
                    "code": item.get("sourceId", "supervisor_finding"),
                    "severity": item.get("severity", "review"),
                    "channelId": "",
                    "detail": item.get("reason", ""),
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


def derive_phase(status: str, stages: list[dict[str, Any]], focus: dict[str, Any]) -> str:
    if status == "blocked":
        return "blocked"
    stage_map = {stage["stageId"]: stage for stage in stages}
    if stage_map.get("hands", {}).get("metrics", {}).get("actionsExecuted", 0):
        return "acting"
    if focus.get("decision"):
        return "reasoning"
    if as_int(nested(stage_map.get("memory_accounting", {}), "metrics", "learningEvents")) > 0:
        return "learning"
    if as_int(nested(stage_map.get("timeline", {}), "metrics", "messages")) > 0:
        return "observing"
    return "waiting"


def build_summary(status: str, states: dict[str, dict[str, Any]], stages: list[dict[str, Any]]) -> str:
    stage_map = {stage["stageId"]: stage for stage in stages}
    expected = as_int(nested(stage_map.get("eyes", {}), "metrics", "expectedChannels"), 3)
    perceived = as_int(nested(stage_map.get("eyes", {}), "metrics", "perceptionChannels"))
    messages = as_int(nested(stage_map.get("timeline", {}), "metrics", "messages"))
    decisions = as_int(nested(stage_map.get("brain", {}), "metrics", "decisions"))
    learning = as_int(nested(stage_map.get("memory_accounting", {}), "metrics", "learningEvents"))
    accounting = as_int(nested(stage_map.get("memory_accounting", {}), "metrics", "accountingEvents"))
    executed = as_int(nested(stage_map.get("hands", {}), "metrics", "actionsExecuted"))
    label = {"ok": "estable", "attention": "con avisos", "blocked": "bloqueado"}[status]
    return (
        f"Ciclo autonomo {label}: WhatsApp {perceived}/{expected}, "
        f"mensajes={messages}, decisiones={decisions}, aprendizaje={learning}, "
        f"contabilidad={accounting}, acciones={executed}."
    )


def run_autonomous_cycle_once(
    runtime_dir: Path | str = RUNTIME_DIR,
    state_file: Path | str | None = None,
    event_file: Path | str | None = None,
    limit: int = 250,
    append_event: bool = True,
) -> dict[str, Any]:
    runtime = Path(runtime_dir)
    state_path = Path(state_file) if state_file is not None else runtime / "autonomous-cycle-state.json"
    event_path = Path(event_file) if event_file is not None else runtime / "autonomous-cycle-events.jsonl"
    states = {
        "vision": read_json(runtime / "vision-health.json"),
        "perception": read_json(runtime / "perception-health.json"),
        "interaction": read_json(runtime / "interaction-state.json"),
        "orchestrator": read_json(runtime / "orchestrator-state.json"),
        "timeline": read_json(runtime / "timeline-state.json"),
        "cognitive": read_json(runtime / "cognitive-state.json"),
        "operating": read_json(runtime / "operating-state.json"),
        "memory": read_json(runtime / "memory-state.json"),
        "supervisor": read_json(runtime / "supervisor-state.json"),
        "hands": read_json(runtime / "hands-state.json"),
    }

    stages = [
        build_eyes_stage(states),
        build_timeline_stage(states["timeline"]),
        build_brain_stage(states["cognitive"], states["operating"]),
        build_memory_accounting_stage(states["memory"], states["operating"]),
        build_supervisor_stage(states["supervisor"]),
        build_hands_stage(states["hands"]),
    ]
    status = worst_status([stage["status"] for stage in stages])
    focus = {
        "decision": latest_decision(states["cognitive"], states["memory"]),
        "lastAction": latest_action(runtime, limit),
    }
    phase = derive_phase(status, stages, focus)
    blockers = collect_blockers(states, stages)
    next_actions = collect_next_actions(states, stages[-1])
    created_at = utc_now()
    cycle = {
        "engine": "ariadgsm_autonomous_cycle",
        "status": status,
        "updatedAt": created_at,
        "cycleId": cycle_id(created_at, stages),
        "phase": phase,
        "summary": build_summary(status, states, stages),
        "stages": stages,
        "currentFocus": focus,
        "blockers": blockers,
        "nextActions": next_actions,
        "metrics": {
            "stagesOk": sum(1 for stage in stages if stage["status"] == "ok"),
            "stagesAttention": sum(1 for stage in stages if stage["status"] == "attention"),
            "stagesBlocked": sum(1 for stage in stages if stage["status"] == "blocked"),
            "blockers": len(blockers),
            "nextActions": len(next_actions),
        },
        "sourceFiles": {
            "runtimeDir": str(runtime),
            "eventFile": str(event_path),
            "stateFile": str(state_path),
        },
    }
    event = {
        "eventType": "autonomous_cycle_event",
        "cycleId": cycle["cycleId"],
        "createdAt": created_at,
        "status": status,
        "phase": phase,
        "summary": cycle["summary"],
        "stages": stages,
        "currentFocus": focus,
        "blockers": blockers,
        "nextActions": next_actions,
        "metrics": cycle["metrics"],
    }
    write_json_atomic(state_path, cycle)
    if append_event:
        append_jsonl(event_path, event)
    return cycle


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="AriadGSM Autonomous Cycle")
    parser.add_argument("--runtime-dir", type=Path, default=RUNTIME_DIR)
    parser.add_argument("--state-file", type=Path, default=None)
    parser.add_argument("--event-file", type=Path, default=None)
    parser.add_argument("--limit", type=int, default=250)
    parser.add_argument("--no-event", action="store_true")
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)

    state = run_autonomous_cycle_once(
        args.runtime_dir,
        state_file=args.state_file,
        event_file=args.event_file,
        limit=args.limit,
        append_event=not args.no_event,
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=True))
    else:
        print(state["summary"])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
