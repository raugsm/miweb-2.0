from __future__ import annotations

import argparse
import json
import re
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


CONTRACT_VERSION = "0.8.1"
STAGE_ID = "stage_zero_product_foundation"

REQUIRED_DOCS: tuple[tuple[str, tuple[str, ...]], ...] = (
    ("docs/ARIADGSM_EXECUTION_LOCK.md", ("Product Foundation", "Domain Event Contracts", "Autonomous Cycle Orchestrator")),
    ("docs/ARIADGSM_STAGE_0_PRODUCT_FOUNDATION.md", ("Product Foundation", "IA operadora", "Definicion de terminado")),
    ("docs/ARIADGSM_FINAL_PRODUCT_BLUEPRINT.md", ("AriadGSM IA Local", "No construimos un bot")),
    ("docs/ARIADGSM_DOMAIN_EVENT_CONTRACTS.md", ("Domain Event Contracts", "PaymentDrafted")),
    ("docs/ARIADGSM_BUSINESS_DOMAIN_MAP.md", ("Business Brain", "Domain Event Contracts")),
    ("docs/ARIADGSM_BUSINESS_OPERATING_MODEL.md", ("AriadGSM", "Atencion al cliente")),
    ("docs/ARIADGSM_AUTONOMOUS_OPERATING_SYSTEM_1.0.md", ("AriadGSM Autonomous Operating System", "Self-Improvement Loop")),
)

REQUIRED_CONTRACTS = (
    "desktop-agent/contracts/stage-zero-readiness.schema.json",
    "desktop-agent/contracts/domain-event-envelope.schema.json",
    "desktop-agent/contracts/domain-event-registry.json",
    "desktop-agent/contracts/autonomous-cycle-event.schema.json",
)

REQUIRED_CONFIG_KEYS = (
    "runtimeDir",
    "domainEventCore",
    "autonomousCycleCore",
    "handsEngine",
    "supervisorCore",
    "architecture",
)

REQUIRED_DOMAIN_EVENTS = (
    "ConversationObserved",
    "CustomerCandidateIdentified",
    "CaseOpened",
    "CaseUpdated",
    "CaseNeedsHumanContext",
    "CaseClosed",
    "ChannelRouteProposed",
    "PaymentDrafted",
    "PaymentEvidenceAttached",
)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def read_text(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except UnicodeDecodeError:
        return path.read_text(encoding="utf-8-sig")


def read_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    try:
        value = json.loads(read_text(path))
    except json.JSONDecodeError:
        return {}
    return value if isinstance(value, dict) else {}


def normalize_path(root: Path, value: str) -> str:
    path = root / value
    return str(path)


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


def check_docs(root: Path) -> dict[str, Any]:
    missing: list[str] = []
    marker_gaps: list[str] = []
    evidence: list[str] = []
    for relative, markers in REQUIRED_DOCS:
        path = root / relative
        if not path.exists():
            missing.append(relative)
            continue
        text = read_text(path)
        evidence.append(relative)
        for marker in markers:
            if marker not in text:
                marker_gaps.append(f"{relative} falta marcador: {marker}")
    if missing:
        return make_check(
            "source_documents",
            "Documentos fuente",
            "blocked",
            "Faltan documentos base de producto.",
            missing,
        )
    if marker_gaps:
        return make_check(
            "source_documents",
            "Documentos fuente",
            "attention",
            "Los documentos existen, pero faltan marcadores de continuidad.",
            marker_gaps,
        )
    return make_check("source_documents", "Documentos fuente", "ok", "Documentos base presentes y coherentes.", evidence)


def check_contracts(root: Path) -> dict[str, Any]:
    missing = [relative for relative in REQUIRED_CONTRACTS if not (root / relative).exists()]
    if missing:
        return make_check("contracts", "Contratos tecnicos", "blocked", "Faltan contratos necesarios.", missing)

    registry = read_json(root / "desktop-agent/contracts/domain-event-registry.json")
    event_types = registry.get("eventTypes") if isinstance(registry.get("eventTypes"), dict) else {}
    missing_events = [event_type for event_type in REQUIRED_DOMAIN_EVENTS if event_type not in event_types]
    if missing_events:
        return make_check(
            "contracts",
            "Contratos tecnicos",
            "attention",
            "El registro existe, pero faltan eventos base para operar como negocio.",
            missing_events,
        )
    return make_check(
        "contracts",
        "Contratos tecnicos",
        "ok",
        "Contratos y eventos base presentes.",
        list(REQUIRED_CONTRACTS),
    )


def check_versioning(root: Path) -> dict[str, Any]:
    version_file = root / "desktop-agent/windows-app/VERSION"
    manifest_file = root / "desktop-agent/update/ariadgsm-update.json"
    version = read_text(version_file).strip() if version_file.exists() else ""
    manifest = read_json(manifest_file)
    evidence = [normalize_path(root, "desktop-agent/windows-app/VERSION"), normalize_path(root, "desktop-agent/update/ariadgsm-update.json")]

    if not re.fullmatch(r"\d+\.\d+\.\d+", version):
        return make_check("versioning", "Versionado", "blocked", f"Version invalida: {version or '(vacia)'}", evidence)
    if manifest.get("version") != version:
        return make_check(
            "versioning",
            "Versionado",
            "attention",
            f"Manifest version={manifest.get('version')} no coincide con VERSION={version}.",
            evidence,
        )
    package_url = str(manifest.get("packageUrl") or "")
    sha256 = str(manifest.get("sha256") or "")
    if version not in package_url or not re.fullmatch(r"[a-fA-F0-9]{64}", sha256):
        return make_check(
            "versioning",
            "Versionado",
            "attention",
            "El manifest existe, pero paquete/hash aun no estan cerrados para esta version.",
            evidence,
        )
    return make_check("versioning", "Versionado", "ok", f"Version {version} coherente con manifest.", evidence)


def check_config(root: Path) -> dict[str, Any]:
    config_path = root / "desktop-agent/config.example.json"
    config = read_json(config_path)
    missing = [key for key in REQUIRED_CONFIG_KEYS if key not in config]
    if missing:
        return make_check("config", "Configuracion base", "blocked", "Faltan secciones de config.", missing)
    architecture = config.get("architecture") if isinstance(config.get("architecture"), dict) else {}
    if architecture.get("rawFramesUploadedToCloud") is not False:
        return make_check(
            "config",
            "Configuracion base",
            "attention",
            "La configuracion debe mantener capturas crudas fuera de la nube por defecto.",
            [str(config_path)],
        )
    return make_check("config", "Configuracion base", "ok", "Config base lista para motores locales.", [str(config_path)])


def check_release_files(root: Path) -> dict[str, Any]:
    expected = (
        "desktop-agent/windows-app/build-agent-package.cmd",
        "desktop-agent/windows-app/src/AriadGSM.Agent.Desktop/AriadGSM.Agent.Desktop.csproj",
        "desktop-agent/windows-app/src/AriadGSM.Agent.Updater/AriadGSM.Agent.Updater.csproj",
        "desktop-agent/windows-app/src/AriadGSM.Agent.Launcher/AriadGSM.Agent.Launcher.csproj",
    )
    missing = [relative for relative in expected if not (root / relative).exists()]
    if missing:
        return make_check("release_files", "Release y actualizacion", "blocked", "Faltan archivos de build/release.", missing)
    return make_check("release_files", "Release y actualizacion", "ok", "Build, launcher y updater presentes.", list(expected))


def build_human_report(checks: list[dict[str, Any]]) -> dict[str, list[str]]:
    ready = [f"{check['name']}: {check['detail']}" for check in checks if check.get("status") == "ok"]
    gaps = [f"{check['name']}: {check['detail']}" for check in checks if check.get("status") != "ok"]
    risks: list[str] = []
    if not gaps:
        risks.append("Etapa 0 cerrada; el riesgo ahora pasa a cerrar Etapa 1 sin volver a parches.")
    else:
        risks.append("No declarar autonomia final mientras existan checks pendientes.")
    return {
        "queQuedoListo": ready,
        "queFalta": gaps,
        "riesgos": risks,
    }


def run_stage_zero_once(repo_root: Path, runtime_dir: Path, state_file: Path | None = None, report_file: Path | None = None) -> dict[str, Any]:
    root = repo_root.resolve()
    runtime = runtime_dir.resolve()
    runtime.mkdir(parents=True, exist_ok=True)
    state_path = state_file or runtime / "stage-zero-state.json"
    report_path = report_file or runtime / "stage-zero-report.json"

    checks = [
        check_docs(root),
        check_contracts(root),
        check_versioning(root),
        check_config(root),
        check_release_files(root),
    ]
    status = worst_status(checks)
    version_path = root / "desktop-agent/windows-app/VERSION"
    version = read_text(version_path).strip() if version_path.exists() else "unknown"
    ok_count = sum(1 for check in checks if check.get("status") == "ok")
    summary = f"Etapa 0 {status}: {ok_count}/{len(checks)} checks listos."
    human_report = build_human_report(checks)

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
            "stageNumber": 1,
            "name": "Domain Event Contracts",
            "status": "base_implemented_needs_final_closure",
            "reason": "Etapa 1 ya tiene base ejecutable, pero falta cerrarla como producto final antes de avanzar a casos.",
        },
    }

    state_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.parent.mkdir(parents=True, exist_ok=True)
    state_path.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
    report_path.write_text(json.dumps({"stageId": STAGE_ID, "summary": summary, **human_report}, ensure_ascii=False, indent=2), encoding="utf-8")
    return state


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM Stage 0 Product Foundation verifier")
    default_repo = Path.cwd().parent if Path.cwd().name == "desktop-agent" else Path.cwd()
    parser.add_argument("--repo-root", default=str(default_repo))
    parser.add_argument("--runtime-dir", default=str(Path("runtime") if Path.cwd().name == "desktop-agent" else Path("desktop-agent") / "runtime"))
    parser.add_argument("--state-file", default=None)
    parser.add_argument("--report-file", default=None)
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    state = run_stage_zero_once(
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
