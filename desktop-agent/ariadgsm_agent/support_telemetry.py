from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import zipfile
from collections import deque
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


VERSION = "0.9.2"
ENGINE = "ariadgsm_support_telemetry_core"
CONTRACT = "support_telemetry_state"
EVENT_CONTRACT = "support_telemetry_event"
ROOT = Path(__file__).resolve().parents[1]
RUNTIME_DIR = ROOT / "runtime"
MAX_EVENT_LOG = 500
MAX_BLACKBOX_EVENTS = 500
MAX_BLACKBOX_BYTES = 24 * 1024 * 1024


STATE_SOURCES: tuple[tuple[str, str, str], ...] = (
    ("runtime_kernel", "runtime-kernel-state.json", "runtime"),
    ("runtime_governor", "runtime-governor-state.json", "runtime"),
    ("window_reality", "window-reality-state.json", "window_reality"),
    ("autonomous_cycle", "autonomous-cycle-state.json", "runtime"),
    ("cabin_authority", "cabin-authority-state.json", "window_reality"),
    ("reader_core", "reader-core-state.json", "reader"),
    ("trust_safety", "trust-safety-state.json", "runtime"),
    ("input_arbiter", "input-arbiter-state.json", "input"),
    ("hands", "hands-state.json", "hands"),
    ("updater", "update-state.json", "updater"),
    ("agent_supervisor", "agent-supervisor-state.json", "runtime"),
    ("cloud_sync", "cloud-sync-state.json", "cloud"),
    ("evaluation_release", "evaluation-release-state.json", "release"),
)


SENSITIVE_TEXT_PATTERNS: tuple[tuple[re.Pattern[str], str], ...] = (
    (re.compile(r"(?i)\b(bearer|authorization)\s+[a-z0-9._\-+/=]{12,}"), r"\1 <redacted>"),
    (
        re.compile(r"(?i)\b(api[_\- ]?key|token|secret|password|contrasena|contraseña|cookie|session)\b\s*[:=]\s*['\"]?[^'\"\s,;]{6,}"),
        r"\1=<redacted>",
    ),
    (re.compile(r"(?i)C:\\Users\\[^\\\s]+"), r"C:\\Users\\<redacted>"),
    (re.compile(r"\b[A-Fa-f0-9]{24,}\b"), "<hex-redacted>"),
    (re.compile(r"\b[\w.+\-]+@[\w.\-]+\.[A-Za-z]{2,}\b"), "<email-redacted>"),
    (re.compile(r"(?<![\w:.\-])(?:\+\d[\d\s().\-]{7,}\d|\d{3}[\s().\-]\d{3}[\s().\-]\d{3,})(?![\w:.\-])"), "<phone-redacted>"),
)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


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


def bounded_text(value: Any, limit: int = 1200) -> str:
    return " ".join(text(value).replace("\r", " ").replace("\n", " ").split())[:limit]


def sha256_text(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8", errors="replace")).hexdigest()


def read_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig", errors="replace"))
        return data if isinstance(data, dict) else {}
    except Exception as exc:
        return {"status": "error", "summary": f"No pude leer {path.name}: {exc}", "lastError": str(exc)}


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(temp, path)


def write_jsonl_retained(path: Path, events: list[dict[str, Any]], max_events: int) -> list[dict[str, Any]]:
    path.parent.mkdir(parents=True, exist_ok=True)
    retained: deque[dict[str, Any]] = deque(maxlen=max_events)
    if path.exists():
        try:
            with path.open("r", encoding="utf-8-sig", errors="replace") as handle:
                for line in handle:
                    if not line.strip():
                        continue
                    try:
                        data = json.loads(line)
                    except json.JSONDecodeError:
                        continue
                    if isinstance(data, dict):
                        retained.append(data)
        except Exception:
            retained.clear()
    for event in events:
        retained.append(event)
    temp = path.with_suffix(path.suffix + ".tmp")
    with temp.open("w", encoding="utf-8") as handle:
        for event in retained:
            handle.write(json.dumps(event, ensure_ascii=False) + "\n")
    os.replace(temp, path)
    return list(retained)


def read_tail_lines(path: Path, limit: int = 100) -> list[str]:
    if not path.exists():
        return []
    lines: deque[str] = deque(maxlen=limit)
    try:
        with path.open("r", encoding="utf-8-sig", errors="replace") as handle:
            for line in handle:
                if line.strip():
                    lines.append(line.rstrip("\n"))
    except Exception:
        return []
    return list(lines)


def redact_text(value: Any, limit: int = 1200) -> tuple[str, int]:
    current = bounded_text(value, limit)
    redactions = 0
    for pattern, replacement in SENSITIVE_TEXT_PATTERNS:
        current, count = pattern.subn(replacement, current)
        redactions += count
    current = current.replace("\x00", "")
    return current[:limit], redactions


def redact_json(value: Any) -> tuple[Any, int]:
    if isinstance(value, dict):
        redactions = 0
        result: dict[str, Any] = {}
        for key, item in value.items():
            lowered = str(key).lower()
            if any(marker in lowered for marker in ("password", "token", "secret", "cookie", "session")):
                result[key] = "<redacted>"
                redactions += 1
                continue
            cleaned, count = redact_json(item)
            result[key] = cleaned
            redactions += count
        return result, redactions
    if isinstance(value, list):
        result_list = []
        total = 0
        for item in value[:80]:
            cleaned, count = redact_json(item)
            result_list.append(cleaned)
            total += count
        return result_list, total
    if isinstance(value, str):
        return redact_text(value)
    return value, 0


def trace_id_for(runtime_dir: Path, states: dict[str, dict[str, Any]], simulated: list[str]) -> str:
    kernel = states.get("runtime_kernel", {})
    seed = json.dumps(
        {
            "runtime": str(runtime_dir.resolve()).lower(),
            "status": kernel.get("status"),
            "updatedAt": kernel.get("updatedAt"),
            "simulated": simulated,
        },
        ensure_ascii=False,
        sort_keys=True,
    )
    return sha256_text(seed)[:32]


def correlation_id_for(states: dict[str, dict[str, Any]], trace_id: str) -> str:
    life = states.get("autonomous_cycle", {})
    kernel = states.get("runtime_kernel", {})
    source = text(life.get("cycleId") or kernel.get("trigger") or kernel.get("source") or "ariadgsm-local")
    return f"corr-{sha256_text(source + trace_id)[:20]}"


def severity_from_status(status: str) -> str:
    lowered = status.lower()
    if lowered in {"blocked"}:
        return "critical"
    if lowered in {"error", "failed", "dead"}:
        return "error"
    if lowered in {"attention", "warning", "degraded", "restarting"}:
        return "warning"
    return "info"


def make_event(
    *,
    trace_id: str,
    correlation_id: str,
    source: str,
    severity: str,
    category: str,
    summary: str,
    detail: str,
    source_files: list[str],
    recommended_action: str,
    confidence: float = 0.78,
) -> tuple[dict[str, Any], int]:
    clean_summary, s_redactions = redact_text(summary, 500)
    clean_detail, d_redactions = redact_text(detail, 1200)
    redactions = s_redactions + d_redactions
    event_id = f"support-{sha256_text(f'{trace_id}|{source}|{category}|{clean_summary}|{clean_detail}')[:24]}"
    return {
        "eventType": EVENT_CONTRACT,
        "eventId": event_id,
        "incidentId": event_id,
        "createdAt": utc_now(),
        "traceId": trace_id,
        "correlationId": correlation_id,
        "source": source,
        "severity": severity,
        "category": category,
        "summary": clean_summary,
        "detail": clean_detail,
        "redaction": {"applied": redactions > 0, "redactions": redactions},
        "evidence": {
            "sourceFiles": source_files,
            "confidence": max(0.0, min(1.0, confidence)),
            "visibleOnly": False,
        },
        "recommendedAction": recommended_action,
        "privacy": {
            "cloudAllowed": severity in {"info", "warning"} and redactions == 0,
            "rawContentIncluded": False,
            "requiresConsent": True,
            "redactedBeforeStorage": True,
        },
    }, redactions


def status_detail(state: dict[str, Any]) -> str:
    human = state.get("humanReport") if isinstance(state.get("humanReport"), dict) else {}
    authority = state.get("authority") if isinstance(state.get("authority"), dict) else {}
    summary = state.get("summary")
    return bounded_text(
        human.get("headline")
        or authority.get("mainBlocker")
        or state.get("lastError")
        or state.get("error")
        or state.get("summary")
        or summary
        or "Estado recibido.",
        900,
    )


def collect_state_sources(runtime_dir: Path) -> tuple[dict[str, dict[str, Any]], list[dict[str, Any]]]:
    states: dict[str, dict[str, Any]] = {}
    sources: list[dict[str, Any]] = []
    for source_id, file_name, category in STATE_SOURCES:
        state = read_json(runtime_dir / file_name)
        states[source_id] = state
        sources.append(
            {
                "sourceId": source_id,
                "file": file_name,
                "status": text(state.get("status"), "missing" if not state else "ok"),
                "category": category,
                "updatedAt": text(state.get("updatedAt") or state.get("observedAt")),
                "hasData": bool(state),
            }
        )
    return states, sources


def incidents_from_states(
    states: dict[str, dict[str, Any]],
    trace_id: str,
    correlation_id: str,
) -> tuple[list[dict[str, Any]], int]:
    events: list[dict[str, Any]] = []
    redactions = 0
    category_by_source = {source: category for source, _file, category in STATE_SOURCES}
    file_by_source = {source: file for source, file, _category in STATE_SOURCES}

    for source, state in states.items():
        status = text(state.get("status"), "missing" if not state else "ok").lower()
        if status in {"ok", "ready", "idle", "current", "running", "dry_run"}:
            continue
        event, count = make_event(
            trace_id=trace_id,
            correlation_id=correlation_id,
            source=source,
            severity=severity_from_status(status),
            category=category_by_source.get(source, "runtime"),
            summary=f"{source} reporta estado {status}.",
            detail=status_detail(state),
            source_files=[file_by_source.get(source, f"{source}.json")],
            recommended_action="Revisar el estado humano y mantener la IA en modo seguro hasta confirmar causa.",
        )
        events.append(event)
        redactions += count

    kernel = states.get("runtime_kernel", {})
    for item in kernel.get("incidents") or []:
        if not isinstance(item, dict):
            continue
        source = text(item.get("source"), "runtime_kernel")
        event, count = make_event(
            trace_id=trace_id,
            correlation_id=correlation_id,
            source=source,
            severity=severity_from_status(text(item.get("severity"), "warning")),
            category=category_by_source.get(source, "runtime"),
            summary=text(item.get("summary"), "Incidente publicado por Runtime Kernel."),
            detail=text(item.get("detail") or item.get("code") or ""),
            source_files=["runtime-kernel-state.json"],
            recommended_action=text(item.get("recoveryAction"), "Seguir politica de recuperacion del Runtime Kernel."),
            confidence=0.86,
        )
        events.append(event)
        redactions += count
    return events, redactions


def incidents_from_logs(runtime_dir: Path, trace_id: str, correlation_id: str) -> tuple[list[dict[str, Any]], int]:
    events: list[dict[str, Any]] = []
    redactions = 0
    log_specs = (
        ("windows_app", "windows-app.log"),
        ("diagnostic_timeline", "diagnostic-timeline.jsonl"),
    )
    for source, file_name in log_specs:
        for line in read_tail_lines(runtime_dir / file_name, 140):
            lower = line.lower()
            if ("exit" in lower or "exited" in lower) and ("code=0" in lower or "code 0" in lower):
                continue
            category = "runtime"
            severity = "warning"
            if "exception" in lower or "error" in lower or "failed" in lower or "fallo" in lower:
                severity = "error"
            if "hang" in lower or "freeze" in lower or "no responde" in lower or "se colgo" in lower:
                category = "crash_hang"
                severity = "error"
            elif "exit" in lower and "code=0" not in lower:
                category = "runtime"
                severity = "error"
            elif "updater" in lower or "update" in lower:
                category = "updater"
            elif "cloud" in lower or "nube" in lower:
                category = "cloud"
            elif "hands" in lower or "mouse" in lower or "teclado" in lower:
                category = "hands"
            elif "reader" in lower or "ocr" in lower:
                category = "reader"
            elif "window" in lower or "cabin" in lower or "cabina" in lower:
                category = "window_reality"
            important = any(
                marker in lower
                for marker in (
                    "exception",
                    "error",
                    "failed",
                    "blocked",
                    "denied",
                    "exit",
                    "restart",
                    "crash",
                    "hang",
                    "no responde",
                    "se colgo",
                    "fallo",
                )
            )
            if not important:
                continue
            event, count = make_event(
                trace_id=trace_id,
                correlation_id=correlation_id,
                source=source,
                severity=severity,
                category=category,
                summary=f"Linea de diagnostico importante en {file_name}.",
                detail=line,
                source_files=[file_name],
                recommended_action="Correlacionar con Runtime Kernel y ultimo ciclo antes de actuar.",
                confidence=0.65,
            )
            events.append(event)
            redactions += count
    deduped: dict[str, dict[str, Any]] = {}
    for event in events:
        deduped[text(event.get("eventId"))] = event
    return list(deduped.values())[-25:], redactions


def collect_local_dump_metadata(runtime_dir: Path, trace_id: str, correlation_id: str) -> tuple[list[dict[str, Any]], int]:
    events: list[dict[str, Any]] = []
    redactions = 0
    candidates = [runtime_dir / "crash-dumps"]
    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        candidates.append(Path(local_app_data) / "CrashDumps")
    for folder in candidates:
        if not folder.exists():
            continue
        for dump in sorted(folder.glob("AriadGSM*.dmp"), key=lambda path: path.stat().st_mtime, reverse=True)[:5]:
            stat = dump.stat()
            event, count = make_event(
                trace_id=trace_id,
                correlation_id=correlation_id,
                source="windows_error_reporting",
                severity="error",
                category="crash_hang",
                summary="Windows tiene un dump local de AriadGSM disponible.",
                detail=f"{dump.name} size={stat.st_size} modified={datetime.fromtimestamp(stat.st_mtime, timezone.utc).isoformat()}",
                source_files=["Windows Error Reporting LocalDumps"],
                recommended_action="No subir dump automatico; revisar localmente o pedir permiso explicito.",
                confidence=0.9,
            )
            events.append(event)
            redactions += count
    return events, redactions


def simulation_events(flags: list[str], trace_id: str, correlation_id: str) -> tuple[list[dict[str, Any]], int]:
    events: list[dict[str, Any]] = []
    redactions = 0
    for flag in flags:
        if flag == "crash":
            event, count = make_event(
                trace_id=trace_id,
                correlation_id=correlation_id,
                source="simulated_failure",
                severity="error",
                category="crash_hang",
                summary="Fallo simulado de crash controlado.",
                detail="Simulacion local para validar contrato, bundle y redaccion.",
                source_files=["support_telemetry.py"],
                recommended_action="Validacion automatizada; no requiere accion real.",
            )
        elif flag == "hang":
            event, count = make_event(
                trace_id=trace_id,
                correlation_id=correlation_id,
                source="simulated_failure",
                severity="warning",
                category="crash_hang",
                summary="Fallo simulado de hang controlado.",
                detail="Simulacion local para validar diagnostico de congelamiento.",
                source_files=["support_telemetry.py"],
                recommended_action="Validacion automatizada; no requiere accion real.",
            )
        else:
            event, count = make_event(
                trace_id=trace_id,
                correlation_id=correlation_id,
                source="simulated_privacy",
                severity="critical",
                category="privacy",
                summary="Fuga simulada de dato sensible detectada antes del bundle.",
                detail="token=1234567890abcdef1234567890abcdef telefono +51999999999 C:\\Users\\Bryams\\secret.txt",
                source_files=["support_telemetry.py"],
                recommended_action="Redactar y bloquear subida automatica.",
                confidence=0.95,
            )
        events.append(event)
        redactions += count
    return events, redactions


def classify_status(events: list[dict[str, Any]]) -> str:
    if any(event.get("category") == "privacy" and event.get("severity") == "critical" for event in events):
        return "blocked"
    if any(event.get("severity") in {"error", "critical"} for event in events):
        return "attention"
    if any(event.get("severity") == "warning" for event in events):
        return "attention"
    return "ok"


def safe_cloud_events(events: list[dict[str, Any]]) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for event in events:
        if len(result) >= 20:
            break
        privacy = event.get("privacy") if isinstance(event.get("privacy"), dict) else {}
        if not bool(privacy.get("cloudAllowed")):
            continue
        result.append(
            {
                "type": "support_telemetry",
                "data": {
                    "eventId": event.get("eventId"),
                    "traceId": event.get("traceId"),
                    "correlationId": event.get("correlationId"),
                    "severity": event.get("severity"),
                    "category": event.get("category"),
                    "summary": event.get("summary"),
                    "recommendedAction": event.get("recommendedAction"),
                },
            }
        )
    return result


def build_manifest(runtime_dir: Path, state: dict[str, Any], events: list[dict[str, Any]]) -> dict[str, Any]:
    return {
        "bundleType": "ariadgsm_support_bundle",
        "version": VERSION,
        "createdAt": utc_now(),
        "traceId": state["traceId"],
        "correlationId": state["correlationId"],
        "privacy": {
            "containsRawScreenshots": False,
            "containsFullChats": False,
            "containsSecrets": False,
            "requiresConsentBeforeUpload": True,
        },
        "retention": {
            "localBlackboxEvents": MAX_BLACKBOX_EVENTS,
            "localBlackboxMaxBytes": MAX_BLACKBOX_BYTES,
        },
        "includedFiles": [
            "manifest.json",
            "support-telemetry-state.json",
            "support-telemetry-events.jsonl",
            "support-blackbox-tail.jsonl",
            "redacted-log-tail.json",
            "states/*.json",
        ],
        "excludedByDesign": [
            "screenshots",
            "raw-frames",
            "full-chat-transcripts",
            "tokens",
            "secret files",
        ],
        "eventCount": len(events),
        "runtimeDir": str(runtime_dir),
    }


def create_support_bundle(
    runtime_dir: Path,
    state: dict[str, Any],
    events: list[dict[str, Any]],
    blackbox_events: list[dict[str, Any]],
) -> tuple[Path, dict[str, Any], int]:
    support_dir = runtime_dir / "support"
    support_dir.mkdir(parents=True, exist_ok=True)
    bundle_path = support_dir / "support-bundle-latest.zip"
    manifest = build_manifest(runtime_dir, state, events)
    redactions = 0
    redacted_logs = []
    for file_name in ("windows-app.log", "diagnostic-timeline.jsonl"):
        for line in read_tail_lines(runtime_dir / file_name, 80):
            clean, count = redact_text(line, 1200)
            redactions += count
            redacted_logs.append({"file": file_name, "line": clean})
    state_files: dict[str, Any] = {}
    for _source_id, file_name, _category in STATE_SOURCES:
        raw = read_json(runtime_dir / file_name)
        if not raw:
            continue
        clean, count = redact_json(raw)
        redactions += count
        state_files[file_name] = clean
    with zipfile.ZipFile(bundle_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("manifest.json", json.dumps(manifest, ensure_ascii=False, indent=2))
        archive.writestr("support-telemetry-state.json", json.dumps(state, ensure_ascii=False, indent=2))
        archive.writestr(
            "support-telemetry-events.jsonl",
            "".join(json.dumps(event, ensure_ascii=False) + "\n" for event in events),
        )
        archive.writestr(
            "support-blackbox-tail.jsonl",
            "".join(json.dumps(event, ensure_ascii=False) + "\n" for event in blackbox_events[-120:]),
        )
        archive.writestr("redacted-log-tail.json", json.dumps(redacted_logs, ensure_ascii=False, indent=2))
        for file_name, content in state_files.items():
            archive.writestr(f"states/{file_name}", json.dumps(content, ensure_ascii=False, indent=2))
    write_json(support_dir / "support-bundle-manifest.json", manifest)
    return bundle_path, manifest, redactions


def build_state(
    runtime_dir: Path,
    states: dict[str, dict[str, Any]],
    sources: list[dict[str, Any]],
    events: list[dict[str, Any]],
    trace_id: str,
    correlation_id: str,
    retained_events: list[dict[str, Any]],
    redactions: int,
    *,
    bundle_path: Path | None = None,
    manifest_path: Path | None = None,
) -> dict[str, Any]:
    status = classify_status(events)
    critical = sum(1 for event in events if event.get("severity") == "critical")
    warnings = sum(1 for event in events if event.get("severity") == "warning")
    safe_events = safe_cloud_events(events)
    support_bundle_ready = bundle_path is not None and bundle_path.exists()
    headline = {
        "ok": "Soporte local listo: sin incidentes nuevos.",
        "attention": "Soporte local encontro incidentes explicables.",
        "blocked": "Soporte local bloqueo subida por privacidad o riesgo critico.",
        "error": "Soporte local fallo al diagnosticar.",
    }.get(status, "Soporte local revisado.")
    return {
        "status": status,
        "engine": ENGINE,
        "version": VERSION,
        "updatedAt": utc_now(),
        "contract": CONTRACT,
        "traceId": trace_id,
        "correlationId": correlation_id,
        "policy": {
            "signals": ["logs", "metrics", "traces", "local_dumps_metadata", "runtime_state"],
            "localFirst": True,
            "redactionRequired": True,
            "supportBundleRequiresConsent": True,
            "rawScreenshotsUploaded": False,
            "fullChatsUploaded": False,
            "tokensLogged": False,
            "openTelemetryCompatibleFields": ["traceId", "correlationId", "severity", "source", "category"],
            "eventSourceEventPipePlanned": True,
            "werLocalDumpsPolicy": "metadata_only_without_explicit_operator_consent",
        },
        "sources": sources,
        "summary": {
            "sourcesRead": sum(1 for source in sources if source.get("hasData")),
            "incidentsOpen": len(events),
            "criticalIncidents": critical,
            "warningIncidents": warnings,
            "traceEventsWritten": len(events),
            "blackboxEventsRetained": len(retained_events),
            "bundleReady": support_bundle_ready,
            "cloudSafeEventsPrepared": len(safe_events),
            "redactionsApplied": redactions,
        },
        "incidents": events[-40:],
        "blackbox": {
            "path": "support-blackbox.jsonl",
            "retentionEvents": MAX_BLACKBOX_EVENTS,
            "retainedEvents": len(retained_events),
            "maxBytes": MAX_BLACKBOX_BYTES,
            "localOnly": True,
        },
        "supportBundle": {
            "ready": support_bundle_ready,
            "path": str(bundle_path) if bundle_path else "",
            "manifest": str(manifest_path) if manifest_path else "",
            "requiresConsent": True,
            "containsRawScreenshots": False,
            "containsFullChats": False,
            "containsSecrets": False,
        },
        "privacy": {
            "redactionApplied": redactions > 0,
            "sensitiveUploadBlocked": True,
            "requiresExplicitUploadConsent": True,
            "safeCloudTelemetryOnly": True,
        },
        "cloudSafeEvents": safe_events,
        "humanReport": {
            "headline": headline,
            "queEstaPasando": [
                f"traceId={trace_id}, correlationId={correlation_id}.",
                f"Revise {sum(1 for source in sources if source.get('hasData'))}/{len(sources)} fuentes locales.",
                f"Incidentes abiertos={len(events)}, criticos={critical}, advertencias={warnings}.",
            ],
            "queQuedoListo": [
                "Caja negra local con retencion y resumen de incidentes.",
                "Support Bundle local redactado, sin capturas, chats completos ni secretos.",
                "Trazas correlacionadas para entender por que la IA observa, decide, falla o se detiene.",
            ],
            "queNecesitoDeBryams": [] if status == "ok" else ["Revisar la zona de soporte si el incidente pide accion humana."],
            "riesgos": [
                "Los dumps de Windows pueden contener memoria sensible; solo guardo metadatos y no los subo.",
                "La nube recibe solo resumen seguro salvo permiso explicito.",
            ],
        },
    }


def run_support_telemetry_once(
    runtime_dir: Path | str = RUNTIME_DIR,
    *,
    state_file: Path | str | None = None,
    events_file: Path | str | None = None,
    make_bundle: bool = True,
    simulate: list[str] | None = None,
) -> dict[str, Any]:
    runtime = Path(runtime_dir).resolve()
    runtime.mkdir(parents=True, exist_ok=True)
    simulated = simulate or []
    states, sources = collect_state_sources(runtime)
    trace_id = trace_id_for(runtime, states, simulated)
    correlation_id = correlation_id_for(states, trace_id)
    events: list[dict[str, Any]] = []
    redactions = 0
    for collector in (
        incidents_from_states(states, trace_id, correlation_id),
        incidents_from_logs(runtime, trace_id, correlation_id),
        collect_local_dump_metadata(runtime, trace_id, correlation_id),
        simulation_events(simulated, trace_id, correlation_id),
    ):
        collected, count = collector
        events.extend(collected)
        redactions += count

    deduped: dict[str, dict[str, Any]] = {}
    for event in events:
        deduped[text(event.get("eventId"))] = event
    events = list(deduped.values())[-80:]

    event_path = Path(events_file) if events_file else runtime / "support-telemetry-events.jsonl"
    retained_events = write_jsonl_retained(event_path, events, MAX_EVENT_LOG)
    blackbox_entry = {
        "eventType": "support_blackbox_cycle",
        "createdAt": utc_now(),
        "traceId": trace_id,
        "correlationId": correlation_id,
        "status": classify_status(events),
        "summary": {
            "incidentsOpen": len(events),
            "criticalIncidents": sum(1 for event in events if event.get("severity") == "critical"),
            "warningIncidents": sum(1 for event in events if event.get("severity") == "warning"),
        },
        "incidentIds": [event.get("eventId") for event in events[-20:]],
    }
    blackbox_events = write_jsonl_retained(runtime / "support-blackbox.jsonl", [blackbox_entry], MAX_BLACKBOX_EVENTS)
    state = build_state(runtime, states, sources, events, trace_id, correlation_id, blackbox_events, redactions)
    output_state = Path(state_file) if state_file else runtime / "support-telemetry-state.json"
    write_json(output_state, state)
    write_json(runtime / "support-telemetry-report.json", state["humanReport"])

    if make_bundle:
        bundle_path, _manifest, bundle_redactions = create_support_bundle(runtime, state, events, blackbox_events)
        redactions += bundle_redactions
        state = build_state(
            runtime,
            states,
            sources,
            events,
            trace_id,
            correlation_id,
            blackbox_events,
            redactions,
            bundle_path=bundle_path,
            manifest_path=runtime / "support" / "support-bundle-manifest.json",
        )
        write_json(output_state, state)
        write_json(runtime / "support-telemetry-report.json", state["humanReport"])
    return state


def build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="AriadGSM Support & Telemetry Core")
    parser.add_argument("--runtime-dir", default="./runtime")
    parser.add_argument("--state-file", default="")
    parser.add_argument("--events-file", default="")
    parser.add_argument("--no-bundle", action="store_true")
    parser.add_argument("--simulate-crash", action="store_true")
    parser.add_argument("--simulate-hang", action="store_true")
    parser.add_argument("--simulate-privacy-leak", action="store_true")
    parser.add_argument("--json", action="store_true")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_arg_parser().parse_args(argv)
    simulate = []
    if args.simulate_crash:
        simulate.append("crash")
    if args.simulate_hang:
        simulate.append("hang")
    if args.simulate_privacy_leak:
        simulate.append("privacy")
    state = run_support_telemetry_once(
        Path(args.runtime_dir),
        state_file=Path(args.state_file) if args.state_file else None,
        events_file=Path(args.events_file) if args.events_file else None,
        make_bundle=not args.no_bundle,
        simulate=simulate,
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=False))
    else:
        print(f"{state['status']}: {state['humanReport']['headline']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
