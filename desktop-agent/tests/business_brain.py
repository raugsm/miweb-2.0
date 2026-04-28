from __future__ import annotations

import json
import sqlite3
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.business_brain import run_business_brain_once
from ariadgsm_agent.contracts import sample_event, validate_contract
from ariadgsm_agent.domain_events import run_domain_events_once
from ariadgsm_agent.memory import run_memory_once
from ariadgsm_agent.trust_safety import run_trust_safety_once


def write_jsonl(path: Path, *events: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("".join(json.dumps(event, ensure_ascii=False) + "\n" for event in events), encoding="utf-8")


def read_jsonl(path: Path) -> list[dict[str, object]]:
    if not path.exists():
        return []
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def create_case_db(path: Path) -> None:
    conn = sqlite3.connect(path)
    try:
        conn.executescript(
            """
            create table cases (
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
            """
        )
        rows = [
            (
                "case-price",
                "customer_waiting",
                "medium",
                8,
                "wa-2",
                "customer-omar",
                "Omar Torres Mexico",
                "MX",
                "samsung",
                "",
                "price_request",
                "medium",
                0.88,
                0,
                "",
                "requested",
                "prepare_quote",
                "Cliente pide precio Samsung Mexico.",
                '["wa-2"]',
                '["wa-2-omar-mx"]',
                '["case-price-event"]',
                1,
                0,
                0,
                0,
                "2026-04-28T12:00:00Z",
                "2026-04-28T12:00:00Z",
                "case-price-event",
                "CaseOpened",
            ),
            (
                "case-pay",
                "waiting_payment_confirmation",
                "high",
                10,
                "wa-1",
                "customer-luis",
                "Luis Peru",
                "PE",
                "xiaomi",
                "redmi note",
                "accounting_payment",
                "high",
                0.9,
                1,
                "draft",
                "",
                "review_payment",
                "Cliente envio posible pago.",
                '["wa-1"]',
                '["wa-1-luis-pe"]',
                '["case-pay-event"]',
                2,
                1,
                0,
                0,
                "2026-04-28T12:01:00Z",
                "2026-04-28T12:01:00Z",
                "case-pay-event",
                "PaymentDrafted",
            ),
            (
                "case-route",
                "customer_waiting",
                "high",
                9,
                "wa-3",
                "customer-maria",
                "Maria Colombia",
                "CO",
                "iphone",
                "iphone 13",
                "service_context",
                "high",
                0.86,
                1,
                "",
                "",
                "review_route",
                "Cliente debe derivarse a otro WhatsApp.",
                '["wa-3"]',
                '["wa-3-maria-co"]',
                '["case-route-event"]',
                1,
                0,
                0,
                0,
                "2026-04-28T12:02:00Z",
                "2026-04-28T12:02:00Z",
                "case-route-event",
                "ChannelRouteProposed",
            ),
        ]
        conn.executemany(
            """
            insert into cases values (
              ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?
            )
            """,
            rows,
        )
        conn.commit()
    finally:
        conn.close()


def create_accounting_db(path: Path) -> None:
    conn = sqlite3.connect(path)
    try:
        conn.executescript(
            """
            create table accounting_records (
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
            """
        )
        conn.execute(
            """
            insert into accounting_records values (
              'acct-pay-1', 'case-pay', 'customer-luis', 'wa-1', 'wa-1-luis-pe',
              'payment', 'needs_confirmation', 35, 'USDT', 'Binance', 'B', 1,
              0.68, 1, 1, 'Falta comprobante nivel A.', '["case-pay-event"]',
              '["captura visible"]', '2026-04-28T12:03:00Z', '2026-04-28T12:03:00Z'
            )
            """
        )
        conn.commit()
    finally:
        conn.close()


def create_route_db(path: Path) -> None:
    conn = sqlite3.connect(path)
    try:
        conn.executescript(
            """
            create table route_decisions (
              decision_id text primary key,
              decision_key text not null unique,
              case_id text not null,
              source_channel_id text not null,
              target_channel_id text not null,
              target_case_id text,
              action text not null,
              route_kind text not null,
              confidence real not null,
              requires_human integer not null,
              status text not null,
              reason_code text not null,
              reason text not null,
              decision_json text not null,
              event_id text,
              created_at text not null
            );
            """
        )
        conn.execute(
            """
            insert into route_decisions values (
              'route-case-route-1', 'route-case-route-1', 'case-route', 'wa-3', 'wa-2',
              '', 'propose_transfer', 'service_channel', 0.82, 1, 'emitted',
              'better_channel', 'Cliente debe continuar por wa-2.', '{}',
              'route-event-1', '2026-04-28T12:04:00Z'
            )
            """
        )
        conn.commit()
    finally:
        conn.close()


def main() -> int:
    assert not validate_contract(sample_event("business_brain_state"), "business_brain_state")

    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        conversation_file = root / "timeline-events.jsonl"
        cognitive_file = root / "cognitive-decision-events.jsonl"
        operating_file = root / "decision-events.jsonl"
        learning_file = root / "learning-events.jsonl"
        accounting_file = root / "accounting-events.jsonl"
        memory_state = root / "memory-state.json"
        memory_db = root / "memory-core.sqlite"
        case_db = root / "case-manager.sqlite"
        route_db = root / "channel-routing.sqlite"
        accounting_db = root / "accounting-core.sqlite"
        business_state = root / "business-brain-state.json"
        business_decisions = root / "business-decision-events.jsonl"
        business_recommendations = root / "business-recommendations.jsonl"
        business_db = root / "business-brain.sqlite"

        conversations = [
            {
                "eventType": "conversation_event",
                "conversationEventId": "conversation-price",
                "conversationId": "wa-2-omar-mx",
                "channelId": "wa-2",
                "observedAt": "2026-04-28T12:00:00Z",
                "conversationTitle": "Omar Torres Mexico",
                "source": "live",
                "messages": [
                    {
                        "messageId": "msg-price",
                        "text": "Bro cuanto vale liberar Samsung en Mexico?",
                        "direction": "client",
                        "confidence": 0.94,
                        "signals": [
                            {"kind": "price_request", "value": "cuanto vale", "confidence": 0.91},
                            {"kind": "service", "value": "samsung", "confidence": 0.88},
                            {"kind": "country", "value": "MX", "confidence": 0.9},
                            {"kind": "slang", "value": "bro", "confidence": 0.8},
                        ],
                    }
                ],
                "timeline": {"historyLimitDays": 30, "complete": False},
                "quality": {"isReliable": True, "identityConfidence": 0.96},
            },
            {
                "eventType": "conversation_event",
                "conversationEventId": "conversation-pay",
                "conversationId": "wa-1-luis-pe",
                "channelId": "wa-1",
                "observedAt": "2026-04-28T12:01:00Z",
                "conversationTitle": "Luis Peru",
                "source": "live",
                "messages": [
                    {
                        "messageId": "msg-pay",
                        "text": "Ya te mande 35 USDT revisa pago",
                        "direction": "client",
                        "confidence": 0.89,
                        "signals": [
                            {"kind": "payment", "value": "USDT", "confidence": 0.75},
                            {"kind": "amount", "value": "35", "confidence": 0.7},
                        ],
                    }
                ],
                "timeline": {"historyLimitDays": 30, "complete": False},
                "quality": {"isReliable": True, "identityConfidence": 0.94},
            },
        ]
        write_jsonl(conversation_file, *conversations)
        write_jsonl(
            learning_file,
            {
                "eventType": "learning_event",
                "learningId": "learning-procedure-price",
                "createdAt": "2026-04-28T12:05:00Z",
                "learningType": "procedure",
                "source": "operator",
                "summary": "Para cotizar Samsung primero confirmar modelo exacto y pais.",
                "confidence": 0.86,
                "after": {"conversationId": "wa-2-omar-mx", "channelId": "wa-2"},
                "appliesTo": ["samsung", "price_quote"],
            },
        )
        run_memory_once(
            conversation_file,
            cognitive_file,
            operating_file,
            learning_file,
            accounting_file,
            memory_state,
            memory_db,
        )
        create_case_db(case_db)
        create_accounting_db(accounting_db)
        create_route_db(route_db)

        state = run_business_brain_once(
            case_db,
            memory_db,
            route_db,
            accounting_db,
            business_state,
            business_decisions,
            business_recommendations,
            business_db,
            autonomy_level=3,
        )

        assert state["status"] == "attention"
        assert state["summary"]["activeCases"] == 3
        assert state["summary"]["recommendations"] == 3
        assert state["summary"]["quoteRecommendations"] == 1
        assert state["summary"]["accountingRecommendations"] == 1
        assert state["summary"]["routeRecommendations"] == 1
        assert state["summary"]["requiresHuman"] == 3
        assert state["summary"]["emittedDecisionEvents"] == 3
        assert state["mentalModel"]["memoryItemsConsulted"] >= 1
        assert any(item["proposedAction"] == "ask_missing_pricing_context" for item in state["recommendations"])
        assert any(item["proposedAction"] == "review_accounting_evidence_before_recording" for item in state["recommendations"])
        assert any(item["proposedAction"] == "review_channel_route_before_moving_context" for item in state["recommendations"])
        assert not validate_contract(state, "business_brain_state")
        assert len(read_jsonl(business_decisions)) == 3

        repeated = run_business_brain_once(
            case_db,
            memory_db,
            route_db,
            accounting_db,
            business_state,
            business_decisions,
            business_recommendations,
            business_db,
            autonomy_level=3,
        )
        assert repeated["summary"]["emittedDecisionEvents"] == 0
        assert len(read_jsonl(business_decisions)) == 3

        domain_events_file = root / "domain-events.jsonl"
        domain_state = run_domain_events_once(
            [business_decisions],
            domain_events_file,
            root / "domain-events-state.json",
            root / "domain-events.sqlite",
        )
        assert domain_state["status"] == "ok"
        domain_events = read_jsonl(domain_events_file)
        assert domain_events
        assert all(event["sourceDomain"] == "BusinessBrain" for event in domain_events if event["eventType"] == "DecisionExplained")

        trust_state = run_trust_safety_once(
            root / "empty-cognitive.jsonl",
            root / "empty-operating.jsonl",
            root / "empty-actions.jsonl",
            domain_events_file,
            root / "trust-safety-state.json",
            business_decision_events_file=business_decisions,
            autonomy_level=3,
            limit=100,
        )
        assert trust_state["summary"]["findings"] >= 3
        assert trust_state["summary"]["requiresHumanConfirmation"] >= 3

    print("business brain OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
