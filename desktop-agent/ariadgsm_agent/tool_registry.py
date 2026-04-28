from __future__ import annotations

import argparse
import hashlib
import json
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from .contracts import validate_contract
from .text import clean_text, normalize


AGENT_ROOT = Path(__file__).resolve().parents[1]
VERSION = "0.8.16"

RISK_RANK = {"low": 1, "medium": 2, "high": 3, "critical": 4}
STATUS_RANK = {"ready": 0, "manual_required": 1, "degraded": 2, "unknown": 3, "blocked": 9}
HIGH_RISK = {"high", "critical"}

DEFAULT_CATALOG: list[dict[str, Any]] = [
    {
        "toolId": "browser-whatsapp-reader",
        "name": "WhatsApp Web visible reader",
        "category": "local_browser",
        "status": "ready",
        "riskLevel": "medium",
        "capabilities": ["read_visible_chat", "capture_conversation", "channel_context"],
        "inputsNeeded": ["visible WhatsApp Web channel", "verified chat row when opening a chat"],
        "outputsProduced": ["visible messages", "chat title", "channel id", "evidence references"],
        "verifiers": ["reader_core_state", "hands_verification_state", "visible_message_count"],
        "failureSignals": ["wrong_channel", "blocked_window", "no_visible_chat", "unverified_coordinates"],
        "alternatives": ["manual-operator-review"],
        "requiresHumanApproval": False,
        "executionOwner": "Hands & Verification",
        "notes": "Reads only visible WhatsApp content; it does not use cookies or private session secrets.",
    },
    {
        "toolId": "gsm-service-panel",
        "name": "Authorized GSM service panel",
        "category": "business_panel",
        "status": "manual_required",
        "riskLevel": "high",
        "capabilities": ["service_order", "price_lookup", "supplier_status", "authorized_device_service"],
        "inputsNeeded": ["customer case", "service type", "country", "operator approval"],
        "outputsProduced": ["quote candidate", "order reference", "supplier status"],
        "verifiers": ["case_id", "supplier_reference", "operator_confirmation"],
        "failureSignals": ["login_required", "insufficient_credits", "service_unavailable", "price_changed"],
        "alternatives": ["pricing-reference-sheet", "manual-operator-review"],
        "requiresHumanApproval": True,
        "executionOwner": "Bryams",
        "notes": "Registry plans authorized service-panel work; execution remains human-approved.",
    },
    {
        "toolId": "usb-remote-session",
        "name": "Remote USB session coordinator",
        "category": "device_connectivity",
        "status": "manual_required",
        "riskLevel": "high",
        "capabilities": ["remote_usb", "device_forwarding", "connection_health_check"],
        "inputsNeeded": ["customer consent", "remote session id", "device model", "operator approval"],
        "outputsProduced": ["connection status", "device visible status", "session evidence"],
        "verifiers": ["device_seen", "session_alive", "operator_confirmation"],
        "failureSignals": ["usb_dropped", "driver_missing", "latency_high", "device_not_seen"],
        "alternatives": ["driver-package-manager", "manual-operator-review"],
        "requiresHumanApproval": True,
        "executionOwner": "Bryams",
        "notes": "Keeps remote USB as a capability with fallbacks instead of hardcoding one vendor.",
    },
    {
        "toolId": "driver-package-manager",
        "name": "Device driver package manager",
        "category": "local_device_support",
        "status": "manual_required",
        "riskLevel": "high",
        "capabilities": ["driver_install", "device_detection", "connection_repair"],
        "inputsNeeded": ["device brand", "Windows admin permission", "operator approval"],
        "outputsProduced": ["driver status", "device manager evidence"],
        "verifiers": ["device_manager_visible", "driver_version", "hands_verification_state"],
        "failureSignals": ["admin_required", "install_failed", "device_not_detected"],
        "alternatives": ["manual-operator-review"],
        "requiresHumanApproval": True,
        "executionOwner": "Bryams",
        "notes": "The registry can recommend the capability; driver installation is never automatic at this stage.",
    },
    {
        "toolId": "pricing-reference-sheet",
        "name": "AriadGSM pricing reference",
        "category": "business_reference",
        "status": "ready",
        "riskLevel": "medium",
        "capabilities": ["price_lookup", "market_reference", "quote_context"],
        "inputsNeeded": ["service type", "country", "device model"],
        "outputsProduced": ["price candidate", "confidence", "missing data"],
        "verifiers": ["business_brain_state", "living_memory_state", "operator_confirmation"],
        "failureSignals": ["missing_market", "stale_price", "conflicting_memory"],
        "alternatives": ["gsm-service-panel", "manual-operator-review"],
        "requiresHumanApproval": True,
        "executionOwner": "Business Brain",
        "notes": "Pricing suggestions remain drafts until Bryams approves the quote.",
    },
    {
        "toolId": "accounting-ledger-local",
        "name": "Local accounting ledger",
        "category": "accounting",
        "status": "ready",
        "riskLevel": "high",
        "capabilities": ["accounting_record", "payment_evidence", "debt_tracking", "refund_tracking"],
        "inputsNeeded": ["case id", "amount", "currency", "evidence level", "operator approval for confirmation"],
        "outputsProduced": ["draft accounting record", "evidence link", "ledger status"],
        "verifiers": ["accounting_core_state", "evidence_level_a", "case_manager_state"],
        "failureSignals": ["missing_evidence", "ambiguous_amount", "duplicate_record"],
        "alternatives": ["manual-operator-review"],
        "requiresHumanApproval": True,
        "executionOwner": "Accounting Core",
        "notes": "Drafting is local; confirmed payments require evidence-first approval.",
    },
    {
        "toolId": "manual-operator-review",
        "name": "Bryams manual review",
        "category": "human_override",
        "status": "ready",
        "riskLevel": "low",
        "capabilities": ["human_override", "risk_review", "fallback_decision", "operator_confirmation"],
        "inputsNeeded": ["case summary", "tool plan", "risk explanation"],
        "outputsProduced": ["approval", "correction", "manual outcome"],
        "verifiers": ["human_feedback_event", "safety_approval_event"],
        "failureSignals": ["operator_unavailable"],
        "alternatives": [],
        "requiresHumanApproval": False,
        "executionOwner": "Bryams",
        "notes": "Final fallback for high-risk or uncertain tool work.",
    },
]


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def stable_hash(value: str, length: int = 24) -> str:
    return hashlib.sha1(value.encode("utf-8")).hexdigest()[:length]


def resolve_runtime_path(value: str | Path) -> Path:
    path = Path(value).expanduser()
    if path.is_absolute():
        return path
    return (AGENT_ROOT / path).resolve()


def read_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, json.JSONDecodeError):
        return {}
    return value if isinstance(value, dict) else {}


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary.replace(path)


def append_jsonl(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")


def read_jsonl_tail(path: Path, limit: int) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    try:
        lines = path.read_text(encoding="utf-8-sig", errors="replace").splitlines()[-max(1, limit):]
    except OSError:
        return []
    events: list[dict[str, Any]] = []
    for line in lines:
        if not line.strip():
            continue
        try:
            item = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(item, dict):
            events.append(item)
    return events


def as_list(value: Any) -> list[Any]:
    return value if isinstance(value, list) else []


def normalized_list(value: Any) -> list[str]:
    return [clean_text(item) for item in as_list(value) if clean_text(item)]


def normalize_status(value: Any) -> str:
    raw = clean_text(value).lower()
    if raw in {"ready", "ok", "manual_required", "degraded", "blocked"}:
        return raw
    return "unknown"


def normalize_risk(value: Any) -> str:
    raw = clean_text(value).lower()
    return raw if raw in RISK_RANK else "medium"


def normalized_tool(raw: dict[str, Any]) -> dict[str, Any]:
    capabilities = normalized_list(raw.get("capabilities"))
    risk = normalize_risk(raw.get("riskLevel"))
    status = normalize_status(raw.get("status"))
    tool_id = clean_text(raw.get("toolId")) or f"tool-{stable_hash(clean_text(raw.get('name')) or json.dumps(raw, sort_keys=True), 16)}"
    return {
        "toolId": tool_id,
        "name": clean_text(raw.get("name")) or tool_id,
        "category": clean_text(raw.get("category")) or "uncategorized",
        "status": status,
        "riskLevel": risk,
        "capabilities": capabilities,
        "inputsNeeded": normalized_list(raw.get("inputsNeeded")),
        "outputsProduced": normalized_list(raw.get("outputsProduced")),
        "verifiers": normalized_list(raw.get("verifiers")),
        "failureSignals": normalized_list(raw.get("failureSignals")),
        "alternatives": normalized_list(raw.get("alternatives")),
        "requiresHumanApproval": bool(raw.get("requiresHumanApproval", risk in HIGH_RISK)),
        "executionOwner": clean_text(raw.get("executionOwner")) or "unassigned",
        "notes": clean_text(raw.get("notes")),
        "valid": bool(capabilities),
        "validationErrors": validate_tool(raw, capabilities, tool_id),
    }


def validate_tool(raw: dict[str, Any], capabilities: list[str], tool_id: str) -> list[str]:
    errors: list[str] = []
    for field in ("name", "riskLevel", "status"):
        if not clean_text(raw.get(field)):
            errors.append(f"{tool_id}: missing {field}")
    if not capabilities:
        errors.append(f"{tool_id}: missing capabilities")
    if not normalized_list(raw.get("verifiers")):
        errors.append(f"{tool_id}: missing verifiers")
    if clean_text(raw.get("secret")) or clean_text(raw.get("password")) or clean_text(raw.get("token")):
        errors.append(f"{tool_id}: catalog must not contain secrets")
    return errors


def load_catalog(catalog_file: Path) -> list[dict[str, Any]]:
    if not catalog_file.exists():
        write_json(catalog_file, {"version": VERSION, "tools": DEFAULT_CATALOG})
        return [normalized_tool(item) for item in DEFAULT_CATALOG]
    raw = read_json(catalog_file)
    tools = raw.get("tools") if isinstance(raw.get("tools"), list) else raw if isinstance(raw, list) else []
    if not tools:
        tools = DEFAULT_CATALOG
    return [normalized_tool(item) for item in tools if isinstance(item, dict)]


def capability_rows(tools: list[dict[str, Any]]) -> list[dict[str, Any]]:
    by_capability: dict[str, dict[str, Any]] = {}
    for tool in tools:
        for capability in tool["capabilities"]:
            row = by_capability.setdefault(
                capability,
                {
                    "capability": capability,
                    "tools": [],
                    "bestStatus": "unknown",
                    "highestRisk": "low",
                    "requiresHumanApproval": False,
                },
            )
            row["tools"].append(tool["toolId"])
            if STATUS_RANK.get(tool["status"], 9) < STATUS_RANK.get(row["bestStatus"], 9):
                row["bestStatus"] = tool["status"]
            if RISK_RANK[tool["riskLevel"]] > RISK_RANK[row["highestRisk"]]:
                row["highestRisk"] = tool["riskLevel"]
            row["requiresHumanApproval"] = bool(row["requiresHumanApproval"] or tool["requiresHumanApproval"])
    return sorted(by_capability.values(), key=lambda item: item["capability"])


def extract_text_blob(event: dict[str, Any]) -> str:
    parts = [
        clean_text(event.get("intent")),
        clean_text(event.get("proposedAction")),
        clean_text(event.get("reasoningSummary")),
        clean_text(event.get("replyDraft")),
        clean_text(event.get("rationale")),
        clean_text(event.get("title")),
        clean_text(event.get("summary")),
        json.dumps(event.get("data") or {}, ensure_ascii=False) if isinstance(event.get("data"), dict) else "",
    ]
    missing = event.get("missingInformation")
    if isinstance(missing, list):
        parts.extend(clean_text(item) for item in missing)
    return normalize(" ".join(part for part in parts if part))


def infer_capabilities(event: dict[str, Any]) -> list[str]:
    text_blob = extract_text_blob(event)
    capabilities: list[str] = []
    checks = [
        ("remote_usb", ("usb", "redirector", "remote usb", "device forwarding", "conexion remota")),
        ("driver_install", ("driver", "controlador", "device manager", "dispositivo no detectado")),
        ("service_order", ("servicio", "orden", "server", "panel", "proveedor", "authorized service")),
        ("authorized_device_service", ("flasheo", "flash", "xiaomi", "samsung", "motorola", "reparacion", "liberacion autorizada")),
        ("price_lookup", ("precio", "cotizar", "quote", "price", "tarifa", "market")),
        ("accounting_record", ("pago", "deuda", "reembolso", "contabilidad", "payment", "debt", "refund")),
        ("capture_conversation", ("leer chat", "historial", "capturar conversacion", "scroll", "read chat")),
    ]
    for capability, tokens in checks:
        if any(token in text_blob for token in tokens):
            capabilities.append(capability)
    return list(dict.fromkeys(capabilities))


def request_from_event(event: dict[str, Any], source: str, index: int) -> dict[str, Any] | None:
    decision_id = clean_text(event.get("decisionId"))
    if source == "business_decision" and (decision_id.startswith("tool-registry-") or clean_text(event.get("intent")) == "external_tool_plan"):
        return None
    capabilities = infer_capabilities(event)
    if not capabilities:
        return None
    source_id = clean_text(event.get("recommendationId")) or decision_id or clean_text(event.get("eventId")) or f"{source}-{index}"
    primary = capabilities[0]
    return {
        "requestId": f"tool-request-{stable_hash(source + '|' + source_id + '|' + ','.join(capabilities), 20)}",
        "source": source,
        "sourceId": source_id,
        "capabilities": capabilities,
        "primaryCapability": primary,
        "caseId": clean_text(event.get("caseId")),
        "channelId": clean_text(event.get("channelId")),
        "conversationId": clean_text(event.get("conversationId")),
        "conversationTitle": clean_text(event.get("conversationTitle") or event.get("title")),
        "summary": clean_text(event.get("rationale") or event.get("reasoningSummary") or event.get("replyDraft") or event.get("summary")),
        "confidence": float(event.get("confidence") or 0.72) if isinstance(event.get("confidence"), (int, float)) else 0.72,
        "rawIntent": clean_text(event.get("intent")),
        "rawProposedAction": clean_text(event.get("proposedAction")),
    }


def load_requests(
    business_recommendations_file: Path,
    business_decisions_file: Path,
    domain_events_file: Path,
    limit: int,
) -> list[dict[str, Any]]:
    requests: list[dict[str, Any]] = []
    streams = [
        ("business_recommendation", read_jsonl_tail(business_recommendations_file, limit)),
        ("business_decision", read_jsonl_tail(business_decisions_file, limit)),
        ("domain_event", read_jsonl_tail(domain_events_file, limit)),
    ]
    seen: set[str] = set()
    for source, events in streams:
        for index, event in enumerate(events):
            request = request_from_event(event, source, index)
            if request is None or request["requestId"] in seen:
                continue
            requests.append(request)
            seen.add(request["requestId"])
    return requests[-limit:]


def select_tool(request: dict[str, Any], tools: list[dict[str, Any]]) -> tuple[dict[str, Any] | None, list[dict[str, Any]]]:
    capabilities = set(request["capabilities"])
    primary = clean_text(request.get("primaryCapability"))
    candidates = [tool for tool in tools if capabilities.intersection(tool["capabilities"]) and tool["valid"]]
    candidates.sort(
        key=lambda tool: (
            0 if primary in tool["capabilities"] else 1,
            STATUS_RANK.get(tool["status"], 9),
            RISK_RANK.get(tool["riskLevel"], 3),
            1 if tool["requiresHumanApproval"] else 0,
            tool["toolId"],
        )
    )
    selected = candidates[0] if candidates else None
    alternatives = candidates[1:4]
    if selected is not None:
        alternative_ids = set(selected.get("alternatives", []))
        alternatives.extend([tool for tool in tools if tool["toolId"] in alternative_ids and tool not in alternatives])
    return selected, alternatives[:5]


def build_tool_plan(request: dict[str, Any], tools: list[dict[str, Any]]) -> dict[str, Any]:
    selected, alternatives = select_tool(request, tools)
    if selected is None:
        return {
            "planId": f"tool-plan-{stable_hash(request['requestId'], 20)}",
            "requestId": request["requestId"],
            "status": "no_match",
            "capability": request["primaryCapability"],
            "selectedToolId": "",
            "selectedToolName": "",
            "fallbackToolIds": [],
            "riskLevel": "medium",
            "requiresHumanApproval": True,
            "handsActionType": "ask_human",
            "verificationRequired": True,
            "reason": f"No registered tool can satisfy {request['primaryCapability']}.",
            "request": request,
        }
    risk = selected["riskLevel"]
    needs_human = bool(selected["requiresHumanApproval"] or risk in HIGH_RISK or selected["status"] != "ready")
    status = "needs_human" if needs_human else "ready"
    if selected["status"] == "blocked":
        status = "blocked"
    return {
        "planId": f"tool-plan-{stable_hash(request['requestId'] + '|' + selected['toolId'], 20)}",
        "requestId": request["requestId"],
        "status": status,
        "capability": request["primaryCapability"],
        "selectedToolId": selected["toolId"],
        "selectedToolName": selected["name"],
        "fallbackToolIds": [tool["toolId"] for tool in alternatives],
        "riskLevel": risk,
        "requiresHumanApproval": needs_human,
        "handsActionType": "prepare_tool_plan",
        "verificationRequired": True,
        "reason": (
            f"{selected['name']} cubre {request['primaryCapability']} con estado {selected['status']} "
            f"y riesgo {risk}."
        ),
        "request": request,
        "requiredInputs": selected["inputsNeeded"],
        "expectedOutputs": selected["outputsProduced"],
        "verifiers": selected["verifiers"],
        "failureSignals": selected["failureSignals"],
    }


def existing_decision_ids(path: Path, limit: int) -> set[str]:
    return {clean_text(item.get("decisionId")) for item in read_jsonl_tail(path, limit * 4) if clean_text(item.get("decisionId"))}


def decision_event_from_plan(plan: dict[str, Any]) -> dict[str, Any]:
    request = plan["request"] if isinstance(plan.get("request"), dict) else {}
    decision_id = f"tool-registry-{stable_hash(plan['planId'], 24)}"
    evidence = [
        plan["planId"],
        request.get("sourceId", ""),
        f"capability:{plan['capability']}",
        f"tool:{plan.get('selectedToolId', '')}",
    ]
    evidence = [clean_text(item) for item in evidence if clean_text(item)]
    return {
        "eventType": "decision_event",
        "decisionId": decision_id,
        "createdAt": utc_now(),
        "goal": "select_authorized_tool_by_capability",
        "intent": "external_tool_plan",
        "confidence": min(0.96, max(0.55, float(request.get("confidence") or 0.72))),
        "autonomyLevel": 6 if plan["riskLevel"] in HIGH_RISK else 4,
        "proposedAction": "prepare_tool_plan",
        "requiresHumanConfirmation": True,
        "reasoningSummary": plan["reason"],
        "evidence": evidence,
        "caseId": clean_text(request.get("caseId")),
        "channelId": clean_text(request.get("channelId")),
        "conversationId": clean_text(request.get("conversationId")),
        "conversationTitle": clean_text(request.get("conversationTitle")),
        "risk": {
            "riskLevel": "critical" if plan["riskLevel"] == "critical" else plan["riskLevel"],
            "riskReasons": [
                "external tool capability",
                "operator approval required",
                "verification required before done",
            ],
        },
        "data": {
            "toolPlanId": plan["planId"],
            "capability": plan["capability"],
            "selectedToolId": plan.get("selectedToolId", ""),
            "fallbackToolIds": plan.get("fallbackToolIds", []),
            "verificationRequired": True,
        },
    }


def human_report(summary: dict[str, Any], plans: list[dict[str, Any]], tools: list[dict[str, Any]]) -> dict[str, Any]:
    if not plans:
        headline = "Tool Registry listo; aun no hay solicitudes de herramienta"
    elif summary["unmatchedRequests"]:
        headline = "Hay solicitudes sin herramienta registrada"
    elif summary["plansNeedHuman"]:
        headline = "Hay planes de herramienta listos, pero necesitan aprobacion"
    else:
        headline = "Tool Registry resolvio las capacidades solicitadas"
    return {
        "headline": headline,
        "queQuedoListo": [
            f"{summary['toolsRegistered']} herramientas registradas.",
            f"{summary['capabilitiesRegistered']} capacidades disponibles.",
            "Las herramientas se eligen por capacidad, no por parches por programa.",
        ],
        "quePuedeHacer": [
            f"{plan['capability']} -> {plan.get('selectedToolName') or 'sin herramienta'} ({plan['status']})"
            for plan in plans[:8]
        ],
        "queNecesitaBryams": [
            f"Aprobar o completar datos para {plan.get('selectedToolName') or plan['capability']}."
            for plan in plans
            if plan.get("requiresHumanApproval") or plan.get("status") in {"needs_human", "no_match", "blocked"}
        ][:8],
        "riesgos": [
            "El registro no ejecuta programas GSM por si solo.",
            "Toda herramienta de alto riesgo queda detras de Trust & Safety.",
            "El catalogo no debe guardar claves, tokens, cookies ni contrasenas.",
        ]
        + [f"{tool['toolId']}: {', '.join(tool['validationErrors'])}" for tool in tools if tool["validationErrors"]][:5],
    }


def run_tool_registry_once(
    catalog_file: Path,
    business_recommendations_file: Path,
    business_decisions_file: Path,
    domain_events_file: Path,
    hands_verification_state_file: Path,
    trust_safety_state_file: Path,
    state_file: Path,
    report_file: Path,
    *,
    limit: int = 500,
    emit_decisions: bool = True,
) -> dict[str, Any]:
    tools = load_catalog(catalog_file)
    capabilities = capability_rows(tools)
    requests = load_requests(business_recommendations_file, business_decisions_file, domain_events_file, limit)
    plans = [build_tool_plan(request, tools) for request in requests]
    existing_ids = existing_decision_ids(business_decisions_file, limit)
    emitted_events: list[dict[str, Any]] = []
    if emit_decisions:
        for plan in plans:
            if plan["status"] not in {"ready", "needs_human"}:
                continue
            event = decision_event_from_plan(plan)
            if event["decisionId"] in existing_ids:
                continue
            errors = validate_contract(event, "decision_event")
            if errors:
                plan["decisionErrors"] = errors
                continue
            append_jsonl(business_decisions_file, event)
            emitted_events.append(event)
            existing_ids.add(event["decisionId"])

    ready_tools = sum(1 for tool in tools if tool["status"] == "ready")
    degraded_tools = sum(1 for tool in tools if tool["status"] in {"manual_required", "degraded", "unknown"})
    blocked_tools = sum(1 for tool in tools if tool["status"] == "blocked" or tool["validationErrors"])
    matched = sum(1 for plan in plans if plan["status"] != "no_match")
    summary = {
        "toolsRegistered": len(tools),
        "capabilitiesRegistered": len(capabilities),
        "readyTools": ready_tools,
        "degradedTools": degraded_tools,
        "blockedTools": blocked_tools,
        "requestsRead": len(requests),
        "matchedRequests": matched,
        "unmatchedRequests": len(plans) - matched,
        "plansReady": sum(1 for plan in plans if plan["status"] == "ready"),
        "plansNeedHuman": sum(1 for plan in plans if plan.get("requiresHumanApproval")),
        "emittedDecisionEvents": len(emitted_events),
    }
    status = "idle" if not requests else "attention" if summary["plansNeedHuman"] or summary["unmatchedRequests"] or blocked_tools else "ok"
    if all(tool["validationErrors"] for tool in tools):
        status = "blocked"
    state = {
        "status": status,
        "engine": "ariadgsm_tool_registry",
        "version": VERSION,
        "updatedAt": utc_now(),
        "contract": "tool_registry_state",
        "policy": {
            "selectionMode": "capability_first_with_fallbacks",
            "executionMode": "plan_only_no_direct_execution",
            "trustSafetyRequired": True,
            "handsVerificationRequired": True,
            "leastPrivilege": True,
            "approvalRequiredForRisk": ["high", "critical"],
            "noSecretsInCatalog": True,
        },
        "sourceFiles": {
            "catalogFile": str(catalog_file),
            "businessRecommendationsFile": str(business_recommendations_file),
            "businessDecisionEventsFile": str(business_decisions_file),
            "domainEventsFile": str(domain_events_file),
            "handsVerificationStateFile": str(hands_verification_state_file),
            "trustSafetyStateFile": str(trust_safety_state_file),
        },
        "outputFiles": {
            "stateFile": str(state_file),
            "reportFile": str(report_file),
            "decisionEventsFile": str(business_decisions_file),
        },
        "summary": summary,
        "tools": tools,
        "capabilities": capabilities,
        "requests": requests[-50:],
        "toolPlans": plans[-50:],
        "emittedDecisionEvents": emitted_events,
        "handsIntegration": {
            "decisionEventContract": "decision_event",
            "handsConsumesDecisionEvents": True,
            "externalExecutionAllowedByRegistry": False,
            "verificationRequiredBeforeDone": True,
            "writesToDecisionEvents": emit_decisions,
            "handsDecisionFile": str(business_decisions_file),
            "handoff": "Tool Registry emits a plan decision; Trust & Safety and Hands decide if anything can move.",
        },
        "runtimeContext": {
            "handsVerificationStatus": clean_text(read_json(hands_verification_state_file).get("status")),
            "trustSafetyStatus": clean_text(read_json(trust_safety_state_file).get("status")),
        },
        "humanReport": human_report(summary, plans, tools),
    }
    errors = validate_contract(state, "tool_registry_state")
    if errors:
        state["status"] = "blocked"
        state["contractErrors"] = errors
    write_json(state_file, state)
    write_json(report_file, {"summary": summary, "plans": plans, "tools": tools, "generatedAt": state["updatedAt"]})
    return state


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM Tool Registry")
    parser.add_argument("--catalog-file", default="runtime/tool-registry-catalog.json")
    parser.add_argument("--business-recommendations", default="runtime/business-recommendations.jsonl")
    parser.add_argument("--business-decisions", default="runtime/business-decision-events.jsonl")
    parser.add_argument("--domain-events", default="runtime/domain-events.jsonl")
    parser.add_argument("--hands-verification-state", default="runtime/hands-verification-state.json")
    parser.add_argument("--trust-safety-state", default="runtime/trust-safety-state.json")
    parser.add_argument("--state-file", default="runtime/tool-registry-state.json")
    parser.add_argument("--report-file", default="runtime/tool-registry-report.json")
    parser.add_argument("--limit", type=int, default=500)
    parser.add_argument("--no-emit-decisions", action="store_true")
    parser.add_argument("--json", action="store_true")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    try:
        sys.stdout.reconfigure(encoding="utf-8")
    except Exception:
        pass
    args = parse_args(argv)
    state = run_tool_registry_once(
        resolve_runtime_path(args.catalog_file),
        resolve_runtime_path(args.business_recommendations),
        resolve_runtime_path(args.business_decisions),
        resolve_runtime_path(args.domain_events),
        resolve_runtime_path(args.hands_verification_state),
        resolve_runtime_path(args.trust_safety_state),
        resolve_runtime_path(args.state_file),
        resolve_runtime_path(args.report_file),
        limit=max(1, args.limit),
        emit_decisions=not args.no_emit_decisions,
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=False, indent=2))
    else:
        summary = state["summary"]
        print(
            "AriadGSM Tool Registry: "
            f"tools={summary['toolsRegistered']} "
            f"capabilities={summary['capabilitiesRegistered']} "
            f"requests={summary['requestsRead']} "
            f"matched={summary['matchedRequests']} "
            f"emitted={summary['emittedDecisionEvents']}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
