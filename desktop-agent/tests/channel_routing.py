from __future__ import annotations

import json
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.case_manager import run_case_manager_once
from ariadgsm_agent.channel_routing import run_channel_routing_once
from ariadgsm_agent.contracts import validate_contract
from ariadgsm_agent.domain_events import make_domain_event, run_domain_events_once


def write_jsonl(path: Path, *events: dict[str, object]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("".join(json.dumps(event, ensure_ascii=False) + "\n" for event in events), encoding="utf-8")


def read_jsonl(path: Path) -> list[dict[str, object]]:
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def source(
    source_id: str,
    channel_id: str,
    conversation_id: str,
    case_id: str,
    customer_id: str,
    event_type: str = "conversation_event",
) -> dict[str, object]:
    return {
        "eventType": event_type,
        "conversationEventId": source_id,
        "feedbackId": source_id,
        "accountingId": source_id,
        "createdAt": "2026-04-28T12:00:00Z",
        "observedAt": "2026-04-28T12:00:00Z",
        "channelId": channel_id,
        "conversationId": conversation_id,
        "caseId": case_id,
        "customerId": customer_id,
    }


def route_fixture_events() -> list[dict[str, object]]:
    wa2_xiaomi = source("conversation-route-wa2-xiaomi", "wa-2", "wa-2-carlos-xiaomi", "case-wa2-carlos", "customer-carlos")
    wa3_xiaomi = source("conversation-route-wa3-xiaomi", "wa-3", "wa-3-carlos-history", "case-wa3-carlos", "customer-carlos")
    wa2_payment = source("accounting-route-wa2-payment", "wa-2", "wa-2-maria-payment", "case-wa2-maria", "customer-maria", "accounting_event")

    return [
        make_domain_event(
            "CustomerIdentified",
            wa2_xiaomi,
            subject_type="customer",
            subject_id="customer-carlos",
            data={"conversationTitle": "Carlos Xiaomi Peru", "identitySource": "test"},
            confidence=0.92,
            summary="Carlos identificado en WhatsApp 2.",
            source_domain="HumanCollaboration",
        ),
        make_domain_event(
            "ServiceDetected",
            wa2_xiaomi,
            subject_type="conversation_signal",
            subject_id="wa2-xiaomi-service",
            data={"signalKind": "service", "signalValue": "Xiaomi unlock", "conversationTitle": "Carlos Xiaomi Peru"},
            confidence=0.88,
            summary="Servicio Xiaomi detectado en WhatsApp 2.",
            source_domain="TimelineEngine",
        ),
        make_domain_event(
            "CustomerIdentified",
            wa3_xiaomi,
            subject_type="customer",
            subject_id="customer-carlos",
            data={"conversationTitle": "Carlos Xiaomi historial", "identitySource": "test"},
            confidence=0.93,
            summary="Mismo Carlos identificado en WhatsApp 3.",
            source_domain="HumanCollaboration",
        ),
        make_domain_event(
            "DeviceDetected",
            wa3_xiaomi,
            subject_type="conversation_signal",
            subject_id="wa3-xiaomi-device",
            data={"signalKind": "device", "signalValue": "Xiaomi Redmi", "conversationTitle": "Carlos Xiaomi historial"},
            confidence=0.86,
            summary="Equipo Xiaomi detectado en historial tecnico.",
            source_domain="TimelineEngine",
        ),
        make_domain_event(
            "PaymentDrafted",
            wa2_payment,
            subject_type="accounting_record",
            subject_id="payment-maria",
            data={"kind": "payment", "amount": 100, "currency": "USD", "clientName": "Maria Pago"},
            confidence=0.82,
            summary="Pago de Maria detectado en WhatsApp 2.",
            source_domain="AccountingBrain",
        ),
    ]


def main() -> int:
    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        domain_file = root / "domain-events.jsonl"
        case_events_file = root / "case-events.jsonl"
        case_state_file = root / "case-manager-state.json"
        case_report_file = root / "case-manager-report.json"
        case_db = root / "case-manager.sqlite"
        route_events_file = root / "route-events.jsonl"
        route_state_file = root / "channel-routing-state.json"
        route_report_file = root / "channel-routing-report.json"
        route_db = root / "channel-routing.sqlite"
        domain_state_file = root / "domain-events-state.json"
        domain_db = root / "domain-events.sqlite"

        write_jsonl(domain_file, *route_fixture_events())
        case_state = run_case_manager_once(
            domain_file,
            case_events_file,
            case_state_file,
            case_db,
            report_file=case_report_file,
            limit=100,
        )
        assert case_state["summary"]["openCases"] >= 3, case_state

        state = run_channel_routing_once(
            case_db,
            route_events_file,
            route_state_file,
            route_db,
            report_file=route_report_file,
            limit=100,
        )
        assert state["status"] == "attention", state
        assert not validate_contract(state, "channel_routing_state"), state
        assert state["summary"]["casesRead"] >= 3, state
        assert state["summary"]["duplicateGroups"] >= 1, state
        assert state["summary"]["proposedRoutes"] >= 1, state
        assert state["summary"]["approvedRoutes"] >= 1, state
        assert state["summary"]["needsHuman"] >= 1, state
        assert route_report_file.exists()
        assert route_events_file.exists()

        route_events = read_jsonl(route_events_file)
        route_types = {event["eventType"] for event in route_events}
        assert "ChannelRouteProposed" in route_types, route_types
        assert "ChannelRouteApproved" in route_types, route_types
        for event in route_events:
            assert not validate_contract(event, "domain_event"), event
            assert event["data"]["channelRoutingEvent"] is True
            assert event["caseId"]
            if event["eventType"] == "ChannelRouteProposed":
                assert event["requiresHumanReview"] is True
                assert event["data"]["sourceChannelId"] != event["data"]["targetChannelId"] or event["data"]["routeKind"] == "merge_context"

        repeated = run_channel_routing_once(
            case_db,
            route_events_file,
            route_state_file,
            route_db,
            report_file=route_report_file,
            limit=100,
        )
        assert repeated["ingested"]["decisions"] == 0, repeated
        assert repeated["ingested"]["duplicates"] >= state["ingested"]["decisions"], repeated

        domain_state = run_domain_events_once(
            [route_events_file],
            domain_file,
            domain_state_file,
            domain_db,
            limit=100,
        )
        assert domain_state["ingested"]["invalid"] == 0, domain_state
        assert "ChannelRouteProposed" in domain_state["summary"]["byType"], domain_state
        assert "ChannelRouteApproved" in domain_state["summary"]["byType"], domain_state

    print("channel routing OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
