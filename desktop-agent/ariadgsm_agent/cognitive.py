from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
import sys
from dataclasses import asdict, dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from .classifier import Decision, classify_messages
from .contracts import validate_contract
from .supervisor import SupervisorPolicy, action_permission
from .text import clean_text


AGENT_ROOT = Path(__file__).resolve().parents[1]

SIGNAL_INTENTS = {
    "payment": "accounting_payment",
    "debt": "accounting_debt",
    "price_request": "price_request",
    "service": "service_context",
    "urgency": "customer_waiting",
}

ACTION_BY_INTENT = {
    "accounting_payment": "review_payment_evidence",
    "accounting_debt": "open_accounting_followup",
    "price_request": "prepare_price_response",
    "service_context": "review_service_context",
    "customer_waiting": "prioritize_visible_chat",
    "conversation_context": "keep_observing",
    "no_signal": "keep_observing",
}

LEARNING_KIND_BY_SIGNAL = {
    "payment": "accounting",
    "debt": "accounting",
    "price_request": "pricing",
    "service": "service",
    "country": "customer_pattern",
    "language_hint": "customer_pattern",
    "urgency": "customer_pattern",
    "amount": "accounting",
}


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def stable_hash(value: str, length: int = 20) -> str:
    return hashlib.sha1(value.encode("utf-8")).hexdigest()[:length]


def clamp(value: float, minimum: float = 0.0, maximum: float = 1.0) -> float:
    return max(minimum, min(maximum, value))


def event_id_for_conversation(conversation_event: dict[str, Any]) -> str:
    explicit = clean_text(conversation_event.get("conversationEventId"))
    if explicit:
        return explicit
    raw = json.dumps(conversation_event, ensure_ascii=False, sort_keys=True)
    return f"conversation-{stable_hash(raw, 24)}"


def evidence_from_messages(messages: list[dict[str, Any]], limit: int = 6) -> list[str]:
    evidence: list[str] = []
    for message in messages[-limit:]:
        if not isinstance(message, dict):
            continue
        evidence.append(str(message.get("messageId") or message.get("text") or ""))
    return [item for item in evidence if item]


def collect_signals(messages: list[dict[str, Any]]) -> list[dict[str, Any]]:
    signals: list[dict[str, Any]] = []
    for message in messages:
        for signal in message.get("signals") or []:
            if isinstance(signal, dict) and clean_text(signal.get("kind")):
                signals.append(
                    {
                        "kind": clean_text(signal.get("kind")),
                        "value": clean_text(signal.get("value")),
                        "confidence": clamp(float(signal.get("confidence") or 0.5)),
                        "messageId": message.get("messageId"),
                        "text": clean_text(message.get("text")),
                    }
                )
    return signals


def best_signal_by_kind(signals: list[dict[str, Any]]) -> dict[str, dict[str, Any]]:
    best: dict[str, dict[str, Any]] = {}
    for signal in signals:
        kind = clean_text(signal.get("kind"))
        if not kind:
            continue
        if kind not in best or float(signal.get("confidence") or 0) > float(best[kind].get("confidence") or 0):
            best[kind] = signal
    return best


def infer_intent(classifier_decision: Decision, signals: list[dict[str, Any]]) -> str:
    ranked = sorted(
        signals,
        key=lambda signal: (
            2 if signal.get("kind") in {"payment", "debt"} else 1,
            float(signal.get("confidence") or 0),
        ),
        reverse=True,
    )
    for signal in ranked:
        intent = SIGNAL_INTENTS.get(str(signal.get("kind")))
        if intent:
            return intent
    return classifier_decision.intent


def required_autonomy_for_intent(intent: str) -> int:
    if intent in {"accounting_payment", "accounting_debt"}:
        return 4
    if intent == "price_request":
        return 2
    if intent == "customer_waiting":
        return 3
    return 1


def confidence_for_reasoning(classifier_decision: Decision, signals: list[dict[str, Any]], messages: list[dict[str, Any]]) -> float:
    classifier_confidence = clamp(classifier_decision.score / 10.0, 0.35, 0.88)
    signal_confidence = max([float(signal.get("confidence") or 0) for signal in signals] or [0.0])
    recent_client_boost = 0.04 if messages and clean_text(messages[-1].get("direction")) in {"client", "unknown"} else 0.0
    signal_count_boost = min(0.08, len({signal.get("kind") for signal in signals}) * 0.02)
    return clamp(max(classifier_confidence, signal_confidence) + recent_client_boost + signal_count_boost, 0.35, 0.97)


def reasoning_summary(intent: str, decision: Decision, signals: list[dict[str, Any]], permission: dict[str, object]) -> str:
    kinds = ", ".join(sorted({str(signal.get("kind")) for signal in signals if signal.get("kind")}))
    base = {
        "accounting_payment": "Possible payment or receipt found in customer conversation.",
        "accounting_debt": "Possible debt, balance or refund topic found.",
        "price_request": "Customer appears to be asking for price.",
        "service_context": "Conversation contains service or device context.",
        "customer_waiting": "Customer message looks urgent or waiting.",
        "conversation_context": "Conversation has context but no strong business action yet.",
        "no_signal": "No strong business signal found.",
    }.get(intent, decision.label)
    reason = str(permission.get("reason") or "policy")
    if kinds:
        return f"{base} Signals: {kinds}. Policy: {reason}."
    return f"{base} Classifier reasons: {', '.join(decision.reasons) or decision.label}. Policy: {reason}."


def learning_event_from_signal(
    source_event_id: str,
    conversation_event: dict[str, Any],
    signal: dict[str, Any],
    intent: str,
) -> dict[str, Any] | None:
    kind = clean_text(signal.get("kind"))
    learning_type = LEARNING_KIND_BY_SIGNAL.get(kind)
    if not learning_type:
        return None
    value = clean_text(signal.get("value"))
    text = clean_text(signal.get("text"))
    summary = f"Signal {kind}={value or 'detected'} was observed in {conversation_event.get('channelId')} for intent {intent}."
    if text:
        summary += f" Text sample: {text[:120]}"
    raw_id = f"{source_event_id}|{learning_type}|{kind}|{value}|{text}"
    return {
        "eventType": "learning_event",
        "learningId": f"learning-{stable_hash(raw_id, 24)}",
        "createdAt": utc_now(),
        "learningType": learning_type,
        "source": "cognitive_core",
        "summary": summary,
        "confidence": clamp(float(signal.get("confidence") or 0.5)),
        "appliesTo": [intent, kind],
        "after": {
            "channelId": conversation_event.get("channelId"),
            "conversationId": conversation_event.get("conversationId"),
            "conversationTitle": conversation_event.get("conversationTitle"),
            "signal": {
                "kind": kind,
                "value": value,
                "messageId": signal.get("messageId"),
            },
        },
    }


@dataclass(frozen=True)
class CognitiveAssessment:
    source_event_id: str
    conversation_id: str
    channel_id: str
    title: str
    intent: str
    confidence: float
    proposed_action: str
    requires_human_confirmation: bool
    evidence: list[str]
    signals: list[dict[str, Any]]
    decision_event: dict[str, Any]
    learning_events: list[dict[str, Any]]

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


class CognitiveCore:
    def assess_conversation(
        self,
        conversation_event: dict[str, Any],
        policy: SupervisorPolicy | None = None,
    ) -> CognitiveAssessment:
        policy = policy or SupervisorPolicy()
        source_event_id = event_id_for_conversation(conversation_event)
        messages = [message for message in conversation_event.get("messages") or [] if isinstance(message, dict)]
        classifier_decision = classify_messages(messages)
        signals = collect_signals(messages)
        intent = infer_intent(classifier_decision, signals)
        confidence = confidence_for_reasoning(classifier_decision, signals, messages)
        required_level = required_autonomy_for_intent(intent)
        permission = action_permission(confidence, required_level, policy)
        proposed_action = ACTION_BY_INTENT.get(intent, "review_case")
        evidence = evidence_from_messages(messages)
        now = utc_now()
        raw_decision_id = "|".join(
            [
                source_event_id,
                clean_text(conversation_event.get("conversationId")),
                intent,
                proposed_action,
                ",".join(evidence),
            ]
        )
        decision_event = {
            "eventType": "decision_event",
            "decisionId": f"cognitive-{stable_hash(raw_decision_id, 24)}",
            "createdAt": now,
            "sourceEventId": source_event_id,
            "conversationId": clean_text(conversation_event.get("conversationId")),
            "channelId": clean_text(conversation_event.get("channelId")),
            "conversationTitle": clean_text(conversation_event.get("conversationTitle") or conversation_event.get("conversationId")),
            "goal": "understand_and_operate_ariadgsm_business",
            "intent": intent,
            "confidence": confidence,
            "autonomyLevel": max(1, min(6, policy.autonomy_level)),
            "proposedAction": proposed_action,
            "requiresHumanConfirmation": bool(permission["requiresHumanConfirmation"]),
            "reasoningSummary": reasoning_summary(intent, classifier_decision, signals, permission),
            "evidence": evidence,
        }
        learning_events = [
            event
            for signal in best_signal_by_kind(signals).values()
            if (event := learning_event_from_signal(source_event_id, conversation_event, signal, intent)) is not None
        ]
        return CognitiveAssessment(
            source_event_id=source_event_id,
            conversation_id=clean_text(conversation_event.get("conversationId")),
            channel_id=clean_text(conversation_event.get("channelId")),
            title=clean_text(conversation_event.get("conversationTitle") or conversation_event.get("conversationId")),
            intent=intent,
            confidence=confidence,
            proposed_action=proposed_action,
            requires_human_confirmation=bool(permission["requiresHumanConfirmation"]),
            evidence=evidence,
            signals=signals,
            decision_event=decision_event,
            learning_events=learning_events,
        )


class CognitiveStore:
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
            create table if not exists processed_events (
              source_event_id text primary key,
              processed_at text not null
            );
            create table if not exists cognitive_decisions (
              decision_id text primary key,
              source_event_id text not null,
              conversation_id text not null,
              channel_id text not null,
              intent text not null,
              confidence real not null,
              proposed_action text not null,
              event_json text not null,
              created_at text not null
            );
            create table if not exists learning_events (
              learning_id text primary key,
              source_event_id text not null,
              learning_type text not null,
              event_json text not null,
              created_at text not null
            );
            create table if not exists client_profiles (
              conversation_id text primary key,
              channel_id text not null,
              title text,
              last_intent text,
              countries_json text not null,
              services_json text not null,
              languages_json text not null,
              message_count integer not null,
              decision_count integer not null,
              first_seen_at text not null,
              last_seen_at text not null
            );
            """
        )
        self.conn.commit()

    def has_processed_event(self, source_event_id: str) -> bool:
        row = self.conn.execute(
            "select 1 from processed_events where source_event_id = ? limit 1",
            (source_event_id,),
        ).fetchone()
        return row is not None

    def save_assessment(self, assessment: CognitiveAssessment, conversation_event: dict[str, Any]) -> dict[str, int]:
        now = utc_now()
        if self.has_processed_event(assessment.source_event_id):
            return {"events": 0, "duplicates": 1, "decisions": 0, "learning": 0, "profiles": 0}

        with self.conn:
            self.conn.execute(
                """
                insert or ignore into cognitive_decisions (
                  decision_id, source_event_id, conversation_id, channel_id,
                  intent, confidence, proposed_action, event_json, created_at
                ) values (?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    assessment.decision_event["decisionId"],
                    assessment.source_event_id,
                    assessment.conversation_id,
                    assessment.channel_id,
                    assessment.intent,
                    assessment.confidence,
                    assessment.proposed_action,
                    json.dumps(assessment.decision_event, ensure_ascii=False, separators=(",", ":")),
                    assessment.decision_event["createdAt"],
                ),
            )

            new_learning = 0
            for event in assessment.learning_events:
                before = self.conn.execute(
                    "select 1 from learning_events where learning_id = ? limit 1",
                    (event["learningId"],),
                ).fetchone()
                if before is None:
                    self.conn.execute(
                        """
                        insert into learning_events (
                          learning_id, source_event_id, learning_type, event_json, created_at
                        ) values (?, ?, ?, ?, ?)
                        """,
                        (
                            event["learningId"],
                            assessment.source_event_id,
                            event["learningType"],
                            json.dumps(event, ensure_ascii=False, separators=(",", ":")),
                            event["createdAt"],
                        ),
                    )
                    new_learning += 1

            profile_created = self._upsert_profile(assessment, conversation_event, now)
            self.conn.execute(
                "insert into processed_events (source_event_id, processed_at) values (?, ?)",
                (assessment.source_event_id, now),
            )

        return {
            "events": 1,
            "duplicates": 0,
            "decisions": 1,
            "learning": new_learning,
            "profiles": 1 if profile_created else 0,
        }

    def _upsert_profile(self, assessment: CognitiveAssessment, conversation_event: dict[str, Any], now: str) -> bool:
        messages = [message for message in conversation_event.get("messages") or [] if isinstance(message, dict)]
        countries = sorted({signal["value"] for signal in assessment.signals if signal.get("kind") == "country" and signal.get("value")})
        services = sorted({signal["value"] for signal in assessment.signals if signal.get("kind") == "service" and signal.get("value")})
        languages = sorted({signal["value"] for signal in assessment.signals if signal.get("kind") == "language_hint" and signal.get("value")})
        existing = self.conn.execute(
            "select * from client_profiles where conversation_id = ? limit 1",
            (assessment.conversation_id,),
        ).fetchone()
        if existing:
            countries = sorted(set(countries) | set(json.loads(existing["countries_json"] or "[]")))
            services = sorted(set(services) | set(json.loads(existing["services_json"] or "[]")))
            languages = sorted(set(languages) | set(json.loads(existing["languages_json"] or "[]")))
            self.conn.execute(
                """
                update client_profiles
                set channel_id = ?, title = ?, last_intent = ?,
                    countries_json = ?, services_json = ?, languages_json = ?,
                    message_count = message_count + ?, decision_count = decision_count + 1,
                    last_seen_at = ?
                where conversation_id = ?
                """,
                (
                    assessment.channel_id,
                    assessment.title,
                    assessment.intent,
                    json.dumps(countries, ensure_ascii=False),
                    json.dumps(services, ensure_ascii=False),
                    json.dumps(languages, ensure_ascii=False),
                    len(messages),
                    now,
                    assessment.conversation_id,
                ),
            )
            return False

        self.conn.execute(
            """
            insert into client_profiles (
              conversation_id, channel_id, title, last_intent,
              countries_json, services_json, languages_json,
              message_count, decision_count, first_seen_at, last_seen_at
            ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            (
                assessment.conversation_id,
                assessment.channel_id,
                assessment.title,
                assessment.intent,
                json.dumps(countries, ensure_ascii=False),
                json.dumps(services, ensure_ascii=False),
                json.dumps(languages, ensure_ascii=False),
                len(messages),
                1,
                now,
                now,
            ),
        )
        return True

    def summary(self) -> dict[str, Any]:
        def scalar(sql: str) -> int:
            row = self.conn.execute(sql).fetchone()
            return int(row[0] if row else 0)

        latest = self.conn.execute(
            """
            select conversation_id, channel_id, intent, confidence, proposed_action, created_at
            from cognitive_decisions
            order by created_at desc
            limit 1
            """
        ).fetchone()
        return {
            "processedEvents": scalar("select count(*) from processed_events"),
            "decisions": scalar("select count(*) from cognitive_decisions"),
            "learningEvents": scalar("select count(*) from learning_events"),
            "clientProfiles": scalar("select count(*) from client_profiles"),
            "latestDecision": dict(latest) if latest else None,
            "db": str(self.db_path),
        }


def read_jsonl_events(path: Path, event_type: str, limit: int = 200) -> list[dict[str, Any]]:
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
        if event.get("eventType") == event_type:
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


def run_cognitive_once(
    conversation_events_file: Path,
    decision_events_file: Path,
    learning_events_file: Path,
    state_file: Path,
    db_path: Path,
    autonomy_level: int = 1,
    limit: int = 200,
) -> dict[str, Any]:
    core = CognitiveCore()
    store = CognitiveStore(db_path)
    policy = SupervisorPolicy(autonomy_level=autonomy_level)
    ingested = {"events": 0, "duplicates": 0, "decisions": 0, "learning": 0, "profiles": 0}
    assessments: list[dict[str, Any]] = []
    try:
        for conversation_event in read_jsonl_events(conversation_events_file, "conversation_event", limit):
            assessment = core.assess_conversation(conversation_event, policy)
            stored = store.save_assessment(assessment, conversation_event)
            for key in ingested:
                ingested[key] += stored[key]
            if stored["decisions"]:
                errors = validate_contract(assessment.decision_event, "decision_event")
                if errors:
                    raise ValueError("; ".join(errors))
                append_jsonl(decision_events_file, assessment.decision_event)
            if stored["learning"]:
                for event in assessment.learning_events:
                    errors = validate_contract(event, "learning_event")
                    if errors:
                        raise ValueError("; ".join(errors))
                    append_jsonl(learning_events_file, event)
            if stored["events"]:
                assessments.append(assessment.to_dict())
        state = {
            "status": "ok",
            "engine": "ariadgsm_cognitive_core",
            "updatedAt": utc_now(),
            "conversationEventsFile": str(conversation_events_file),
            "decisionEventsFile": str(decision_events_file),
            "learningEventsFile": str(learning_events_file),
            "ingested": ingested,
            "latestAssessments": assessments[-10:],
            "summary": store.summary(),
        }
        write_json(state_file, state)
        return state
    finally:
        store.close()


def decision_event_from_conversation(conversation_event: dict[str, Any], policy: SupervisorPolicy | None = None) -> dict[str, Any]:
    return CognitiveCore().assess_conversation(conversation_event, policy).decision_event


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM Cognitive Core")
    parser.add_argument("--conversation-events", default="runtime/conversation-events.jsonl")
    parser.add_argument("--decision-events", default="runtime/cognitive-decision-events.jsonl")
    parser.add_argument("--learning-events", default="runtime/learning-events.jsonl")
    parser.add_argument("--state-file", default="runtime/cognitive-state.json")
    parser.add_argument("--db", default="runtime/cognitive-core.sqlite")
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
    state = run_cognitive_once(
        resolve_runtime_path(args.conversation_events),
        resolve_runtime_path(args.decision_events),
        resolve_runtime_path(args.learning_events),
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
            "AriadGSM Cognitive Core: "
            f"events={ingested['events']} "
            f"duplicates={ingested['duplicates']} "
            f"decisions={summary['decisions']} "
            f"learning={summary['learningEvents']} "
            f"profiles={summary['clientProfiles']} "
            f"db={summary['db']}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
