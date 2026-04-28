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

from .contracts import validate_contract
from .domain_events import make_domain_event, resolve_runtime_path
from .text import clean_text


AGENT_ROOT = Path(__file__).resolve().parents[1]
RUNTIME_DIR = AGENT_ROOT / "runtime"
SCHEMA_VERSION = "0.8.3"

CASE_EVENT_TYPES = {"CaseOpened", "CaseUpdated", "CaseNeedsHumanContext", "CaseClosed", "CaseMerged"}
LOW_LEVEL_TYPES = {
    "ObservationCreated",
    "ObservationRejected",
    "VisibleConversationObserved",
    "ConversationObserved",
    "ChatRowDetected",
    "MessageObjectDetected",
    "WindowStateObserved",
    "CycleStarted",
    "CycleCheckpointCreated",
    "CycleBlocked",
    "CycleRecovered",
    "PolicyViolationDetected",
    "PrivacyRiskDetected",
    "EvaluationFindingCreated",
}
IGNORED_TYPES = {"GroupDetected", "LowLearningValueDetected"}
BUSINESS_TYPES = {
    "CustomerCandidateIdentified",
    "CustomerIdentified",
    "ProviderCandidateIdentified",
    "ServiceDetected",
    "DeviceDetected",
    "ProcedureCandidateCreated",
    "ProcedureRiskAssessed",
    "ToolCapabilityObserved",
    "ToolActionRequested",
    "ToolActionVerified",
    "QuoteRequested",
    "MarketSignalDetected",
    "QuoteProposed",
    "QuoteRecorded",
    "OfferDetected",
    "DemandPatternDetected",
    "PaymentDrafted",
    "PaymentEvidenceAttached",
    "PaymentConfirmed",
    "DebtDetected",
    "DebtUpdated",
    "RefundCandidate",
    "AccountingCorrectionReceived",
    "DecisionRequested",
    "DecisionExplained",
    "DecisionApproved",
    "DecisionRejected",
    "HumanApprovalRequired",
    "ActionRequested",
    "ActionExecuted",
    "ActionVerified",
    "ActionFailed",
    "ActionBlocked",
    "LearningCandidateCreated",
    "HumanCorrectionReceived",
    "HumanApprovalGranted",
    "HumanApprovalRejected",
    "OperatorOverrideRecorded",
    "OperatorNoteAdded",
    *CASE_EVENT_TYPES,
}
ACCOUNTING_TYPES = {
    "PaymentDrafted",
    "PaymentEvidenceAttached",
    "PaymentConfirmed",
    "DebtDetected",
    "DebtUpdated",
    "RefundCandidate",
    "AccountingCorrectionReceived",
}
PRICE_TYPES = {
    "QuoteRequested",
    "QuoteProposed",
    "QuoteRecorded",
    "QuoteApproved",
    "QuoteRejected",
    "OfferDetected",
    "DemandPatternDetected",
}
SERVICE_TYPES = {
    "ServiceDetected",
    "DeviceDetected",
    "ProcedureCandidateCreated",
    "ProcedureRiskAssessed",
    "ToolCapabilityObserved",
    "ToolActionRequested",
    "ToolActionVerified",
}
ACTION_TYPES = {"ActionRequested", "ActionExecuted", "ActionVerified", "ActionFailed", "ActionBlocked"}
HUMAN_TYPES = {
    "HumanApprovalRequired",
    "HumanCorrectionReceived",
    "HumanApprovalGranted",
    "HumanApprovalRejected",
    "OperatorOverrideRecorded",
    "OperatorNoteAdded",
}


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def stable_hash(value: str, length: int = 20) -> str:
    return hashlib.sha1(value.encode("utf-8")).hexdigest()[:length]


def compact_json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def clamp(value: Any, default: float = 0.5) -> float:
    try:
        number = float(value)
    except (TypeError, ValueError):
        number = default
    return max(0.0, min(1.0, number))


def read_jsonl_events(path: Path, limit: int) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    try:
        lines = [line for line in path.read_text(encoding="utf-8-sig", errors="replace").splitlines() if line.strip()]
    except OSError:
        return []
    events: list[dict[str, Any]] = []
    for line in lines[-limit:]:
        try:
            item = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(item, dict):
            events.append(item)
    return events


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    temp.replace(path)


def append_jsonl(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")


def as_list_json(value: str | None) -> list[Any]:
    if not value:
        return []
    try:
        parsed = json.loads(value)
    except json.JSONDecodeError:
        return []
    return parsed if isinstance(parsed, list) else []


def merge_unique(existing_json: str | None, values: list[str]) -> str:
    existing = [clean_text(item) for item in as_list_json(existing_json)]
    merged = [item for item in existing if item]
    for value in values:
        text = clean_text(value)
        if text and text not in merged:
            merged.append(text)
    return json.dumps(merged[-80:], ensure_ascii=False, separators=(",", ":"))


def event_type(event: dict[str, Any]) -> str:
    return clean_text(event.get("eventType"))


def event_id(event: dict[str, Any]) -> str:
    return clean_text(event.get("eventId")) or f"event-{stable_hash(compact_json(event), 24)}"


def event_data(event: dict[str, Any]) -> dict[str, Any]:
    data = event.get("data")
    return data if isinstance(data, dict) else {}


def event_risk(event: dict[str, Any]) -> str:
    risk = event.get("risk")
    if isinstance(risk, dict):
        return clean_text(risk.get("riskLevel"),)
    return "medium"


def event_category(kind: str) -> str:
    if kind in ACCOUNTING_TYPES:
        return "accounting"
    if kind in PRICE_TYPES:
        return "pricing"
    if kind in SERVICE_TYPES:
        return "service"
    if kind in ACTION_TYPES:
        return "action"
    if kind in HUMAN_TYPES:
        return "human"
    if kind in CASE_EVENT_TYPES:
        return "case"
    if kind.startswith("Customer") or kind.startswith("Provider"):
        return "identity"
    if kind.startswith("Learning"):
        return "learning"
    return "business"


def derive_case_id(event: dict[str, Any]) -> str | None:
    value = clean_text(event.get("caseId"))
    if value:
        return value
    data = event_data(event)
    value = clean_text(data.get("caseId"))
    if value:
        return value
    target = data.get("target") if isinstance(data.get("target"), dict) else {}
    for field in ("caseId", "conversationId", "customerId"):
        value = clean_text(target.get(field))
        if value:
            return f"case-{stable_hash(value, 16)}" if field != "caseId" else value
    conversation_id = clean_text(event.get("conversationId") or data.get("conversationId"))
    channel_id = clean_text(event.get("channelId") or data.get("channelId"))
    if conversation_id:
        return f"case-{stable_hash(channel_id + '|' + conversation_id, 16)}"
    customer_id = clean_text(event.get("customerId") or data.get("customerId"))
    if customer_id:
        return f"case-{stable_hash(customer_id, 16)}"
    if event_type(event) in BUSINESS_TYPES:
        return f"case-{stable_hash(event_id(event), 16)}"
    return None


def conversation_id(event: dict[str, Any]) -> str:
    data = event_data(event)
    target = data.get("target") if isinstance(data.get("target"), dict) else {}
    return clean_text(event.get("conversationId") or data.get("conversationId") or target.get("conversationId"))


def channel_id(event: dict[str, Any]) -> str:
    data = event_data(event)
    target = data.get("target") if isinstance(data.get("target"), dict) else {}
    return clean_text(event.get("channelId") or data.get("channelId") or target.get("channelId"))


def customer_id(event: dict[str, Any]) -> str:
    data = event_data(event)
    target = data.get("target") if isinstance(data.get("target"), dict) else {}
    return clean_text(event.get("customerId") or data.get("customerId") or target.get("customerId") or "customer_pending")


def case_title(event: dict[str, Any], fallback: str) -> str:
    data = event_data(event)
    subject = event.get("subject") if isinstance(event.get("subject"), dict) else {}
    return clean_text(
        data.get("conversationTitle")
        or data.get("clientName")
        or data.get("signalValue")
        or subject.get("id")
        or conversation_id(event)
        or fallback
    )[:180]


def field_from_data(event: dict[str, Any], *names: str) -> str:
    data = event_data(event)
    for name in names:
        value = clean_text(data.get(name))
        if value:
            return value
    return ""


def event_summary(event: dict[str, Any]) -> str:
    evidence = event.get("evidence") if isinstance(event.get("evidence"), list) else []
    if evidence and isinstance(evidence[0], dict):
        value = clean_text(evidence[0].get("summary"))
        if value:
            return value
    data = event_data(event)
    for field in ("reasoningSummary", "summary", "reason", "signalValue", "conversationTitle"):
        value = clean_text(data.get(field))
        if value:
            return value
    return f"{event_type(event)} recibido."


def is_case_manager_event(event: dict[str, Any]) -> bool:
    data = event_data(event)
    return event_type(event) in CASE_EVENT_TYPES and bool(data.get("caseManagerEvent"))


def should_skip_event(event: dict[str, Any]) -> bool:
    kind = event_type(event)
    if is_case_manager_event(event):
        return True
    if kind in LOW_LEVEL_TYPES:
        return True
    return kind not in BUSINESS_TYPES and kind not in IGNORED_TYPES


def should_project_ignored(event: dict[str, Any]) -> bool:
    return event_type(event) in IGNORED_TYPES


def status_for_event(kind: str, current: str) -> str:
    if kind == "CaseClosed":
        return "closed"
    if kind in IGNORED_TYPES:
        return "ignored"
    if kind in {"ActionBlocked", "ActionFailed"}:
        return "blocked"
    if kind in {"HumanApprovalRequired", "HumanCorrectionReceived", "OperatorOverrideRecorded"}:
        return "needs_human"
    if kind in {"PaymentDrafted", "PaymentEvidenceAttached", "DebtDetected", "RefundCandidate", "AccountingCorrectionReceived"}:
        return "accounting_review"
    if kind in {"PaymentConfirmed", "DebtUpdated"}:
        return "in_progress"
    if kind in {"QuoteRequested", "QuoteProposed", "QuoteRecorded"}:
        return "needs_quote"
    if kind in {"ServiceDetected", "DeviceDetected", "ProcedureCandidateCreated", "ProcedureRiskAssessed"}:
        return "in_progress"
    if kind in {"ActionExecuted", "ActionVerified"}:
        return "in_progress"
    if current in {"", "new"}:
        return "open"
    return current


def payment_state_for_event(kind: str, current: str) -> str:
    if kind in {"PaymentDrafted", "PaymentEvidenceAttached"}:
        return "draft_needs_evidence"
    if kind == "PaymentConfirmed":
        return "confirmed"
    if kind in {"DebtDetected", "DebtUpdated"}:
        return "debt_review"
    if kind == "RefundCandidate":
        return "refund_review"
    return current


def quote_state_for_event(kind: str, current: str) -> str:
    if kind == "QuoteRequested":
        return "requested"
    if kind == "QuoteProposed":
        return "proposed_needs_approval"
    if kind == "QuoteRecorded":
        return "recorded"
    if kind == "QuoteApproved":
        return "approved"
    if kind == "QuoteRejected":
        return "rejected"
    return current


def priority_delta(event: dict[str, Any]) -> int:
    kind = event_type(event)
    score = 0
    if kind in {"HumanApprovalRequired", "ActionFailed", "ActionBlocked"}:
        score += 40
    if kind in ACCOUNTING_TYPES:
        score += 35
    if kind in PRICE_TYPES:
        score += 25
    if kind in SERVICE_TYPES:
        score += 20
    if event.get("requiresHumanReview"):
        score += 20
    risk = event_risk(event)
    if risk == "critical":
        score += 60
    elif risk == "high":
        score += 35
    elif risk == "medium":
        score += 15
    score += int(clamp(event.get("confidence"), 0.5) * 10)
    return score


def priority_label(score: int) -> str:
    if score >= 90:
        return "urgent"
    if score >= 55:
        return "high"
    if score >= 25:
        return "normal"
    return "low"


def next_action_for(kind: str, status: str) -> str:
    if status == "blocked":
        return "Pedir ayuda a Bryams antes de continuar."
    if status == "needs_human":
        return "Mostrar el caso a Bryams para contexto o aprobacion."
    if kind in {"PaymentDrafted", "PaymentEvidenceAttached", "DebtDetected", "RefundCandidate"}:
        return "Revisar evidencia contable antes de confirmar."
    if kind in {"QuoteRequested", "QuoteProposed", "QuoteRecorded"}:
        return "Preparar respuesta de precio y pedir aprobacion si cambia dinero."
    if kind in {"ServiceDetected", "DeviceDetected"}:
        return "Completar modelo, pais, servicio y estado del cliente."
    if kind in ACTION_TYPES:
        return "Verificar que la mano abrio el chat correcto."
    return "Seguir leyendo la conversacion y unir evidencia."


class CaseManagerStore:
    def __init__(self, db_path: Path, case_events_file: Path):
        self.db_path = db_path
        self.case_events_file = case_events_file
        self.db_path.parent.mkdir(parents=True, exist_ok=True)
        self.case_events_file.parent.mkdir(parents=True, exist_ok=True)
        self.conn = sqlite3.connect(self.db_path)
        self.conn.row_factory = sqlite3.Row
        self.init_schema()

    def close(self) -> None:
        self.conn.close()

    def init_schema(self) -> None:
        self.conn.executescript(
            """
            create table if not exists case_manager_processed_events (
              source_event_id text primary key,
              processed_at text not null
            );
            create table if not exists cases (
              case_id text primary key,
              status text not null,
              priority text not null,
              priority_score integer not null,
              primary_channel_id text,
              customer_id text,
              title text,
              country text,
              service text,
              device text,
              intent text,
              risk_level text,
              confidence real not null,
              requires_human integer not null,
              payment_state text,
              quote_state text,
              next_action text,
              summary text,
              channels_json text not null,
              conversations_json text not null,
              linked_event_ids_json text not null,
              event_count integer not null,
              accounting_count integer not null,
              action_count integer not null,
              learning_count integer not null,
              created_at text not null,
              updated_at text not null,
              last_event_id text,
              last_event_type text
            );
            create table if not exists case_event_links (
              link_id text primary key,
              case_id text not null,
              event_id text not null,
              event_type text not null,
              category text not null,
              source_domain text,
              created_at text not null
            );
            create table if not exists case_manager_emitted_events (
              event_id text primary key,
              idempotency_key text not null unique,
              event_type text not null,
              case_id text not null,
              source_event_id text not null,
              created_at text not null
            );
            create index if not exists idx_cases_status on cases(status);
            create index if not exists idx_cases_customer on cases(customer_id);
            create index if not exists idx_case_links_case on case_event_links(case_id);
            """
        )
        self.conn.commit()

    def processed(self, source_id: str) -> bool:
        row = self.conn.execute(
            "select source_event_id from case_manager_processed_events where source_event_id = ? limit 1",
            (source_id,),
        ).fetchone()
        return row is not None

    def mark_processed(self, source_id: str) -> None:
        self.conn.execute(
            "insert or ignore into case_manager_processed_events(source_event_id, processed_at) values (?, ?)",
            (source_id, utc_now()),
        )

    def get_case(self, case_id: str) -> dict[str, Any] | None:
        row = self.conn.execute("select * from cases where case_id = ? limit 1", (case_id,)).fetchone()
        return dict(row) if row is not None else None

    def save_case(self, row: dict[str, Any]) -> None:
        columns = [
            "case_id",
            "status",
            "priority",
            "priority_score",
            "primary_channel_id",
            "customer_id",
            "title",
            "country",
            "service",
            "device",
            "intent",
            "risk_level",
            "confidence",
            "requires_human",
            "payment_state",
            "quote_state",
            "next_action",
            "summary",
            "channels_json",
            "conversations_json",
            "linked_event_ids_json",
            "event_count",
            "accounting_count",
            "action_count",
            "learning_count",
            "created_at",
            "updated_at",
            "last_event_id",
            "last_event_type",
        ]
        placeholders = ",".join("?" for _ in columns)
        update = ",".join(f"{column}=excluded.{column}" for column in columns[1:])
        values = [row.get(column) for column in columns]
        self.conn.execute(
            f"insert into cases ({','.join(columns)}) values ({placeholders}) "
            f"on conflict(case_id) do update set {update}",
            values,
        )

    def link_event(self, case_id: str, event: dict[str, Any]) -> None:
        source_id = event_id(event)
        link_id = f"link-{stable_hash(case_id + '|' + source_id, 24)}"
        self.conn.execute(
            """
            insert or ignore into case_event_links(
              link_id, case_id, event_id, event_type, category, source_domain, created_at
            ) values (?, ?, ?, ?, ?, ?, ?)
            """,
            (
                link_id,
                case_id,
                source_id,
                event_type(event),
                event_category(event_type(event)),
                clean_text(event.get("sourceDomain")),
                utc_now(),
            ),
        )

    def emit_case_event(self, case_event: dict[str, Any], source_event_id: str) -> bool:
        errors = validate_contract(case_event, "domain_event")
        if errors:
            raise ValueError("; ".join(errors))
        row = self.conn.execute(
            "select event_id from case_manager_emitted_events where idempotency_key = ? limit 1",
            (case_event["idempotencyKey"],),
        ).fetchone()
        if row is not None:
            return False
        self.conn.execute(
            """
            insert into case_manager_emitted_events(
              event_id, idempotency_key, event_type, case_id, source_event_id, created_at
            ) values (?, ?, ?, ?, ?, ?)
            """,
            (
                case_event["eventId"],
                case_event["idempotencyKey"],
                case_event["eventType"],
                clean_text(case_event.get("caseId")),
                source_event_id,
                utc_now(),
            ),
        )
        append_jsonl(self.case_events_file, case_event)
        return True

    def summary(self) -> dict[str, Any]:
        def scalar(sql: str) -> int:
            row = self.conn.execute(sql).fetchone()
            return int(row[0] if row else 0)

        def counter(sql: str) -> dict[str, int]:
            return {str(row[0]): int(row[1]) for row in self.conn.execute(sql).fetchall()}

        latest_rows = self.conn.execute(
            """
            select case_id, status, priority, title, customer_id, primary_channel_id,
                   requires_human, next_action, event_count, updated_at
            from cases
            where status not in ('ignored', 'closed')
            order by priority_score desc, updated_at desc
            limit 8
            """
        ).fetchall()
        ignored_rows = self.conn.execute(
            """
            select case_id, title, primary_channel_id, updated_at
            from cases
            where status = 'ignored'
            order by updated_at desc
            limit 6
            """
        ).fetchall()
        return {
            "processedEvents": scalar("select count(*) from case_manager_processed_events"),
            "eventsRead": scalar("select count(*) from case_event_links"),
            "cases": scalar("select count(*) from cases"),
            "openCases": scalar("select count(*) from cases where status not in ('closed', 'ignored')"),
            "needsHuman": scalar("select count(*) from cases where requires_human = 1 and status not in ('closed', 'ignored')"),
            "ignoredCases": scalar("select count(*) from cases where status = 'ignored'"),
            "linkedEvents": scalar("select count(*) from case_event_links"),
            "emittedCaseEvents": scalar("select count(*) from case_manager_emitted_events"),
            "accountingCases": scalar("select count(*) from cases where accounting_count > 0 and status != 'ignored'"),
            "priceCases": scalar("select count(*) from cases where quote_state != '' and status != 'ignored'"),
            "serviceCases": scalar("select count(*) from cases where service != '' or device != ''"),
            "byStatus": counter("select status, count(*) from cases group by status order by status"),
            "byPriority": counter("select priority, count(*) from cases group by priority order by priority"),
            "latestOpen": [dict(row) for row in latest_rows],
            "ignoredSamples": [dict(row) for row in ignored_rows],
        }


def new_case_row(case_id: str, event: dict[str, Any], ignored: bool = False) -> dict[str, Any]:
    now = utc_now()
    title = case_title(event, case_id)
    status = "ignored" if ignored else status_for_event(event_type(event), "open")
    score = 0 if ignored else priority_delta(event)
    return {
        "case_id": case_id,
        "status": status,
        "priority": priority_label(score),
        "priority_score": score,
        "primary_channel_id": channel_id(event),
        "customer_id": customer_id(event),
        "title": title,
        "country": field_from_data(event, "country", "market", "signalValue") if event_type(event) == "MarketSignalDetected" else "",
        "service": field_from_data(event, "service", "signalValue") if event_type(event) == "ServiceDetected" else "",
        "device": field_from_data(event, "device", "model", "signalValue") if event_type(event) == "DeviceDetected" else "",
        "intent": field_from_data(event, "intent", "signalKind"),
        "risk_level": event_risk(event) or "medium",
        "confidence": clamp(event.get("confidence"), 0.5),
        "requires_human": 1 if (event.get("requiresHumanReview") and not ignored) else 0,
        "payment_state": payment_state_for_event(event_type(event), ""),
        "quote_state": quote_state_for_event(event_type(event), ""),
        "next_action": "Ignorar como canal de bajo aprendizaje." if ignored else next_action_for(event_type(event), status),
        "summary": event_summary(event),
        "channels_json": json.dumps([channel_id(event)] if channel_id(event) else [], ensure_ascii=False, separators=(",", ":")),
        "conversations_json": json.dumps([conversation_id(event)] if conversation_id(event) else [], ensure_ascii=False, separators=(",", ":")),
        "linked_event_ids_json": json.dumps([], ensure_ascii=False, separators=(",", ":")),
        "event_count": 0,
        "accounting_count": 0,
        "action_count": 0,
        "learning_count": 0,
        "created_at": now,
        "updated_at": now,
        "last_event_id": "",
        "last_event_type": "",
    }


def update_case_row(row: dict[str, Any], event: dict[str, Any], ignored: bool = False) -> dict[str, Any]:
    kind = event_type(event)
    category = event_category(kind)
    existing_status = clean_text(row.get("status")) or "open"
    status = "ignored" if ignored else status_for_event(kind, existing_status)
    score = int(row.get("priority_score") or 0)
    if not ignored:
        score = min(100, score + priority_delta(event))

    data = event_data(event)
    row["status"] = status
    row["priority_score"] = score
    row["priority"] = priority_label(score)
    row["primary_channel_id"] = row.get("primary_channel_id") or channel_id(event)
    row["customer_id"] = customer_id(event) if customer_id(event) != "customer_pending" else row.get("customer_id") or "customer_pending"
    row["title"] = row.get("title") or case_title(event, clean_text(row.get("case_id")))
    row["country"] = row.get("country") or (
        field_from_data(event, "country", "market", "signalValue") if kind == "MarketSignalDetected" else ""
    )
    if kind == "ServiceDetected":
        row["service"] = clean_text(data.get("signalValue") or data.get("service") or row.get("service"))
    if kind == "DeviceDetected":
        row["device"] = clean_text(data.get("signalValue") or data.get("device") or data.get("model") or row.get("device"))
    row["intent"] = clean_text(data.get("intent") or data.get("signalKind") or row.get("intent"))
    row["risk_level"] = event_risk(event) or row.get("risk_level") or "medium"
    row["confidence"] = max(float(row.get("confidence") or 0.0), clamp(event.get("confidence"), 0.5))
    row["requires_human"] = 0 if ignored else 1 if bool(row.get("requires_human")) or bool(event.get("requiresHumanReview")) or kind in HUMAN_TYPES else 0
    row["payment_state"] = payment_state_for_event(kind, clean_text(row.get("payment_state")))
    row["quote_state"] = quote_state_for_event(kind, clean_text(row.get("quote_state")))
    row["next_action"] = "Ignorar como canal de bajo aprendizaje." if ignored else next_action_for(kind, status)
    row["summary"] = event_summary(event)
    row["channels_json"] = merge_unique(clean_text(row.get("channels_json")), [channel_id(event)])
    row["conversations_json"] = merge_unique(clean_text(row.get("conversations_json")), [conversation_id(event)])
    row["linked_event_ids_json"] = merge_unique(clean_text(row.get("linked_event_ids_json")), [event_id(event)])
    row["event_count"] = int(row.get("event_count") or 0) + 1
    row["accounting_count"] = int(row.get("accounting_count") or 0) + (1 if category == "accounting" else 0)
    row["action_count"] = int(row.get("action_count") or 0) + (1 if category == "action" else 0)
    row["learning_count"] = int(row.get("learning_count") or 0) + (1 if category == "learning" else 0)
    row["updated_at"] = utc_now()
    row["last_event_id"] = event_id(event)
    row["last_event_type"] = kind
    return row


def case_snapshot(row: dict[str, Any]) -> dict[str, Any]:
    return {
        "caseId": clean_text(row.get("case_id")),
        "status": clean_text(row.get("status")),
        "priority": clean_text(row.get("priority")),
        "priorityScore": int(row.get("priority_score") or 0),
        "title": clean_text(row.get("title")),
        "customerId": clean_text(row.get("customer_id")),
        "primaryChannelId": clean_text(row.get("primary_channel_id")),
        "channels": as_list_json(clean_text(row.get("channels_json"))),
        "conversations": as_list_json(clean_text(row.get("conversations_json"))),
        "country": clean_text(row.get("country")),
        "service": clean_text(row.get("service")),
        "device": clean_text(row.get("device")),
        "intent": clean_text(row.get("intent")),
        "paymentState": clean_text(row.get("payment_state")),
        "quoteState": clean_text(row.get("quote_state")),
        "riskLevel": clean_text(row.get("risk_level")),
        "confidence": float(row.get("confidence") or 0.0),
        "requiresHuman": bool(row.get("requires_human")),
        "nextAction": clean_text(row.get("next_action")),
        "summary": clean_text(row.get("summary")),
        "eventCount": int(row.get("event_count") or 0),
        "accountingCount": int(row.get("accounting_count") or 0),
        "actionCount": int(row.get("action_count") or 0),
        "learningCount": int(row.get("learning_count") or 0),
        "createdAt": clean_text(row.get("created_at")),
        "updatedAt": clean_text(row.get("updated_at")),
        "lastEventId": clean_text(row.get("last_event_id")),
        "lastEventType": clean_text(row.get("last_event_type")),
    }


def make_case_domain_event(kind: str, source_event: dict[str, Any], row: dict[str, Any]) -> dict[str, Any]:
    snapshot = case_snapshot(row)
    data = {
        "caseManagerEvent": True,
        "case": snapshot,
        "caseId": snapshot["caseId"],
        "status": snapshot["status"],
        "priority": snapshot["priority"],
        "nextAction": snapshot["nextAction"],
        "sourceDomainEventId": event_id(source_event),
        "sourceDomainEventType": event_type(source_event),
    }
    if kind == "CaseNeedsHumanContext":
        summary = f"Caso necesita a Bryams: {snapshot['title']} - {snapshot['nextAction']}"
    elif kind == "CaseClosed":
        summary = f"Caso cerrado: {snapshot['title']}"
    elif kind == "CaseOpened":
        summary = f"Caso abierto: {snapshot['title']}"
    else:
        summary = f"Caso actualizado: {snapshot['title']} -> {snapshot['status']}"

    event = make_domain_event(
        kind,
        {
            **source_event,
            "caseId": snapshot["caseId"],
            "customerId": snapshot["customerId"],
            "channelId": snapshot["primaryChannelId"],
            "conversationId": (snapshot["conversations"] or [None])[0],
        },
        subject_type="case",
        subject_id=snapshot["caseId"],
        data=data,
        confidence=max(0.55, snapshot["confidence"]),
        summary=summary,
        source_domain="OperatingCore",
        autonomy_level=3,
        limitations=["Case Manager is a projection; source domain events remain the audit truth."],
    )
    raw_key = compact_json(
        {
            "kind": kind,
            "caseId": snapshot["caseId"],
            "sourceEventId": event_id(source_event),
            "status": snapshot["status"],
        }
    )
    event["eventId"] = f"caseevt-{stable_hash(raw_key, 24)}"
    event["idempotencyKey"] = f"{kind}:{stable_hash(raw_key, 32)}"
    event["correlationId"] = snapshot["caseId"]
    event["caseId"] = snapshot["caseId"]
    event["customerId"] = snapshot["customerId"]
    event["channelId"] = snapshot["primaryChannelId"] or None
    event["conversationId"] = (snapshot["conversations"] or [None])[0]
    event["causationId"] = event_id(source_event)
    event["data"]["sourceEventType"] = "domain_event"
    event["data"]["sourceEventId"] = event_id(source_event)
    if kind in {"CaseNeedsHumanContext", "CaseClosed"}:
        event["requiresHumanReview"] = True
    return event


def process_event(store: CaseManagerStore, event: dict[str, Any]) -> tuple[str, list[dict[str, Any]], bool]:
    source_id = event_id(event)
    if store.processed(source_id):
        return "duplicate", [], False
    errors = validate_contract(event, "domain_event")
    if errors:
        with store.conn:
            store.mark_processed(source_id)
        return "skipped", [], False
    if should_skip_event(event):
        with store.conn:
            store.mark_processed(source_id)
        return "skipped", [], False

    ignored = should_project_ignored(event)
    case_id = derive_case_id(event)
    if not case_id:
        with store.conn:
            store.mark_processed(source_id)
        return "skipped", [], False

    with store.conn:
        existing = store.get_case(case_id)
        created = existing is None
        row = new_case_row(case_id, event, ignored=ignored) if created else existing
        row = update_case_row(row, event, ignored=ignored)
        store.save_case(row)
        store.link_event(case_id, event)
        store.mark_processed(source_id)

        emitted: list[dict[str, Any]] = []
        if not ignored:
            emit_kinds = ["CaseOpened"] if created else ["CaseUpdated"]
            if row["requires_human"] and event_type(event) not in {"HumanApprovalGranted", "HumanApprovalRejected"}:
                emit_kinds.append("CaseNeedsHumanContext")
            if row["status"] == "closed":
                emit_kinds.append("CaseClosed")
            for kind in emit_kinds:
                case_event = make_case_domain_event(kind, event, row)
                if store.emit_case_event(case_event, source_id):
                    emitted.append(case_event)
        return ("created" if created else "updated"), emitted, created


def build_human_report(summary: dict[str, Any]) -> dict[str, Any]:
    latest = summary.get("latestOpen") if isinstance(summary.get("latestOpen"), list) else []
    needs = [item for item in latest if isinstance(item, dict) and item.get("requires_human")]
    proximas = [
        {
            "caseId": item.get("case_id"),
            "title": item.get("title"),
            "action": item.get("next_action"),
            "priority": item.get("priority"),
        }
        for item in latest
        if isinstance(item, dict)
    ][:8]
    risks: list[str] = []
    if int(summary.get("needsHuman") or 0) > 0:
        risks.append("Hay casos que no deben avanzar sin Bryams.")
    if int(summary.get("accountingCases") or 0) > 0:
        risks.append("Contabilidad queda como borrador hasta tener evidencia fuerte.")
    if int(summary.get("ignoredCases") or 0) > 0:
        risks.append("Grupos de pagos se auditan pero no entrenan clientes directamente.")
    return {
        "quePaso": (
            f"Case Manager tiene {summary.get('openCases', 0)} casos abiertos, "
            f"{summary.get('needsHuman', 0)} necesitan a Bryams y "
            f"{summary.get('ignoredCases', 0)} canales fueron ignorados por bajo aprendizaje."
        ),
        "casosAbiertos": latest,
        "necesitanBryams": needs[:8],
        "proximasAcciones": proximas,
        "riesgos": risks,
    }


def run_case_manager_once(
    domain_events_file: Path,
    case_events_file: Path,
    state_file: Path,
    db_path: Path,
    report_file: Path | None = None,
    limit: int = 500,
) -> dict[str, Any]:
    store = CaseManagerStore(db_path, case_events_file)
    ingested = {"events": 0, "duplicates": 0, "skipped": 0, "casesCreated": 0, "casesUpdated": 0, "caseEvents": 0}
    emitted_counts: Counter[str] = Counter()
    try:
        events = read_jsonl_events(domain_events_file, max(1, limit))
        for event in events:
            ingested["events"] += 1
            result, emitted, created = process_event(store, event)
            if result == "duplicate":
                ingested["duplicates"] += 1
            elif result == "skipped":
                ingested["skipped"] += 1
            elif result == "created":
                ingested["casesCreated"] += 1
            elif result == "updated":
                ingested["casesUpdated"] += 1
            ingested["caseEvents"] += len(emitted)
            emitted_counts.update(item["eventType"] for item in emitted)

        summary = store.summary()
        status = "idle" if ingested["events"] == 0 else "ok"
        if summary.get("needsHuman", 0):
            status = "attention"
        human_report = build_human_report(summary)
        state = {
            "status": status,
            "engine": "ariadgsm_case_manager",
            "version": SCHEMA_VERSION,
            "updatedAt": utc_now(),
            "domainEventsFile": str(domain_events_file),
            "caseEventsFile": str(case_events_file),
            "db": str(db_path),
            "ingested": ingested,
            "summary": {**summary, "emittedByType": dict(emitted_counts)},
            "humanReport": human_report,
        }
        errors = validate_contract(state, "case_manager_state")
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
    parser = argparse.ArgumentParser(description="AriadGSM Case Manager")
    parser.add_argument("--domain-events", default="runtime/domain-events.jsonl")
    parser.add_argument("--case-events", default="runtime/case-events.jsonl")
    parser.add_argument("--state-file", default="runtime/case-manager-state.json")
    parser.add_argument("--report-file", default="runtime/case-manager-report.json")
    parser.add_argument("--db", default="runtime/case-manager.sqlite")
    parser.add_argument("--limit", type=int, default=500)
    parser.add_argument("--json", action="store_true")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    args = parse_args(argv)
    state = run_case_manager_once(
        resolve_runtime_path(args.domain_events),
        resolve_runtime_path(args.case_events),
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
            "AriadGSM Case Manager: "
            f"cases={summary['cases']} "
            f"open={summary['openCases']} "
            f"needs_human={summary['needsHuman']} "
            f"ignored={summary['ignoredCases']} "
            f"emitted={summary['emittedCaseEvents']}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
