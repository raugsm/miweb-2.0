from __future__ import annotations

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


AGENT_ROOT = Path(__file__).resolve().parents[1]
RUNTIME_DIR = AGENT_ROOT / "runtime"
VERSION = "0.9.7"
ENGINE = "ariadgsm_window_reality_resolver"
CONTRACT = "window_reality_state"

READY_STATUSES = {"ready", "ok", "visible", "visible_ready", "action_ready", "current"}
COVERED_STATUSES = {"covered_by_window", "covered", "occluded"}
HUMAN_STATUSES = {"login_required", "needs_use_here", "profile_error", "duplicate_whatsapp_windows"}
MISSING_STATUSES = {
    "not_open",
    "browser_not_found",
    "browser_running_no_visible_whatsapp",
    "browser_busy_open_web",
}


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
    raw = str(value).strip()
    return raw if raw else default


def as_bool(value: Any) -> bool:
    return bool(value) if isinstance(value, bool) else False


def as_int(value: Any, default: int = 0) -> int:
    try:
        if isinstance(value, bool) or value is None or value == "":
            return default
        return int(float(value))
    except (TypeError, ValueError):
        return default


def normalized_status(value: Any) -> str:
    return text(value, "unknown").lower()


def freshness(value: Any, now: datetime, max_age_ms: int) -> dict[str, Any]:
    age = age_ms(value, now)
    if age is None:
        return {"status": "unknown", "ageMs": None, "maxAgeMs": max_age_ms, "fresh": False}
    return {"status": "fresh" if age <= max_age_ms else "stale", "ageMs": age, "maxAgeMs": max_age_ms, "fresh": age <= max_age_ms}


def nested(document: dict[str, Any], *keys: str) -> Any:
    value: Any = document
    for key in keys:
        if not isinstance(value, dict):
            return None
        value = value.get(key)
    return value


def channel_list(document: dict[str, Any]) -> list[dict[str, Any]]:
    value = document.get("channels")
    return [item for item in value if isinstance(item, dict)] if isinstance(value, list) else []


def expected_channels(cabin: dict[str, Any], manager: dict[str, Any], authority: dict[str, Any]) -> list[str]:
    ids: list[str] = []
    for source in (cabin, manager, authority):
        for item in channel_list(source):
            channel_id = text(item.get("channelId"))
            if channel_id and channel_id not in ids:
                ids.append(channel_id)
    return ids or ["wa-1", "wa-2", "wa-3"]


def find_channel(document: dict[str, Any], channel_id: str) -> dict[str, Any]:
    for item in channel_list(document):
        if text(item.get("channelId")).lower() == channel_id.lower():
            return item
    return {}


def reader_messages_for_channel(reader: dict[str, Any], channel_id: str) -> list[dict[str, Any]]:
    messages: list[dict[str, Any]] = []
    for key in ("latestMessages", "messages", "visibleMessages", "samples"):
        value = reader.get(key)
        if not isinstance(value, list):
            continue
        for item in value:
            if isinstance(item, dict) and text(item.get("channelId")).lower() == channel_id.lower():
                messages.append(item)
    return messages


def build_signal(kind: str, status: str, confidence: float, detail: str, evidence: list[str] | None = None) -> dict[str, Any]:
    return {
        "kind": kind,
        "status": status,
        "confidence": round(max(0.0, min(1.0, confidence)), 3),
        "detail": detail,
        "evidence": evidence or [],
    }


def structural_signal(channel: dict[str, Any], cabin_fresh: dict[str, Any]) -> dict[str, Any]:
    raw_status = normalized_status(channel.get("status"))
    is_ready = as_bool(channel.get("isReady")) or as_bool(channel.get("structuralReady")) or raw_status in READY_STATUSES
    if not cabin_fresh["fresh"]:
        return build_signal("structural", "stale", 0.1, "Cabin readiness esta viejo o sin fecha.")
    if is_ready:
        return build_signal("structural", "ok", 0.9, "Windows encontro el navegador asignado y lo marca listo.")
    if raw_status in COVERED_STATUSES:
        return build_signal("structural", "conflict", 0.55, "Windows encontro WhatsApp, pero otra ventana lo cubre.", evidence_from_channel(channel))
    if raw_status in HUMAN_STATUSES:
        return build_signal("structural", "needs_human", 0.35, text(channel.get("detail"), "WhatsApp requiere accion humana."))
    if raw_status in MISSING_STATUSES:
        return build_signal("structural", "missing", 0.2, text(channel.get("detail"), "No encontre WhatsApp listo."))
    return build_signal("structural", "unknown", 0.35, text(channel.get("detail"), "Windows no confirmo si el canal esta listo."))


def visual_signal(channel: dict[str, Any], cabin_fresh: dict[str, Any]) -> dict[str, Any]:
    window = channel.get("window") if isinstance(channel.get("window"), dict) else {}
    title = text(window.get("title") if isinstance(window, dict) else "")
    process = text(window.get("processName") if isinstance(window, dict) else "")
    raw_status = normalized_status(channel.get("status"))
    if not cabin_fresh["fresh"]:
        return build_signal("visual", "stale", 0.1, "No puedo confiar en geometria visual vieja.")
    if not window:
        return build_signal("visual", "missing", 0.2, "No hay ventana asociada al canal.")
    if raw_status in COVERED_STATUSES:
        return build_signal("visual", "conflict", 0.45, "La ventana existe, pero la zona aparece cubierta.", [title])
    if "whatsapp" in title.lower() or as_bool(channel.get("isReady")):
        return build_signal("visual", "ok", 0.8, f"{process} muestra una ventana WhatsApp en la cabina.", [title])
    return build_signal("visual", "unknown", 0.4, "Hay ventana de navegador, pero no confirma WhatsApp visible.", [title])


def semantic_signal(reader: dict[str, Any], channel_id: str, reader_fresh: dict[str, Any]) -> dict[str, Any]:
    if not reader:
        return build_signal("semantic", "unknown", 0.25, "Reader Core aun no publico lectura.")
    if not reader_fresh["fresh"]:
        return build_signal("semantic", "stale", 0.2, "Reader Core publico una lectura vieja.")
    status = normalized_status(reader.get("status"))
    if status in {"blocked", "error", "failed"}:
        return build_signal("semantic", "blocked", 0.2, text(reader.get("summary"), "Reader Core esta bloqueado."))
    messages = reader_messages_for_channel(reader, channel_id)
    if messages:
        return build_signal("semantic", "ok", 0.88, f"Reader Core vio {len(messages)} mensajes del canal.", [text(item.get("text") or item.get("summary")) for item in messages[:3]])
    if status in {"ok", "idle", "attention"}:
        return build_signal("semantic", "unknown", 0.45, "Reader Core esta vivo, pero aun no asocio mensajes a este canal.")
    return build_signal("semantic", "unknown", 0.3, "No hay evidencia semantica suficiente.")


def actionability_signal(input_state: dict[str, Any], hands: dict[str, Any], input_fresh: dict[str, Any], hands_fresh: dict[str, Any]) -> dict[str, Any]:
    if input_state and input_fresh["fresh"]:
        phase = normalized_status(input_state.get("phase"))
        if as_bool(input_state.get("operatorHasPriority")) or phase in {"operator_control", "operator_cooldown"}:
            return build_signal("actionability", "operator_busy", 0.9, text(input_state.get("summary"), "Bryams tiene prioridad sobre mouse y teclado."))
    if hands and hands_fresh["fresh"]:
        status = normalized_status(hands.get("status"))
        if status in {"blocked", "error", "failed"}:
            return build_signal("actionability", "blocked", 0.4, text(hands.get("lastSummary") or hands.get("summary"), "Hands reporta bloqueo."))
        return build_signal("actionability", "ok", 0.72, "Hands esta vivo y puede verificar acciones cuando Trust & Safety lo permita.")
    if input_state or hands:
        return build_signal("actionability", "stale", 0.25, "Input/Hands existen, pero la evidencia no esta fresca.")
    return build_signal("actionability", "unknown", 0.35, "Aun no hay estado de manos/input.")


def freshness_signal(*fresh_inputs: dict[str, Any]) -> dict[str, Any]:
    stale = [item for item in fresh_inputs if item["status"] == "stale"]
    unknown = [item for item in fresh_inputs if item["status"] == "unknown"]
    if stale:
        return build_signal("freshness", "stale", 0.2, f"{len(stale)} fuentes estan viejas.")
    if unknown:
        return build_signal("freshness", "partial", 0.55, f"{len(unknown)} fuentes aun no publicaron fecha.")
    return build_signal("freshness", "ok", 0.85, "La evidencia principal esta fresca.")


def evidence_from_channel(channel: dict[str, Any]) -> list[str]:
    evidence = channel.get("evidence")
    if isinstance(evidence, list):
        return [text(item) for item in evidence if text(item)][:6]
    return []


def decide_channel(channel_id: str, signals: list[dict[str, Any]], channel: dict[str, Any]) -> dict[str, Any]:
    by_kind = {signal["kind"]: signal for signal in signals}
    structural = by_kind["structural"]["status"]
    visual = by_kind["visual"]["status"]
    semantic = by_kind["semantic"]["status"]
    actionability = by_kind["actionability"]["status"]
    freshness_status = by_kind["freshness"]["status"]

    status = "UNKNOWN"
    is_operational = False
    requires_human = False
    hands_may_act = False
    reason = "Evidencia insuficiente."
    confidence = sum(signal["confidence"] for signal in signals) / len(signals)

    if freshness_status == "stale" or structural == "stale":
        status = "STALE_STATE"
        confidence = min(confidence, 0.35)
        reason = "El estado no esta fresco; no se toma como listo aunque antes lo haya parecido."
    elif structural == "ok" and visual == "ok" and semantic in {"ok", "unknown"}:
        status = "READY" if semantic == "ok" else "READY_PENDING_SEMANTIC"
        is_operational = True
        hands_may_act = actionability == "ok"
        reason = "Ventana, pantalla y lectura no se contradicen."
        confidence = max(confidence, 0.78 if semantic == "unknown" else 0.86)
    elif structural == "conflict" and semantic == "ok":
        status = "READY_WITH_CONFLICT"
        is_operational = True
        hands_may_act = False
        reason = "Reader ve mensajes, pero la geometria dice que la ventana esta cubierta; puedo leer, no debo mover manos aun."
        confidence = max(confidence, 0.72)
    elif structural == "conflict" or visual == "conflict":
        status = "COVERED_CONFIRMED"
        requires_human = True
        reason = text(channel.get("detail"), "La zona WhatsApp esta cubierta por otra ventana.")
        confidence = min(confidence, 0.58)
    elif structural == "needs_human":
        status = "HUMAN_REQUIRED"
        requires_human = True
        reason = text(channel.get("detail"), "WhatsApp requiere accion humana.")
        confidence = min(confidence, 0.55)
    elif structural in {"missing", "unknown"}:
        status = "MISSING_OR_WRONG_SESSION"
        reason = text(channel.get("detail"), "No hay consenso de que este canal este visible.")
        confidence = min(confidence, 0.48)

    if actionability == "operator_busy" and is_operational:
        status = "READY_OPERATOR_BUSY"
        hands_may_act = False
        reason = "El canal se puede leer, pero las manos ceden porque Bryams esta usando el mouse/teclado."
    elif actionability == "blocked":
        hands_may_act = False

    return {
        "channelId": channel_id,
        "status": status,
        "confidence": round(max(0.0, min(1.0, confidence)), 3),
        "structuralReady": is_operational,
        "semanticFresh": semantic == "ok",
        "actionReady": hands_may_act,
        "isOperational": is_operational,
        "requiresHuman": requires_human,
        "handsMayAct": hands_may_act,
        "decision": {
            "reason": reason,
            "accepted": is_operational,
            "actionPolicy": "read_only" if is_operational and not hands_may_act else ("read_and_act" if hands_may_act else "hold"),
        },
        "signals": signals,
        "evidence": {
            "detail": text(channel.get("detail")),
            "window": channel.get("window") if isinstance(channel.get("window"), dict) else None,
            "rawStatus": text(channel.get("status")),
            "sourceEvidence": evidence_from_channel(channel),
        },
    }


def build_window_reality_state(runtime_dir: Path) -> dict[str, Any]:
    runtime = runtime_dir.resolve()
    now = datetime.now(timezone.utc)
    cabin = read_json(runtime / "cabin-readiness.json")
    manager = read_json(runtime / "cabin-manager-state.json")
    authority = read_json(runtime / "cabin-authority-state.json")
    reader = read_json(runtime / "reader-core-state.json")
    input_state = read_json(runtime / "input-arbiter-state.json")
    hands = read_json(runtime / "hands-state.json")

    policy = {
        "evidenceFusion": [
            "structural_windows",
            "visual_geometry",
            "semantic_reader_core",
            "freshness_ttl",
            "actionability_input_hands",
        ],
        "freshness": {
            "cabinReadinessMaxAgeMs": 45_000,
            "readerCoreMaxAgeMs": 90_000,
            "inputArbiterMaxAgeMs": 30_000,
            "handsMaxAgeMs": 60_000,
        },
        "actionability": {
            "operatorHasPriority": True,
            "doNotActOnCoveredWindow": True,
            "allowReadWhenSemanticFreshButVisualConflicted": True,
        },
    }

    cabin_fresh = freshness(cabin.get("updatedAt") or manager.get("updatedAt"), now, policy["freshness"]["cabinReadinessMaxAgeMs"])
    reader_fresh = freshness(reader.get("updatedAt") or reader.get("observedAt"), now, policy["freshness"]["readerCoreMaxAgeMs"])
    input_fresh = freshness(input_state.get("updatedAt") or input_state.get("observedAt"), now, policy["freshness"]["inputArbiterMaxAgeMs"])
    hands_fresh = freshness(hands.get("updatedAt") or hands.get("observedAt"), now, policy["freshness"]["handsMaxAgeMs"])

    channels = []
    for channel_id in expected_channels(cabin, manager, authority):
        channel = find_channel(cabin, channel_id) or find_channel(manager, channel_id) or find_channel(authority, channel_id)
        signals = [
            structural_signal(channel, cabin_fresh),
            visual_signal(channel, cabin_fresh),
            semantic_signal(reader, channel_id, reader_fresh),
            actionability_signal(input_state, hands, input_fresh, hands_fresh),
            freshness_signal(cabin_fresh, reader_fresh, input_fresh, hands_fresh),
        ]
        channels.append(decide_channel(channel_id, signals, channel))

    operational = sum(1 for item in channels if item["isOperational"])
    ready = sum(1 for item in channels if item["status"] in {"READY", "READY_PENDING_SEMANTIC", "READY_OPERATOR_BUSY"})
    structural_ready = sum(1 for item in channels if item["structuralReady"])
    action_ready = sum(1 for item in channels if item["actionReady"])
    conflicted = sum(1 for item in channels if item["status"] in {"READY_WITH_CONFLICT", "COVERED_CONFIRMED"})
    human = sum(1 for item in channels if item["requiresHuman"])
    stale_inputs = sum(1 for item in (cabin_fresh, reader_fresh, input_fresh, hands_fresh) if item["status"] == "stale")

    if operational == len(channels) and human == 0 and stale_inputs == 0 and conflicted == 0:
        status = "ok"
        headline = "Cabina verificable"
    elif operational > 0:
        status = "attention"
        headline = "Cabina usable con dudas claras"
    elif channels:
        status = "blocked"
        headline = "Cabina sin realidad accionable"
    else:
        status = "idle"
        headline = "Cabina sin canales observados"

    what_accept = [
        f"{item['channelId']}: {item['status']} ({int(item['confidence'] * 100)}%)"
        for item in channels
        if item["isOperational"]
    ]
    what_doubt = [
        f"{item['channelId']}: {item['status']} - {item['decision']['reason']}"
        for item in channels
        if not item["isOperational"] or item["status"] in {"READY_WITH_CONFLICT", "READY_OPERATOR_BUSY"}
    ]
    needs = [
        f"{item['channelId']}: {item['decision']['reason']}"
        for item in channels
        if item["requiresHuman"]
    ]
    if not needs and any(item["status"] == "STALE_STATE" for item in channels):
        needs.append("Ejecutar Alistar WhatsApps o Encender IA para refrescar evidencia antes de actuar.")
    if not needs:
        needs.append("No necesito ayuda inmediata; seguire leyendo solo canales con evidencia fresca.")

    return {
        "status": status,
        "engine": ENGINE,
        "version": VERSION,
        "updatedAt": utc_now(),
        "contract": CONTRACT,
        "policy": policy,
        "inputs": [
            {"file": "cabin-readiness.json", "freshness": cabin_fresh},
            {"file": "reader-core-state.json", "freshness": reader_fresh},
            {"file": "input-arbiter-state.json", "freshness": input_fresh},
            {"file": "hands-state.json", "freshness": hands_fresh},
        ],
        "summary": {
            "expectedChannels": len(channels),
            "operationalChannels": operational,
            "readyChannels": ready,
            "structuralReadyChannels": structural_ready,
            "actionReadyChannels": action_ready,
            "conflictedChannels": conflicted,
            "requiresHumanChannels": human,
            "staleInputs": stale_inputs,
            "handsMayActChannels": sum(1 for item in channels if item["handsMayAct"]),
        },
        "channels": channels,
        "humanReport": {
            "headline": headline,
            "queEstaPasando": [
                f"Fusione ventana, pantalla, Reader Core, frescura e input para {len(channels)} canales.",
                f"Operables={operational}, conflictos={conflicted}, evidencia vieja={stale_inputs}.",
            ],
            "queAcepte": what_accept[:6],
            "queDude": what_doubt[:8],
            "queNecesitoDeBryams": needs[:6],
            "riesgos": [
                "No permito manos si la ventana esta cubierta aunque parezca WhatsApp.",
                "No tomo estados viejos como verdad operativa.",
                "Si Bryams usa mouse/teclado, manos se pausan pero ojos y memoria pueden seguir.",
            ],
        },
    }


def run_window_reality_once(runtime_dir: Path | str = RUNTIME_DIR, state_file: Path | str | None = None) -> dict[str, Any]:
    runtime = Path(runtime_dir)
    state = build_window_reality_state(runtime)
    write_json_atomic(Path(state_file) if state_file else runtime / "window-reality-state.json", state)
    return state


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="AriadGSM Window Reality Resolver")
    parser.add_argument("--runtime-dir", type=Path, default=RUNTIME_DIR)
    parser.add_argument("--state-file", type=Path, default=None)
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args(argv)
    state = run_window_reality_once(args.runtime_dir, args.state_file)
    if args.json:
        print(json.dumps(state, ensure_ascii=True))
    else:
        print(state["humanReport"]["headline"])
    return 0 if state["status"] != "blocked" else 2


if __name__ == "__main__":
    raise SystemExit(main())
