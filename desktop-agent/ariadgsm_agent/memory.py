from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from .classifier import Decision
from .text import clean_text, looks_like_browser_ui_title, text_hash


AGENT_ROOT = Path(__file__).resolve().parents[1]


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def stable_hash(value: str, length: int = 24) -> str:
    return hashlib.sha1(value.encode("utf-8")).hexdigest()[:length]


def safe_float(value: Any, default: float = 0.0) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def event_key(event: dict[str, Any], id_fields: tuple[str, ...], fallback_prefix: str) -> str:
    for field in id_fields:
        value = clean_text(event.get(field))
        if value:
            return value
    raw = json.dumps(event, ensure_ascii=False, sort_keys=True)
    return f"{fallback_prefix}-{stable_hash(raw)}"


def read_jsonl_events(path: Path, event_type: str | None = None, limit: int = 500) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    events: list[dict[str, Any]] = []
    for line in path.read_text(encoding="utf-8").splitlines()[-limit:]:
        if not line.strip():
            continue
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        if event_type is None or event.get("eventType") == event_type:
            events.append(event)
    return events


def append_state(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    temp.replace(path)


def resolve_runtime_path(value: str | Path) -> Path:
    path = Path(value).expanduser()
    if path.is_absolute():
        return path
    return (AGENT_ROOT / path).resolve()


def message_key(conversation_id: str, message: dict[str, Any], index: int) -> str:
    explicit = clean_text(message.get("messageId"))
    if explicit:
        return explicit
    raw = "|".join(
        [
            conversation_id,
            clean_text(message.get("direction")),
            clean_text(message.get("sentAt")),
            clean_text(message.get("text")),
            str(index),
        ]
    )
    return f"msg-{stable_hash(raw)}"


class MemoryStore:
    def __init__(self, db_path: Path):
        self.db_path = db_path
        self.db_path.parent.mkdir(parents=True, exist_ok=True)
        self.conn = sqlite3.connect(self.db_path)
        self.conn.row_factory = sqlite3.Row
        self.init_schema()

    def close(self) -> None:
        self.conn.close()

    def init_schema(self) -> None:
        self.conn.executescript(
            """
            create table if not exists memory_processed_events (
              source_key text primary key,
              event_type text not null,
              processed_at text not null
            );
            create table if not exists memory_conversations (
              conversation_id text primary key,
              channel_id text not null,
              title text,
              source text,
              first_seen_at text not null,
              last_seen_at text not null,
              message_count integer not null default 0,
              decision_count integer not null default 0,
              accounting_count integer not null default 0,
              learning_count integer not null default 0,
              last_intent text,
              countries_json text not null default '[]',
              services_json text not null default '[]',
              languages_json text not null default '[]',
              updated_at text not null
            );
            create table if not exists memory_messages (
              message_id text primary key,
              conversation_id text not null,
              channel_id text not null,
              direction text,
              sender_name text,
              sent_at text,
              text text not null,
              text_hash text not null,
              confidence real not null,
              source_event_id text not null,
              signals_json text not null,
              created_at text not null
            );
            create index if not exists idx_memory_messages_conversation on memory_messages(conversation_id);
            create index if not exists idx_memory_messages_text_hash on memory_messages(text_hash);
            create table if not exists memory_message_signals (
              signal_id text primary key,
              message_id text not null,
              conversation_id text not null,
              kind text not null,
              value text,
              confidence real not null,
              created_at text not null
            );
            create index if not exists idx_memory_signals_conversation on memory_message_signals(conversation_id);
            create index if not exists idx_memory_signals_kind on memory_message_signals(kind);
            create table if not exists memory_decisions (
              decision_id text primary key,
              source_key text not null,
              conversation_id text,
              channel_id text,
              intent text not null,
              confidence real not null,
              proposed_action text,
              requires_confirmation integer not null,
              reasoning_summary text,
              event_json text not null,
              created_at text not null
            );
            create table if not exists memory_learning_events (
              learning_id text primary key,
              source_key text not null,
              conversation_id text,
              channel_id text,
              learning_type text not null,
              summary text not null,
              confidence real not null,
              event_json text not null,
              created_at text not null
            );
            create table if not exists memory_accounting_events (
              accounting_id text primary key,
              source_key text not null,
              conversation_id text,
              client_name text,
              kind text,
              amount real,
              currency text,
              status text,
              confidence real not null,
              event_json text not null,
              created_at text not null
            );
            create table if not exists memory_knowledge (
              knowledge_id text primary key,
              knowledge_type text not null,
              source_key text not null,
              conversation_id text,
              title text,
              summary text not null,
              confidence real not null,
              applies_to_json text not null,
              created_at text not null,
              updated_at text not null
            );
            create table if not exists memory_domain_events (
              event_id text primary key,
              event_type text not null,
              source_domain text not null,
              correlation_id text,
              conversation_id text,
              case_id text,
              customer_id text,
              risk_level text,
              requires_human_review integer not null,
              confidence real not null,
              summary text,
              event_json text not null,
              created_at text not null
            );
            create index if not exists idx_memory_domain_events_conversation on memory_domain_events(conversation_id);
            create index if not exists idx_memory_domain_events_case on memory_domain_events(case_id);
            create index if not exists idx_memory_domain_events_type on memory_domain_events(event_type);

            -- Legacy tables used by DesktopAgentService. Kept for compatibility.
            create table if not exists observations (
              observation_key text primary key,
              channel_id text not null,
              source text not null,
              confidence real not null,
              captured_at text,
              conversation_title text,
              raw_json text not null,
              ingested_at text not null
            );
            create table if not exists messages (
              id integer primary key autoincrement,
              observation_key text not null,
              channel_id text not null,
              text_hash text not null,
              text text not null,
              sender_name text,
              direction text,
              sent_at text,
              learning_type text,
              created_at text not null,
              unique(observation_key, text_hash)
            );
            create table if not exists decisions (
              id integer primary key autoincrement,
              observation_key text not null,
              channel_id text not null,
              intent text not null,
              label text not null,
              priority text not null,
              score integer not null,
              text text,
              reasons_json text not null,
              created_at text not null
            );
            """
        )
        self.conn.commit()

    def has_processed_event(self, source_key: str) -> bool:
        row = self.conn.execute(
            "select 1 from memory_processed_events where source_key = ? limit 1",
            (source_key,),
        ).fetchone()
        return row is not None

    def mark_processed(self, source_key: str, event_type: str, now: str) -> None:
        self.conn.execute(
            "insert into memory_processed_events (source_key, event_type, processed_at) values (?, ?, ?)",
            (source_key, event_type, now),
        )

    def ingest_conversation(self, event: dict[str, Any]) -> dict[str, int]:
        source_key = event_key(event, ("conversationEventId",), "conversation")
        if self.has_processed_event(source_key):
            return {"events": 0, "duplicates": 1, "conversations": 0, "messages": 0, "signals": 0}

        now = utc_now()
        conversation_id = clean_text(event.get("conversationId") or source_key)
        channel_id = clean_text(event.get("channelId") or "unknown")
        title = clean_text(event.get("conversationTitle") or conversation_id)
        source = clean_text(event.get("source") or "unknown")
        messages = [message for message in event.get("messages") or [] if isinstance(message, dict)]
        if looks_like_browser_ui_title(title):
            with self.conn:
                self.mark_processed(source_key, "conversation_event_rejected_ui_title", now)
            return {"events": 0, "duplicates": 0, "conversations": 0, "messages": 0, "signals": 0, "rejected": 1}

        countries: set[str] = set()
        services: set[str] = set()
        languages: set[str] = set()
        new_messages = 0
        new_signals = 0

        with self.conn:
            existing = self.conn.execute(
                "select * from memory_conversations where conversation_id = ? limit 1",
                (conversation_id,),
            ).fetchone()
            if existing is None:
                self.conn.execute(
                    """
                    insert into memory_conversations (
                      conversation_id, channel_id, title, source, first_seen_at, last_seen_at,
                      message_count, countries_json, services_json, languages_json, updated_at
                    ) values (?, ?, ?, ?, ?, ?, 0, '[]', '[]', '[]', ?)
                    """,
                    (conversation_id, channel_id, title, source, now, now, now),
                )

            for index, message in enumerate(messages):
                text = clean_text(message.get("text"))
                if not text:
                    continue
                msg_id = message_key(conversation_id, message, index)
                signals = [signal for signal in message.get("signals") or [] if isinstance(signal, dict)]
                before = self.conn.execute(
                    "select 1 from memory_messages where message_id = ? limit 1",
                    (msg_id,),
                ).fetchone()
                self.conn.execute(
                    """
                    insert or ignore into memory_messages (
                      message_id, conversation_id, channel_id, direction, sender_name,
                      sent_at, text, text_hash, confidence, source_event_id,
                      signals_json, created_at
                    ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        msg_id,
                        conversation_id,
                        channel_id,
                        clean_text(message.get("direction") or "unknown"),
                        clean_text(message.get("senderName")),
                        clean_text(message.get("sentAt")),
                        text,
                        text_hash(text),
                        safe_float(message.get("confidence"), 0.0),
                        source_key,
                        json.dumps(signals, ensure_ascii=False, separators=(",", ":")),
                        now,
                    ),
                )
                if before is None:
                    new_messages += 1
                for signal in signals:
                    kind = clean_text(signal.get("kind"))
                    value = clean_text(signal.get("value"))
                    confidence = safe_float(signal.get("confidence"), 0.5)
                    if not kind:
                        continue
                    if kind == "country" and value:
                        countries.add(value)
                    elif kind == "service" and value:
                        services.add(value)
                    elif kind == "language_hint" and value:
                        languages.add(value)
                    signal_id = f"sig-{stable_hash('|'.join([msg_id, kind, value]))}"
                    signal_before = self.conn.execute(
                        "select 1 from memory_message_signals where signal_id = ? limit 1",
                        (signal_id,),
                    ).fetchone()
                    self.conn.execute(
                        """
                        insert or ignore into memory_message_signals (
                          signal_id, message_id, conversation_id, kind, value, confidence, created_at
                        ) values (?, ?, ?, ?, ?, ?, ?)
                        """,
                        (signal_id, msg_id, conversation_id, kind, value, confidence, now),
                    )
                    if signal_before is None:
                        new_signals += 1

            self._merge_conversation_profile(conversation_id, channel_id, title, source, countries, services, languages, new_messages, now)
            self.mark_processed(source_key, "conversation_event", now)

        return {
            "events": 1,
            "duplicates": 0,
            "conversations": 1 if existing is None else 0,
            "messages": new_messages,
            "signals": new_signals,
        }

    def _merge_conversation_profile(
        self,
        conversation_id: str,
        channel_id: str,
        title: str,
        source: str,
        countries: set[str],
        services: set[str],
        languages: set[str],
        message_count_delta: int,
        now: str,
    ) -> None:
        existing = self.conn.execute(
            "select * from memory_conversations where conversation_id = ? limit 1",
            (conversation_id,),
        ).fetchone()
        existing_countries = set(json.loads(existing["countries_json"] or "[]")) if existing else set()
        existing_services = set(json.loads(existing["services_json"] or "[]")) if existing else set()
        existing_languages = set(json.loads(existing["languages_json"] or "[]")) if existing else set()
        self.conn.execute(
            """
            update memory_conversations
            set channel_id = ?, title = ?, source = ?, last_seen_at = ?,
                message_count = message_count + ?,
                countries_json = ?, services_json = ?, languages_json = ?,
                updated_at = ?
            where conversation_id = ?
            """,
            (
                channel_id,
                title,
                source,
                now,
                message_count_delta,
                json.dumps(sorted(existing_countries | countries), ensure_ascii=False),
                json.dumps(sorted(existing_services | services), ensure_ascii=False),
                json.dumps(sorted(existing_languages | languages), ensure_ascii=False),
                now,
                conversation_id,
            ),
        )

    def ingest_decision(self, event: dict[str, Any], source_label: str = "decision_event") -> dict[str, int]:
        source_key = event_key(event, ("decisionId",), "decision")
        if self.has_processed_event(source_key):
            return {"events": 0, "duplicates": 1, "decisions": 0}

        now = utc_now()
        conversation_id = clean_text(event.get("conversationId"))
        channel_id = clean_text(event.get("channelId"))
        intent = clean_text(event.get("intent") or "unknown")
        with self.conn:
            self.conn.execute(
                """
                insert or ignore into memory_decisions (
                  decision_id, source_key, conversation_id, channel_id, intent,
                  confidence, proposed_action, requires_confirmation,
                  reasoning_summary, event_json, created_at
                ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    source_key,
                    source_key,
                    conversation_id,
                    channel_id,
                    intent,
                    safe_float(event.get("confidence"), 0.0),
                    clean_text(event.get("proposedAction")),
                    1 if bool(event.get("requiresHumanConfirmation")) else 0,
                    clean_text(event.get("reasoningSummary")),
                    json.dumps(event, ensure_ascii=False, separators=(",", ":")),
                    clean_text(event.get("createdAt") or now),
                ),
            )
            if conversation_id:
                self.conn.execute(
                    """
                    update memory_conversations
                    set last_intent = ?, decision_count = decision_count + 1, updated_at = ?
                    where conversation_id = ?
                    """,
                    (intent, now, conversation_id),
                )
            self.mark_processed(source_key, "decision_event", now)
        return {"events": 1, "duplicates": 0, "decisions": 1}

    def ingest_learning(self, event: dict[str, Any]) -> dict[str, int]:
        source_key = event_key(event, ("learningId",), "learning")
        if self.has_processed_event(source_key):
            return {"events": 0, "duplicates": 1, "learning": 0, "knowledge": 0}

        now = utc_now()
        after = event.get("after") if isinstance(event.get("after"), dict) else {}
        conversation_id = clean_text(after.get("conversationId"))
        channel_id = clean_text(after.get("channelId"))
        learning_type = clean_text(event.get("learningType") or "customer_pattern")
        summary = clean_text(event.get("summary"))
        confidence = safe_float(event.get("confidence"), 0.5)
        applies_to = event.get("appliesTo") if isinstance(event.get("appliesTo"), list) else []
        knowledge_id = f"knowledge-{stable_hash('|'.join([learning_type, conversation_id, summary]))}"
        new_knowledge = 0

        with self.conn:
            self.conn.execute(
                """
                insert or ignore into memory_learning_events (
                  learning_id, source_key, conversation_id, channel_id,
                  learning_type, summary, confidence, event_json, created_at
                ) values (?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    source_key,
                    source_key,
                    conversation_id,
                    channel_id,
                    learning_type,
                    summary,
                    confidence,
                    json.dumps(event, ensure_ascii=False, separators=(",", ":")),
                    clean_text(event.get("createdAt") or now),
                ),
            )
            before = self.conn.execute(
                "select 1 from memory_knowledge where knowledge_id = ? limit 1",
                (knowledge_id,),
            ).fetchone()
            self.conn.execute(
                """
                insert into memory_knowledge (
                  knowledge_id, knowledge_type, source_key, conversation_id, title,
                  summary, confidence, applies_to_json, created_at, updated_at
                ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                on conflict(knowledge_id) do update set
                  confidence = excluded.confidence,
                  updated_at = excluded.updated_at
                """,
                (
                    knowledge_id,
                    learning_type,
                    source_key,
                    conversation_id,
                    clean_text(after.get("conversationTitle")),
                    summary,
                    confidence,
                    json.dumps(applies_to, ensure_ascii=False),
                    now,
                    now,
                ),
            )
            if before is None:
                new_knowledge = 1
            if conversation_id:
                self.conn.execute(
                    """
                    update memory_conversations
                    set learning_count = learning_count + 1, updated_at = ?
                    where conversation_id = ?
                    """,
                    (now, conversation_id),
                )
            self.mark_processed(source_key, "learning_event", now)
        return {"events": 1, "duplicates": 0, "learning": 1, "knowledge": new_knowledge}

    def ingest_accounting(self, event: dict[str, Any]) -> dict[str, int]:
        source_key = event_key(event, ("accountingId",), "accounting")
        if self.has_processed_event(source_key):
            return {"events": 0, "duplicates": 1, "accounting": 0}

        now = utc_now()
        conversation_id = clean_text(event.get("conversationId"))
        with self.conn:
            self.conn.execute(
                """
                insert or ignore into memory_accounting_events (
                  accounting_id, source_key, conversation_id, client_name, kind,
                  amount, currency, status, confidence, event_json, created_at
                ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    source_key,
                    source_key,
                    conversation_id,
                    clean_text(event.get("clientName")),
                    clean_text(event.get("kind")),
                    safe_float(event.get("amount"), 0.0) if event.get("amount") is not None else None,
                    clean_text(event.get("currency")),
                    clean_text(event.get("status")),
                    safe_float(event.get("confidence"), 0.0),
                    json.dumps(event, ensure_ascii=False, separators=(",", ":")),
                    clean_text(event.get("createdAt") or now),
                ),
            )
            if conversation_id:
                self.conn.execute(
                    """
                    update memory_conversations
                    set accounting_count = accounting_count + 1, updated_at = ?
                    where conversation_id = ?
                    """,
                    (now, conversation_id),
                )
            self.mark_processed(source_key, "accounting_event", now)
        return {"events": 1, "duplicates": 0, "accounting": 1}

    def ingest_domain_event(self, event: dict[str, Any]) -> dict[str, int]:
        event_id = clean_text(event.get("eventId"))
        if not event_id or not clean_text(event.get("sourceDomain")):
            return {"events": 0, "duplicates": 0, "domainEvents": 0, "domainKnowledge": 0}
        source_key = event_key(event, ("eventId",), "domain")
        if self.has_processed_event(source_key):
            return {"events": 0, "duplicates": 1, "domainEvents": 0, "domainKnowledge": 0}

        now = utc_now()
        event_type = clean_text(event.get("eventType"))
        data = event.get("data") if isinstance(event.get("data"), dict) else {}
        risk = event.get("risk") if isinstance(event.get("risk"), dict) else {}
        evidence = event.get("evidence") if isinstance(event.get("evidence"), list) else []
        first_evidence = evidence[0] if evidence and isinstance(evidence[0], dict) else {}
        summary = clean_text(data.get("summary") or first_evidence.get("summary") or event_type)
        conversation_id = clean_text(event.get("conversationId")) or "domain-only"
        channel_id = clean_text(event.get("channelId")) or "unknown"
        title = clean_text(data.get("conversationTitle")) or conversation_id
        knowledge_created = 0

        with self.conn:
            self.conn.execute(
                """
                insert or ignore into memory_domain_events (
                  event_id, event_type, source_domain, correlation_id, conversation_id,
                  case_id, customer_id, risk_level, requires_human_review, confidence,
                  summary, event_json, created_at
                ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    event_id,
                    event_type,
                    clean_text(event.get("sourceDomain")),
                    clean_text(event.get("correlationId")),
                    clean_text(event.get("conversationId")),
                    clean_text(event.get("caseId")),
                    clean_text(event.get("customerId")),
                    clean_text(risk.get("riskLevel")),
                    1 if event.get("requiresHumanReview") else 0,
                    safe_float(event.get("confidence"), 0.0),
                    summary,
                    json.dumps(event, ensure_ascii=False, separators=(",", ":")),
                    clean_text(event.get("createdAt")) or now,
                ),
            )
            self.conn.execute(
                """
                insert into memory_conversations (
                  conversation_id, channel_id, title, source, first_seen_at, last_seen_at,
                  message_count, decision_count, accounting_count, learning_count,
                  last_intent, countries_json, services_json, languages_json, updated_at
                ) values (?, ?, ?, ?, ?, ?, 0, 0, 0, 0, ?, '[]', '[]', '[]', ?)
                on conflict(conversation_id) do update set
                  last_seen_at = excluded.last_seen_at,
                  updated_at = excluded.updated_at
                """,
                (conversation_id, channel_id, title, "domain_event", now, now, event_type, now),
            )

            if event_type.startswith("Payment") or event_type.startswith("Debt") or event_type.startswith("Refund") or event_type.startswith("Quote"):
                self.conn.execute(
                    """
                    insert or ignore into memory_accounting_events (
                      accounting_id, source_key, conversation_id, client_name, kind,
                      amount, currency, status, confidence, event_json, created_at
                    ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        event_id,
                        source_key,
                        clean_text(event.get("conversationId")),
                        clean_text(data.get("clientName") or event.get("customerId")),
                        event_type,
                        data.get("amount"),
                        clean_text(data.get("currency")),
                        clean_text(data.get("status") or "domain_event"),
                        safe_float(event.get("confidence"), 0.0),
                        json.dumps(event, ensure_ascii=False, separators=(",", ":")),
                        clean_text(event.get("createdAt")) or now,
                    ),
                )
                self.conn.execute(
                    "update memory_conversations set accounting_count = accounting_count + 1, updated_at = ? where conversation_id = ?",
                    (now, conversation_id),
                )

            if event_type.startswith("Decision") or event_type.startswith("HumanApproval") or event_type.startswith("Action") or event_type.startswith("ChannelRoute"):
                self.conn.execute(
                    """
                    insert or ignore into memory_decisions (
                      decision_id, source_key, conversation_id, channel_id, intent,
                      confidence, proposed_action, requires_confirmation,
                      reasoning_summary, event_json, created_at
                    ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        event_id,
                        source_key,
                        clean_text(event.get("conversationId")),
                        channel_id,
                        event_type,
                        safe_float(event.get("confidence"), 0.0),
                        clean_text(data.get("proposedAction") or data.get("actionType") or event_type),
                        1 if event.get("requiresHumanReview") else 0,
                        summary,
                        json.dumps(event, ensure_ascii=False, separators=(",", ":")),
                        clean_text(event.get("createdAt")) or now,
                    ),
                )
                self.conn.execute(
                    "update memory_conversations set decision_count = decision_count + 1, last_intent = ?, updated_at = ? where conversation_id = ?",
                    (event_type, now, conversation_id),
                )

            if event_type.startswith("Learning") or event_type.startswith("Memory") or event_type.startswith("HumanCorrection") or event_type == "OperatorNoteAdded":
                knowledge_id = f"domain-knowledge-{stable_hash(event_type + '|' + conversation_id + '|' + summary)}"
                self.conn.execute(
                    """
                    insert or ignore into memory_knowledge (
                      knowledge_id, knowledge_type, source_key, conversation_id,
                      title, summary, confidence, applies_to_json, created_at, updated_at
                    ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        knowledge_id,
                        event_type,
                        source_key,
                        clean_text(event.get("conversationId")),
                        event_type,
                        summary,
                        safe_float(event.get("confidence"), 0.0),
                        json.dumps([event_type], ensure_ascii=False),
                        now,
                        now,
                    ),
                )
                if self.conn.total_changes:
                    knowledge_created = 1
                self.conn.execute(
                    "update memory_conversations set learning_count = learning_count + 1, updated_at = ? where conversation_id = ?",
                    (now, conversation_id),
                )

            self.mark_processed(source_key, "domain_event", now)
        return {"events": 1, "duplicates": 0, "domainEvents": 1, "domainKnowledge": knowledge_created}

    def customer_profile(self, conversation_id: str) -> dict[str, Any] | None:
        row = self.conn.execute(
            "select * from memory_conversations where conversation_id = ? limit 1",
            (conversation_id,),
        ).fetchone()
        if row is None:
            return None
        latest_messages = [
            dict(item)
            for item in self.conn.execute(
                """
                select message_id, direction, text, confidence, created_at
                from memory_messages
                where conversation_id = ?
                order by created_at desc
                limit 10
                """,
                (conversation_id,),
            ).fetchall()
        ]
        return {
            "conversationId": row["conversation_id"],
            "channelId": row["channel_id"],
            "title": row["title"],
            "lastIntent": row["last_intent"],
            "messageCount": row["message_count"],
            "decisionCount": row["decision_count"],
            "accountingCount": row["accounting_count"],
            "learningCount": row["learning_count"],
            "countries": json.loads(row["countries_json"] or "[]"),
            "services": json.loads(row["services_json"] or "[]"),
            "languages": json.loads(row["languages_json"] or "[]"),
            "latestMessages": latest_messages,
        }

    def search_messages(self, query: str, limit: int = 20) -> list[dict[str, Any]]:
        like = f"%{query}%"
        return [
            dict(row)
            for row in self.conn.execute(
                """
                select conversation_id, channel_id, direction, text, confidence, created_at
                from memory_messages
                where text like ?
                order by created_at desc
                limit ?
                """,
                (like, max(1, limit)),
            ).fetchall()
        ]

    def summary(self) -> dict[str, Any]:
        def scalar(sql: str) -> int:
            row = self.conn.execute(sql).fetchone()
            return int(row[0] if row else 0)

        latest_decision = self.conn.execute(
            """
            select conversation_id, channel_id, intent, proposed_action, confidence, created_at
            from memory_decisions
            order by created_at desc
            limit 1
            """
        ).fetchone()
        legacy_latest = self.conn.execute(
            """
            select channel_id, intent, label, text, created_at
            from decisions
            order by id desc
            limit 1
            """
        ).fetchone()
        return {
            "processedEvents": scalar("select count(*) from memory_processed_events"),
            "conversations": scalar("select count(*) from memory_conversations"),
            "messages": scalar("select count(*) from memory_messages") + scalar("select count(*) from messages"),
            "memoryMessages": scalar("select count(*) from memory_messages"),
            "legacyMessages": scalar("select count(*) from messages"),
            "signals": scalar("select count(*) from memory_message_signals"),
            "decisions": scalar("select count(*) from memory_decisions") + scalar("select count(*) from decisions"),
            "memoryDecisions": scalar("select count(*) from memory_decisions"),
            "learningEvents": scalar("select count(*) from memory_learning_events"),
            "accountingEvents": scalar("select count(*) from memory_accounting_events"),
            "domainEvents": scalar("select count(*) from memory_domain_events"),
            "knowledgeItems": scalar("select count(*) from memory_knowledge"),
            "observations": scalar("select count(*) from observations"),
            "latestDecision": dict(latest_decision) if latest_decision else (dict(legacy_latest) if legacy_latest else None),
            "db": str(self.db_path),
        }

    # Legacy DesktopAgentService API.
    def has_observation(self, observation_key: str) -> bool:
        row = self.conn.execute(
            "select 1 from observations where observation_key = ? limit 1",
            (observation_key,),
        ).fetchone()
        return row is not None

    def ingest_observation(self, observation: dict[str, Any], decision: Decision) -> dict[str, int]:
        observation_key = str(observation["observationKey"])
        if self.has_observation(observation_key):
            return {"observations": 0, "messages": 0, "decisions": 0}

        now = utc_now()
        with self.conn:
            self.conn.execute(
                """
                insert into observations (
                  observation_key, channel_id, source, confidence, captured_at,
                  conversation_title, raw_json, ingested_at
                ) values (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    observation_key,
                    observation.get("channelId"),
                    observation.get("source"),
                    observation.get("confidence"),
                    observation.get("capturedAt"),
                    observation.get("conversationTitle"),
                    json.dumps(observation, ensure_ascii=False, separators=(",", ":")),
                    now,
                ),
            )
            message_count = 0
            for message in observation.get("messages") or []:
                text = str(message.get("text") or "").strip()
                if not text:
                    continue
                self.conn.execute(
                    """
                    insert or ignore into messages (
                      observation_key, channel_id, text_hash, text, sender_name,
                      direction, sent_at, learning_type, created_at
                    ) values (?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        observation_key,
                        observation.get("channelId"),
                        text_hash(text),
                        text,
                        message.get("senderName"),
                        message.get("direction"),
                        message.get("sentAt"),
                        decision.intent,
                        now,
                    ),
                )
                message_count += 1
            self.conn.execute(
                """
                insert into decisions (
                  observation_key, channel_id, intent, label, priority, score,
                  text, reasons_json, created_at
                ) values (?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    observation_key,
                    observation.get("channelId"),
                    decision.intent,
                    decision.label,
                    decision.priority,
                    decision.score,
                    decision.text,
                    json.dumps(decision.reasons, ensure_ascii=False),
                    now,
                ),
            )
        return {"observations": 1, "messages": message_count, "decisions": 1}


class AgentMemory(MemoryStore):
    pass


def add_counts(target: dict[str, int], source: dict[str, int]) -> None:
    for key, value in source.items():
        target[key] = target.get(key, 0) + int(value or 0)


def run_memory_once(
    conversation_events_file: Path,
    cognitive_decision_events_file: Path,
    operating_decision_events_file: Path,
    learning_events_file: Path,
    accounting_events_file: Path,
    state_file: Path,
    db_path: Path,
    limit: int = 500,
    domain_events_file: Path | None = None,
) -> dict[str, Any]:
    store = MemoryStore(db_path)
    ingested: dict[str, int] = {
        "events": 0,
        "duplicates": 0,
        "conversations": 0,
        "messages": 0,
        "signals": 0,
        "decisions": 0,
        "learning": 0,
        "knowledge": 0,
        "accounting": 0,
        "domainEvents": 0,
        "domainKnowledge": 0,
    }
    try:
        if domain_events_file is not None:
            for event in read_jsonl_events(domain_events_file, None, limit):
                add_counts(ingested, store.ingest_domain_event(event))
        for event in read_jsonl_events(conversation_events_file, "conversation_event", limit):
            add_counts(ingested, store.ingest_conversation(event))
        for event in read_jsonl_events(cognitive_decision_events_file, "decision_event", limit):
            add_counts(ingested, store.ingest_decision(event, "cognitive"))
        for event in read_jsonl_events(operating_decision_events_file, "decision_event", limit):
            add_counts(ingested, store.ingest_decision(event, "operating"))
        for event in read_jsonl_events(learning_events_file, "learning_event", limit):
            add_counts(ingested, store.ingest_learning(event))
        for event in read_jsonl_events(accounting_events_file, "accounting_event", limit):
            add_counts(ingested, store.ingest_accounting(event))

        state = {
            "status": "ok",
            "engine": "ariadgsm_memory_core",
            "updatedAt": utc_now(),
            "conversationEventsFile": str(conversation_events_file),
            "cognitiveDecisionEventsFile": str(cognitive_decision_events_file),
            "operatingDecisionEventsFile": str(operating_decision_events_file),
            "learningEventsFile": str(learning_events_file),
            "accountingEventsFile": str(accounting_events_file),
            "domainEventsFile": str(domain_events_file) if domain_events_file is not None else None,
            "ingested": ingested,
            "summary": store.summary(),
        }
        append_state(state_file, state)
        return state
    finally:
        store.close()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM Memory Core")
    parser.add_argument("--conversation-events", default="runtime/timeline-events.jsonl")
    parser.add_argument("--cognitive-decisions", default="runtime/cognitive-decision-events.jsonl")
    parser.add_argument("--operating-decisions", default="runtime/decision-events.jsonl")
    parser.add_argument("--learning-events", default="runtime/learning-events.jsonl")
    parser.add_argument("--accounting-events", default="runtime/accounting-events.jsonl")
    parser.add_argument("--domain-events", default="runtime/domain-events.jsonl")
    parser.add_argument("--state-file", default="runtime/memory-state.json")
    parser.add_argument("--db", default="runtime/memory-core.sqlite")
    parser.add_argument("--limit", type=int, default=500)
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def main() -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    args = parse_args()
    state = run_memory_once(
        resolve_runtime_path(args.conversation_events),
        resolve_runtime_path(args.cognitive_decisions),
        resolve_runtime_path(args.operating_decisions),
        resolve_runtime_path(args.learning_events),
        resolve_runtime_path(args.accounting_events),
        resolve_runtime_path(args.state_file),
        resolve_runtime_path(args.db),
        limit=args.limit,
        domain_events_file=resolve_runtime_path(args.domain_events),
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=False, indent=2))
    else:
        summary = state["summary"]
        ingested = state["ingested"]
        print(
            "AriadGSM Memory Core: "
            f"events={ingested['events']} "
            f"duplicates={ingested['duplicates']} "
            f"conversations={summary['conversations']} "
            f"messages={summary['memoryMessages']} "
            f"signals={summary['signals']} "
            f"knowledge={summary['knowledgeItems']} "
            f"db={summary['db']}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
