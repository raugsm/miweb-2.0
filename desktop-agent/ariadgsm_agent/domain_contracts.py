from __future__ import annotations

import argparse
import json
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from .contracts import sample_event, validate_contract
from .domain_events import adapt_engine_event


CONTRACT_VERSION = "0.8.2"
STAGE_ID = "stage_one_domain_event_contracts"

ROOT = Path(__file__).resolve().parents[2]
AGENT_ROOT = Path(__file__).resolve().parents[1]

ENGINE_CONTRACTS: tuple[str, ...] = (
    "vision_event",
    "perception_event",
    "conversation_event",
    "decision_event",
    "action_event",
    "accounting_event",
    "learning_event",
    "autonomous_cycle_event",
    "human_feedback_event",
)

REQUIRED_DOMAIN_EVENTS: tuple[str, ...] = (
    "ObservationCreated",
    "ObservationRejected",
    "ConversationObserved",
    "CustomerCandidateIdentified",
    "CaseOpened",
    "CaseUpdated",
    "CaseNeedsHumanContext",
    "CaseClosed",
    "ChannelRouteProposed",
    "ChannelRouteApproved",
    "ChannelRouteRejected",
    "PaymentDrafted",
    "PaymentEvidenceAttached",
    "PaymentConfirmed",
    "DebtDetected",
    "DebtUpdated",
    "RefundCandidate",
    "AccountingCorrectionReceived",
    "DecisionExplained",
    "HumanApprovalRequired",
    "HumanCorrectionReceived",
    "HumanApprovalGranted",
    "HumanApprovalRejected",
    "OperatorOverrideRecorded",
    "OperatorNoteAdded",
    "ActionRequested",
    "ActionExecuted",
    "ActionVerified",
    "ActionFailed",
    "ActionBlocked",
    "LearningCandidateCreated",
    "LearningAccepted",
    "LearningRejected",
    "MemoryFactCreated",
    "MemoryFactUpdated",
    "MemoryConflictDetected",
    "SensitiveDataDetected",
    "CloudSyncBlocked",
    "CycleStarted",
    "CycleCheckpointCreated",
    "CycleBlocked",
    "CycleRecovered",
)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def read_text(path: Path) -> str:
    if not path.exists():
        return ""
    return path.read_text(encoding="utf-8-sig")


def read_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    try:
        value = json.loads(read_text(path))
    except json.JSONDecodeError:
        return {}
    return value if isinstance(value, dict) else {}


def make_check(check_id: str, name: str, status: str, detail: str, evidence: list[str] | None = None) -> dict[str, Any]:
    return {
        "checkId": check_id,
        "name": name,
        "status": status,
        "detail": detail,
        "evidence": evidence or [],
    }


def worst_status(checks: list[dict[str, Any]]) -> str:
    if any(check.get("status") == "blocked" for check in checks):
        return "blocked"
    if any(check.get("status") == "attention" for check in checks):
        return "attention"
    return "ok"


def check_registry(repo_root: Path) -> dict[str, Any]:
    registry = read_json(repo_root / "desktop-agent/contracts/domain-event-registry.json")
    event_types = registry.get("eventTypes") if isinstance(registry.get("eventTypes"), dict) else {}
    missing = [event_type for event_type in REQUIRED_DOMAIN_EVENTS if event_type not in event_types]
    malformed: list[str] = []
    for event_type, entry in event_types.items():
        if not isinstance(entry, dict):
            malformed.append(f"{event_type}: entry invalida")
            continue
        for field in ("category", "allowedSourceDomains", "defaultRiskLevel", "description"):
            if field not in entry:
                malformed.append(f"{event_type}: falta {field}")
        if entry.get("defaultRiskLevel") not in {"low", "medium", "high", "critical"}:
            malformed.append(f"{event_type}: risk invalido")
    if missing:
        return make_check("registry", "Registro de eventos", "blocked", "Faltan eventos de dominio obligatorios.", missing)
    if malformed:
        return make_check("registry", "Registro de eventos", "blocked", "Hay entradas mal formadas.", malformed[:12])
    version = str(registry.get("schemaVersion") or "")
    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        return make_check("registry", "Registro de eventos", "blocked", f"schemaVersion invalido: {version}", ["desktop-agent/contracts/domain-event-registry.json"])
    if version != CONTRACT_VERSION:
        return make_check("registry", "Registro de eventos", "attention", f"Registry version={version}, esperado={CONTRACT_VERSION}.", ["desktop-agent/contracts/domain-event-registry.json"])
    return make_check("registry", "Registro de eventos", "ok", f"Registry versionado {version} con eventos obligatorios.", ["desktop-agent/contracts/domain-event-registry.json"])


def check_adapter_coverage() -> dict[str, Any]:
    failures: list[str] = []
    evidence: list[str] = []
    for contract_name in ENGINE_CONTRACTS:
        engine_event = sample_event(contract_name)
        contract_errors = validate_contract(engine_event, contract_name)
        if contract_errors:
            failures.append(f"{contract_name}: contrato tecnico invalido: {'; '.join(contract_errors[:3])}")
            continue
        domain_events = adapt_engine_event(engine_event)
        if not domain_events:
            failures.append(f"{contract_name}: no produjo domain events")
            continue
        for domain_event in domain_events:
            errors = validate_contract(domain_event, "domain_event")
            if errors:
                failures.append(f"{contract_name}->{domain_event.get('eventType')}: {'; '.join(errors[:3])}")
        evidence.append(f"{contract_name}:{len(domain_events)}")
    if failures:
        return make_check("adapter_coverage", "Cobertura de adaptadores", "blocked", "Hay motores sin adaptador valido.", failures[:16])
    return make_check("adapter_coverage", "Cobertura de adaptadores", "ok", "Todos los eventos de motor relevantes producen domain events validos.", evidence)


def check_memory_projection(repo_root: Path) -> dict[str, Any]:
    memory_path = repo_root / "desktop-agent/ariadgsm_agent/memory.py"
    text = read_text(memory_path)
    required = ("ingest_domain_event", "--domain-events", "memory_domain_events", "domainEventsFile")
    missing = [item for item in required if item not in text]
    if missing:
        return make_check("memory_projection", "Memory como proyeccion", "blocked", "Memory no consume domain events como fuente de negocio.", missing)
    return make_check("memory_projection", "Memory como proyeccion", "ok", "Memory ingiere domain events y conserva compatibilidad tecnica.", [str(memory_path)])


def check_runtime_sequence(repo_root: Path) -> dict[str, Any]:
    runtime_path = repo_root / "desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/AgentRuntime.cs"
    text = read_text(runtime_path)
    required_names = ("DomainContracts", "DomainEventsBeforeMemory", "Memory", "DomainEventsAfterCycle")
    missing = [name for name in required_names if name not in text]
    if missing:
        return make_check("runtime_sequence", "Secuencia del runtime", "blocked", "El runtime no ejecuta contratos/eventos en orden final.", missing)
    module_start = text.find("var modules = new[]")
    module_end = text.find("};", module_start)
    module_text = text[module_start:module_end] if module_start >= 0 and module_end > module_start else text
    before_memory = module_text.find('("DomainEventsBeforeMemory"') < module_text.find('("Memory"')
    after_cycle = module_text.find('("DomainEventsAfterCycle"') > module_text.find('("AutonomousCycle"')
    if not before_memory or not after_cycle:
        return make_check("runtime_sequence", "Secuencia del runtime", "blocked", "DomainEvents debe correr antes de Memory y despues del checkpoint autonomo.", [str(runtime_path)])
    return make_check("runtime_sequence", "Secuencia del runtime", "ok", "Runtime ejecuta contratos, domain events antes de Memory y checkpoint final.", [str(runtime_path)])


def check_config(repo_root: Path) -> dict[str, Any]:
    config = read_json(repo_root / "desktop-agent/config.example.json")
    domain_core = config.get("domainEventCore") if isinstance(config.get("domainEventCore"), dict) else {}
    missing = []
    for key in ("humanFeedbackEventsFile", "domainEventsFile", "stateFile", "db"):
        if key not in domain_core:
            missing.append(f"domainEventCore.{key}")
    if "domainContractsCore" not in config:
        missing.append("domainContractsCore")
    memory_core = config.get("memoryCore") if isinstance(config.get("memoryCore"), dict) else {}
    if "domainEventsFile" not in memory_core:
        missing.append("memoryCore.domainEventsFile")
    if missing:
        return make_check("config", "Configuracion", "blocked", "Faltan rutas de contratos/eventos finales.", missing)
    return make_check("config", "Configuracion", "ok", "Config expone domain contracts, feedback humano y memoria por domain events.", ["desktop-agent/config.example.json"])


def check_versioning(repo_root: Path) -> dict[str, Any]:
    version = read_text(repo_root / "desktop-agent/windows-app/VERSION").strip()
    manifest = read_json(repo_root / "desktop-agent/update/ariadgsm-update.json")
    if version != CONTRACT_VERSION:
        return make_check("versioning", "Versionado", "attention", f"VERSION={version}, esperado={CONTRACT_VERSION}.", ["desktop-agent/windows-app/VERSION"])
    if manifest.get("version") != version:
        return make_check("versioning", "Versionado", "attention", f"Manifest version={manifest.get('version')} no coincide con VERSION={version}.", ["desktop-agent/update/ariadgsm-update.json"])
    return make_check("versioning", "Versionado", "ok", f"Version {version} coherente.", ["desktop-agent/windows-app/VERSION", "desktop-agent/update/ariadgsm-update.json"])


def build_human_report(checks: list[dict[str, Any]]) -> dict[str, list[str]]:
    ready = [f"{check['name']}: {check['detail']}" for check in checks if check.get("status") == "ok"]
    gaps = [f"{check['name']}: {check['detail']}" for check in checks if check.get("status") != "ok"]
    risks = ["No saltar a Case Manager si algun contrato queda en attention/blocked."] if gaps else ["Etapa 1 cerrada; Case Manager debe usar estos eventos como fuente."]
    return {"queQuedoListo": ready, "queFalta": gaps, "riesgos": risks}


def run_domain_contracts_once(repo_root: Path, runtime_dir: Path, state_file: Path | None = None, report_file: Path | None = None) -> dict[str, Any]:
    root = repo_root.resolve()
    runtime = runtime_dir.resolve()
    runtime.mkdir(parents=True, exist_ok=True)
    state_path = state_file or runtime / "domain-contracts-final-state.json"
    report_path = report_file or runtime / "domain-contracts-final-report.json"

    checks = [
        check_registry(root),
        check_adapter_coverage(),
        check_memory_projection(root),
        check_runtime_sequence(root),
        check_config(root),
        check_versioning(root),
    ]
    status = worst_status(checks)
    ok_count = sum(1 for check in checks if check.get("status") == "ok")
    summary = f"Etapa 1 {status}: {ok_count}/{len(checks)} checks listos."
    human_report = build_human_report(checks)
    version = read_text(root / "desktop-agent/windows-app/VERSION").strip() or CONTRACT_VERSION

    state: dict[str, Any] = {
        "contractVersion": CONTRACT_VERSION,
        "stageId": STAGE_ID,
        "createdAt": utc_now(),
        "version": version,
        "status": status,
        "summary": summary,
        "checks": checks,
        "humanReport": human_report,
        "nextStage": {
            "stageNumber": 2,
            "name": "Autonomous Cycle Orchestrator",
            "status": "implemented_as_central_cycle",
            "reason": "Etapa 2 ya esta implementada; despues de reconocerla, el siguiente bloque funcional es Case Manager.",
        },
    }

    state_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.parent.mkdir(parents=True, exist_ok=True)
    state_path.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
    report_path.write_text(json.dumps({"stageId": STAGE_ID, "summary": summary, **human_report}, ensure_ascii=False, indent=2), encoding="utf-8")
    return state


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM Domain Event Contracts final readiness")
    default_repo = Path.cwd().parent if Path.cwd().name == "desktop-agent" else Path.cwd()
    parser.add_argument("--repo-root", default=str(default_repo))
    parser.add_argument("--runtime-dir", default=str(Path("runtime") if Path.cwd().name == "desktop-agent" else Path("desktop-agent") / "runtime"))
    parser.add_argument("--state-file", default=None)
    parser.add_argument("--report-file", default=None)
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    state = run_domain_contracts_once(
        Path(args.repo_root),
        Path(args.runtime_dir),
        Path(args.state_file) if args.state_file else None,
        Path(args.report_file) if args.report_file else None,
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=False, indent=2))
    else:
        print(state["summary"])
    return 0 if state["status"] != "blocked" else 2


if __name__ == "__main__":
    raise SystemExit(main())
