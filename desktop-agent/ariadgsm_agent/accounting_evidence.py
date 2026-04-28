from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
import sys
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from .accounting import extract_amounts
from .contracts import validate_contract
from .domain_events import make_domain_event, resolve_runtime_path
from .text import clean_text, normalize


AGENT_ROOT = Path(__file__).resolve().parents[1]
RUNTIME_DIR = AGENT_ROOT / "runtime"
SCHEMA_VERSION = "0.8.5"
POLICY_VERSION = "ariadgsm-accounting-evidence-policy-0.8.5"

ACCOUNTING_INPUT_TYPES = {
    "PaymentDrafted",
    "PaymentEvidenceAttached",
    "PaymentConfirmed",
    "DebtDetected",
    "DebtUpdated",
    "RefundCandidate",
    "AccountingEvidenceAttached",
    "AccountingRecordConfirmed",
    "AccountingCorrectionReceived",
}
CONFIRMING_TYPES = {"PaymentConfirmed", "AccountingRecordConfirmed"}
EVIDENCE_TYPES = {"PaymentEvidenceAttached", "AccountingEvidenceAttached", "PaymentConfirmed", "AccountingRecordConfirmed"}
KIND_BY_EVENT = {
    "PaymentDrafted": "payment",
    "PaymentEvidenceAttached": "payment",
    "PaymentConfirmed": "payment",
    "DebtDetected": "debt",
    "DebtUpdated": "debt",
    "RefundCandidate": "refund",
    "AccountingEvidenceAttached": "payment",
    "AccountingRecordConfirmed": "payment",
    "AccountingCorrectionReceived": "correction",
}
LEVEL_SCORE = {"F": 0, "E": 1, "D": 2, "C": 3, "B": 4, "A": 5}
STATUS_SCORE = {
    "draft": 1,
    "needs_evidence": 2,
    "needs_human": 3,
    "evidence_attached": 4,
    "confirmed": 5,
}
DEFAULT_POLICY: dict[str, Any] = {
    "version": POLICY_VERSION,
    "levels": ["A", "B", "C", "D", "E", "F"],
    "confirmationRequires": ["evidence_level_A"],
    "minimumEvidenceLevelToAttach": "C",
    "minimumConfidenceToAttach": 0.55,
}


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def stable_hash(value: str, length: int = 24) -> str:
    return hashlib.sha1(value.encode("utf-8")).hexdigest()[:length]


def compact_json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def clamp(value: Any, default: float = 0.5) -> float:
    try:
        number = float(value)
    except (TypeError, ValueError):
        number = default
    return max(0.0, min(1.0, number))


def as_float(value: Any) -> float | None:
    if value in (None, ""):
        return None
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def read_jsonl(path: Path, limit: int) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    rows: list[dict[str, Any]] = []
    with path.open("r", encoding="utf-8-sig") as handle:
        for line in handle:
            if not line.strip():
                continue
            try:
                value = json.loads(line)
            except json.JSONDecodeError:
                continue
            if isinstance(value, dict):
                rows.append(value)
    return rows[-max(1, limit):]


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    temp.replace(path)


def append_jsonl(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")


def event_id(event: dict[str, Any]) -> str:
    explicit = clean_text(event.get("eventId"))
    if explicit:
        return explicit
    return f"event-{stable_hash(compact_json(event), 24)}"


def event_data(event: dict[str, Any]) -> dict[str, Any]:
    data = event.get("data")
    return data if isinstance(data, dict) else {}


def best_evidence_level(event: dict[str, Any]) -> str:
    levels: list[str] = []
    data = event_data(event)
    if clean_text(data.get("evidenceLevel")):
        levels.append(clean_text(data.get("evidenceLevel")).upper())
    for evidence in event.get("evidence") or []:
        if isinstance(evidence, dict):
            levels.append(clean_text(evidence.get("evidenceLevel")).upper())
    valid = [level for level in levels if level in LEVEL_SCORE]
    if not valid:
        return "F"
    return max(valid, key=lambda level: LEVEL_SCORE[level])


def normalize_evidence(event: dict[str, Any], record_id: str) -> list[dict[str, Any]]:
    output: list[dict[str, Any]] = []
    for index, evidence in enumerate(event.get("evidence") or []):
        if not isinstance(evidence, dict):
            continue
        level = clean_text(evidence.get("evidenceLevel")).upper()
        if level not in LEVEL_SCORE:
            level = "F"
        evidence_id = clean_text(evidence.get("evidenceId")) or f"acctev-{stable_hash(record_id + '|' + event_id(event) + '|' + str(index), 20)}"
        output.append(
            {
                "evidenceId": evidence_id,
                "source": clean_text(evidence.get("source")) or clean_text(event.get("eventType")) or "domain_event",
                "evidenceLevel": level,
                "observedAt": clean_text(evidence.get("observedAt")) or clean_text(event.get("createdAt")) or utc_now(),
                "summary": clean_text(evidence.get("summary")) or clean_text(event.get("summary")) or "Accounting evidence observed.",
                "rawReference": clean_text(evidence.get("rawReference")) or f"local://domain_event/{event_id(event)}",
                "confidence": clamp(evidence.get("confidence"), clamp(event.get("confidence"), 0.5)),
                "redactionState": clean_text(evidence.get("redactionState")) or "safe_summary",
                "limitations": [clean_text(item) for item in evidence.get("limitations") or [] if clean_text(item)],
            }
        )
    if not output:
        output.append(
            {
                "evidenceId": f"acctev-{stable_hash(record_id + '|' + event_id(event), 20)}",
                "source": clean_text(event.get("eventType")) or "domain_event",
                "evidenceLevel": best_evidence_level(event),
                "observedAt": clean_text(event.get("createdAt")) or utc_now(),
                "summary": clean_text(event.get("summary")) or "Accounting event observed.",
                "rawReference": f"local://domain_event/{event_id(event)}",
                "confidence": clamp(event.get("confidence"), 0.5),
                "redactionState": "safe_summary",
                "limitations": ["Generated from event envelope because source evidence was missing."],
            }
        )
    deduped: dict[str, dict[str, Any]] = {}
    for item in output:
        deduped[item["evidenceId"]] = item
    return list(deduped.values())


def merge_evidence(existing_json: str, incoming: list[dict[str, Any]]) -> list[dict[str, Any]]:
    try:
        existing = json.loads(existing_json) if existing_json else []
    except json.JSONDecodeError:
        existing = []
    merged: dict[str, dict[str, Any]] = {}
    for item in existing if isinstance(existing, list) else []:
        if isinstance(item, dict) and clean_text(item.get("evidenceId")):
            merged[clean_text(item.get("evidenceId"))] = item
    for item in incoming:
        merged[item["evidenceId"]] = item
    return list(merged.values())


def level_from_evidence(evidence: list[dict[str, Any]]) -> str:
    levels = [clean_text(item.get("evidenceLevel")).upper() for item in evidence if isinstance(item, dict)]
    levels = [level for level in levels if level in LEVEL_SCORE]
    return max(levels, key=lambda level: LEVEL_SCORE[level]) if levels else "F"


def amount_from_event(event: dict[str, Any]) -> tuple[float | None, str]:
    data = event_data(event)
    amount = as_float(data.get("amount"))
    currency = clean_text(data.get("currency")).upper()
    if amount is not None:
        return amount, currency
    haystack = " ".join(
        [
            clean_text(data.get("amountText")),
            clean_text(data.get("summary")),
            clean_text(event.get("summary")),
            compact_json(data),
        ]
    )
    amounts = extract_amounts(haystack)
    if not amounts:
        return None, currency
    first = amounts[0]
    return as_float(first.get("amount")), clean_text(first.get("currency")).upper()


def kind_from_event(event: dict[str, Any]) -> str:
    data = event_data(event)
    value = normalize(data.get("kind") or data.get("recordKind"))
    if value in {"payment", "debt", "refund", "price_quote", "correction"}:
        return value
    return KIND_BY_EVENT.get(clean_text(event.get("eventType")), "unknown")


def record_id_for(event: dict[str, Any], case_id: str, kind: str) -> str:
    data = event_data(event)
    explicit = clean_text(data.get("accountingRecordId") or data.get("recordId") or data.get("accountingId"))
    if explicit:
        return f"acct-{stable_hash(case_id + '|' + explicit, 28)}"
    raw = compact_json(
        {
            "kind": kind,
            "caseId": case_id,
            "customerId": event.get("customerId"),
            "conversationId": event.get("conversationId"),
            "amount": data.get("amount"),
            "currency": data.get("currency"),
            "eventId": event_id(event),
        }
    )
    return f"acct-{stable_hash(raw, 28)}"


def read_case_rows(case_manager_db: Path) -> list[dict[str, Any]]:
    if not case_manager_db.exists():
        return []
    conn = sqlite3.connect(case_manager_db)
    conn.row_factory = sqlite3.Row
    try:
        rows = conn.execute("select * from cases order by updated_at desc").fetchall()
        return [dict(row) for row in rows]
    except sqlite3.Error:
        return []
    finally:
        conn.close()


def case_context_for(event: dict[str, Any], cases: list[dict[str, Any]]) -> dict[str, Any]:
    case_id = clean_text(event.get("caseId"))
    customer_id = clean_text(event.get("customerId"))
    conversation_id = clean_text(event.get("conversationId"))
    if case_id:
        for row in cases:
            if clean_text(row.get("case_id") or row.get("caseId")) == case_id:
                return row
    if customer_id and customer_id != "customer_pending":
        for row in cases:
            if clean_text(row.get("customer_id") or row.get("customerId")) == customer_id:
                return row
    if conversation_id:
        for row in cases:
            raw = clean_text(row.get("conversations_json") or row.get("conversations"))
            if conversation_id and conversation_id in raw:
                return row
    return {}


def context_value(context: dict[str, Any], *names: str) -> str:
    for name in names:
        value = clean_text(context.get(name))
        if value:
            return value
    return ""


def source_ids(existing_json: str, incoming_id: str) -> list[str]:
    try:
        values = json.loads(existing_json) if existing_json else []
    except json.JSONDecodeError:
        values = []
    output = [clean_text(item) for item in values if clean_text(item)] if isinstance(values, list) else []
    if incoming_id and incoming_id not in output:
        output.append(incoming_id)
    return output


def status_for(event: dict[str, Any], kind: str, amount: float | None, evidence_level: str) -> tuple[str, str]:
    event_type = clean_text(event.get("eventType"))
    if event_type in CONFIRMING_TYPES:
        if evidence_level == "A":
            return "confirmed", "confirmed_with_level_a_evidence"
        return "needs_human", "confirmation_event_without_level_a_evidence"
    if event_type in EVIDENCE_TYPES and LEVEL_SCORE.get(evidence_level, 0) >= LEVEL_SCORE["C"]:
        return "evidence_attached", "evidence_attached_to_draft"
    if kind in {"payment", "refund"} and amount is None:
        return "needs_evidence", "missing_amount"
    if kind == "debt" and amount is None and event_type not in {"DebtUpdated"}:
        return "needs_evidence", "debt_without_amount"
    return "draft", "accounting_signal_captured"


class AccountingEvidenceStore:
    def __init__(self, db_path: Path, accounting_events_file: Path):
        self.db_path = db_path
        self.accounting_events_file = accounting_events_file
        self.db_path.parent.mkdir(parents=True, exist_ok=True)
        self.accounting_events_file.parent.mkdir(parents=True, exist_ok=True)
        self.conn = sqlite3.connect(self.db_path)
        self.conn.row_factory = sqlite3.Row
        self.init_schema()

    def close(self) -> None:
        self.conn.close()

    def init_schema(self) -> None:
        self.conn.executescript(
            """
            create table if not exists processed_events (
              source_event_id text primary key,
              processed_at text not null
            );
            create table if not exists accounting_records (
              record_id text primary key,
              case_id text,
              customer_id text,
              channel_id text,
              conversation_id text,
              record_kind text not null,
              status text not null,
              amount real,
              currency text,
              method text,
              evidence_level text not null,
              evidence_count integer not null,
              confidence real not null,
              requires_human integer not null,
              ambiguous integer not null,
              reason text not null,
              source_event_ids_json text not null,
              evidence_json text not null,
              created_at text not null,
              updated_at text not null
            );
            create index if not exists idx_accounting_records_case on accounting_records(case_id);
            create index if not exists idx_accounting_records_status on accounting_records(status);
            create table if not exists emitted_events (
              emit_key text primary key,
              event_id text not null,
              event_type text not null,
              record_id text not null,
              emitted_at text not null
            );
            """
        )
        self.conn.commit()

    def processed(self, source_event_id: str) -> bool:
        row = self.conn.execute(
            "select 1 from processed_events where source_event_id = ? limit 1",
            (source_event_id,),
        ).fetchone()
        return row is not None

    def mark_processed(self, source_event_id: str) -> None:
        self.conn.execute(
            "insert or ignore into processed_events(source_event_id, processed_at) values (?, ?)",
            (source_event_id, utc_now()),
        )

    def emitted(self, emit_key: str) -> bool:
        row = self.conn.execute("select 1 from emitted_events where emit_key = ? limit 1", (emit_key,)).fetchone()
        return row is not None

    def mark_emitted(self, emit_key: str, event: dict[str, Any], record_id: str) -> None:
        self.conn.execute(
            """
            insert or ignore into emitted_events(emit_key, event_id, event_type, record_id, emitted_at)
            values (?, ?, ?, ?, ?)
            """,
            (emit_key, clean_text(event.get("eventId")), clean_text(event.get("eventType")), record_id, utc_now()),
        )

    def save_record(self, record: dict[str, Any]) -> tuple[dict[str, Any], bool]:
        now = utc_now()
        existing = self.conn.execute(
            "select * from accounting_records where record_id = ? limit 1",
            (record["recordId"],),
        ).fetchone()
        if existing is None:
            with self.conn:
                self.conn.execute(
                    """
                    insert into accounting_records(
                      record_id, case_id, customer_id, channel_id, conversation_id,
                      record_kind, status, amount, currency, method, evidence_level,
                      evidence_count, confidence, requires_human, ambiguous, reason,
                      source_event_ids_json, evidence_json, created_at, updated_at
                    ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        record["recordId"],
                        record["caseId"],
                        record["customerId"],
                        record["channelId"],
                        record["conversationId"],
                        record["kind"],
                        record["status"],
                        record["amount"],
                        record["currency"],
                        record["method"],
                        record["evidenceLevel"],
                        record["evidenceCount"],
                        record["confidence"],
                        1 if record["requiresHuman"] else 0,
                        1 if record["ambiguous"] else 0,
                        record["reason"],
                        json.dumps([record["sourceEventId"]], ensure_ascii=False),
                        json.dumps(record["evidence"], ensure_ascii=False, separators=(",", ":")),
                        now,
                        now,
                    ),
                )
                self.mark_processed(record["sourceEventId"])
            return record, True

        existing_dict = dict(existing)
        merged_evidence = merge_evidence(existing_dict.get("evidence_json") or "[]", record["evidence"])
        evidence_level = level_from_evidence(merged_evidence)
        existing_status = clean_text(existing_dict.get("status"))
        status = record["status"] if STATUS_SCORE[record["status"]] >= STATUS_SCORE.get(existing_status, 0) else existing_status
        source_event_ids = source_ids(existing_dict.get("source_event_ids_json") or "[]", record["sourceEventId"])
        amount = record["amount"] if record["amount"] is not None else existing_dict.get("amount")
        currency = record["currency"] or clean_text(existing_dict.get("currency"))
        method = record["method"] or clean_text(existing_dict.get("method"))
        requires_human = bool(record["requiresHuman"] or existing_dict.get("requires_human"))
        ambiguous = bool(record["ambiguous"] or existing_dict.get("ambiguous"))
        confidence = max(float(existing_dict.get("confidence") or 0.0), float(record["confidence"]))
        reason = record["reason"] if STATUS_SCORE[record["status"]] >= STATUS_SCORE.get(existing_status, 0) else clean_text(existing_dict.get("reason"))
        with self.conn:
            self.conn.execute(
                """
                update accounting_records
                set status = ?, amount = ?, currency = ?, method = ?, evidence_level = ?,
                    evidence_count = ?, confidence = ?, requires_human = ?, ambiguous = ?,
                    reason = ?, source_event_ids_json = ?, evidence_json = ?, updated_at = ?
                where record_id = ?
                """,
                (
                    status,
                    amount,
                    currency,
                    method,
                    evidence_level,
                    len(merged_evidence),
                    confidence,
                    1 if requires_human else 0,
                    1 if ambiguous else 0,
                    reason,
                    json.dumps(source_event_ids, ensure_ascii=False),
                    json.dumps(merged_evidence, ensure_ascii=False, separators=(",", ":")),
                    now,
                    record["recordId"],
                ),
            )
            self.mark_processed(record["sourceEventId"])
        merged = {**record, "status": status, "amount": amount, "currency": currency, "method": method, "evidenceLevel": evidence_level, "evidence": merged_evidence, "evidenceCount": len(merged_evidence), "confidence": confidence, "requiresHuman": requires_human, "ambiguous": ambiguous, "reason": reason}
        return merged, False

    def append_event_if_new(self, emit_key: str, event: dict[str, Any], record_id: str) -> bool:
        if self.emitted(emit_key):
            return False
        with self.conn:
            append_jsonl(self.accounting_events_file, event)
            self.mark_emitted(emit_key, event, record_id)
        return True

    def summary(self, domain_events_read: int) -> dict[str, Any]:
        def scalar(sql: str) -> int:
            row = self.conn.execute(sql).fetchone()
            return int(row[0] if row else 0)

        def counter(sql: str) -> dict[str, int]:
            return {str(row[0]): int(row[1]) for row in self.conn.execute(sql).fetchall()}

        latest_rows = self.conn.execute(
            """
            select record_id, case_id, customer_id, channel_id, conversation_id,
                   record_kind, status, amount, currency, evidence_level,
                   evidence_count, confidence, requires_human, ambiguous, reason, updated_at
            from accounting_records
            order by updated_at desc
            limit 12
            """
        ).fetchall()
        return {
            "domainEventsRead": domain_events_read,
            "accountingRecords": scalar("select count(*) from accounting_records"),
            "drafts": scalar("select count(*) from accounting_records where status in ('draft', 'evidence_attached')"),
            "needsEvidence": scalar("select count(*) from accounting_records where status = 'needs_evidence'"),
            "needsHuman": scalar("select count(*) from accounting_records where requires_human = 1 and status != 'confirmed'"),
            "confirmedRecords": scalar("select count(*) from accounting_records where status = 'confirmed'"),
            "payments": scalar("select count(*) from accounting_records where record_kind = 'payment'"),
            "debts": scalar("select count(*) from accounting_records where record_kind = 'debt'"),
            "refunds": scalar("select count(*) from accounting_records where record_kind = 'refund'"),
            "evidenceAttached": scalar("select count(*) from accounting_records where evidence_count > 0"),
            "emittedAccountingEvents": scalar("select count(*) from emitted_events"),
            "ambiguousRecords": scalar("select count(*) from accounting_records where ambiguous = 1"),
            "byStatus": counter("select status, count(*) from accounting_records group by status order by status"),
            "latest": [dict(row) for row in latest_rows],
        }


def record_from_event(event: dict[str, Any], cases: list[dict[str, Any]]) -> dict[str, Any]:
    kind = kind_from_event(event)
    context = case_context_for(event, cases)
    case_id = clean_text(event.get("caseId")) or context_value(context, "case_id", "caseId") or "case_pending"
    customer_id = clean_text(event.get("customerId")) or context_value(context, "customer_id", "customerId") or "customer_pending"
    channel_id = clean_text(event.get("channelId")) or context_value(context, "primary_channel_id", "primaryChannelId")
    conversation_id = clean_text(event.get("conversationId"))
    if not conversation_id:
        raw_conversations = context_value(context, "conversations_json", "conversations")
        try:
            conversations = json.loads(raw_conversations) if raw_conversations else []
        except json.JSONDecodeError:
            conversations = []
        if isinstance(conversations, list) and conversations:
            conversation_id = clean_text(conversations[0])

    amount, currency = amount_from_event(event)
    data = event_data(event)
    method = clean_text(data.get("method"))
    record_id = record_id_for(event, case_id, kind)
    evidence = normalize_evidence(event, record_id)
    evidence_level = level_from_evidence(evidence)
    status, reason = status_for(event, kind, amount, evidence_level)
    ambiguous = case_id == "case_pending" or customer_id == "customer_pending" or (kind in {"payment", "refund"} and amount is None)
    requires_human = bool(event.get("requiresHumanReview")) or ambiguous or status in {"needs_human", "needs_evidence"}
    confidence = clamp(event.get("confidence"), 0.5)
    if evidence_level in {"A", "B"}:
        confidence = max(confidence, 0.72)
    return {
        "recordId": record_id,
        "sourceEventId": event_id(event),
        "sourceEventType": clean_text(event.get("eventType")),
        "caseId": case_id,
        "customerId": customer_id,
        "channelId": channel_id or None,
        "conversationId": conversation_id or None,
        "kind": kind,
        "status": status,
        "amount": amount,
        "currency": currency,
        "method": method,
        "evidenceLevel": evidence_level,
        "evidenceCount": len(evidence),
        "confidence": confidence,
        "requiresHuman": requires_human,
        "ambiguous": ambiguous,
        "reason": reason,
        "evidence": evidence,
    }


def make_accounting_event(event_type: str, record: dict[str, Any], source_event: dict[str, Any], summary: str) -> dict[str, Any]:
    data = {
        "accountingCoreEvent": True,
        "accountingRecordId": record["recordId"],
        "recordKind": record["kind"],
        "status": record["status"],
        "amount": record["amount"],
        "currency": record["currency"],
        "method": record["method"],
        "evidenceLevel": record["evidenceLevel"],
        "evidenceCount": record["evidenceCount"],
        "reason": record["reason"],
        "requiresHumanApproval": bool(record["requiresHuman"]),
        "sourceEventType": record["sourceEventType"],
    }
    event = make_domain_event(
        event_type,
        source_event,
        subject_type="accounting_record",
        subject_id=record["recordId"],
        data=data,
        confidence=record["confidence"],
        summary=summary,
        source_domain="AccountingBrain",
        autonomy_level=3,
        limitations=["Accounting Core never confirms without level A evidence or explicit human/accounting confirmation."],
    )
    event["eventId"] = f"acctevt-{stable_hash(event_type + '|' + record['recordId'] + '|' + record['status'], 24)}"
    event["idempotencyKey"] = f"{event_type}:{stable_hash(record['recordId'] + '|' + record['status'], 32)}"
    event["correlationId"] = record["caseId"]
    event["caseId"] = record["caseId"]
    event["customerId"] = record["customerId"]
    event["channelId"] = record["channelId"]
    event["conversationId"] = record["conversationId"]
    event["evidence"] = record["evidence"]
    if event_type == "AccountingRecordConfirmed":
        event["risk"]["riskLevel"] = "critical"
        event["risk"]["riskReasons"] = sorted(set(event["risk"].get("riskReasons", []) + ["accounting_confirmation"]))
        event["risk"]["blockedActions"] = sorted(set(event["risk"].get("blockedActions", []) + ["confirm_without_level_a_evidence"]))
        event["requiresHumanReview"] = True
    errors = validate_contract(event, "domain_event")
    if errors:
        raise ValueError("; ".join(errors))
    return event


def events_for_record(record: dict[str, Any], source_event: dict[str, Any]) -> list[tuple[str, dict[str, Any]]]:
    output: list[tuple[str, dict[str, Any]]] = []
    source_type = record["sourceEventType"]
    if source_type not in {"AccountingEvidenceAttached", "AccountingRecordConfirmed"} and LEVEL_SCORE.get(record["evidenceLevel"], 0) >= LEVEL_SCORE["C"]:
        event = make_accounting_event(
            "AccountingEvidenceAttached",
            record,
            source_event,
            f"Evidencia contable adjunta para {record['kind']} en {record['caseId']}.",
        )
        output.append((f"AccountingEvidenceAttached:{record['recordId']}:{record['evidenceLevel']}", event))
    if record["status"] == "confirmed" and source_type != "AccountingRecordConfirmed":
        event = make_accounting_event(
            "AccountingRecordConfirmed",
            record,
            source_event,
            f"Registro contable confirmado para {record['kind']} en {record['caseId']}.",
        )
        output.append((f"AccountingRecordConfirmed:{record['recordId']}", event))
    if record["requiresHuman"] and record["status"] != "confirmed":
        event = make_accounting_event(
            "HumanApprovalRequired",
            record,
            source_event,
            f"Bryams debe revisar registro contable {record['recordId']} antes de confirmar.",
        )
        output.append((f"HumanApprovalRequired:{record['recordId']}:{record['reason']}", event))
    return output


def build_human_report(summary: dict[str, Any]) -> dict[str, Any]:
    latest = summary.get("latest") if isinstance(summary.get("latest"), list) else []
    pending = [item for item in latest if isinstance(item, dict) and item.get("status") != "confirmed"]
    confirmed = [item for item in latest if isinstance(item, dict) and item.get("status") == "confirmed"]
    needs = [item for item in latest if isinstance(item, dict) and item.get("requires_human")]
    risks: list[str] = []
    if summary.get("needsEvidence", 0):
        risks.append("Hay registros con monto, cliente o evidencia incompleta; no deben confirmarse.")
    if summary.get("needsHuman", 0):
        risks.append("Hay registros contables que necesitan revision de Bryams.")
    if summary.get("ambiguousRecords", 0):
        risks.append("Hay registros sin caso/cliente firme; mezclar caja sin resolver eso seria riesgoso.")
    if not risks:
        risks.append("Contabilidad trazada con evidencia; las confirmaciones siguen siendo auditables.")
    return {
        "quePaso": (
            f"Accounting Core proceso {summary.get('domainEventsRead', 0)} eventos, "
            f"mantiene {summary.get('accountingRecords', 0)} registros, "
            f"{summary.get('confirmedRecords', 0)} confirmados y {summary.get('needsHuman', 0)} para Bryams."
        ),
        "pendientes": pending[:8],
        "confirmados": confirmed[:8],
        "necesitanBryams": needs[:8],
        "riesgos": risks,
    }


def run_accounting_evidence_once(
    domain_events_file: Path,
    case_manager_db: Path,
    accounting_events_file: Path,
    state_file: Path,
    db_path: Path,
    report_file: Path | None = None,
    limit: int = 500,
) -> dict[str, Any]:
    store = AccountingEvidenceStore(db_path, accounting_events_file)
    cases = read_case_rows(case_manager_db)
    domain_events = read_jsonl(domain_events_file, limit)
    ingested = {"domainEvents": len(domain_events), "accountingEvents": 0, "duplicates": 0, "invalid": 0, "records": 0, "emittedEvents": 0}
    emitted_counts: Counter[str] = Counter()
    try:
        for event in domain_events:
            if clean_text(event.get("eventType")) not in ACCOUNTING_INPUT_TYPES:
                continue
            ingested["accountingEvents"] += 1
            source_id = event_id(event)
            if bool(event_data(event).get("accountingCoreEvent")):
                ingested["duplicates"] += 1
                with store.conn:
                    store.mark_processed(source_id)
                continue
            if store.processed(source_id):
                ingested["duplicates"] += 1
                continue
            errors = validate_contract(event, "domain_event")
            if errors:
                ingested["invalid"] += 1
                with store.conn:
                    store.mark_processed(source_id)
                continue
            record = record_from_event(event, cases)
            saved_record, _ = store.save_record(record)
            ingested["records"] += 1
            for emit_key, output_event in events_for_record(saved_record, event):
                if store.append_event_if_new(emit_key, output_event, saved_record["recordId"]):
                    ingested["emittedEvents"] += 1
                    emitted_counts[output_event["eventType"]] += 1

        summary = store.summary(len(domain_events))
        status = "idle" if summary["accountingRecords"] == 0 else "attention" if summary["needsHuman"] or summary["needsEvidence"] else "ok"
        human_report = build_human_report(summary)
        state = {
            "status": status,
            "engine": "ariadgsm_accounting_core_evidence_first",
            "version": SCHEMA_VERSION,
            "updatedAt": utc_now(),
            "domainEventsFile": str(domain_events_file),
            "caseManagerDb": str(case_manager_db),
            "accountingEventsFile": str(accounting_events_file),
            "db": str(db_path),
            "evidencePolicy": DEFAULT_POLICY,
            "ingested": ingested,
            "summary": {**summary, "emittedByType": dict(emitted_counts)},
            "humanReport": human_report,
        }
        errors = validate_contract(state, "accounting_core_state")
        if errors:
            state["status"] = "blocked"
            state["contractErrors"] = errors
        write_json(state_file, state)
        if report_file is not None:
            write_json(report_file, human_report)
        return state
    finally:
        store.close()


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM Accounting Core evidence-first")
    parser.add_argument("--domain-events", default="runtime/domain-events.jsonl")
    parser.add_argument("--case-manager-db", default="runtime/case-manager.sqlite")
    parser.add_argument("--accounting-events", default="runtime/accounting-core-events.jsonl")
    parser.add_argument("--state-file", default="runtime/accounting-core-state.json")
    parser.add_argument("--report-file", default="runtime/accounting-core-report.json")
    parser.add_argument("--db", default="runtime/accounting-core.sqlite")
    parser.add_argument("--limit", type=int, default=500)
    parser.add_argument("--json", action="store_true")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    args = parse_args(argv)
    state = run_accounting_evidence_once(
        resolve_runtime_path(args.domain_events),
        resolve_runtime_path(args.case_manager_db),
        resolve_runtime_path(args.accounting_events),
        resolve_runtime_path(args.state_file),
        resolve_runtime_path(args.db),
        report_file=resolve_runtime_path(args.report_file),
        limit=max(1, args.limit),
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=False, indent=2))
    else:
        summary = state["summary"]
        print(
            "AriadGSM Accounting Core: "
            f"records={summary['accountingRecords']} "
            f"confirmed={summary['confirmedRecords']} "
            f"needs_evidence={summary['needsEvidence']} "
            f"needs_human={summary['needsHuman']} "
            f"events={summary['emittedAccountingEvents']}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
