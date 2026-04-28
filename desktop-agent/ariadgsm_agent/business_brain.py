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
from .text import clean_text, normalize


AGENT_ROOT = Path(__file__).resolve().parents[1]
VERSION = "0.8.13"

BUSINESS_OBJECTIVES = (
    "protect_revenue",
    "answer_customer_fast",
    "preserve_accounting_evidence",
    "route_to_best_channel",
    "learn_business_patterns",
    "avoid_unsafe_autonomy",
)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def stable_hash(value: str, length: int = 24) -> str:
    return hashlib.sha1(value.encode("utf-8")).hexdigest()[:length]


def clamp(value: Any, default: float = 0.5, minimum: float = 0.0, maximum: float = 1.0) -> float:
    try:
        number = float(value)
    except (TypeError, ValueError):
        number = default
    return max(minimum, min(maximum, number))


def read_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}
    return value if isinstance(value, dict) else {}


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    temp.replace(path)


def append_jsonl(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")


def resolve_runtime_path(value: str | Path) -> Path:
    path = Path(value).expanduser()
    if path.is_absolute():
        return path
    return (AGENT_ROOT / path).resolve()


def json_list(value: Any) -> list[Any]:
    if isinstance(value, list):
        return value
    if not value:
        return []
    try:
        parsed = json.loads(str(value))
    except (json.JSONDecodeError, TypeError):
        return []
    return parsed if isinstance(parsed, list) else []


def sqlite_rows(db_path: Path, sql: str, args: tuple[Any, ...] = ()) -> list[dict[str, Any]]:
    if not db_path.exists():
        return []
    try:
        conn = sqlite3.connect(db_path)
        conn.row_factory = sqlite3.Row
        try:
            return [dict(row) for row in conn.execute(sql, args).fetchall()]
        finally:
            conn.close()
    except sqlite3.Error:
        return []


def load_cases(case_manager_db: Path, limit: int) -> list[dict[str, Any]]:
    rows = sqlite_rows(
        case_manager_db,
        """
        select *
        from cases
        where status not in ('ignored', 'closed')
        order by priority_score desc, updated_at desc
        limit ?
        """,
        (max(1, limit),),
    )
    for row in rows:
        row["channels"] = json_list(row.get("channels_json"))
        row["conversations"] = json_list(row.get("conversations_json"))
        row["linkedEventIds"] = json_list(row.get("linked_event_ids_json"))
    return rows


def load_memory_items(memory_db: Path, limit: int) -> list[dict[str, Any]]:
    rows = sqlite_rows(
        memory_db,
        """
        select memory_id, memory_type, truth_status, source_key, source_event_type,
               conversation_id, case_id, customer_id, title, summary, confidence,
               evidence_json, tags_json, review_required, updated_at
        from living_memory_items
        order by updated_at desc
        limit ?
        """,
        (max(1, limit),),
    )
    for row in rows:
        row["evidence"] = json_list(row.get("evidence_json"))
        row["tags"] = [clean_text(item) for item in json_list(row.get("tags_json")) if clean_text(item)]
        row["confidence"] = clamp(row.get("confidence"))
        row["reviewRequired"] = bool(row.get("review_required"))
    return rows


def load_route_decisions(route_db: Path, limit: int) -> dict[str, dict[str, Any]]:
    rows = sqlite_rows(
        route_db,
        """
        select decision_id, case_id, source_channel_id, target_channel_id, target_case_id,
               action, route_kind, confidence, requires_human, reason, created_at
        from route_decisions
        order by created_at desc
        limit ?
        """,
        (max(1, limit),),
    )
    latest: dict[str, dict[str, Any]] = {}
    for row in rows:
        case_id = clean_text(row.get("case_id"))
        if case_id and case_id not in latest:
            row["confidence"] = clamp(row.get("confidence"))
            row["requiresHuman"] = bool(row.get("requires_human"))
            latest[case_id] = row
    return latest


def load_accounting_records(accounting_db: Path, limit: int) -> dict[str, list[dict[str, Any]]]:
    rows = sqlite_rows(
        accounting_db,
        """
        select record_id, case_id, customer_id, channel_id, conversation_id,
               record_kind, status, amount, currency, method, evidence_level,
               evidence_count, confidence, requires_human, ambiguous, reason, updated_at
        from accounting_records
        order by updated_at desc
        limit ?
        """,
        (max(1, limit),),
    )
    by_case: dict[str, list[dict[str, Any]]] = {}
    for row in rows:
        case_id = clean_text(row.get("case_id"))
        if not case_id:
            continue
        row["confidence"] = clamp(row.get("confidence"))
        row["requiresHuman"] = bool(row.get("requires_human"))
        row["ambiguous"] = bool(row.get("ambiguous"))
        by_case.setdefault(case_id, []).append(row)
    return by_case


def case_value(case: dict[str, Any], key: str) -> str:
    return clean_text(case.get(key))


def case_tokens(case: dict[str, Any]) -> set[str]:
    values = [
        case_value(case, "case_id"),
        case_value(case, "customer_id"),
        case_value(case, "title"),
        case_value(case, "country"),
        case_value(case, "service"),
        case_value(case, "device"),
        case_value(case, "intent"),
        case_value(case, "summary"),
    ]
    tokens: set[str] = set()
    for value in values:
        normalized = normalize(value)
        if normalized:
            tokens.update(part for part in normalized.split() if len(part) > 2)
    return tokens


def memory_for_case(case: dict[str, Any], memory_items: list[dict[str, Any]], limit: int = 12) -> list[dict[str, Any]]:
    case_id = case_value(case, "case_id")
    conversation_ids = {case_value(case, "conversation_id"), *[clean_text(item) for item in case.get("conversations", [])]}
    customer_id = case_value(case, "customer_id")
    tokens = case_tokens(case)
    matched: list[tuple[int, dict[str, Any]]] = []
    for item in memory_items:
        score = 0
        if case_id and clean_text(item.get("case_id")) == case_id:
            score += 8
        if clean_text(item.get("conversation_id")) in conversation_ids and clean_text(item.get("conversation_id")):
            score += 6
        if customer_id and clean_text(item.get("customer_id")) == customer_id:
            score += 5
        haystack = normalize(" ".join([clean_text(item.get("summary")), clean_text(item.get("title")), " ".join(item.get("tags", []))]))
        score += min(4, sum(1 for token in tokens if token and token in haystack))
        if score:
            matched.append((score, item))
    matched.sort(key=lambda pair: (pair[0], pair[1].get("confidence", 0.0)), reverse=True)
    return [item for _, item in matched[:limit]]


def infer_business_intent(case: dict[str, Any], memories: list[dict[str, Any]], accounting: list[dict[str, Any]], route: dict[str, Any] | None) -> str:
    if route and clean_text(route.get("action")) in {"propose_transfer", "propose_merge"}:
        return "channel_route_review"
    if accounting:
        return "accounting_review"
    intent = normalize(case_value(case, "intent"))
    quote_state = normalize(case_value(case, "quote_state"))
    payment_state = normalize(case_value(case, "payment_state"))
    memory_text = normalize(" ".join(clean_text(item.get("summary")) for item in memories[:8]))
    if "payment" in intent or "pago" in payment_state or "deuda" in memory_text or "usdt" in memory_text:
        return "accounting_review"
    if "price" in intent or "precio" in memory_text or "quote" in quote_state or "cuanto" in memory_text:
        return "quote_or_price"
    if case_value(case, "service") or "service" in intent:
        return "service_triage"
    return "business_context_review"


def missing_information(case: dict[str, Any], intent: str, accounting: list[dict[str, Any]]) -> list[str]:
    missing: list[str] = []
    if intent in {"quote_or_price", "service_triage"}:
        if not case_value(case, "service"):
            missing.append("servicio")
        if not case_value(case, "device"):
            missing.append("modelo/dispositivo")
        if not case_value(case, "country"):
            missing.append("pais/mercado")
    if intent == "accounting_review":
        if not accounting:
            missing.append("registro contable vinculado")
        elif any(clean_text(row.get("evidence_level")) != "A" for row in accounting):
            missing.append("evidencia contable nivel A")
    return missing


def confidence_for_case(case: dict[str, Any], memories: list[dict[str, Any]], accounting: list[dict[str, Any]], missing: list[str]) -> float:
    base = clamp(case.get("confidence"), 0.55)
    memory_conf = max([clamp(item.get("confidence")) for item in memories] or [0.45])
    accounting_conf = max([clamp(row.get("confidence")) for row in accounting] or [0.55])
    uncertain_penalty = min(
        0.22,
        0.04
        * sum(
            1
            for item in memories
            if clean_text(item.get("truth_status")) in {"hypothesis", "uncertain", "deprecated", "conflict"}
            or bool(item.get("reviewRequired"))
        ),
    )
    missing_penalty = min(0.22, 0.05 * len(missing))
    return clamp(
        (base * 0.45) + (memory_conf * 0.35) + (accounting_conf * 0.20) - uncertain_penalty - missing_penalty,
        minimum=0.25,
        maximum=0.97,
    )


def priority_for(case: dict[str, Any], intent: str, accounting: list[dict[str, Any]], route: dict[str, Any] | None) -> str:
    if route and bool(route.get("requiresHuman")):
        return "high"
    if intent == "accounting_review" or accounting:
        return "high"
    if intent == "quote_or_price":
        return "medium"
    score = int(case.get("priority_score") or 0)
    if score >= 8:
        return "high"
    if score >= 5:
        return "medium"
    return "low"


def proposed_action(intent: str, missing: list[str], accounting: list[dict[str, Any]], route: dict[str, Any] | None) -> tuple[str, bool]:
    if route and clean_text(route.get("action")) in {"propose_transfer", "propose_merge"}:
        return "review_channel_route_before_moving_context", True
    if intent == "accounting_review":
        return "review_accounting_evidence_before_recording" if accounting else "ask_for_payment_context", True
    if intent == "quote_or_price":
        return ("ask_missing_pricing_context" if missing else "prepare_quote_draft"), True
    if intent == "service_triage":
        return ("ask_missing_service_context" if missing else "prepare_service_next_step"), True
    return "keep_learning_and_observing", False


def draft_reply(intent: str, missing: list[str], case: dict[str, Any]) -> str:
    if intent == "quote_or_price":
        if missing:
            return "Para cotizar bien, necesito confirmar: " + ", ".join(missing) + "."
        return "Tengo el contexto base para preparar una cotizacion; falta que Bryams confirme precio final antes de responder."
    if intent == "accounting_review":
        return "Antes de marcar pago/deuda, necesito comprobante o confirmacion de Bryams."
    if intent == "service_triage":
        return "Puedo preparar el siguiente paso del servicio, pero Bryams debe confirmar herramienta/procedimiento."
    return f"Sigo observando el caso {case_value(case, 'title') or case_value(case, 'case_id')} y acumulando contexto."


def build_recommendation(
    case: dict[str, Any],
    memories: list[dict[str, Any]],
    accounting: list[dict[str, Any]],
    route: dict[str, Any] | None,
    autonomy_level: int,
) -> dict[str, Any]:
    intent = infer_business_intent(case, memories, accounting, route)
    missing = missing_information(case, intent, accounting)
    action, requires_human = proposed_action(intent, missing, accounting, route)
    confidence = confidence_for_case(case, memories, accounting, missing)
    priority = priority_for(case, intent, accounting, route)
    risk_level = "high" if requires_human and intent in {"accounting_review", "channel_route_review"} else case_value(case, "risk_level") or "medium"
    evidence: list[str] = []
    if case_value(case, "last_event_id"):
        evidence.append(case_value(case, "last_event_id"))
    evidence.extend(clean_text(item.get("source_key")) for item in memories[:6] if clean_text(item.get("source_key")))
    evidence.extend(clean_text(row.get("record_id")) for row in accounting[:3] if clean_text(row.get("record_id")))
    if route and clean_text(route.get("decision_id")):
        evidence.append(clean_text(route.get("decision_id")))
    evidence = list(dict.fromkeys(evidence))
    raw_id = "|".join([case_value(case, "case_id"), intent, action, ",".join(evidence[:6])])
    recommendation_id = f"business-{stable_hash(raw_id, 28)}"
    rationale_bits = [
        f"Intento={intent}",
        f"accion={action}",
        f"memorias_consultadas={len(memories)}",
        f"faltantes={', '.join(missing) if missing else 'ninguno'}",
    ]
    if route:
        rationale_bits.append(f"ruta={clean_text(route.get('action'))}:{clean_text(route.get('target_channel_id'))}")
    if accounting:
        rationale_bits.append(f"contabilidad={len(accounting)} registro(s)")
    return {
        "recommendationId": recommendation_id,
        "caseId": case_value(case, "case_id"),
        "channelId": case_value(case, "primary_channel_id"),
        "conversationId": (case.get("conversations") or [""])[0] if isinstance(case.get("conversations"), list) and case.get("conversations") else "",
        "customerId": case_value(case, "customer_id"),
        "title": case_value(case, "title"),
        "intent": intent,
        "priority": priority,
        "riskLevel": risk_level,
        "confidence": confidence,
        "autonomyLevel": min(6, max(1, autonomy_level)),
        "proposedAction": action,
        "requiresHumanConfirmation": requires_human,
        "rationale": "; ".join(rationale_bits),
        "missingInformation": missing,
        "replyDraft": draft_reply(intent, missing, case),
        "evidence": evidence,
        "memory": [
            {
                "memoryId": clean_text(item.get("memory_id")),
                "type": clean_text(item.get("memory_type")),
                "status": clean_text(item.get("truth_status")),
                "confidence": clamp(item.get("confidence")),
                "summary": clean_text(item.get("summary"))[:260],
                "sourceKey": clean_text(item.get("source_key")),
            }
            for item in memories[:8]
        ],
        "accounting": [
            {
                "recordId": clean_text(row.get("record_id")),
                "kind": clean_text(row.get("record_kind")),
                "status": clean_text(row.get("status")),
                "amount": row.get("amount"),
                "currency": clean_text(row.get("currency")),
                "evidenceLevel": clean_text(row.get("evidence_level")),
                "requiresHuman": bool(row.get("requiresHuman")),
            }
            for row in accounting[:5]
        ],
        "route": {
            "decisionId": clean_text(route.get("decision_id")) if route else "",
            "action": clean_text(route.get("action")) if route else "",
            "targetChannelId": clean_text(route.get("target_channel_id")) if route else "",
            "requiresHuman": bool(route.get("requiresHuman")) if route else False,
        },
    }


def decision_event_from_recommendation(recommendation: dict[str, Any]) -> dict[str, Any]:
    return {
        "eventType": "decision_event",
        "decisionId": f"business-decision-{recommendation['recommendationId'].removeprefix('business-')}",
        "createdAt": utc_now(),
        "goal": "business_reasoning",
        "intent": recommendation["intent"],
        "confidence": recommendation["confidence"],
        "autonomyLevel": recommendation["autonomyLevel"],
        "proposedAction": recommendation["proposedAction"],
        "requiresHumanConfirmation": recommendation["requiresHumanConfirmation"],
        "reasoningSummary": recommendation["rationale"],
        "evidence": recommendation["evidence"],
        "caseId": recommendation["caseId"],
        "channelId": recommendation["channelId"],
        "conversationId": recommendation["conversationId"],
        "customerId": recommendation["customerId"],
        "recommendationId": recommendation["recommendationId"],
        "replyDraft": recommendation["replyDraft"],
        "riskLevel": recommendation["riskLevel"],
    }


class BusinessBrainStore:
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
            create table if not exists business_recommendations (
              recommendation_id text primary key,
              case_id text,
              intent text not null,
              proposed_action text not null,
              priority text not null,
              confidence real not null,
              requires_human integer not null,
              risk_level text not null,
              recommendation_json text not null,
              decision_event_id text,
              created_at text not null,
              updated_at text not null
            );
            create index if not exists idx_business_recommendations_case on business_recommendations(case_id);
            create index if not exists idx_business_recommendations_intent on business_recommendations(intent);
            """
        )
        self.conn.commit()

    def save_recommendation(self, recommendation: dict[str, Any], decision_event: dict[str, Any]) -> bool:
        now = utc_now()
        recommendation_id = recommendation["recommendationId"]
        existing = self.conn.execute(
            "select recommendation_id from business_recommendations where recommendation_id = ? limit 1",
            (recommendation_id,),
        ).fetchone()
        with self.conn:
            self.conn.execute(
                """
                insert into business_recommendations (
                  recommendation_id, case_id, intent, proposed_action, priority,
                  confidence, requires_human, risk_level, recommendation_json,
                  decision_event_id, created_at, updated_at
                ) values (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                on conflict(recommendation_id) do update set
                  confidence = excluded.confidence,
                  requires_human = excluded.requires_human,
                  risk_level = excluded.risk_level,
                  recommendation_json = excluded.recommendation_json,
                  updated_at = excluded.updated_at
                """,
                (
                    recommendation_id,
                    recommendation["caseId"],
                    recommendation["intent"],
                    recommendation["proposedAction"],
                    recommendation["priority"],
                    recommendation["confidence"],
                    1 if recommendation["requiresHumanConfirmation"] else 0,
                    recommendation["riskLevel"],
                    json.dumps(recommendation, ensure_ascii=False, separators=(",", ":")),
                    decision_event["decisionId"],
                    now,
                    now,
                ),
            )
        return existing is None

    def summary(self) -> dict[str, Any]:
        def scalar(sql: str) -> int:
            row = self.conn.execute(sql).fetchone()
            return int(row[0] if row else 0)

        by_intent = {
            row["intent"]: int(row["count"])
            for row in self.conn.execute(
                "select intent, count(*) as count from business_recommendations group by intent"
            ).fetchall()
        }
        return {
            "storedRecommendations": scalar("select count(*) from business_recommendations"),
            "requiresHuman": scalar("select count(*) from business_recommendations where requires_human = 1"),
            "highRisk": scalar("select count(*) from business_recommendations where risk_level in ('high', 'critical')"),
            "byIntent": by_intent,
            "db": str(self.db_path),
        }


def build_mental_model(cases: list[dict[str, Any]], memory_items: list[dict[str, Any]], recommendations: list[dict[str, Any]]) -> dict[str, Any]:
    services = Counter(case_value(case, "service") for case in cases if case_value(case, "service"))
    countries = Counter(case_value(case, "country") for case in cases if case_value(case, "country"))
    customers = Counter(case_value(case, "customer_id") for case in cases if case_value(case, "customer_id"))
    memory_layers = Counter(clean_text(item.get("memory_type")) for item in memory_items if clean_text(item.get("memory_type")))
    uncertainties = [
        {
            "memoryId": clean_text(item.get("memory_id")),
            "type": clean_text(item.get("memory_type")),
            "status": clean_text(item.get("truth_status")),
            "summary": clean_text(item.get("summary"))[:220],
            "confidence": clamp(item.get("confidence")),
        }
        for item in memory_items
        if clean_text(item.get("truth_status")) in {"hypothesis", "uncertain", "deprecated", "conflict"} or bool(item.get("reviewRequired"))
    ][:10]
    return {
        "activeCases": len(cases),
        "customers": [{"customerId": key, "cases": count} for key, count in customers.most_common(10)],
        "services": [{"service": key, "cases": count} for key, count in services.most_common(10)],
        "markets": [{"country": key, "cases": count} for key, count in countries.most_common(10)],
        "memoryLayersConsulted": dict(memory_layers),
        "memoryItemsConsulted": len(memory_items),
        "uncertainties": uncertainties,
        "topPriorities": [
            {
                "caseId": item["caseId"],
                "title": item["title"],
                "intent": item["intent"],
                "priority": item["priority"],
                "requiresHumanConfirmation": item["requiresHumanConfirmation"],
            }
            for item in recommendations[:8]
        ],
    }


def build_human_report(state_summary: dict[str, Any], recommendations: list[dict[str, Any]], mental_model: dict[str, Any]) -> dict[str, Any]:
    if not recommendations:
        return {
            "headline": "Estoy pensando, pero aun no tengo casos activos para decidir",
            "queEntendi": [f"Memorias consultadas: {mental_model['memoryItemsConsulted']}."],
            "quePropongo": [],
            "queDudo": [item["summary"] for item in mental_model["uncertainties"][:5]],
            "queNecesitoDeBryams": ["Abrir o leer casos reales para generar propuestas de negocio."],
        }
    return {
        "headline": "Ya puedo razonar sobre casos de AriadGSM sin mover las manos",
        "queEntendi": [
            f"Casos activos: {mental_model['activeCases']}.",
            f"Memorias consultadas: {mental_model['memoryItemsConsulted']}.",
            f"Recomendaciones: {state_summary['recommendations']}.",
        ],
        "quePropongo": [
            f"{item['title'] or item['caseId']}: {item['proposedAction']} ({item['intent']}, confianza {item['confidence']:.2f})"
            for item in recommendations[:8]
        ],
        "queDudo": [
            f"{item['title'] or item['caseId']}: falta {', '.join(item['missingInformation'])}"
            for item in recommendations
            if item["missingInformation"]
        ][:8],
        "queNecesitoDeBryams": [
            f"Confirmar: {item['title'] or item['caseId']} -> {item['proposedAction']}"
            for item in recommendations
            if item["requiresHumanConfirmation"]
        ][:8],
    }


def run_business_brain_once(
    case_manager_db: Path,
    memory_db: Path,
    route_db: Path,
    accounting_db: Path,
    state_file: Path,
    decision_events_file: Path,
    recommendations_file: Path,
    db_path: Path,
    *,
    case_manager_state_file: Path | None = None,
    memory_state_file: Path | None = None,
    channel_routing_state_file: Path | None = None,
    accounting_state_file: Path | None = None,
    autonomy_level: int = 1,
    limit: int = 200,
) -> dict[str, Any]:
    store = BusinessBrainStore(db_path)
    emitted_events: list[dict[str, Any]] = []
    try:
        cases = load_cases(case_manager_db, limit)
        memory_items = load_memory_items(memory_db, max(limit, 200))
        routes = load_route_decisions(route_db, limit)
        accounting = load_accounting_records(accounting_db, limit)

        recommendations: list[dict[str, Any]] = []
        for case in cases:
            case_id = case_value(case, "case_id")
            memories = memory_for_case(case, memory_items)
            recommendation = build_recommendation(
                case,
                memories,
                accounting.get(case_id, []),
                routes.get(case_id),
                autonomy_level,
            )
            recommendations.append(recommendation)

        priority_rank = {"high": 3, "medium": 2, "low": 1}
        recommendations.sort(key=lambda item: (priority_rank.get(item["priority"], 0), item["confidence"]), reverse=True)

        emitted = 0
        for recommendation in recommendations:
            decision_event = decision_event_from_recommendation(recommendation)
            if store.save_recommendation(recommendation, decision_event):
                append_jsonl(decision_events_file, decision_event)
                append_jsonl(recommendations_file, recommendation)
                emitted_events.append(decision_event)
                emitted += 1

        mental_model = build_mental_model(cases, memory_items, recommendations)
        summary = {
            **store.summary(),
            "activeCases": len(cases),
            "recommendations": len(recommendations),
            "emittedDecisionEvents": emitted,
            "memoryItemsRead": len(memory_items),
            "routeDecisionsRead": len(routes),
            "accountingCasesRead": len(accounting),
            "quoteRecommendations": sum(1 for item in recommendations if item["intent"] == "quote_or_price"),
            "accountingRecommendations": sum(1 for item in recommendations if item["intent"] == "accounting_review"),
            "routeRecommendations": sum(1 for item in recommendations if item["intent"] == "channel_route_review"),
            "missingInfoRecommendations": sum(1 for item in recommendations if item["missingInformation"]),
        }
        status = "idle" if not cases else "attention" if summary["requiresHuman"] else "ok"
        state = {
            "status": status,
            "engine": "ariadgsm_business_brain",
            "version": VERSION,
            "updatedAt": utc_now(),
            "contract": "business_brain_state",
            "policy": {
                "objectives": list(BUSINESS_OBJECTIVES),
                "decisionMode": "recommend_only_no_physical_action",
                "usesLivingMemory": True,
                "requiresTrustSafetyBeforeAction": True,
                "customerFacingDraftsRequireHuman": True,
            },
            "sourceFiles": {
                "caseManagerDb": str(case_manager_db),
                "memoryDb": str(memory_db),
                "routeDb": str(route_db),
                "accountingDb": str(accounting_db),
                "caseManagerState": str(case_manager_state_file or ""),
                "memoryState": str(memory_state_file or ""),
                "channelRoutingState": str(channel_routing_state_file or ""),
                "accountingState": str(accounting_state_file or ""),
            },
            "outputFiles": {
                "decisionEvents": str(decision_events_file),
                "recommendations": str(recommendations_file),
                "db": str(db_path),
            },
            "ingested": {
                "casesRead": len(cases),
                "memoryItemsRead": len(memory_items),
                "routeDecisionsRead": len(routes),
                "accountingRecordsRead": sum(len(items) for items in accounting.values()),
                "recommendations": len(recommendations),
                "emittedDecisionEvents": emitted,
            },
            "summary": summary,
            "mentalModel": mental_model,
            "recommendations": recommendations[:25],
            "emittedDecisionEvents": emitted_events,
            "humanReport": build_human_report(summary, recommendations, mental_model),
        }
        errors = validate_contract(state, "business_brain_state")
        if errors:
            state["status"] = "blocked"
            state["contractErrors"] = errors
        write_json(state_file, state)
        return state
    finally:
        store.close()


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM Business Brain")
    parser.add_argument("--case-manager-db", default="runtime/case-manager.sqlite")
    parser.add_argument("--memory-db", default="runtime/memory-core.sqlite")
    parser.add_argument("--route-db", default="runtime/channel-routing.sqlite")
    parser.add_argument("--accounting-db", default="runtime/accounting-core.sqlite")
    parser.add_argument("--state-file", default="runtime/business-brain-state.json")
    parser.add_argument("--decision-events", default="runtime/business-decision-events.jsonl")
    parser.add_argument("--recommendations", default="runtime/business-recommendations.jsonl")
    parser.add_argument("--db", default="runtime/business-brain.sqlite")
    parser.add_argument("--case-manager-state", default="runtime/case-manager-state.json")
    parser.add_argument("--memory-state", default="runtime/memory-state.json")
    parser.add_argument("--channel-routing-state", default="runtime/channel-routing-state.json")
    parser.add_argument("--accounting-state", default="runtime/accounting-core-state.json")
    parser.add_argument("--autonomy-level", type=int, default=1)
    parser.add_argument("--limit", type=int, default=200)
    parser.add_argument("--json", action="store_true")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    args = parse_args(argv)
    state = run_business_brain_once(
        resolve_runtime_path(args.case_manager_db),
        resolve_runtime_path(args.memory_db),
        resolve_runtime_path(args.route_db),
        resolve_runtime_path(args.accounting_db),
        resolve_runtime_path(args.state_file),
        resolve_runtime_path(args.decision_events),
        resolve_runtime_path(args.recommendations),
        resolve_runtime_path(args.db),
        case_manager_state_file=resolve_runtime_path(args.case_manager_state),
        memory_state_file=resolve_runtime_path(args.memory_state),
        channel_routing_state_file=resolve_runtime_path(args.channel_routing_state),
        accounting_state_file=resolve_runtime_path(args.accounting_state),
        autonomy_level=args.autonomy_level,
        limit=max(1, args.limit),
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=False, indent=2))
    else:
        summary = state["summary"]
        print(
            "AriadGSM Business Brain: "
            f"cases={summary['activeCases']} "
            f"recommendations={summary['recommendations']} "
            f"emitted={summary['emittedDecisionEvents']} "
            f"requires_human={summary['requiresHuman']}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
