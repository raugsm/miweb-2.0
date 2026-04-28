from __future__ import annotations

import argparse
import hashlib
import json
import re
import sqlite3
import sys
import time
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from .classifier import classify_text
from .contracts import validate_contract
from .event_backbone import compact_jsonl_tail, read_jsonl_incremental
from .text import clean_text, looks_like_browser_ui_title, normalize


AGENT_ROOT = Path(__file__).resolve().parents[1]
VERSION = "0.9.9"
EXPECTED_CHANNELS = ("wa-1", "wa-2", "wa-3")

SOURCE_RANKS: dict[str, int] = {
    "dom": 100,
    "accessibility": 90,
    "uia": 80,
    "ocr": 40,
}

BROWSER_CHANNELS: dict[str, str] = {
    "msedge": "wa-1",
    "microsoftedge": "wa-1",
    "edge": "wa-1",
    "chrome": "wa-2",
    "googlechrome": "wa-2",
    "firefox": "wa-3",
    "mozilla": "wa-3",
    "mozillafirefox": "wa-3",
}

CHANNEL_BROWSERS = {
    "wa-1": "msedge",
    "wa-2": "chrome",
    "wa-3": "firefox",
}

NOISE_TEXTS = {
    "",
    "buscar",
    "buscar o iniciar un chat nuevo",
    "todos",
    "no leidos",
    "favoritos",
    "grupos",
    "chat fijado",
    "foto",
    "audio",
    "video",
    "mensaje eliminado",
    "whatsapp",
    "whatsapp business",
    "whatsapp business en la web",
    "organiza administra y haz crecer tu empresa",
    "tus mensajes personales estan cifrados de extremo a extremo",
    "anadir esta pagina a marcadores ctrl d",
    "añadir esta pagina a marcadores ctrl d",
    "cerrar",
    "usar aqui",
    "usar aqui para abrir whatsapp en esta ventana",
}

COUNTRY_KEYWORDS = {
    "mexico": "MX",
    "méxico": "MX",
    "mx": "MX",
    "peru": "PE",
    "perú": "PE",
    "chile": "CL",
    "colombia": "CO",
    "ecuador": "EC",
    "bolivia": "BO",
    "argentina": "AR",
    "venezuela": "VE",
    "usa": "US",
    "estados unidos": "US",
}

SERVICE_KEYWORDS = (
    "samsung",
    "xiaomi",
    "huawei",
    "honor",
    "tecno",
    "infinix",
    "iphone",
    "frp",
    "mdm",
    "imei",
    "liberar",
    "unlock",
    "flash",
    "flasheo",
)


@dataclass(frozen=True)
class IdentityResult:
    accepted: bool
    browser_process: str
    channel_id: str
    confidence: float
    source: str
    url: str
    window_title: str
    rejection_reasons: tuple[str, ...]


@dataclass(frozen=True)
class SourceCandidate:
    source_event_id: str
    source_kind: str
    source_rank: int
    browser_process: str
    channel_id: str
    conversation_id: str
    conversation_title: str
    message_key: str
    direction: str
    text: str
    raw_text: str
    confidence: float
    identity_confidence: float
    identity_source: str
    url: str
    window_title: str
    observed_at: str
    sent_at: str
    sender_name: str
    bounds: dict[str, Any]
    adapter: str
    selector: str
    automation_id: str
    rejection_reasons: tuple[str, ...] = ()


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def stable_hash(value: str, length: int = 24) -> str:
    return hashlib.sha1(value.encode("utf-8")).hexdigest()[:length]


def clamp(value: Any, default: float = 0.5) -> float:
    try:
        number = float(value)
    except (TypeError, ValueError):
        number = default
    return max(0.0, min(1.0, number))


def compact_json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def browser_key(value: Any) -> str:
    raw = normalize(value)
    raw = re.sub(r"[^a-z0-9]+", "", raw)
    for candidate, channel in BROWSER_CHANNELS.items():
        if candidate in raw:
            return CHANNEL_BROWSERS[channel]
    return raw


def source_kind(value: Any) -> str:
    normalized = normalize(value)
    compact = re.sub(r"[^a-z0-9]+", "", normalized)
    if normalized in {"dom", "cdp_dom", "domsnapshot", "structured_dom"}:
        return "dom"
    if normalized in {"accessibility", "ax", "a11y", "cdp_accessibility"} or "accessibility" in compact:
        return "accessibility"
    if normalized in {"uia", "ui_automation", "windows_ui_automation"} or "uiautomation" in compact:
        return "uia"
    if normalized in {"ocr", "screen_ocr", "vision_ocr"}:
        return "ocr"
    return "ocr"


def event_source_id(event: dict[str, Any]) -> str:
    for field in ("sourceEventId", "readerSourceEventId", "eventId", "perceptionEventId", "visionEventId"):
        value = clean_text(event.get(field))
        if value:
            return value
    return f"source-{stable_hash(compact_json(event))}"


def looks_like_time_only(value: str) -> bool:
    text = normalize(value)
    return bool(re.fullmatch(r"(?:[01]?\d|2[0-3])(?::[0-5]\d)?(?:\s*(?:am|pm|a m|p m))?", text))


def is_noise_text(value: str) -> bool:
    text = normalize(value)
    if text in NOISE_TEXTS:
        return True
    if looks_like_time_only(text):
        return True
    letter_count = sum(1 for char in text if char.isalpha())
    if letter_count == 0:
        return True
    if len(text) <= 2 and letter_count <= 1:
        return True
    return False


def identity_for_event(event: dict[str, Any], fallback_channel: str = "") -> IdentityResult:
    metadata = event.get("metadata") if isinstance(event.get("metadata"), dict) else {}
    browser = browser_key(
        event.get("browserProcess")
        or event.get("browser")
        or event.get("browserName")
        or event.get("processName")
        or metadata.get("browserProcess")
        or metadata.get("browser")
    )
    channel_id = clean_text(event.get("channelId") or event.get("channelHint") or fallback_channel)
    if not channel_id and browser in BROWSER_CHANNELS:
        channel_id = BROWSER_CHANNELS[browser]
    if channel_id not in {"wa-1", "wa-2", "wa-3"} and browser in BROWSER_CHANNELS:
        channel_id = BROWSER_CHANNELS[browser]

    canonical_browser = CHANNEL_BROWSERS.get(channel_id, browser if browser in {"msedge", "chrome", "firefox"} else "")
    url = clean_text(event.get("url") or event.get("documentUrl") or metadata.get("url") or metadata.get("documentUrl"))
    title = clean_text(event.get("windowTitle") or event.get("title") or metadata.get("windowTitle") or metadata.get("title"))
    url_ok = "web.whatsapp.com" in url.lower()
    title_ok = "whatsapp" in normalize(title)
    browser_ok = canonical_browser in {"msedge", "chrome", "firefox"}
    reasons: list[str] = []
    if not browser_ok:
        reasons.append("unknown_or_non_browser_process")
    if channel_id not in {"wa-1", "wa-2", "wa-3"}:
        reasons.append("unknown_channel")
    if not url_ok and not title_ok:
        reasons.append("not_positive_whatsapp_web_identity")
    if browser and browser not in {"msedge", "chrome", "firefox"} and "whatsapp" in browser:
        reasons.append("standalone_whatsapp_app_is_not_allowed_source")

    if reasons:
        return IdentityResult(False, canonical_browser, channel_id, 0.0, "rejected", url, title, tuple(reasons))
    if url_ok:
        return IdentityResult(True, canonical_browser, channel_id, 0.98, "url:web.whatsapp.com", url, title, ())
    return IdentityResult(True, canonical_browser, channel_id, 0.76, "window_title:whatsapp_browser", url, title, ())


def conversation_from_event(event: dict[str, Any], identity: IdentityResult) -> tuple[str, str]:
    conversation = event.get("conversation") if isinstance(event.get("conversation"), dict) else {}
    conversation_id = clean_text(
        conversation.get("id")
        or conversation.get("conversationId")
        or event.get("conversationId")
        or event.get("chatId")
        or event.get("targetConversationId")
    )
    title = clean_text(
        conversation.get("title")
        or conversation.get("conversationTitle")
        or event.get("conversationTitle")
        or event.get("chatTitle")
        or event.get("title")
    )
    if not title or looks_like_browser_ui_title(title):
        title = clean_text(conversation_id)
    if not conversation_id:
        conversation_id = f"{identity.channel_id}-{stable_hash(title or identity.window_title or identity.url, 16)}"
    return conversation_id, title or conversation_id


def normalize_direction(value: Any) -> str:
    text = normalize(value)
    if text in {"client", "customer", "incoming", "in", "received", "remitente", "cliente"}:
        return "client"
    if text in {"agent", "business", "outgoing", "out", "sent", "me", "yo", "ariadgsm"}:
        return "agent"
    return "unknown"


def message_items(event: dict[str, Any]) -> list[dict[str, Any]]:
    for field in ("messages", "visibleMessages", "items"):
        value = event.get(field)
        if isinstance(value, list):
            return [item for item in value if isinstance(item, dict)]
    if clean_text(event.get("text")):
        return [event]
    return []


def candidate_from_message(event: dict[str, Any], message: dict[str, Any], identity: IdentityResult) -> SourceCandidate:
    kind = source_kind(message.get("sourceKind") or event.get("sourceKind") or event.get("source"))
    rank = SOURCE_RANKS[kind]
    conversation_id, conversation_title = conversation_from_event(event, identity)
    raw_text = clean_text(message.get("rawText") or message.get("text") or message.get("name") or message.get("value"))
    text = clean_text(message.get("text") or raw_text)
    metadata = message.get("metadata") if isinstance(message.get("metadata"), dict) else {}
    source_id = event_source_id(event)
    message_key = clean_text(
        message.get("messageKey")
        or message.get("messageId")
        or message.get("nodeId")
        or message.get("automationId")
        or metadata.get("messageKey")
    )
    if not message_key:
        message_key = stable_hash("|".join([identity.channel_id, conversation_id, normalize(text), clean_text(message.get("sentAt"))]), 18)
    rejection_reasons: list[str] = []
    if is_noise_text(text):
        rejection_reasons.append("ui_noise_or_empty_text")
    if looks_like_browser_ui_title(conversation_title):
        rejection_reasons.append("browser_or_generic_conversation_title")
    return SourceCandidate(
        source_event_id=source_id,
        source_kind=kind,
        source_rank=rank,
        browser_process=identity.browser_process,
        channel_id=identity.channel_id,
        conversation_id=conversation_id,
        conversation_title=conversation_title,
        message_key=message_key,
        direction=normalize_direction(message.get("direction") or message.get("role")),
        text=text,
        raw_text=raw_text,
        confidence=clamp(message.get("confidence") or event.get("confidence"), 0.62 if kind == "ocr" else 0.82),
        identity_confidence=identity.confidence,
        identity_source=identity.source,
        url=identity.url,
        window_title=identity.window_title,
        observed_at=clean_text(message.get("observedAt") or event.get("observedAt") or event.get("createdAt")) or utc_now(),
        sent_at=clean_text(message.get("sentAt") or message.get("time") or message.get("timestamp")),
        sender_name=clean_text(message.get("senderName") or message.get("sender") or metadata.get("senderName")),
        bounds=message.get("bounds") if isinstance(message.get("bounds"), dict) else {},
        adapter=clean_text(message.get("adapter") or event.get("adapter") or metadata.get("adapter")),
        selector=clean_text(message.get("selector") or metadata.get("selector")),
        automation_id=clean_text(message.get("automationId") or metadata.get("automationId")),
        rejection_reasons=tuple(rejection_reasons),
    )


def candidates_from_reader_event(event: dict[str, Any]) -> tuple[list[SourceCandidate], list[dict[str, Any]]]:
    identity = identity_for_event(event)
    if not identity.accepted:
        return [], [
            {
                "sourceEventId": event_source_id(event),
                "sourceKind": source_kind(event.get("sourceKind") or event.get("source")),
                "reason": list(identity.rejection_reasons),
                "windowTitle": identity.window_title,
                "url": identity.url,
            }
        ]
    candidates = [candidate_from_message(event, item, identity) for item in message_items(event)]
    rejected = [
        {
            "sourceEventId": candidate.source_event_id,
            "sourceKind": candidate.source_kind,
            "reason": list(candidate.rejection_reasons),
            "text": candidate.text[:120],
            "conversationTitle": candidate.conversation_title,
        }
        for candidate in candidates
        if candidate.rejection_reasons
    ]
    return [candidate for candidate in candidates if not candidate.rejection_reasons], rejected


def perception_contexts(event: dict[str, Any]) -> dict[str, dict[str, Any]]:
    contexts: dict[str, dict[str, Any]] = defaultdict(dict)
    objects = [item for item in event.get("objects") or [] if isinstance(item, dict)]
    for item in objects:
        metadata = item.get("metadata") if isinstance(item.get("metadata"), dict) else {}
        object_type = clean_text(item.get("objectType"))
        channel_id = clean_text(metadata.get("channelId") or event.get("channelId"))
        if not channel_id:
            continue
        context = contexts[channel_id]
        if object_type == "window":
            context.setdefault("channelId", channel_id)
            context.setdefault("browserProcess", metadata.get("browserProcess") or metadata.get("processName") or event.get("browserProcess"))
            context.setdefault("url", metadata.get("url") or event.get("url"))
            context.setdefault("windowTitle", metadata.get("windowTitle") or item.get("text") or event.get("windowTitle"))
            context.setdefault("sourceEventId", event_source_id(event))
        elif object_type == "conversation":
            context.setdefault("channelId", channel_id)
            context.setdefault("conversationId", metadata.get("conversationId") or event.get("conversationId"))
            context.setdefault("conversationTitle", metadata.get("conversationTitle") or item.get("text") or event.get("conversationTitle"))
            context.setdefault("browserProcess", metadata.get("browserProcess") or metadata.get("processName") or event.get("browserProcess"))
            context.setdefault("url", metadata.get("url") or event.get("url"))
            context.setdefault("windowTitle", metadata.get("windowTitle") or event.get("windowTitle"))
    return contexts


def context_for_message(event: dict[str, Any], item: dict[str, Any], contexts: dict[str, dict[str, Any]]) -> dict[str, Any]:
    metadata = item.get("metadata") if isinstance(item.get("metadata"), dict) else {}
    channel_id = clean_text(metadata.get("channelId") or event.get("channelId"))
    if not channel_id and len(contexts) == 1:
        channel_id = next(iter(contexts.keys()))
    context = dict(contexts.get(channel_id, {}))
    context.setdefault("channelId", channel_id)
    context.setdefault("browserProcess", metadata.get("browserProcess") or metadata.get("processName") or event.get("browserProcess"))
    context.setdefault("url", metadata.get("url") or event.get("url"))
    context.setdefault("windowTitle", metadata.get("windowTitle") or event.get("windowTitle"))
    context.setdefault("conversationId", metadata.get("conversationId") or event.get("conversationId"))
    context.setdefault("conversationTitle", metadata.get("conversationTitle") or event.get("conversationTitle"))
    return context


def candidates_from_perception_event(event: dict[str, Any]) -> tuple[list[SourceCandidate], list[dict[str, Any]]]:
    objects = [item for item in event.get("objects") or [] if isinstance(item, dict)]
    contexts = perception_contexts(event)
    message_objects = [item for item in objects if clean_text(item.get("objectType")) == "message_bubble"][-60:]
    output: list[SourceCandidate] = []
    rejected: list[dict[str, Any]] = []
    for index, item in enumerate(message_objects):
        metadata = item.get("metadata") if isinstance(item.get("metadata"), dict) else {}
        context = context_for_message(event, item, contexts)
        channel_id = clean_text(context.get("channelId"))
        wrapped = {
            "sourceEventId": event_source_id(event),
            "sourceKind": metadata.get("sourceKind") or metadata.get("readerSource") or metadata.get("source") or "ocr",
            "browserProcess": metadata.get("browserProcess") or context.get("browserProcess"),
            "channelId": channel_id,
            "url": metadata.get("url") or context.get("url"),
            "windowTitle": metadata.get("windowTitle") or context.get("windowTitle"),
            "observedAt": event.get("observedAt"),
            "conversationId": metadata.get("conversationId") or context.get("conversationId"),
            "conversationTitle": metadata.get("conversationTitle") or context.get("conversationTitle"),
            "messages": [
                {
                    "messageKey": metadata.get("messageKey") or clean_text(item.get("objectId")) or f"{event_source_id(event)}:{index}",
                    "text": item.get("text"),
                    "direction": item.get("role"),
                    "confidence": item.get("confidence"),
                    "bounds": item.get("bounds") if isinstance(item.get("bounds"), dict) else {},
                    "metadata": metadata,
                }
            ],
        }
        candidates, local_rejections = candidates_from_reader_event(wrapped)
        output.extend(candidates)
        rejected.extend(local_rejections)
    return output, rejected


def collect_candidates(source_events: list[dict[str, Any]]) -> tuple[list[SourceCandidate], list[dict[str, Any]], Counter[str]]:
    candidates: list[SourceCandidate] = []
    rejected: list[dict[str, Any]] = []
    by_source: Counter[str] = Counter()
    for event in source_events:
        if not isinstance(event, dict):
            continue
        if event.get("eventType") == "perception_event":
            local_candidates, local_rejected = candidates_from_perception_event(event)
        else:
            local_candidates, local_rejected = candidates_from_reader_event(event)
        candidates.extend(local_candidates)
        rejected.extend(local_rejected)
        for candidate in local_candidates:
            by_source[candidate.source_kind] += 1
        for item in local_rejected:
            by_source[clean_text(item.get("sourceKind") or "rejected")] += 0
    return candidates, rejected, by_source


def group_key(candidate: SourceCandidate) -> tuple[str, str, str]:
    return (candidate.channel_id, candidate.conversation_id, candidate.message_key)


def candidate_score(candidate: SourceCandidate) -> float:
    return clamp((candidate.source_rank / 100.0) * 0.55 + candidate.confidence * 0.35 + candidate.identity_confidence * 0.10)


def extract_signals(text: str, confidence: float) -> list[dict[str, Any]]:
    signals: list[dict[str, Any]] = []
    decision = classify_text(text)
    intent_signal = {
        "accounting_payment": "payment",
        "accounting_debt": "debt",
        "price_request": "price_request",
        "service_context": "service",
    }.get(decision.intent)
    if intent_signal:
        value = decision.reasons[0] if decision.reasons else decision.intent
        signals.append({"kind": intent_signal, "value": value, "confidence": clamp(confidence)})
    normalized = normalize(text)
    for keyword, country in COUNTRY_KEYWORDS.items():
        if normalize(keyword) in normalized:
            signals.append({"kind": "country", "value": country, "confidence": clamp(confidence * 0.88)})
            break
    for service in SERVICE_KEYWORDS:
        if service in normalized:
            signals.append({"kind": "service", "value": service, "confidence": clamp(confidence * 0.86)})
            break
    for match in re.finditer(r"\b\d+(?:[.,]\d+)?\s*(?:usdt|usd|dolares|dolares|soles|mxn|cop|clp|s/)\b", normalized):
        signals.append({"kind": "amount", "value": match.group(0).upper(), "confidence": clamp(confidence * 0.82)})
        break
    if any(token in normalized for token in ("urgent", "apurado", "rapido", "ya bro", "please", "porfa")):
        signals.append({"kind": "urgency", "value": "customer_waiting", "confidence": clamp(confidence * 0.78)})
    if any(token in normalized for token in ("price", "unlock", "please")):
        signals.append({"kind": "language_hint", "value": "en", "confidence": 0.7})
    return signals


def visible_message_from_group(candidates: list[SourceCandidate]) -> dict[str, Any]:
    candidates = sorted(candidates, key=lambda item: (item.source_rank, item.confidence, item.identity_confidence), reverse=True)
    best = candidates[0]
    normalized_texts = {normalize(candidate.text) for candidate in candidates if candidate.text}
    disagreements: list[dict[str, Any]] = []
    if len(normalized_texts) > 1:
        disagreements.append(
            {
                "kind": "text_mismatch",
                "messageKey": best.message_key,
                "sources": [
                    {"kind": candidate.source_kind, "text": candidate.text[:160], "confidence": candidate.confidence}
                    for candidate in candidates
                ],
                "resolution": f"accepted_{best.source_kind}_as_highest_ranked_source",
            }
        )
    confidence = clamp(candidate_score(best) - (0.08 if disagreements else 0.0))
    message_id = f"reader-msg-{stable_hash('|'.join([best.channel_id, best.conversation_id, best.message_key]), 24)}"
    local_ref = f"local://reader-core/{best.source_kind}/{best.source_event_id}/{best.message_key}"
    learning_weight = "low" if any(group in normalize(best.conversation_title) for group in ("pagos mexico", "pagos chile", "pagos colombia")) else "normal"
    return {
        "schemaVersion": VERSION,
        "messageId": message_id,
        "channelId": best.channel_id,
        "browserProcess": best.browser_process,
        "conversationId": best.conversation_id,
        "conversationTitle": best.conversation_title,
        "senderName": best.sender_name,
        "direction": best.direction,
        "text": best.text,
        "sentAt": best.sent_at,
        "observedAt": best.observed_at,
        "source": {
            "kind": best.source_kind,
            "rank": best.source_rank,
            "sourceEventId": best.source_event_id,
            "adapter": best.adapter,
            "selector": best.selector,
            "automationId": best.automation_id,
        },
        "identity": {
            "isWhatsAppWeb": True,
            "identityConfidence": best.identity_confidence,
            "identitySource": best.identity_source,
            "url": best.url,
            "windowTitle": best.window_title,
        },
        "confidence": confidence,
        "evidence": {
            "localReference": local_ref,
            "visibleOnly": True,
            "rawText": best.raw_text,
            "bounds": best.bounds,
        },
        "signals": extract_signals(best.text, confidence),
        "sourcesCompared": [
            {"kind": candidate.source_kind, "confidence": candidate.confidence, "text": candidate.text[:220]}
            for candidate in candidates
        ],
        "disagreements": disagreements,
        "quality": {
            "accepted": True,
            "rejectionReasons": [],
            "learningWeight": learning_weight,
        },
    }


def conversation_event_from_messages(messages: list[dict[str, Any]]) -> dict[str, Any]:
    first = messages[0]
    digest = stable_hash("|".join(message["messageId"] for message in messages), 16)
    event_id = f"reader-{first['channelId']}-{stable_hash(first['conversationId'], 12)}-{digest}"
    return {
        "eventType": "conversation_event",
        "conversationEventId": event_id,
        "conversationId": first["conversationId"],
        "channelId": first["channelId"],
        "observedAt": utc_now(),
        "conversationTitle": first["conversationTitle"],
        "source": "live",
        "messages": [
            {
                "messageId": message["messageId"],
                "text": message["text"],
                "direction": message["direction"],
                "senderName": message.get("senderName") or "",
                "sentAt": message.get("sentAt") or "",
                "confidence": message["confidence"],
                "signals": message.get("signals") or [],
                "sourceKind": message["source"]["kind"],
                "readerEvidence": message["evidence"]["localReference"],
                "sourceConfidence": message["identity"]["identityConfidence"],
                "sourceDisagreements": message.get("disagreements") or [],
            }
            for message in messages
        ],
        "timeline": {
            "historyLimitDays": 30,
            "complete": False,
            "dedupeStrategy": "reader_core_channel_conversation_message_key",
            "readerCore": True,
            "sourceCounts": dict(Counter(message["source"]["kind"] for message in messages)),
        },
        "quality": {
            "isReliable": True,
            "identityConfidence": min(message["identity"]["identityConfidence"] for message in messages),
            "identitySource": "reader_core_positive_whatsapp_identity",
            "rejectionReasons": [],
        },
    }


class ReaderCoreStore:
    def __init__(self, db_path: Path):
        self.db_path = db_path
        self.db_path.parent.mkdir(parents=True, exist_ok=True)
        self.conn = sqlite3.connect(self.db_path)
        self.conn.row_factory = sqlite3.Row
        self.init_schema()

    def close(self) -> None:
        self.conn.close()

    def init_schema(self) -> None:
        self.conn.execute("pragma journal_mode=WAL")
        self.conn.execute("pragma synchronous=NORMAL")
        self.conn.executescript(
            """
            create table if not exists visible_messages (
              message_id text primary key,
              channel_id text not null,
              browser_process text not null,
              conversation_id text not null,
              conversation_title text not null,
              source_kind text not null,
              confidence real not null,
              text text not null,
              observed_at text not null,
              message_json text not null,
              stored_at text not null
            );
            create index if not exists idx_reader_messages_conversation
              on visible_messages(channel_id, conversation_id);
            create index if not exists idx_reader_messages_source
              on visible_messages(source_kind);
            """
        )
        self.conn.commit()

    def save_message(self, message: dict[str, Any]) -> bool:
        payload = json.dumps(message, ensure_ascii=False, separators=(",", ":"))
        try:
            with self.conn:
                self.conn.execute(
                    """
                    insert into visible_messages (
                      message_id, channel_id, browser_process, conversation_id,
                      conversation_title, source_kind, confidence, text,
                      observed_at, message_json, stored_at
                    ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        message["messageId"],
                        message["channelId"],
                        message["browserProcess"],
                        message["conversationId"],
                        message["conversationTitle"],
                        message["source"]["kind"],
                        float(message["confidence"]),
                        message["text"],
                        message["observedAt"],
                        payload,
                        utc_now(),
                    ),
                )
            return True
        except sqlite3.IntegrityError:
            return False

    def summary(self) -> dict[str, Any]:
        def scalar(sql: str) -> int:
            row = self.conn.execute(sql).fetchone()
            return int(row[0] if row else 0)

        by_source = {
            str(row[0]): int(row[1])
            for row in self.conn.execute("select source_kind, count(*) from visible_messages group by source_kind")
        }
        by_channel = {
            str(row[0]): int(row[1])
            for row in self.conn.execute("select channel_id, count(*) from visible_messages group by channel_id")
        }
        return {
            "storedMessages": scalar("select count(*) from visible_messages"),
            "storedConversations": scalar("select count(distinct channel_id || ':' || conversation_id) from visible_messages"),
            "bySource": by_source,
            "byChannel": by_channel,
        }

    def recent_messages(self, limit: int = 100) -> list[dict[str, Any]]:
        rows = self.conn.execute(
            """
            select message_json from visible_messages
            order by stored_at desc
            limit ?
            """,
            (limit,),
        ).fetchall()
        messages: list[dict[str, Any]] = []
        for row in rows:
            try:
                payload = json.loads(str(row[0]))
            except (TypeError, json.JSONDecodeError):
                continue
            if isinstance(payload, dict):
                messages.append(payload)
        return list(reversed(messages))


def read_jsonl(path: Path, limit: int = 500) -> list[dict[str, Any]]:
    checkpoint = path.parent / ".reader-core-tail-checkpoint.json"
    batch = read_jsonl_incremental(
        [path],
        checkpoint,
        namespace=f"reader_core_tail:{path.name}",
        limit=limit,
        max_bytes=8 * 1024 * 1024,
        bootstrap_bytes=8 * 1024 * 1024,
    )
    return batch.events


def append_jsonl(path: Path, events: list[dict[str, Any]]) -> None:
    if not events:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        for event in events:
            handle.write(json.dumps(event, ensure_ascii=False, separators=(",", ":")) + "\n")


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    temp.replace(path)


def build_human_report(
    messages: list[dict[str, Any]],
    rejected: list[dict[str, Any]],
    disagreements: list[dict[str, Any]],
    channels: list[dict[str, Any]],
) -> dict[str, Any]:
    blocked_channels = [
        f"{item['channelId']}: {item['status']} - {item['readiness']['reason']}"
        for item in channels
        if not item["readiness"]["freshRead"]
    ]
    return {
        "quePaso": (
            f"Reader Core comparo fuentes estructuradas y OCR; acepto {len(messages)} mensaje(s) visible(s) "
            f"y rechazo {len(rejected)} lectura(s) insegura(s)."
        ),
        "queLei": [
            f"{message['channelId']} {message['conversationTitle']}: {message['text'][:90]}"
            for message in messages[-8:]
        ],
        "queRechace": [
            f"{clean_text(item.get('sourceKind') or 'fuente')}: {', '.join(item.get('reason') or [])}"
            for item in rejected[-8:]
        ],
        "queNecesitoDeBryams": [
            *blocked_channels[:5],
            "Abre o alista Edge, Chrome y Firefox si Reader Core queda idle sin fuentes visibles.",
            "Revisa desacuerdos si DOM/accesibilidad/UIA y OCR no leen igual.",
        ]
        if disagreements or blocked_channels
        else [],
        "riesgos": [
            "OCR se usa solo como respaldo y queda marcado con menor confianza.",
            "Si WhatsApp cambia su DOM, el adaptador debe emitir la misma forma de reader-source-event.",
        ],
    }


def build_channel_readiness(valid_messages: list[dict[str, Any]], stored_summary: dict[str, Any], recent_messages: list[dict[str, Any]]) -> list[dict[str, Any]]:
    current_by_channel: dict[str, list[dict[str, Any]]] = defaultdict(list)
    recent_by_channel: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for message in valid_messages:
        current_by_channel[clean_text(message.get("channelId"))].append(message)
    for message in recent_messages:
        recent_by_channel[clean_text(message.get("channelId"))].append(message)

    stored_by_channel = stored_summary.get("byChannel") if isinstance(stored_summary.get("byChannel"), dict) else {}
    channels: list[dict[str, Any]] = []
    for channel_id in EXPECTED_CHANNELS:
        current = current_by_channel.get(channel_id, [])
        recent = recent_by_channel.get(channel_id, [])
        sample = current[-5:] if current else recent[-5:]
        source_kinds = sorted({clean_text(message.get("source", {}).get("kind")) for message in sample if isinstance(message.get("source"), dict)})
        last_observed = max((clean_text(message.get("observedAt")) for message in current if clean_text(message.get("observedAt"))), default="")
        stored_count = int(stored_by_channel.get(channel_id, 0) or 0)
        if current:
            status = "fresh_messages_confirmed"
            reason = f"Lectura fresca con {len(current)} mensaje(s) aceptado(s)."
        elif stored_count > 0:
            status = "no_fresh_read"
            reason = "Tengo memoria de mensajes anteriores, pero no lectura fresca de este ciclo."
        else:
            status = "empty"
            reason = "Aun no confirme mensajes reales de este canal."
        channels.append(
            {
                "channelId": channel_id,
                "status": status,
                "messageConfirmed": bool(current),
                "latestAcceptedMessages": len(current),
                "storedMessages": stored_count,
                "latestObservedAt": last_observed,
                "sourceKinds": [kind for kind in source_kinds if kind],
                "latestMessages": [
                    {
                        "messageId": message["messageId"],
                        "conversationTitle": message["conversationTitle"],
                        "sourceKind": message["source"]["kind"],
                        "confidence": message["confidence"],
                        "text": message["text"][:160],
                    }
                    for message in sample
                ],
                "readiness": {
                    "freshRead": bool(current),
                    "canRead": bool(current),
                    "canUnlockHands": bool(current),
                    "reason": reason,
                },
            }
        )
    return channels


def run_reader_core_once(
    source_files: list[Path],
    visible_messages_file: Path,
    conversation_events_file: Path,
    state_file: Path,
    report_file: Path,
    db_path: Path,
    limit: int = 500,
    checkpoint_file: Path | None = None,
    max_read_bytes: int = 8 * 1024 * 1024,
) -> dict[str, Any]:
    cycle_started = time.perf_counter()
    checkpoint = checkpoint_file or state_file.with_name("event-backbone-state.json")
    source_batch = read_jsonl_incremental(
        source_files,
        checkpoint,
        namespace="reader_core_sources",
        limit=limit,
        max_bytes=max_read_bytes,
        bootstrap_bytes=max_read_bytes,
    )
    source_events: list[dict[str, Any]] = []
    source_events.extend(source_batch.events)
    candidates, rejected, by_source = collect_candidates(source_events)
    grouped: dict[tuple[str, str, str], list[SourceCandidate]] = defaultdict(list)
    for candidate in candidates:
        grouped[group_key(candidate)].append(candidate)

    visible_messages = [visible_message_from_group(group) for group in grouped.values()]
    valid_messages: list[dict[str, Any]] = []
    invalid_messages: list[dict[str, Any]] = []
    for message in visible_messages:
        errors = validate_contract(message, "visible_message")
        if errors:
            invalid_messages.append({"messageId": message.get("messageId"), "errors": errors})
        else:
            valid_messages.append(message)

    store = ReaderCoreStore(db_path)
    new_messages: list[dict[str, Any]] = []
    duplicates = 0
    try:
        for message in valid_messages:
            if store.save_message(message):
                new_messages.append(message)
            else:
                duplicates += 1
        summary = store.summary()
        recent_messages = store.recent_messages()
    finally:
        store.close()

    conversation_groups: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for message in new_messages:
        conversation_groups[(message["channelId"], message["conversationId"])].append(message)
    conversation_events = [conversation_event_from_messages(messages) for messages in conversation_groups.values()]
    invalid_conversations = [
        {"conversationEventId": event.get("conversationEventId"), "errors": errors}
        for event in conversation_events
        if (errors := validate_contract(event, "conversation_event"))
    ]
    conversation_events = [event for event in conversation_events if not validate_contract(event, "conversation_event")]

    append_jsonl(visible_messages_file, new_messages)
    append_jsonl(conversation_events_file, conversation_events)
    compactions = [
        compact_jsonl_tail(visible_messages_file),
        compact_jsonl_tail(conversation_events_file),
    ]
    disagreements = [item for message in valid_messages for item in (message.get("disagreements") or [])]
    cycle_ms = int((time.perf_counter() - cycle_started) * 1000)

    if not source_events:
        status = "idle"
    elif invalid_messages or invalid_conversations:
        status = "attention"
    elif rejected and not valid_messages:
        status = "attention"
    else:
        status = "ok"

    state = {
        "status": status,
        "engine": "ariadgsm_reader_core",
        "version": VERSION,
        "updatedAt": utc_now(),
        "policy": {
            "sourcePriority": ["dom", "accessibility", "uia", "ocr"],
            "channelMap": {"msedge": "wa-1", "chrome": "wa-2", "firefox": "wa-3"},
            "ocrPolicy": "fallback_only_after_positive_whatsapp_identity",
        },
        "sourceFiles": [str(path) for path in source_files],
        "outputFiles": {
            "visibleMessages": str(visible_messages_file),
            "conversationEvents": str(conversation_events_file),
            "report": str(report_file),
            "db": str(db_path),
            "checkpoint": str(checkpoint),
        },
        "ingested": {
            "sourceEvents": len(source_events),
            "candidateMessages": len(candidates),
            "acceptedCandidates": len(valid_messages),
            "newMessages": len(new_messages),
            "conversationEvents": len(conversation_events),
            "duplicates": duplicates,
            "rejected": len(rejected),
            "invalidMessages": len(invalid_messages),
            "invalidConversations": len(invalid_conversations),
            "bySource": dict(by_source),
            "sourceBytesRead": source_batch.bytes_read,
            "sourceBacklogBytes": source_batch.backlog_bytes,
            "sourceSkippedBacklogBytes": source_batch.skipped_backlog_bytes,
            "cycleDurationMs": cycle_ms,
        },
        "summary": {
            **summary,
            "latestRunMessages": len(valid_messages),
            "latestRunDisagreements": len(disagreements),
            "structuredSourceMessages": sum(1 for item in valid_messages if item["source"]["kind"] in {"dom", "accessibility", "uia"}),
            "ocrFallbackMessages": sum(1 for item in valid_messages if item["source"]["kind"] == "ocr"),
        },
        "freshnessPolicy": {
            "perChannelFreshReadRequired": True,
            "handsRequireFreshRead": True,
            "windowVisibleIsNotEnough": True,
            "ocrIsFallbackOnly": True,
        },
        "backbone": {
            **source_batch.telemetry(),
            "cycleDurationMs": cycle_ms,
            "compactions": compactions,
            "humanSummary": (
                f"Lei {source_batch.bytes_read} bytes incrementales; "
                f"backlog={source_batch.backlog_bytes}; "
                f"saltado={source_batch.skipped_backlog_bytes}; "
                f"ciclo={cycle_ms}ms."
            ),
        },
        "channels": build_channel_readiness(valid_messages, summary, recent_messages),
        "latestMessages": [
            {
                "messageId": message["messageId"],
                "channelId": message["channelId"],
                "conversationTitle": message["conversationTitle"],
                "sourceKind": message["source"]["kind"],
                "confidence": message["confidence"],
                "text": message["text"][:160],
            }
            for message in valid_messages[-20:]
        ],
        "latestDisagreements": disagreements[-20:],
        "invalid": {"messages": invalid_messages, "conversations": invalid_conversations},
    }
    state["humanReport"] = build_human_report(valid_messages, rejected, disagreements, state["channels"])
    write_json(state_file, state)
    write_json(report_file, state["humanReport"])
    return state


def resolve_runtime_path(value: str | Path) -> Path:
    path = Path(value).expanduser()
    if path.is_absolute():
        return path
    return (AGENT_ROOT / path).resolve()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM Safe Eyes / Reader Core")
    parser.add_argument("--reader-source-events", default="runtime/reader-source-events.jsonl")
    parser.add_argument("--perception-events", default="runtime/perception-events.jsonl")
    parser.add_argument("--visible-messages", default="runtime/reader-visible-messages.jsonl")
    parser.add_argument("--conversation-events", default="runtime/conversation-events.jsonl")
    parser.add_argument("--state-file", default="runtime/reader-core-state.json")
    parser.add_argument("--report-file", default="runtime/reader-core-report.json")
    parser.add_argument("--db", default="runtime/reader-core.sqlite")
    parser.add_argument("--checkpoint-file", default="runtime/event-backbone-state.json")
    parser.add_argument("--limit", type=int, default=500)
    parser.add_argument("--max-read-bytes", type=int, default=8 * 1024 * 1024)
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def main() -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    args = parse_args()
    source_files = [
        resolve_runtime_path(args.reader_source_events),
        resolve_runtime_path(args.perception_events),
    ]
    state = run_reader_core_once(
        source_files,
        resolve_runtime_path(args.visible_messages),
        resolve_runtime_path(args.conversation_events),
        resolve_runtime_path(args.state_file),
        resolve_runtime_path(args.report_file),
        resolve_runtime_path(args.db),
        limit=max(1, args.limit),
        checkpoint_file=resolve_runtime_path(args.checkpoint_file),
        max_read_bytes=max(4096, args.max_read_bytes),
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=False, indent=2))
    else:
        ingested = state["ingested"]
        print(
            "AriadGSM Reader Core: "
            f"status={state['status']} "
            f"sources={ingested['sourceEvents']} "
            f"new_messages={ingested['newMessages']} "
            f"rejected={ingested['rejected']} "
            f"disagreements={state['summary']['latestRunDisagreements']}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
