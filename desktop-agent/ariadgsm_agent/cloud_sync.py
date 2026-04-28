from __future__ import annotations

import argparse
import hashlib
import json
import os
import random
import time
from collections import deque
from datetime import datetime, timezone
from pathlib import Path
from typing import Any
from urllib import error, request


VERSION = "0.9.2"
ENGINE = "ariadgsm_cloud_sync"
CONTRACT = "cloud_sync_state"
DEFAULT_CLOUD_URL = "https://ariadgsm.com"
SYNC_PATH = "/api/operativa-v2/cloud/sync"
MAX_EVENTS_PER_BATCH = 240
MAX_TIMELINE_LINES = 80
MAX_MESSAGES_PER_CONVERSATION = 24
MAX_REVIEW_EVENTS = 50
MAX_RETRY_ATTEMPTS = 3
TRANSIENT_STATUS_CODES = {408, 409, 425, 429, 500, 502, 503, 504}


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def read_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        return data if isinstance(data, dict) else {}
    except Exception as exc:
        return {"status": "error", "lastError": f"No pude leer {path.name}: {exc}"}


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(temp, path)


def sha256_text(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8", errors="replace")).hexdigest()


def normalize_url(value: str) -> str:
    url = (value or DEFAULT_CLOUD_URL).strip().rstrip("/")
    if not url:
        return DEFAULT_CLOUD_URL
    return url


def sync_url(base_url: str) -> str:
    return f"{normalize_url(base_url)}{SYNC_PATH}"


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


def bool_value(value: Any, default: bool = False) -> bool:
    if isinstance(value, bool):
        return value
    if isinstance(value, str):
        return value.strip().lower() in {"1", "true", "yes", "si", "on"}
    return default


def bounded_text(value: Any, limit: int = 1500) -> str:
    raw = " ".join(text(value).replace("\r", " ").replace("\n", " ").split())
    return raw[:limit]


def read_jsonl_tail(path: Path, limit: int) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    lines: deque[str] = deque(maxlen=limit)
    try:
        with path.open("r", encoding="utf-8-sig", errors="replace") as handle:
            for line in handle:
                if line.strip():
                    lines.append(line)
    except Exception:
        return []

    result: list[dict[str, Any]] = []
    for line in lines:
        try:
            data = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(data, dict):
            result.append(data)
    return result


def load_ledger(path: Path) -> dict[str, Any]:
    ledger = read_json(path)
    if not ledger:
        ledger = {"version": VERSION, "sentKeys": [], "batches": []}
    if not isinstance(ledger.get("sentKeys"), list):
        ledger["sentKeys"] = []
    if not isinstance(ledger.get("batches"), list):
        ledger["batches"] = []
    return ledger


def save_ledger(path: Path, ledger: dict[str, Any], sent_keys: list[str], batch_id: str, payload_hash: str) -> None:
    existing = [text(item) for item in ledger.get("sentKeys") or []]
    combined = list(dict.fromkeys([*existing, *sent_keys]))[-5000:]
    batches = list(ledger.get("batches") or [])
    batches.insert(
        0,
        {
            "batchId": batch_id,
            "payloadHash": payload_hash,
            "sentAt": utc_now(),
            "events": len(sent_keys),
        },
    )
    write_json(
        path,
        {
            "version": VERSION,
            "updatedAt": utc_now(),
            "sentKeys": combined,
            "batches": batches[:120],
        },
    )


def read_secret_file(path: Path) -> str:
    if not path.exists():
        return ""
    try:
        content = path.read_text(encoding="utf-8-sig").strip()
    except Exception:
        return ""
    for line in content.splitlines():
        clean = line.strip()
        if not clean or clean.startswith("#"):
            continue
        if "=" in clean:
            name, value = clean.split("=", 1)
            if name.strip().upper() in {"OPERATIVA_AGENT_KEY", "OPERATIVA_AGENT_TOKEN", "ARIADGSM_CLOUD_TOKEN"}:
                return value.strip()
        elif len(clean) >= 24:
            return clean
    return ""


def resolve_token(repo_root: Path) -> tuple[str, str]:
    for name in ("ARIADGSM_CLOUD_TOKEN", "OPERATIVA_AGENT_KEY", "OPERATIVA_AGENT_TOKEN"):
        value = os.environ.get(name, "").strip()
        if value:
            return value, f"env:{name}"

    secret_paths = [
        repo_root / "scripts" / "visual-agent" / "railway-operativa-agent-key.secret.txt",
        repo_root / "desktop-agent" / "cloud-agent-token.secret.txt",
    ]
    for path in secret_paths:
        token = read_secret_file(path)
        if token:
            return token, str(path.relative_to(repo_root))
    return "", ""


def resolve_enabled(explicit_enabled: bool | None, token: str) -> bool:
    if explicit_enabled is not None:
        return explicit_enabled
    env = os.environ.get("ARIADGSM_CLOUD_SYNC_ENABLED", "").strip()
    if env:
        return bool_value(env)
    return bool(token)


def event_key(channel_id: str, conversation_id: str, message_id: str, text_value: str) -> str:
    stable = sha256_text(f"{channel_id}|{conversation_id}|{message_id}|{bounded_text(text_value, 300)}")[:16]
    return f"cloud:{channel_id}:{conversation_id}:{message_id}:{stable}"


def looks_cloud_safe_message(text_value: str) -> bool:
    clean = bounded_text(text_value, 300).lower()
    if not clean:
        return False
    blocked = {
        "abrir foto",
        "reenviar archivo multimedia",
        "se fijo el chat",
        "se fij",
        "anadir esta pagina a marcadores",
        "escribir un mensaje para",
        "abrir los detalles del chat",
    }
    return not any(item in clean for item in blocked)


def direction(value: Any) -> str:
    raw = text(value, "unknown").lower()
    if raw in {"client", "customer", "incoming"}:
        return "client"
    if raw in {"agent", "business", "outgoing", "me"}:
        return "agent"
    return "unknown"


def build_agent_status_event(runtime_kernel: dict[str, Any]) -> dict[str, Any]:
    authority = runtime_kernel.get("authority") if isinstance(runtime_kernel.get("authority"), dict) else {}
    summary = runtime_kernel.get("summary") if isinstance(runtime_kernel.get("summary"), dict) else {}
    status = text(runtime_kernel.get("status"), "idle")
    can_sync = bool(authority.get("canSync"))
    return {
        "type": "agent_status",
        "data": {
            "mode": "local_ia",
            "autonomyLevel": 3 if bool(authority.get("canThink")) else 1,
            "connected": can_sync,
            "lastHeartbeat": utc_now(),
            "message": (
                f"Runtime Kernel {status}: "
                f"{number(summary.get('enginesRunning'))}/{number(summary.get('enginesTotal'))} motores activos; "
                f"incidentes abiertos={number(summary.get('incidentsOpen'))}."
            ),
        },
    }


def build_checkpoint_events(runtime_dir: Path) -> list[dict[str, Any]]:
    cabin = read_json(runtime_dir / "cabin-readiness.json")
    channels = cabin.get("channels") if isinstance(cabin.get("channels"), list) else []
    events: list[dict[str, Any]] = []
    for item in channels:
        if not isinstance(item, dict):
            continue
        channel_id = text(item.get("channelId") or item.get("id"))
        if not channel_id:
            continue
        events.append(
            {
                "type": "checkpoint",
                "data": {
                    "id": f"checkpoint-{channel_id}",
                    "channelId": channel_id,
                    "status": text(item.get("status"), "unknown").lower(),
                    "summary": bounded_text(item.get("detail") or item.get("summary"), 400),
                    "observedAt": text(item.get("updatedAt") or cabin.get("updatedAt") or utc_now()),
                },
            }
        )
    return events


def build_message_events(runtime_dir: Path, ledger: dict[str, Any]) -> tuple[list[dict[str, Any]], list[str], dict[str, int]]:
    sent = {text(item) for item in ledger.get("sentKeys") or []}
    events: list[dict[str, Any]] = []
    keys: list[str] = []
    conversations = 0
    rejected = 0
    for conversation in read_jsonl_tail(runtime_dir / "timeline-events.jsonl", MAX_TIMELINE_LINES):
        messages = conversation.get("messages") if isinstance(conversation.get("messages"), list) else []
        if not messages:
            continue
        conversations += 1
        channel_id = text(conversation.get("channelId"), "wa-1")
        conversation_id = text(conversation.get("conversationId"), sha256_text(json.dumps(conversation, sort_keys=True))[:16])
        title = bounded_text(conversation.get("conversationTitle") or conversation.get("title") or "Chat sin nombre", 250)
        for message in messages[-MAX_MESSAGES_PER_CONVERSATION:]:
            if len(events) >= MAX_EVENTS_PER_BATCH:
                break
            if not isinstance(message, dict):
                rejected += 1
                continue
            message_id = text(message.get("messageId") or message.get("id"))
            message_text = bounded_text(message.get("text"))
            if not message_id:
                message_id = sha256_text(f"{channel_id}:{conversation_id}:{message_text}")[:20]
            key = event_key(channel_id, conversation_id, message_id, message_text)
            if key in sent or not looks_cloud_safe_message(message_text):
                rejected += 1
                continue
            sent_at = text(message.get("sentAt") or message.get("observedAt") or conversation.get("observedAt") or utc_now())
            events.append(
                {
                    "type": "whatsapp_message",
                    "data": {
                        "channelId": channel_id,
                        "conversationId": conversation_id,
                        "title": title,
                        "contactName": title,
                        "customerName": title,
                        "messageId": message_id,
                        "messageKey": key,
                        "senderName": bounded_text(message.get("senderName"), 160),
                        "text": message_text,
                        "sentAt": sent_at,
                        "direction": direction(message.get("direction")),
                        "processed": False,
                    },
                }
            )
            keys.append(key)
        if len(events) >= MAX_EVENTS_PER_BATCH:
            break
    return events, keys, {"conversationsSeen": conversations, "messagesPrepared": len(events), "messagesRejected": rejected}


def build_review_events(runtime_dir: Path) -> list[dict[str, Any]]:
    events: list[dict[str, Any]] = []
    for event in read_jsonl_tail(runtime_dir / "domain-events.jsonl", MAX_REVIEW_EVENTS * 4):
        if len(events) >= MAX_REVIEW_EVENTS:
            break
        privacy = event.get("privacy") if isinstance(event.get("privacy"), dict) else {}
        if not bool(privacy.get("cloudAllowed")):
            continue
        if bool(privacy.get("redactionRequired")):
            continue
        risk = event.get("risk") if isinstance(event.get("risk"), dict) else {}
        confidence = event.get("confidence")
        if not event.get("requiresHumanReview") and text(risk.get("riskLevel"), "low") not in {"medium", "high", "critical"}:
            continue
        event_id = text(event.get("eventId") or event.get("id") or sha256_text(json.dumps(event, sort_keys=True))[:20])
        summary = bounded_text(
            event.get("summary")
            or event.get("eventType")
            or (event.get("subject") if isinstance(event.get("subject"), str) else ""),
            600,
        )
        events.append(
            {
                "type": "review_item",
                "data": {
                    "type": "domain_event_review",
                    "title": f"IA detecto {text(event.get('eventType'), 'evento')} para revisar",
                    "description": summary,
                    "confidence": confidence if isinstance(confidence, (int, float)) else 0.72,
                    "priority": "alta" if text(risk.get("riskLevel")) in {"high", "critical"} else "media",
                    "relatedEntityType": "domain_event",
                    "relatedEntityId": event_id,
                    "dedupeKey": f"domain-event-review:{event_id}",
                },
            }
        )
    return events


def build_support_telemetry_events(runtime_dir: Path) -> list[dict[str, Any]]:
    support_state = read_json(runtime_dir / "support-telemetry-state.json")
    if not support_state:
        return []
    summary = support_state.get("summary") if isinstance(support_state.get("summary"), dict) else {}
    human = support_state.get("humanReport") if isinstance(support_state.get("humanReport"), dict) else {}
    safe_events = support_state.get("cloudSafeEvents") if isinstance(support_state.get("cloudSafeEvents"), list) else []
    events: list[dict[str, Any]] = [
        {
            "type": "support_telemetry",
            "data": {
                "eventId": f"support-telemetry-{sha256_text(text(support_state.get('traceId')) + text(support_state.get('updatedAt')))[:18]}",
                "traceId": text(support_state.get("traceId")),
                "correlationId": text(support_state.get("correlationId")),
                "status": text(support_state.get("status"), "missing"),
                "summary": bounded_text(human.get("headline") or "Soporte local revisado.", 500),
                "incidentsOpen": number(summary.get("incidentsOpen")),
                "criticalIncidents": number(summary.get("criticalIncidents")),
                "warningIncidents": number(summary.get("warningIncidents")),
                "redactionsApplied": number(summary.get("redactionsApplied")),
                "bundleReady": bool(summary.get("bundleReady")),
                "privacy": {
                    "rawScreenshotsUploaded": False,
                    "fullChatsUploaded": False,
                    "tokensLogged": False,
                    "supportBundleUploaded": False,
                },
            },
        }
    ]
    for item in safe_events[:12]:
        if isinstance(item, dict):
            events.append(item)
    return events


def build_payload(
    runtime_dir: Path,
    cloud_url: str,
    token_source: str,
    *,
    enabled: bool,
    ledger: dict[str, Any],
) -> tuple[dict[str, Any], list[str], dict[str, Any]]:
    runtime_kernel = read_json(runtime_dir / "runtime-kernel-state.json")
    authority = runtime_kernel.get("authority") if isinstance(runtime_kernel.get("authority"), dict) else {}
    message_events, sent_keys, message_summary = build_message_events(runtime_dir, ledger)
    checkpoint_events = build_checkpoint_events(runtime_dir)
    review_events = build_review_events(runtime_dir)
    support_events = build_support_telemetry_events(runtime_dir)
    events = [build_agent_status_event(runtime_kernel), *checkpoint_events, *message_events, *review_events, *support_events]
    events = events[:MAX_EVENTS_PER_BATCH]
    events_json = json.dumps(events, ensure_ascii=False, sort_keys=True)
    batch_id = f"cloudsync-{sha256_text(events_json)[:18]}"
    payload = {
        "schemaVersion": "cloud_sync_payload_v1",
        "id": batch_id,
        "idempotencyKey": batch_id,
        "payloadHash": sha256_text(events_json),
        "actor": "desktop_agent",
        "source": "ariadgsm_local_agent",
        "mode": "local_ia",
        "status": "ok" if enabled else "idle",
        "endpoint": sync_url(cloud_url),
        "runtimeKernel": {
            "status": text(runtime_kernel.get("status"), "missing"),
            "updatedAt": text(runtime_kernel.get("updatedAt")),
            "canObserve": bool(authority.get("canObserve")),
            "canThink": bool(authority.get("canThink")),
            "canAct": bool(authority.get("canAct")),
            "canSync": bool(authority.get("canSync")),
            "operatorHasPriority": bool(authority.get("operatorHasPriority")),
            "mainBlocker": bounded_text(authority.get("mainBlocker"), 500),
        },
        "summary": {
            **message_summary,
            "checkpointEvents": len(checkpoint_events),
            "reviewEvents": len(review_events),
            "supportTelemetryEvents": len(support_events),
            "eventsPrepared": len(events),
        },
        "conversations": message_summary["conversationsSeen"],
        "messages": message_summary["messagesPrepared"],
        "accountingEvents": len(review_events),
        "learningEvents": 0,
        "events": events,
        "security": {
            "tokenSource": token_source or "none",
            "tokenIncluded": False,
            "rawFramesUploaded": False,
            "screenshotsUploaded": False,
            "secretsLogged": False,
            "supportBundleUploaded": False,
        },
    }
    return payload, sent_keys, payload["summary"]


def post_payload(endpoint: str, token: str, payload: dict[str, Any], timeout: float) -> dict[str, Any]:
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    req = request.Request(
        endpoint,
        data=body,
        method="POST",
        headers={
            "Content-Type": "application/json; charset=utf-8",
            "Authorization": f"Bearer {token}",
            "Idempotency-Key": text(payload.get("idempotencyKey")),
            "User-Agent": f"AriadGSM-CloudSync/{VERSION}",
        },
    )
    with request.urlopen(req, timeout=timeout) as response:
        raw = response.read().decode("utf-8", errors="replace")
        try:
            parsed = json.loads(raw)
        except json.JSONDecodeError:
            parsed = {"raw": raw[:1000]}
        return {"statusCode": response.status, "body": parsed}


def run_with_retry(endpoint: str, token: str, payload: dict[str, Any], timeout: float) -> tuple[str, dict[str, Any], list[str]]:
    attempts: list[str] = []
    for attempt in range(1, MAX_RETRY_ATTEMPTS + 1):
        try:
            result = post_payload(endpoint, token, payload, timeout)
            return "ok", result, attempts
        except error.HTTPError as exc:
            detail = f"HTTP {exc.code}: {exc.reason}"
            attempts.append(detail)
            if exc.code not in TRANSIENT_STATUS_CODES:
                return "blocked", {"statusCode": exc.code, "error": detail}, attempts
        except error.URLError as exc:
            attempts.append(f"network: {exc.reason}")
        except TimeoutError:
            attempts.append("timeout")
        if attempt < MAX_RETRY_ATTEMPTS:
            time.sleep(min(4.0, 0.6 * (2 ** (attempt - 1))) + random.random() * 0.2)
    return "attention", {"error": attempts[-1] if attempts else "sync failed"}, attempts


def build_state(
    *,
    status: str,
    runtime_dir: Path,
    cloud_url: str,
    enabled: bool,
    authenticated: bool,
    token_source: str,
    payload: dict[str, Any] | None,
    sent_keys: list[str],
    result: dict[str, Any] | None,
    attempts: list[str],
    dry_run: bool,
    error_text: str = "",
) -> dict[str, Any]:
    runtime_kernel = read_json(runtime_dir / "runtime-kernel-state.json")
    authority = runtime_kernel.get("authority") if isinstance(runtime_kernel.get("authority"), dict) else {}
    summary = payload.get("summary") if isinstance(payload, dict) and isinstance(payload.get("summary"), dict) else {}
    main_blocker = error_text or bounded_text(authority.get("mainBlocker"), 500)
    can_sync = enabled and authenticated and status in {"ok", "dry_run"}
    return {
        "status": status,
        "engine": ENGINE,
        "version": VERSION,
        "updatedAt": utc_now(),
        "contract": CONTRACT,
        "endpoint": sync_url(cloud_url),
        "enabled": enabled,
        "authenticated": authenticated,
        "dryRun": dry_run,
        "runtimeKernel": {
            "status": text(runtime_kernel.get("status"), "missing"),
            "canSync": bool(authority.get("canSync")),
            "canAct": bool(authority.get("canAct")),
            "operatorHasPriority": bool(authority.get("operatorHasPriority")),
            "mainBlocker": bounded_text(authority.get("mainBlocker"), 500),
        },
        "policy": {
            "idempotencyRequired": True,
            "retryAttemptsMax": MAX_RETRY_ATTEMPTS,
            "rawFramesUploaded": False,
            "screenshotsUploaded": False,
            "secretsLogged": False,
            "tokenSource": token_source or "none",
        },
        "summary": {
            "eventsPrepared": number(summary.get("eventsPrepared")),
            "eventsSent": len(sent_keys) if status == "ok" else 0,
            "messagesPrepared": number(summary.get("messagesPrepared")),
            "messagesRejected": number(summary.get("messagesRejected")),
            "conversationsSeen": number(summary.get("conversationsSeen")),
            "reviewEvents": number(summary.get("reviewEvents")),
            "supportTelemetryEvents": number(summary.get("supportTelemetryEvents")),
            "attempts": len(attempts),
            "canSync": can_sync,
        },
        "batch": {
            "id": text(payload.get("id")) if isinstance(payload, dict) else "",
            "idempotencyKey": text(payload.get("idempotencyKey")) if isinstance(payload, dict) else "",
            "payloadHash": text(payload.get("payloadHash")) if isinstance(payload, dict) else "",
            "cloudAccepted": status == "ok",
            "responseStatusCode": number((result or {}).get("statusCode")),
        },
        "retry": {
            "attempts": attempts,
            "circuit": "closed" if status in {"ok", "dry_run", "idle"} else "open",
            "lastError": error_text or (attempts[-1] if attempts else ""),
        },
        "sourceFiles": {
            "runtimeKernel": "runtime-kernel-state.json",
            "timeline": "timeline-events.jsonl",
            "domainEvents": "domain-events.jsonl",
            "cabinReadiness": "cabin-readiness.json",
            "supportTelemetry": "support-telemetry-state.json",
        },
        "outputFiles": {
            "state": "cloud-sync-state.json",
            "report": "cloud-sync-report.json",
            "payload": "cloud-sync-payload.json",
            "ledger": "cloud-sync-ledger.json",
        },
        "humanReport": {
            "headline": {
                "ok": "Nube sincronizada con ariadgsm.com.",
                "dry_run": "Nube probada localmente sin publicar.",
                "idle": "Cloud Sync listo, esperando token o activacion.",
                "attention": "Cloud Sync no pudo publicar y dejo circuito abierto.",
                "blocked": "Cloud Sync fue rechazado por autenticacion o politica.",
                "error": "Cloud Sync encontro un error local.",
            }.get(status, "Cloud Sync revisado."),
            "queQuedoListo": [
                "Subida por eventos entendidos, no por capturas.",
                "Idempotencia activa para no duplicar mensajes si hay reintentos.",
                "Token fuera del navegador y sin registro en archivos visibles.",
            ],
            "queNecesitoDeBryams": [] if enabled and authenticated else ["Verificar OPERATIVA_AGENT_KEY o activar Cloud Sync."],
            "riesgos": [main_blocker] if main_blocker else [],
        },
    }


def run_cloud_sync_once(
    runtime_dir: Path,
    *,
    repo_root: Path | None = None,
    cloud_url: str = DEFAULT_CLOUD_URL,
    token: str = "",
    token_source: str = "",
    enabled: bool | None = None,
    dry_run: bool = False,
    timeout: float = 8.0,
) -> dict[str, Any]:
    runtime = runtime_dir.resolve()
    if repo_root is not None:
        root = repo_root.resolve()
    elif len(runtime.parents) > 1:
        root = runtime.parents[1].resolve()
    else:
        root = Path.cwd().resolve()
    resolved_token = token
    resolved_source = token_source
    if not resolved_token:
        resolved_token, resolved_source = resolve_token(root)
    active = resolve_enabled(enabled, resolved_token)
    ledger_path = runtime / "cloud-sync-ledger.json"
    ledger = load_ledger(ledger_path)
    payload, sent_keys, _summary = build_payload(runtime, cloud_url, resolved_source, enabled=active, ledger=ledger)
    write_json(runtime / "cloud-sync-payload.json", payload)

    if not active:
        state = build_state(
            status="idle",
            runtime_dir=runtime,
            cloud_url=cloud_url,
            enabled=False,
            authenticated=bool(resolved_token),
            token_source=resolved_source,
            payload=payload,
            sent_keys=[],
            result=None,
            attempts=[],
            dry_run=dry_run,
        )
        write_json(runtime / "cloud-sync-state.json", state)
        write_json(runtime / "cloud-sync-report.json", state["humanReport"])
        return state

    if not resolved_token:
        state = build_state(
            status="blocked",
            runtime_dir=runtime,
            cloud_url=cloud_url,
            enabled=True,
            authenticated=False,
            token_source="none",
            payload=payload,
            sent_keys=[],
            result=None,
            attempts=[],
            dry_run=dry_run,
            error_text="Falta token local para publicar en ariadgsm.com.",
        )
        write_json(runtime / "cloud-sync-state.json", state)
        write_json(runtime / "cloud-sync-report.json", state["humanReport"])
        return state

    if dry_run:
        state = build_state(
            status="dry_run",
            runtime_dir=runtime,
            cloud_url=cloud_url,
            enabled=True,
            authenticated=True,
            token_source=resolved_source,
            payload=payload,
            sent_keys=sent_keys,
            result={"statusCode": 0},
            attempts=[],
            dry_run=True,
        )
        write_json(runtime / "cloud-sync-state.json", state)
        write_json(runtime / "cloud-sync-report.json", state["humanReport"])
        return state

    status, result, attempts = run_with_retry(sync_url(cloud_url), resolved_token, payload, timeout)
    if status == "ok":
        save_ledger(ledger_path, ledger, sent_keys, text(payload.get("id")), text(payload.get("payloadHash")))

    state = build_state(
        status=status,
        runtime_dir=runtime,
        cloud_url=cloud_url,
        enabled=True,
        authenticated=True,
        token_source=resolved_source,
        payload=payload,
        sent_keys=sent_keys,
        result=result,
        attempts=attempts,
        dry_run=False,
        error_text=text(result.get("error")) if isinstance(result, dict) else "",
    )
    write_json(runtime / "cloud-sync-state.json", state)
    write_json(runtime / "cloud-sync-report.json", state["humanReport"])
    return state


def build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="AriadGSM Cloud Sync")
    parser.add_argument("--runtime-dir", default="./runtime")
    parser.add_argument("--repo-root", default="")
    parser.add_argument("--cloud-url", default=os.environ.get("ARIADGSM_CLOUD_URL", DEFAULT_CLOUD_URL))
    parser.add_argument("--enabled", action="store_true")
    parser.add_argument("--disabled", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--timeout", type=float, default=8.0)
    parser.add_argument("--json", action="store_true")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_arg_parser().parse_args(argv)
    enabled: bool | None = None
    if args.enabled:
        enabled = True
    if args.disabled:
        enabled = False
    state = run_cloud_sync_once(
        Path(args.runtime_dir),
        repo_root=Path(args.repo_root).resolve() if args.repo_root else None,
        cloud_url=args.cloud_url,
        enabled=enabled,
        dry_run=args.dry_run,
        timeout=args.timeout,
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=False))
    else:
        print(f"{state['status']}: {state['humanReport']['headline']}")
    return 0 if state["status"] in {"ok", "idle", "dry_run", "attention", "blocked"} else 1


if __name__ == "__main__":
    raise SystemExit(main())
