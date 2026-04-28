from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
import sys
from collections import Counter, defaultdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from .contracts import validate_contract
from .domain_events import make_domain_event, resolve_runtime_path
from .text import clean_text, normalize


AGENT_ROOT = Path(__file__).resolve().parents[1]
RUNTIME_DIR = AGENT_ROOT / "runtime"
SCHEMA_VERSION = "0.8.4"
POLICY_VERSION = "ariadgsm-channel-policy-0.8.4"

DEFAULT_POLICY: dict[str, Any] = {
    "version": POLICY_VERSION,
    "minimumTransferConfidence": 0.62,
    "minimumMergeConfidence": 0.72,
    "minimumRejectConfidence": 0.52,
    "channels": {
        "wa-1": {
            "browser": "msedge",
            "name": "WhatsApp 1",
            "role": "general_intake",
            "specialties": ["intake", "general", "continuity", "customer_context"],
        },
        "wa-2": {
            "browser": "chrome",
            "name": "WhatsApp 2",
            "role": "sales_accounting",
            "specialties": ["pricing", "quote", "sales", "payment", "debt", "refund", "accounting"],
        },
        "wa-3": {
            "browser": "firefox",
            "name": "WhatsApp 3",
            "role": "technical_services",
            "specialties": ["technical", "service", "device", "procedure", "tool", "repair", "gsm"],
        },
    },
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


def as_int(value: Any, default: int = 0) -> int:
    try:
        if value is None or value == "":
            return default
        return int(float(value))
    except (TypeError, ValueError):
        return default


def as_list_json(value: Any) -> list[Any]:
    if isinstance(value, list):
        return value
    if not value:
        return []
    try:
        parsed = json.loads(str(value))
    except json.JSONDecodeError:
        return []
    return parsed if isinstance(parsed, list) else []


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    temp.replace(path)


def append_jsonl(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")


def load_policy(path: Path | None = None) -> dict[str, Any]:
    if path is not None and path.exists():
        try:
            value = json.loads(path.read_text(encoding="utf-8-sig"))
        except (OSError, json.JSONDecodeError):
            value = {}
        if isinstance(value, dict) and isinstance(value.get("channels"), dict):
            merged = {**DEFAULT_POLICY, **value}
            merged["channels"] = {**DEFAULT_POLICY["channels"], **value.get("channels", {})}
            return merged
    return DEFAULT_POLICY


def case_id(case: dict[str, Any]) -> str:
    return clean_text(case.get("case_id") or case.get("caseId"))


def primary_channel(case: dict[str, Any]) -> str:
    return clean_text(case.get("primary_channel_id") or case.get("primaryChannelId") or "unknown")


def customer_id(case: dict[str, Any]) -> str:
    return clean_text(case.get("customer_id") or case.get("customerId") or "customer_pending")


def case_title(case: dict[str, Any]) -> str:
    return clean_text(case.get("title") or case_id(case))


def case_status(case: dict[str, Any]) -> str:
    return clean_text(case.get("status") or "open")


def case_updated_at(case: dict[str, Any]) -> str:
    return clean_text(case.get("updated_at") or case.get("updatedAt"))


def case_channels(case: dict[str, Any]) -> list[str]:
    values = as_list_json(case.get("channels_json") or case.get("channels"))
    primary = primary_channel(case)
    output = [clean_text(item) for item in values if clean_text(item)]
    if primary and primary not in output:
        output.insert(0, primary)
    return output


def case_conversations(case: dict[str, Any]) -> list[str]:
    return [clean_text(item) for item in as_list_json(case.get("conversations_json") or case.get("conversations")) if clean_text(item)]


def case_signal_text(case: dict[str, Any]) -> str:
    return normalize(
        " ".join(
            [
                case_title(case),
                clean_text(case.get("country")),
                clean_text(case.get("service")),
                clean_text(case.get("device")),
                clean_text(case.get("intent")),
                clean_text(case.get("payment_state") or case.get("paymentState")),
                clean_text(case.get("quote_state") or case.get("quoteState")),
                clean_text(case.get("summary")),
            ]
        )
    )


def route_decision_key(decision: dict[str, Any]) -> str:
    raw = compact_json(
        {
            "caseId": decision["caseId"],
            "caseUpdatedAt": decision.get("caseUpdatedAt"),
            "action": decision["action"],
            "source": decision["sourceChannelId"],
            "target": decision["targetChannelId"],
            "targetCaseId": decision.get("targetCaseId"),
            "reasonCode": decision.get("reasonCode"),
        }
    )
    return f"route-{stable_hash(raw, 32)}"


def extract_case_snapshot(case: dict[str, Any]) -> dict[str, Any]:
    return {
        "caseId": case_id(case),
        "status": case_status(case),
        "title": case_title(case),
        "customerId": customer_id(case),
        "primaryChannelId": primary_channel(case),
        "channels": case_channels(case),
        "conversations": case_conversations(case),
        "country": clean_text(case.get("country")),
        "service": clean_text(case.get("service")),
        "device": clean_text(case.get("device")),
        "intent": clean_text(case.get("intent")),
        "paymentState": clean_text(case.get("payment_state") or case.get("paymentState")),
        "quoteState": clean_text(case.get("quote_state") or case.get("quoteState")),
        "priority": clean_text(case.get("priority")),
        "priorityScore": as_int(case.get("priority_score") or case.get("priorityScore")),
        "requiresHuman": bool(case.get("requires_human") or case.get("requiresHuman")),
        "eventCount": as_int(case.get("event_count") or case.get("eventCount")),
        "accountingCount": as_int(case.get("accounting_count") or case.get("accountingCount")),
        "actionCount": as_int(case.get("action_count") or case.get("actionCount")),
        "learningCount": as_int(case.get("learning_count") or case.get("learningCount")),
        "updatedAt": case_updated_at(case),
    }


def channel_scores(case: dict[str, Any], policy: dict[str, Any]) -> tuple[dict[str, int], list[str]]:
    channels = policy.get("channels") if isinstance(policy.get("channels"), dict) else DEFAULT_POLICY["channels"]
    scores = {clean_text(channel): 0 for channel in channels}
    current = primary_channel(case)
    text = case_signal_text(case)
    evidence: list[str] = []

    if current in scores:
        scores[current] += 15
        evidence.append(f"canal actual={current}")

    payment_state = clean_text(case.get("payment_state") or case.get("paymentState"))
    quote_state = clean_text(case.get("quote_state") or case.get("quoteState"))
    service = clean_text(case.get("service"))
    device = clean_text(case.get("device"))
    intent = clean_text(case.get("intent"))
    accounting_count = as_int(case.get("accounting_count") or case.get("accountingCount"))

    if payment_state or accounting_count > 0 or any(token in text for token in ("pago", "payment", "deuda", "debt", "refund", "reembolso")):
        scores["wa-2"] = scores.get("wa-2", 0) + 55
        evidence.append("senales contables/pago apuntan a wa-2")
    if quote_state or any(token in text for token in ("precio", "price", "quote", "cotiza", "cuanto")):
        scores["wa-2"] = scores.get("wa-2", 0) + 35
        evidence.append("senales de precio/venta apuntan a wa-2")
    if service or device or any(token in text for token in ("xiaomi", "samsung", "imei", "unlock", "liberar", "flashe", "frp", "tool", "procedimiento")):
        scores["wa-3"] = scores.get("wa-3", 0) + 70
        evidence.append("senales tecnicas/servicio apuntan a wa-3")
    if not evidence and current in scores:
        scores[current] += 45
        evidence.append("sin senal fuerte; mantener continuidad en canal actual")
    if intent and "accounting" in intent:
        scores["wa-2"] = scores.get("wa-2", 0) + 25
    if intent and any(token in intent for token in ("service", "technical", "procedure")):
        scores["wa-3"] = scores.get("wa-3", 0) + 25

    return scores, evidence


def preferred_channel(case: dict[str, Any], policy: dict[str, Any]) -> tuple[str, float, list[str], dict[str, int]]:
    scores, evidence = channel_scores(case, policy)
    if not scores:
        return primary_channel(case), 0.5, ["no hay canales configurados"], {}
    ranked = sorted(scores.items(), key=lambda item: item[1], reverse=True)
    top_channel, top_score = ranked[0]
    second_score = ranked[1][1] if len(ranked) > 1 else 0
    confidence = clamp(0.52 + ((top_score - second_score) / 120.0), 0.5)
    if top_score >= 70:
        confidence = max(confidence, 0.72)
    return top_channel, confidence, evidence, scores


def duplicate_groups(cases: list[dict[str, Any]]) -> dict[str, list[dict[str, Any]]]:
    groups: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for case in cases:
        if case_status(case) in {"ignored", "closed"}:
            continue
        customer = customer_id(case)
        title_key = normalize(case_title(case))
        if customer and customer != "customer_pending":
            groups[f"customer:{customer}"].append(case)
        elif title_key:
            groups[f"title:{title_key}"].append(case)
    return {key: value for key, value in groups.items() if len({case_id(item) for item in value}) > 1}


def find_duplicate_target(case: dict[str, Any], cases: list[dict[str, Any]]) -> tuple[dict[str, Any] | None, float, str]:
    current_id = case_id(case)
    current_customer = customer_id(case)
    current_title = normalize(case_title(case))
    candidates: list[tuple[dict[str, Any], float, str, int]] = []
    for other in cases:
        if case_id(other) == current_id or case_status(other) in {"ignored", "closed"}:
            continue
        confidence = 0.0
        reason = ""
        if current_customer != "customer_pending" and current_customer == customer_id(other):
            confidence = 0.86
            reason = "mismo customerId confirmado"
        elif current_title and current_title == normalize(case_title(other)):
            confidence = 0.66
            reason = "mismo titulo normalizado"
        if confidence <= 0:
            continue
        evidence_score = (
            as_int(other.get("event_count") or other.get("eventCount"))
            + as_int(other.get("accounting_count") or other.get("accountingCount")) * 2
            + as_int(other.get("action_count") or other.get("actionCount"))
            + as_int(other.get("priority_score") or other.get("priorityScore")) // 10
        )
        candidates.append((other, confidence, reason, evidence_score))
    if not candidates:
        return None, 0.0, ""
    candidates.sort(key=lambda item: (item[1], item[3], item[0].get("updated_at") or ""), reverse=True)
    target, confidence, reason, _ = candidates[0]
    return target, confidence, reason


def decide_route(case: dict[str, Any], cases: list[dict[str, Any]], policy: dict[str, Any]) -> dict[str, Any]:
    current = primary_channel(case)
    target_duplicate, duplicate_confidence, duplicate_reason = find_duplicate_target(case, cases)
    preferred, channel_confidence, channel_evidence, scores = preferred_channel(case, policy)
    transfer_threshold = float(policy.get("minimumTransferConfidence") or DEFAULT_POLICY["minimumTransferConfidence"])
    merge_threshold = float(policy.get("minimumMergeConfidence") or DEFAULT_POLICY["minimumMergeConfidence"])
    reject_threshold = float(policy.get("minimumRejectConfidence") or DEFAULT_POLICY["minimumRejectConfidence"])

    if case_status(case) in {"ignored", "closed"}:
        action = "skip"
        target = current
        confidence = 0.95
        reason_code = "case_not_routable"
        reason = "Caso ignorado o cerrado; no se enruta."
    elif target_duplicate is not None and duplicate_confidence >= merge_threshold:
        action = "propose_merge"
        target = primary_channel(target_duplicate) or preferred
        confidence = duplicate_confidence
        reason_code = "same_customer_cross_channel"
        reason = f"Posible mismo cliente en otro canal: {duplicate_reason}."
    elif preferred != current and channel_confidence >= transfer_threshold:
        action = "propose_transfer"
        target = preferred
        confidence = channel_confidence
        reason_code = "content_points_to_other_channel"
        reason = f"El contenido del caso apunta mejor a {preferred}."
    elif preferred != current and channel_confidence >= reject_threshold:
        action = "reject_transfer"
        target = preferred
        confidence = channel_confidence
        reason_code = "insufficient_route_confidence"
        reason = f"Hay senales hacia {preferred}, pero no alcanzan confianza para proponer traslado."
    else:
        action = "stay_current"
        target = current if current != "unknown" else preferred
        confidence = max(0.62, channel_confidence)
        reason_code = "current_channel_ok"
        reason = "El canal actual es suficiente para continuar el caso."

    decision = {
        "caseId": case_id(case),
        "caseUpdatedAt": case_updated_at(case),
        "customerId": customer_id(case),
        "action": action,
        "routeKind": "merge_context" if action == "propose_merge" else "transfer" if action == "propose_transfer" else "stay_current" if action == "stay_current" else "reject_transfer",
        "sourceChannelId": current,
        "targetChannelId": target,
        "targetCaseId": case_id(target_duplicate) if target_duplicate is not None else "",
        "candidateCaseIds": [case_id(target_duplicate)] if target_duplicate is not None else [],
        "confidence": round(confidence, 4),
        "reasonCode": reason_code,
        "reason": reason,
        "requiresHumanApproval": action in {"propose_transfer", "propose_merge"},
        "scores": scores,
        "evidence": channel_evidence + ([duplicate_reason] if duplicate_reason else []),
        "case": extract_case_snapshot(case),
    }
    decision["decisionId"] = route_decision_key(decision)
    return decision


class ChannelRoutingStore:
    def __init__(self, db_path: Path, route_events_file: Path):
        self.db_path = db_path
        self.route_events_file = route_events_file
        self.db_path.parent.mkdir(parents=True, exist_ok=True)
        self.route_events_file.parent.mkdir(parents=True, exist_ok=True)
        self.conn = sqlite3.connect(self.db_path)
        self.conn.row_factory = sqlite3.Row
        self.init_schema()

    def close(self) -> None:
        self.conn.close()

    def init_schema(self) -> None:
        self.conn.executescript(
            """
            create table if not exists route_decisions (
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
            create index if not exists idx_route_decisions_case on route_decisions(case_id);
            create index if not exists idx_route_decisions_action on route_decisions(action);
            """
        )
        self.conn.commit()

    def seen(self, decision_key: str) -> bool:
        row = self.conn.execute(
            "select decision_id from route_decisions where decision_key = ? limit 1",
            (decision_key,),
        ).fetchone()
        return row is not None

    def save_decision(self, decision: dict[str, Any], event: dict[str, Any] | None) -> bool:
        decision_key = decision["decisionId"]
        if self.seen(decision_key):
            return False
        with self.conn:
            self.conn.execute(
                """
                insert into route_decisions(
                  decision_id, decision_key, case_id, source_channel_id, target_channel_id,
                  target_case_id, action, route_kind, confidence, requires_human, status,
                  reason_code, reason, decision_json, event_id, created_at
                ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    decision["decisionId"],
                    decision_key,
                    decision["caseId"],
                    decision["sourceChannelId"],
                    decision["targetChannelId"],
                    decision.get("targetCaseId", ""),
                    decision["action"],
                    decision["routeKind"],
                    float(decision["confidence"]),
                    1 if decision.get("requiresHumanApproval") else 0,
                    "emitted" if event is not None else "recorded",
                    decision["reasonCode"],
                    decision["reason"],
                    json.dumps(decision, ensure_ascii=False, separators=(",", ":")),
                    event.get("eventId") if event else "",
                    utc_now(),
                ),
            )
            if event is not None:
                append_jsonl(self.route_events_file, event)
        return True

    def summary(self, cases_read: int, duplicate_group_count: int) -> dict[str, Any]:
        def scalar(sql: str) -> int:
            row = self.conn.execute(sql).fetchone()
            return int(row[0] if row else 0)

        def counter(sql: str) -> dict[str, int]:
            return {str(row[0]): int(row[1]) for row in self.conn.execute(sql).fetchall()}

        latest_rows = self.conn.execute(
            """
            select decision_id, case_id, source_channel_id, target_channel_id,
                   target_case_id, action, route_kind, confidence, requires_human,
                   reason, created_at
            from route_decisions
            order by created_at desc
            limit 10
            """
        ).fetchall()
        return {
            "casesRead": cases_read,
            "routeDecisions": scalar("select count(*) from route_decisions"),
            "proposedRoutes": scalar("select count(*) from route_decisions where action in ('propose_transfer', 'propose_merge')"),
            "approvedRoutes": scalar("select count(*) from route_decisions where action = 'stay_current'"),
            "rejectedRoutes": scalar("select count(*) from route_decisions where action = 'reject_transfer'"),
            "needsHuman": scalar("select count(*) from route_decisions where requires_human = 1"),
            "duplicateGroups": duplicate_group_count,
            "crossChannelCandidates": scalar("select count(*) from route_decisions where source_channel_id != target_channel_id"),
            "currentChannelOk": scalar("select count(*) from route_decisions where action = 'stay_current'"),
            "emittedRouteEvents": scalar("select count(*) from route_decisions where event_id != ''"),
            "byAction": counter("select action, count(*) from route_decisions group by action order by action"),
            "latest": [dict(row) for row in latest_rows],
        }


def read_cases(case_manager_db: Path) -> list[dict[str, Any]]:
    if not case_manager_db.exists():
        return []
    conn = sqlite3.connect(case_manager_db)
    conn.row_factory = sqlite3.Row
    try:
        rows = conn.execute(
            """
            select *
            from cases
            where status not in ('ignored', 'closed')
            order by updated_at desc
            """
        ).fetchall()
        return [dict(row) for row in rows]
    except sqlite3.Error:
        return []
    finally:
        conn.close()


def make_route_event(decision: dict[str, Any]) -> dict[str, Any] | None:
    action = decision["action"]
    if action == "skip":
        return None
    if action == "stay_current":
        event_type = "ChannelRouteApproved"
        source_domain = "BusinessBrain"
        summary = f"Ruta aprobada: mantener {decision['caseId']} en {decision['sourceChannelId']}."
    elif action == "reject_transfer":
        event_type = "ChannelRouteRejected"
        source_domain = "BusinessBrain"
        summary = f"Ruta rechazada: no mover {decision['caseId']} hacia {decision['targetChannelId']}."
    else:
        event_type = "ChannelRouteProposed"
        source_domain = "ChannelRoutingBrain"
        summary = (
            f"Ruta propuesta: {decision['caseId']} "
            f"{decision['sourceChannelId']} -> {decision['targetChannelId']} ({decision['routeKind']})."
        )

    source = {
        "eventType": "domain_event",
        "eventId": f"channel-routing-source-{decision['decisionId']}",
        "createdAt": utc_now(),
        "caseId": decision["caseId"],
        "customerId": decision["customerId"],
        "channelId": decision["sourceChannelId"],
        "conversationId": (decision["case"].get("conversations") or [""])[0],
        "requiresHumanConfirmation": bool(decision.get("requiresHumanApproval")),
    }
    data = {
        "channelRoutingEvent": True,
        "routeDecisionId": decision["decisionId"],
        "routeKind": decision["routeKind"],
        "action": action,
        "sourceChannelId": decision["sourceChannelId"],
        "targetChannelId": decision["targetChannelId"],
        "targetCaseId": decision.get("targetCaseId", ""),
        "candidateCaseIds": decision.get("candidateCaseIds", []),
        "reasonCode": decision["reasonCode"],
        "reason": decision["reason"],
        "confidence": decision["confidence"],
        "requiresHumanApproval": bool(decision.get("requiresHumanApproval")),
        "approvalSource": "policy_same_channel" if action == "stay_current" else "",
        "case": decision["case"],
        "scores": decision["scores"],
        "evidence": decision["evidence"],
        "policyVersion": POLICY_VERSION,
    }
    event = make_domain_event(
        event_type,
        source,
        subject_type="case_route",
        subject_id=decision["caseId"],
        data=data,
        confidence=clamp(decision["confidence"], 0.5),
        summary=summary,
        source_domain=source_domain,
        autonomy_level=3,
        limitations=["Channel Routing only proposes/records route decisions; Hands must not move chats from this event alone."],
    )
    raw = compact_json({"eventType": event_type, "decisionId": decision["decisionId"]})
    event["eventId"] = f"routeevt-{stable_hash(raw, 24)}"
    event["idempotencyKey"] = f"{event_type}:{stable_hash(raw, 32)}"
    event["correlationId"] = decision["caseId"]
    event["caseId"] = decision["caseId"]
    event["customerId"] = decision["customerId"]
    event["channelId"] = decision["sourceChannelId"]
    event["conversationId"] = (decision["case"].get("conversations") or [None])[0]
    event["causationId"] = source["eventId"]
    if action in {"stay_current", "reject_transfer"}:
        event["risk"]["riskLevel"] = "low"
        event["risk"]["riskReasons"] = []
        event["risk"]["allowedActions"] = ["record_audit", "continue_cycle"]
        event["risk"]["blockedActions"] = []
        event["requiresHumanReview"] = False
    else:
        event["risk"]["riskLevel"] = "high"
        event["risk"]["riskReasons"] = sorted(set(event["risk"].get("riskReasons", []) + ["cross_channel_context"]))
        event["risk"]["blockedActions"] = sorted(set(event["risk"].get("blockedActions", []) + ["move_or_message_without_human_approval"]))
        event["requiresHumanReview"] = True
    errors = validate_contract(event, "domain_event")
    if errors:
        raise ValueError("; ".join(errors))
    return event


def build_human_report(summary: dict[str, Any]) -> dict[str, Any]:
    latest = summary.get("latest") if isinstance(summary.get("latest"), list) else []
    proposed = [item for item in latest if isinstance(item, dict) and item.get("action") in {"propose_transfer", "propose_merge"}]
    approved = [item for item in latest if isinstance(item, dict) and item.get("action") == "stay_current"]
    needs = [item for item in latest if isinstance(item, dict) and item.get("requires_human")]
    risks: list[str] = []
    if summary.get("needsHuman", 0):
        risks.append("Las rutas entre WhatsApps necesitan confirmacion antes de mover contexto o responder.")
    if summary.get("duplicateGroups", 0):
        risks.append("Hay posibles clientes repetidos entre canales; fusionar sin evidencia puede mezclar conversaciones.")
    return {
        "quePaso": (
            f"Channel Routing analizo {summary.get('casesRead', 0)} casos, "
            f"propuso {summary.get('proposedRoutes', 0)} rutas, aprobo {summary.get('approvedRoutes', 0)} "
            f"y marco {summary.get('needsHuman', 0)} para Bryams."
        ),
        "rutasPropuestas": proposed[:8],
        "rutasAprobadas": approved[:8],
        "necesitanBryams": needs[:8],
        "riesgos": risks,
    }


def run_channel_routing_once(
    case_manager_db: Path,
    route_events_file: Path,
    state_file: Path,
    db_path: Path,
    report_file: Path | None = None,
    policy_file: Path | None = None,
    limit: int = 500,
) -> dict[str, Any]:
    policy = load_policy(policy_file)
    store = ChannelRoutingStore(db_path, route_events_file)
    ingested = {"cases": 0, "duplicates": 0, "skipped": 0, "decisions": 0, "routeEvents": 0}
    emitted_counts: Counter[str] = Counter()
    cases = read_cases(case_manager_db)[: max(1, limit)]
    groups = duplicate_groups(cases)
    try:
        for case in cases:
            ingested["cases"] += 1
            decision = decide_route(case, cases, policy)
            if decision["action"] == "skip":
                ingested["skipped"] += 1
                continue
            if store.seen(decision["decisionId"]):
                ingested["duplicates"] += 1
                continue
            route_event = make_route_event(decision)
            if store.save_decision(decision, route_event):
                ingested["decisions"] += 1
                if route_event is not None:
                    ingested["routeEvents"] += 1
                    emitted_counts[route_event["eventType"]] += 1

        summary = store.summary(len(cases), len(groups))
        status = "idle" if not cases else "attention" if summary.get("needsHuman", 0) else "ok"
        human_report = build_human_report(summary)
        state = {
            "status": status,
            "engine": "ariadgsm_channel_routing_brain",
            "version": SCHEMA_VERSION,
            "updatedAt": utc_now(),
            "caseManagerDb": str(case_manager_db),
            "routeEventsFile": str(route_events_file),
            "db": str(db_path),
            "policy": policy,
            "ingested": ingested,
            "summary": {**summary, "emittedByType": dict(emitted_counts)},
            "humanReport": human_report,
        }
        errors = validate_contract(state, "channel_routing_state")
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
    parser = argparse.ArgumentParser(description="AriadGSM Channel Routing Brain")
    parser.add_argument("--case-manager-db", default="runtime/case-manager.sqlite")
    parser.add_argument("--route-events", default="runtime/route-events.jsonl")
    parser.add_argument("--state-file", default="runtime/channel-routing-state.json")
    parser.add_argument("--report-file", default="runtime/channel-routing-report.json")
    parser.add_argument("--db", default="runtime/channel-routing.sqlite")
    parser.add_argument("--policy-file", default="")
    parser.add_argument("--limit", type=int, default=500)
    parser.add_argument("--json", action="store_true")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    args = parse_args(argv)
    policy_path = resolve_runtime_path(args.policy_file) if args.policy_file else None
    state = run_channel_routing_once(
        resolve_runtime_path(args.case_manager_db),
        resolve_runtime_path(args.route_events),
        resolve_runtime_path(args.state_file),
        resolve_runtime_path(args.db),
        report_file=resolve_runtime_path(args.report_file),
        policy_file=policy_path,
        limit=max(1, args.limit),
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=False, indent=2))
    else:
        summary = state["summary"]
        print(
            "AriadGSM Channel Routing: "
            f"cases={summary['casesRead']} "
            f"proposed={summary['proposedRoutes']} "
            f"approved={summary['approvedRoutes']} "
            f"needs_human={summary['needsHuman']} "
            f"events={summary['emittedRouteEvents']}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
