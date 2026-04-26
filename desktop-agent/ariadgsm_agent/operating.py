from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
import sys
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from .classifier import Decision, classify_messages
from .contracts import validate_contract


AGENT_ROOT = Path(__file__).resolve().parents[1]

PRIORITY_ORDER = {
    "customer_waiting": 10,
    "accounting_risk": 9,
    "price_request": 8,
    "active_case": 7,
    "service_context": 6,
    "conversation_context": 4,
    "history_learning": 3,
    "ignored_group": 1,
}

IGNORED_GROUP_TITLES = (
    "pagos mexico",
    "pagos méxico",
    "pagos chile",
    "pagos colombia",
)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def stable_hash(value: str, length: int = 16) -> str:
    return hashlib.sha1(value.encode("utf-8")).hexdigest()[:length]


def clean_text(value: Any) -> str:
    return " ".join(str(value or "").split()).strip()


def evidence_from_messages(messages: list[dict[str, Any]], limit: int = 5) -> list[str]:
    evidence: list[str] = []
    for message in messages[-limit:]:
        if not isinstance(message, dict):
            continue
        evidence.append(str(message.get("messageId") or message.get("text") or ""))
    return [item for item in evidence if item]


@dataclass(order=True)
class WorkItem:
    sort_priority: int
    kind: str = field(compare=False)
    channel_id: str = field(compare=False)
    conversation_id: str = field(compare=False)
    summary: str = field(compare=False)


def make_work_item(kind: str, channel_id: str, conversation_id: str, summary: str) -> WorkItem:
    return WorkItem(
        sort_priority=-PRIORITY_ORDER.get(kind, 5),
        kind=kind,
        channel_id=channel_id,
        conversation_id=conversation_id,
        summary=summary,
    )


@dataclass(frozen=True)
class BusinessCase:
    case_id: str
    channel_id: str
    conversation_id: str
    title: str
    intent: str
    status: str
    priority: str
    confidence: float
    last_message: str
    updated_at: str
    evidence: list[str]


@dataclass(frozen=True)
class OperatingTask:
    task_id: str
    case_id: str
    kind: str
    status: str
    priority_score: int
    summary: str
    proposed_action: str
    evidence: list[str]
    created_at: str


@dataclass(frozen=True)
class OperatingUpdate:
    case: BusinessCase
    tasks: list[OperatingTask]
    work_items: list[WorkItem]
    decision_event: dict[str, Any] | None
    ignored: bool = False

    def to_dict(self) -> dict[str, Any]:
        return {
            "case": asdict(self.case),
            "tasks": [asdict(task) for task in self.tasks],
            "workItems": [asdict(item) for item in self.work_items],
            "decisionEvent": self.decision_event,
            "ignored": self.ignored,
        }


def is_ignored_group(conversation_event: dict[str, Any]) -> bool:
    haystack = " ".join(
        [
            clean_text(conversation_event.get("conversationTitle")).lower(),
            clean_text(conversation_event.get("conversationId")).lower(),
        ]
    )
    return any(title in haystack for title in IGNORED_GROUP_TITLES)


def priority_kind_for_decision(decision: Decision, conversation_event: dict[str, Any]) -> str:
    if decision.intent in {"accounting_payment", "accounting_debt"}:
        return "accounting_risk"
    if decision.intent == "price_request":
        return "price_request"
    if decision.intent == "service_context":
        return "active_case"
    messages = conversation_event.get("messages") or []
    last_direction = clean_text((messages[-1] or {}).get("direction") if messages else "")
    if last_direction in {"client", "unknown"}:
        return "customer_waiting"
    return "conversation_context"


def proposed_action_for_decision(decision: Decision) -> str:
    return {
        "accounting_payment": "create_payment_review_task",
        "accounting_debt": "create_debt_followup_task",
        "price_request": "prepare_price_response",
        "service_context": "review_service_case",
        "conversation_context": "keep_observing",
        "no_signal": "keep_observing",
    }.get(decision.intent, "review_case")


def confidence_for_decision(decision: Decision) -> float:
    return min(0.95, max(0.35, decision.score / 10.0))


def case_status_for_decision(decision: Decision, ignored: bool) -> str:
    if ignored:
        return "ignored"
    if decision.intent in {"no_signal", "conversation_context"}:
        return "observing"
    return "open"


class OperatingCore:
    def process_conversation(self, conversation_event: dict[str, Any], autonomy_level: int = 1) -> OperatingUpdate:
        messages = [message for message in conversation_event.get("messages") or [] if isinstance(message, dict)]
        ignored = is_ignored_group(conversation_event)
        decision = classify_messages(messages)
        if ignored:
            decision = Decision(
                status="ignored",
                intent="ignored_group",
                label="Grupo ignorado",
                priority="baja",
                score=0,
                text="",
                reasons=["group_title"],
            )

        channel_id = clean_text(conversation_event.get("channelId") or "unknown")
        conversation_id = clean_text(conversation_event.get("conversationId") or f"{channel_id}-unknown")
        title = clean_text(conversation_event.get("conversationTitle") or conversation_id)
        evidence = evidence_from_messages(messages)
        priority_kind = "ignored_group" if ignored else priority_kind_for_decision(decision, conversation_event)
        confidence = confidence_for_decision(decision)
        now = utc_now()
        last_message = clean_text((messages[-1] or {}).get("text") if messages else decision.text)
        case = BusinessCase(
            case_id=f"case-{stable_hash(channel_id + '|' + conversation_id)}",
            channel_id=channel_id,
            conversation_id=conversation_id,
            title=title,
            intent=decision.intent,
            status=case_status_for_decision(decision, ignored),
            priority=priority_kind,
            confidence=confidence,
            last_message=last_message,
            updated_at=now,
            evidence=evidence,
        )
        tasks = [] if ignored else self._tasks_for_case(case, decision, evidence, now)
        work_items = [
            make_work_item(task.kind, case.channel_id, case.conversation_id, task.summary)
            for task in tasks
        ]
        decision_event = None if ignored else self._decision_event(case, decision, tasks, autonomy_level, evidence, now)
        return OperatingUpdate(case, tasks, work_items, decision_event, ignored=ignored)

    def _tasks_for_case(self, case: BusinessCase, decision: Decision, evidence: list[str], now: str) -> list[OperatingTask]:
        task_kind = case.priority
        summary = {
            "accounting_payment": "Revisar posible pago o comprobante del cliente.",
            "accounting_debt": "Revisar posible deuda, saldo o reembolso.",
            "price_request": "Preparar respuesta de precio con servicio y país correctos.",
            "service_context": "Revisar servicio/equipo mencionado y mantener caso activo.",
            "conversation_context": "Observar conversación y esperar señal de negocio.",
            "no_signal": "Observar conversación y esperar señal de negocio.",
        }.get(decision.intent, "Revisar caso operativo.")
        proposed_action = proposed_action_for_decision(decision)
        task_id = f"task-{stable_hash(case.case_id + '|' + task_kind + '|' + proposed_action + '|' + '|'.join(evidence))}"
        return [
            OperatingTask(
                task_id=task_id,
                case_id=case.case_id,
                kind=task_kind,
                status="open",
                priority_score=PRIORITY_ORDER.get(task_kind, 5),
                summary=summary,
                proposed_action=proposed_action,
                evidence=evidence,
                created_at=now,
            )
        ]

    def _decision_event(
        self,
        case: BusinessCase,
        decision: Decision,
        tasks: list[OperatingTask],
        autonomy_level: int,
        evidence: list[str],
        now: str,
    ) -> dict[str, Any]:
        raw_id = "|".join([case.case_id, case.intent, ",".join(evidence), ",".join(task.task_id for task in tasks)])
        requires_confirmation = autonomy_level < 4 or decision.intent in {"accounting_payment", "accounting_debt"}
        return {
            "eventType": "decision_event",
            "decisionId": f"operating-{stable_hash(raw_id, 24)}",
            "createdAt": now,
            "goal": "operate_ariadgsm_business",
            "intent": decision.intent,
            "confidence": case.confidence,
            "autonomyLevel": max(1, min(6, autonomy_level)),
            "proposedAction": proposed_action_for_decision(decision),
            "requiresHumanConfirmation": requires_confirmation,
            "reasoningSummary": "; ".join(decision.reasons) or decision.label,
            "evidence": evidence,
        }


class OperatingStore:
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
            create table if not exists cases (
              case_id text primary key,
              channel_id text not null,
              conversation_id text not null,
              title text,
              intent text not null,
              status text not null,
              priority text not null,
              confidence real not null,
              last_message text,
              evidence_json text not null,
              created_at text not null,
              updated_at text not null
            );
            create table if not exists tasks (
              task_id text primary key,
              case_id text not null,
              kind text not null,
              status text not null,
              priority_score integer not null,
              summary text not null,
              proposed_action text not null,
              evidence_json text not null,
              created_at text not null,
              updated_at text not null
            );
            create table if not exists decisions (
              decision_id text primary key,
              case_id text not null,
              conversation_id text not null,
              event_json text not null,
              created_at text not null
            );
            """
        )
        self.conn.commit()

    def save_update(self, update: OperatingUpdate) -> dict[str, int]:
        now = utc_now()
        with self.conn:
            existing_case = self.conn.execute(
                "select 1 from cases where case_id = ? limit 1",
                (update.case.case_id,),
            ).fetchone()
            self.conn.execute(
                """
                insert into cases (
                  case_id, channel_id, conversation_id, title, intent, status,
                  priority, confidence, last_message, evidence_json, created_at, updated_at
                ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                on conflict(case_id) do update set
                  title = excluded.title,
                  intent = excluded.intent,
                  status = excluded.status,
                  priority = excluded.priority,
                  confidence = excluded.confidence,
                  last_message = excluded.last_message,
                  evidence_json = excluded.evidence_json,
                  updated_at = excluded.updated_at
                """,
                (
                    update.case.case_id,
                    update.case.channel_id,
                    update.case.conversation_id,
                    update.case.title,
                    update.case.intent,
                    update.case.status,
                    update.case.priority,
                    update.case.confidence,
                    update.case.last_message,
                    json.dumps(update.case.evidence, ensure_ascii=False),
                    now,
                    update.case.updated_at,
                ),
            )
            new_tasks = 0
            for task in update.tasks:
                before = self.conn.execute(
                    "select 1 from tasks where task_id = ? limit 1",
                    (task.task_id,),
                ).fetchone()
                self.conn.execute(
                    """
                    insert into tasks (
                      task_id, case_id, kind, status, priority_score, summary,
                      proposed_action, evidence_json, created_at, updated_at
                    ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                    on conflict(task_id) do update set
                      status = excluded.status,
                      priority_score = excluded.priority_score,
                      summary = excluded.summary,
                      proposed_action = excluded.proposed_action,
                      evidence_json = excluded.evidence_json,
                      updated_at = excluded.updated_at
                    """,
                    (
                        task.task_id,
                        task.case_id,
                        task.kind,
                        task.status,
                        task.priority_score,
                        task.summary,
                        task.proposed_action,
                        json.dumps(task.evidence, ensure_ascii=False),
                        task.created_at,
                        now,
                    ),
                )
                if before is None:
                    new_tasks += 1
            new_decisions = 0
            if update.decision_event:
                decision_id = str(update.decision_event["decisionId"])
                before = self.conn.execute(
                    "select 1 from decisions where decision_id = ? limit 1",
                    (decision_id,),
                ).fetchone()
                if before is None:
                    self.conn.execute(
                        """
                        insert into decisions (
                          decision_id, case_id, conversation_id, event_json, created_at
                        ) values (?, ?, ?, ?, ?)
                        """,
                        (
                            decision_id,
                            update.case.case_id,
                            update.case.conversation_id,
                            json.dumps(update.decision_event, ensure_ascii=False, separators=(",", ":")),
                            update.decision_event["createdAt"],
                        ),
                    )
                    new_decisions = 1
        return {
            "cases": 0 if existing_case else 1,
            "tasks": new_tasks,
            "decisions": new_decisions,
        }

    def summary(self) -> dict[str, Any]:
        def scalar(sql: str) -> int:
            row = self.conn.execute(sql).fetchone()
            return int(row[0] if row else 0)

        queue = [
            dict(row)
            for row in self.conn.execute(
                """
                select t.task_id, t.kind, t.status, t.priority_score, t.summary,
                       c.channel_id, c.conversation_id, c.title
                from tasks t
                join cases c on c.case_id = t.case_id
                where t.status = 'open'
                order by t.priority_score desc, t.updated_at desc
                limit 20
                """
            ).fetchall()
        ]
        return {
            "cases": scalar("select count(*) from cases"),
            "openTasks": scalar("select count(*) from tasks where status = 'open'"),
            "decisions": scalar("select count(*) from decisions"),
            "queue": queue,
            "db": str(self.db_path),
        }


def read_conversation_events(path: Path, limit: int = 200) -> list[dict[str, Any]]:
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
        if event.get("eventType") == "conversation_event":
            events.append(event)
    return events


def append_jsonl(path: Path, event: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(event, ensure_ascii=False, separators=(",", ":")) + "\n")


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    temp.replace(path)


def resolve_runtime_path(value: str | Path) -> Path:
    path = Path(value).expanduser()
    if path.is_absolute():
        return path
    return (AGENT_ROOT / path).resolve()


def run_operating_once(
    conversation_events_file: Path,
    decision_events_file: Path,
    state_file: Path,
    db_path: Path,
    autonomy_level: int = 1,
    limit: int = 200,
) -> dict[str, Any]:
    core = OperatingCore()
    store = OperatingStore(db_path)
    ingested = {"events": 0, "cases": 0, "tasks": 0, "decisions": 0, "ignored": 0}
    updates: list[dict[str, Any]] = []
    try:
        for conversation_event in read_conversation_events(conversation_events_file, limit=limit):
            update = core.process_conversation(conversation_event, autonomy_level=autonomy_level)
            stored = store.save_update(update)
            ingested["events"] += 1
            ingested["cases"] += stored["cases"]
            ingested["tasks"] += stored["tasks"]
            ingested["decisions"] += stored["decisions"]
            if update.ignored:
                ingested["ignored"] += 1
            if update.decision_event and stored["decisions"]:
                errors = validate_contract(update.decision_event, "decision_event")
                if errors:
                    raise ValueError("; ".join(errors))
                append_jsonl(decision_events_file, update.decision_event)
            updates.append(update.to_dict())
        state = {
            "status": "ok",
            "engine": "ariadgsm_operating_core",
            "updatedAt": utc_now(),
            "conversationEventsFile": str(conversation_events_file),
            "decisionEventsFile": str(decision_events_file),
            "ingested": ingested,
            "latestUpdates": updates[-10:],
            "summary": store.summary(),
        }
        write_json(state_file, state)
        return state
    finally:
        store.close()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM Operating Core")
    parser.add_argument("--conversation-events", default="runtime/conversation-events.jsonl")
    parser.add_argument("--decision-events", default="runtime/decision-events.jsonl")
    parser.add_argument("--state-file", default="runtime/operating-state.json")
    parser.add_argument("--db", default="runtime/operating-core.sqlite")
    parser.add_argument("--autonomy-level", type=int, default=1)
    parser.add_argument("--limit", type=int, default=200)
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def main() -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    args = parse_args()
    state = run_operating_once(
        resolve_runtime_path(args.conversation_events),
        resolve_runtime_path(args.decision_events),
        resolve_runtime_path(args.state_file),
        resolve_runtime_path(args.db),
        autonomy_level=args.autonomy_level,
        limit=args.limit,
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=False, indent=2))
    else:
        ingested = state["ingested"]
        summary = state["summary"]
        print(
            "AriadGSM Operating Core: "
            f"events={ingested['events']} "
            f"cases={summary['cases']} "
            f"open_tasks={summary['openTasks']} "
            f"decisions={summary['decisions']} "
            f"db={summary['db']}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
