from __future__ import annotations

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


VERSION = "0.8.18"
ENGINE = "ariadgsm_runtime_kernel"
CONTRACT = "runtime_kernel_state"


ENGINE_FILES: tuple[tuple[str, str, str, str], ...] = (
    ("vision", "Vision", "worker", "vision-health.json"),
    ("perception", "Perception", "worker", "perception-health.json"),
    ("interaction", "Interaction", "worker", "interaction-state.json"),
    ("orchestrator", "Orchestrator", "worker", "orchestrator-state.json"),
    ("reader_core", "Reader Core", "python_core", "reader-core-state.json"),
    ("timeline", "Timeline", "python_core", "timeline-state.json"),
    ("cognitive", "Cognitive", "python_core", "cognitive-state.json"),
    ("operating", "Operating", "python_core", "operating-state.json"),
    ("case_manager", "Case Manager", "python_core", "case-manager-state.json"),
    ("channel_routing", "Channel Routing", "python_core", "channel-routing-state.json"),
    ("accounting_core", "Accounting Core", "python_core", "accounting-core-state.json"),
    ("memory", "Memory", "python_core", "memory-state.json"),
    ("business_brain", "Business Brain", "python_core", "business-brain-state.json"),
    ("tool_registry", "Tool Registry", "python_core", "tool-registry-state.json"),
    ("cloud_sync", "Cloud Sync", "python_core", "cloud-sync-state.json"),
    ("trust_safety", "Trust & Safety", "python_core", "trust-safety-state.json"),
    ("hands", "Hands", "worker", "hands-state.json"),
    ("input_arbiter", "Input Arbiter", "worker", "input-arbiter-state.json"),
    ("supervisor", "Business Supervisor", "python_core", "supervisor-state.json"),
    ("autonomous_cycle", "Autonomous Cycle", "python_core", "autonomous-cycle-state.json"),
    ("cabin_authority", "Cabin Authority", "control", "cabin-authority-state.json"),
    ("life_controller", "Life Controller", "control", "life-controller-state.json"),
    ("agent_supervisor", "Agent Supervisor", "control", "agent-supervisor-state.json"),
)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def read_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
        return data if isinstance(data, dict) else {}
    except Exception as exc:
        return {"status": "error", "summary": f"No pude leer {path.name}: {exc}", "lastError": str(exc)}


def parse_dt(value: Any) -> datetime | None:
    if not isinstance(value, str) or not value:
        return None
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(timezone.utc)
    except ValueError:
        return None


def age_ms(value: Any, now: datetime) -> int | None:
    parsed = parse_dt(value)
    if parsed is None:
        return None
    return max(0, int((now - parsed).total_seconds() * 1000))


def text(value: Any, default: str = "") -> str:
    if value is None:
        return default
    return str(value)


def number(value: Any, default: int = 0) -> int:
    try:
        if isinstance(value, bool):
            return default
        return int(value)
    except (TypeError, ValueError):
        return default


def classify_lifecycle(state: dict[str, Any], *, desired_running: bool, process_running: bool, now: datetime) -> str:
    status = text(state.get("status")).lower()
    updated_age = age_ms(state.get("updatedAt") or state.get("observedAt"), now)
    last_error = text(state.get("lastError") or state.get("error")).strip()
    if status in {"blocked"}:
        return "blocked"
    if status in {"error", "failed"} or last_error:
        return "degraded" if process_running else "dead"
    if process_running and updated_age is not None and updated_age > 45_000:
        return "degraded"
    if process_running and status in {"ok", "ready", "idle", "running", "current"}:
        return "running"
    if process_running and not state:
        return "starting"
    if desired_running and not process_running:
        return "restarting"
    if not desired_running:
        return "stopped"
    return "unknown"


def build_engine(
    runtime_dir: Path,
    engine_id: str,
    name: str,
    kind: str,
    file_name: str,
    *,
    desired_running: bool,
    active_processes: set[str],
    now: datetime,
) -> dict[str, Any]:
    state = read_json(runtime_dir / file_name)
    status = text(state.get("status"), "missing" if not state else "ok")
    summary = (
        text(state.get("summary"))
        or text(state.get("lastSummary"))
        or text(state.get("detail"))
        or ("Sin estado publicado." if not state else "Estado recibido.")
    )
    process_running = engine_id in active_processes or name.lower().replace(" ", "_") in active_processes
    lifecycle = classify_lifecycle(state, desired_running=desired_running, process_running=process_running, now=now)
    return {
        "engineId": engine_id,
        "name": name,
        "kind": kind,
        "lifecycle": lifecycle,
        "status": status,
        "sourceFile": file_name,
        "updatedAt": state.get("updatedAt") or state.get("observedAt") or "",
        "ageMs": age_ms(state.get("updatedAt") or state.get("observedAt"), now),
        "processRunning": process_running,
        "summary": summary,
        "lastError": text(state.get("lastError") or state.get("error")),
    }


def incident(
    source: str,
    code: str,
    summary: str,
    detail: str,
    *,
    severity: str = "warning",
    recovery: str = "report",
    requires_human: bool = False,
) -> dict[str, Any]:
    created = utc_now()
    return {
        "incidentId": f"{source}-{code}-{abs(hash((source, code, detail))) % 10_000_000}",
        "severity": severity,
        "source": source,
        "code": code,
        "summary": summary,
        "detail": detail,
        "detectedAt": created,
        "recoveryAction": recovery,
        "requiresHuman": requires_human,
    }


def read_recent_log_lines(path: Path, limit: int = 160) -> list[str]:
    if not path.exists():
        return []
    try:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
        return lines[-limit:]
    except Exception:
        return []


def incidents_from_logs(log_lines: list[str]) -> list[dict[str, Any]]:
    incidents: list[dict[str, Any]] = []
    for line in log_lines[-80:]:
        lower = line.lower()
        if "unauthorizedaccessexception" in lower or "access to the path is denied" in lower:
            incidents.append(
                incident(
                    "vision",
                    "state_write_denied",
                    "Windows nego escritura a un estado local.",
                    line,
                    severity="error",
                    recovery="mantener motor vivo, reintentar escritura y publicar incidente",
                    requires_human=False,
                )
            )
        elif "was not running. restarting from reliability supervisor" in lower:
            source = line.split(" was not running", 1)[0].split(" ")[-1].lower()
            incidents.append(
                incident(
                    source,
                    "engine_restart",
                    "Un motor cayo y el supervisor lo reinicio.",
                    line,
                    severity="warning",
                    recovery="supervisor_restart",
                    requires_human=False,
                )
            )
        elif "zone_covered" in lower or "cabina con atencion" in lower and "covered" in lower:
            incidents.append(
                incident(
                    "cabin_authority",
                    "workspace_covered",
                    "Una zona WhatsApp esta cubierta por otra ventana.",
                    line,
                    severity="warning",
                    recovery="observar y pedir zona libre sin cerrar ventanas del operador",
                    requires_human=True,
                )
            )
    deduped: dict[tuple[str, str, str], dict[str, Any]] = {}
    for item in incidents:
        deduped[(item["source"], item["code"], item["detail"][-120:])] = item
    return list(deduped.values())[-12:]


def build_runtime_kernel_state(
    runtime_dir: Path,
    *,
    log_file: Path | None = None,
    desired_running: bool | None = None,
    is_running: bool | None = None,
    active_processes: set[str] | None = None,
) -> dict[str, Any]:
    runtime = runtime_dir.resolve()
    now = datetime.now(timezone.utc)
    life = read_json(runtime / "life-controller-state.json")
    agent_supervisor = read_json(runtime / "agent-supervisor-state.json")
    input_arbiter = read_json(runtime / "input-arbiter-state.json")
    cabin = read_json(runtime / "cabin-authority-state.json")
    trust = read_json(runtime / "trust-safety-state.json")

    if desired_running is None:
        desired_running = bool(life.get("desiredRunning"))
    if is_running is None:
        is_running = bool(life.get("isRunning"))

    active = active_processes or set()
    for worker in agent_supervisor.get("workers") or []:
        if isinstance(worker, dict) and worker.get("running"):
            name = text(worker.get("name")).lower().replace(" ", "_")
            active.add(name)
    if agent_supervisor.get("coreLoopRunning"):
        active.update(
            {
                "reader_core",
                "timeline",
                "cognitive",
                "operating",
                "case_manager",
                "channel_routing",
                "accounting_core",
                "memory",
                "business_brain",
                "tool_registry",
                "cloud_sync",
                "trust_safety",
                "supervisor",
                "autonomous_cycle",
            }
        )

    engines = [
        build_engine(
            runtime,
            engine_id,
            name,
            kind,
            file_name,
            desired_running=desired_running,
            active_processes=active,
            now=now,
        )
        for engine_id, name, kind, file_name in ENGINE_FILES
    ]

    incidents = incidents_from_logs(read_recent_log_lines(log_file or runtime / "windows-app.log"))
    for item in engines:
        if item["lifecycle"] in {"degraded", "blocked", "dead"}:
            incidents.append(
                incident(
                    item["engineId"],
                    f"engine_{item['lifecycle']}",
                    f"{item['name']} esta en {item['lifecycle']}.",
                    item["lastError"] or item["summary"],
                    severity="error" if item["lifecycle"] == "dead" else "warning",
                    recovery="supervisor_restart" if item["lifecycle"] == "dead" else "observe_and_report",
                    requires_human=item["engineId"] == "cabin_authority",
                )
            )

    operator_has_priority = bool(input_arbiter.get("operatorHasPriority")) or text(input_arbiter.get("phase")).lower() == "operator_control"
    cabin_status = text(cabin.get("status")).lower()
    trust_gate = text(((trust.get("permissionGate") or {}) if isinstance(trust.get("permissionGate"), dict) else {}).get("decision"))
    running_count = sum(1 for item in engines if item["lifecycle"] in {"running", "ready"})
    degraded_count = sum(1 for item in engines if item["lifecycle"] == "degraded")
    blocked_count = sum(1 for item in engines if item["lifecycle"] == "blocked")
    dead_count = sum(1 for item in engines if item["lifecycle"] in {"dead", "restarting"})
    restart_count = number(agent_supervisor.get("restartCount"))
    can_observe = any(item["engineId"] == "vision" and item["lifecycle"] in {"running", "degraded"} for item in engines)
    can_think = any(item["engineId"] == "cognitive" and item["lifecycle"] in {"running", "degraded"} for item in engines)
    can_act = (
        is_running
        and not operator_has_priority
        and cabin_status in {"ok", "ready"}
        and dead_count == 0
        and trust_gate not in {"BLOCK", "PAUSE_FOR_OPERATOR"}
    )
    can_sync = is_running and dead_count == 0 and blocked_count == 0

    main_blocker = ""
    if dead_count:
        main_blocker = "Hay motores caidos o reiniciando."
    elif operator_has_priority:
        main_blocker = "Bryams tiene prioridad sobre mouse y teclado."
    elif cabin_status not in {"ok", "ready", ""}:
        main_blocker = text(cabin.get("summary"), "Cabina necesita revision.")
    elif incidents:
        main_blocker = incidents[-1]["summary"]

    status = "ok"
    if dead_count or any(item["severity"] == "error" for item in incidents):
        status = "attention"
    if blocked_count or any(item["severity"] == "critical" for item in incidents):
        status = "blocked"
    if not is_running and not desired_running:
        status = "idle"

    headline = "Runtime estable"
    if status == "idle":
        headline = "IA detenida esperando inicio"
    elif status == "attention":
        headline = "IA trabajando con incidente explicado"
    elif status == "blocked":
        headline = "IA bloqueada por seguridad o cabina"

    state = {
        "status": status,
        "engine": ENGINE,
        "version": VERSION,
        "updatedAt": utc_now(),
        "contract": CONTRACT,
        "authority": {
            "truthSource": "runtime-kernel-state.json",
            "desiredRunning": bool(desired_running),
            "isRunning": bool(is_running),
            "canObserve": bool(can_observe),
            "canThink": bool(can_think),
            "canAct": bool(can_act),
            "canSync": bool(can_sync),
            "operatorHasPriority": bool(operator_has_priority),
            "mainBlocker": main_blocker,
        },
        "summary": {
            "enginesTotal": len(engines),
            "enginesRunning": running_count,
            "enginesDegraded": degraded_count,
            "enginesBlocked": blocked_count,
            "enginesDead": dead_count,
            "incidentsOpen": len(incidents),
            "restartsRecent": restart_count,
        },
        "engines": engines,
        "incidents": incidents[-16:],
        "recovery": {
            "supervisorActive": bool(agent_supervisor.get("supervisorRunning")) or text(agent_supervisor.get("status")).lower() == "ok",
            "recentRestartCount": restart_count,
            "lastCheckpointAt": text(life.get("updatedAt") or agent_supervisor.get("updatedAt") or ""),
            "lastRecoveryAction": "supervisor_restart" if restart_count else "none",
        },
        "sourceFiles": {engine_id: file_name for engine_id, _, _, file_name in ENGINE_FILES},
        "outputFiles": {
            "state": "runtime-kernel-state.json",
            "report": "runtime-kernel-report.json",
            "diagnosticTimeline": "diagnostic-timeline.jsonl",
        },
        "humanReport": {
            "headline": headline,
            "queEstaPasando": [
                f"{running_count}/{len(engines)} motores reportan vida util.",
                main_blocker or "No hay bloqueo principal.",
            ],
            "queHice": [
                "Unifique estados de motores, cabina, supervisor, seguridad e input.",
                "Converti logs recientes en incidentes entendibles.",
            ],
            "queNecesitoDeBryams": [
                item["summary"] for item in incidents if item.get("requiresHuman")
            ][:5],
            "riesgos": [
                "Si un motor no publica estado por mas de 45 segundos, queda degradado.",
                "Cloud Sync debe consumir este kernel antes de subir reportes.",
            ],
        },
    }
    return state


def write_state(runtime_dir: Path, state: dict[str, Any], state_file: Path | None = None, report_file: Path | None = None) -> None:
    runtime_dir.mkdir(parents=True, exist_ok=True)
    state_path = state_file or runtime_dir / "runtime-kernel-state.json"
    report_path = report_file or runtime_dir / "runtime-kernel-report.json"
    state_path.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
    report = {
        "engine": ENGINE,
        "status": state["status"],
        "summary": state["summary"],
        "authority": state["authority"],
        "humanReport": state["humanReport"],
    }
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM Runtime Kernel state builder")
    default_runtime = Path("runtime") if Path.cwd().name == "desktop-agent" else Path("desktop-agent") / "runtime"
    parser.add_argument("--runtime-dir", default=str(default_runtime))
    parser.add_argument("--state-file", default=None)
    parser.add_argument("--report-file", default=None)
    parser.add_argument("--desired-running", action="store_true")
    parser.add_argument("--is-running", action="store_true")
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    runtime_dir = Path(args.runtime_dir)
    state = build_runtime_kernel_state(
        runtime_dir,
        desired_running=args.desired_running or None,
        is_running=args.is_running or None,
    )
    write_state(
        runtime_dir,
        state,
        Path(args.state_file) if args.state_file else None,
        Path(args.report_file) if args.report_file else None,
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=False, indent=2))
    else:
        print(state["humanReport"]["headline"])
    return 0 if state["status"] != "blocked" else 2


if __name__ == "__main__":
    raise SystemExit(main())
