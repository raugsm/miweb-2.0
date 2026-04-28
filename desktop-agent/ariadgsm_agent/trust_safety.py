from __future__ import annotations

import argparse
import json
from dataclasses import asdict, dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any


AGENT_ROOT = Path(__file__).resolve().parents[1]
VERSION = "0.8.14"

DECISIONS = ("ALLOW", "ALLOW_WITH_LIMIT", "ASK_HUMAN", "PAUSE_FOR_OPERATOR", "BLOCK")
RISK_ORDER = {"low": 1, "medium": 2, "high": 3, "critical": 4}
EVIDENCE_REQUIREMENTS = {
    "accounting_draft": "any",
    "text_draft": "any",
    "cross_channel_transfer": "any",
    "external_tool": "strong",
    "message_send": "strong",
    "accounting_confirmation": "level_a",
}
APPROVAL_TTL_SECONDS = 15 * 60


@dataclass(frozen=True)
class TrustSafetyPermissions:
    allow_local_navigation: bool = True
    allow_text_draft: bool = False
    allow_message_send: bool = False
    allow_external_tool_execution: bool = False
    allow_accounting_draft: bool = True
    allow_accounting_confirmation: bool = False
    allow_cross_channel_transfer: bool = False

    @classmethod
    def from_dict(cls, value: dict[str, Any] | None) -> "TrustSafetyPermissions":
        raw = value or {}
        return cls(
            allow_local_navigation=bool(raw.get("allowLocalNavigation", True)),
            allow_text_draft=bool(raw.get("allowTextDraft", False)),
            allow_message_send=bool(raw.get("allowMessageSend", False)),
            allow_external_tool_execution=bool(raw.get("allowExternalToolExecution", False)),
            allow_accounting_draft=bool(raw.get("allowAccountingDraft", True)),
            allow_accounting_confirmation=bool(raw.get("allowAccountingConfirmation", False)),
            allow_cross_channel_transfer=bool(raw.get("allowCrossChannelTransfer", False)),
        )

    def to_public(self) -> dict[str, bool]:
        return {
            "allowLocalNavigation": self.allow_local_navigation,
            "allowTextDraft": self.allow_text_draft,
            "allowMessageSend": self.allow_message_send,
            "allowExternalToolExecution": self.allow_external_tool_execution,
            "allowAccountingDraft": self.allow_accounting_draft,
            "allowAccountingConfirmation": self.allow_accounting_confirmation,
            "allowCrossChannelTransfer": self.allow_cross_channel_transfer,
        }


@dataclass(frozen=True)
class ActionRequirement:
    key: str
    required_level: int
    risk_level: str
    reversible: bool
    permission: str | None
    confidence_threshold: float
    human_name: str
    allowed_when_limited: list[str]
    blocked_when_denied: list[str]

    def to_public(self) -> dict[str, Any]:
        return {
            "key": self.key,
            "requiredAutonomyLevel": self.required_level,
            "riskLevel": self.risk_level,
            "reversible": self.reversible,
            "permission": self.permission,
            "confidenceThreshold": self.confidence_threshold,
            "humanName": self.human_name,
            "allowedWhenLimited": self.allowed_when_limited,
            "blockedWhenDenied": self.blocked_when_denied,
        }


ACTION_REQUIREMENTS: dict[str, ActionRequirement] = {
    "observe": ActionRequirement(
        "observe",
        1,
        "low",
        True,
        None,
        0.0,
        "observar y auditar",
        ["record_audit", "continue_cycle"],
        [],
    ),
    "suggest": ActionRequirement(
        "suggest",
        2,
        "medium",
        True,
        None,
        0.55,
        "sugerir a Bryams",
        ["suggest_next_step", "ask_human"],
        ["act_without_context"],
    ),
    "local_navigation": ActionRequirement(
        "local_navigation",
        3,
        "medium",
        True,
        "allowLocalNavigation",
        0.72,
        "leer o navegar WhatsApp local",
        ["focus_window", "open_chat", "capture_conversation", "scroll_history"],
        ["click_unverified_target", "read_wrong_chat"],
    ),
    "accounting_draft": ActionRequirement(
        "accounting_draft",
        4,
        "high",
        True,
        "allowAccountingDraft",
        0.78,
        "crear borrador contable",
        ["record_draft", "attach_evidence", "ask_human"],
        ["confirm_payment", "close_debt"],
    ),
    "text_draft": ActionRequirement(
        "text_draft",
        5,
        "high",
        True,
        "allowTextDraft",
        0.86,
        "preparar texto al cliente",
        ["draft_message", "ask_human"],
        ["send_message"],
    ),
    "cross_channel_transfer": ActionRequirement(
        "cross_channel_transfer",
        5,
        "high",
        True,
        "allowCrossChannelTransfer",
        0.86,
        "derivar o fusionar contexto entre WhatsApps",
        ["propose_route", "ask_human"],
        ["move_customer_context_without_approval"],
    ),
    "external_tool": ActionRequirement(
        "external_tool",
        6,
        "critical",
        False,
        "allowExternalToolExecution",
        0.94,
        "usar herramienta externa",
        ["ask_human", "prepare_tool_plan"],
        ["execute_external_tool", "modify_device"],
    ),
    "message_send": ActionRequirement(
        "message_send",
        6,
        "critical",
        False,
        "allowMessageSend",
        0.94,
        "enviar mensaje al cliente",
        ["ask_human", "show_draft"],
        ["send_message", "promise_price", "confirm_service"],
    ),
    "accounting_confirmation": ActionRequirement(
        "accounting_confirmation",
        6,
        "critical",
        False,
        "allowAccountingConfirmation",
        0.96,
        "confirmar pago, deuda o caja",
        ["ask_human", "record_evidence"],
        ["confirm_payment", "close_debt", "mark_paid"],
    ),
}


@dataclass(frozen=True)
class TrustFinding:
    source_id: str
    source_type: str
    action_key: str
    proposed_action: str
    decision: str
    severity: str
    allowed: bool
    requires_human_confirmation: bool
    risk_level: str
    confidence: float
    required_level: int
    permission: str | None
    reversible: bool
    evidence_count: int
    evidence_levels: list[str]
    approval_id: str
    reasons: list[str]
    allowed_actions: list[str]
    blocked_actions: list[str]
    human_summary: str

    def to_dict(self) -> dict[str, Any]:
        return {
            "sourceId": self.source_id,
            "sourceType": self.source_type,
            "actionKey": self.action_key,
            "proposedAction": self.proposed_action,
            "decision": self.decision,
            "severity": self.severity,
            "allowed": self.allowed,
            "requiresHumanConfirmation": self.requires_human_confirmation,
            "riskLevel": self.risk_level,
            "confidence": self.confidence,
            "requiredLevel": self.required_level,
            "permission": self.permission or "",
            "reversible": self.reversible,
            "evidenceCount": self.evidence_count,
            "evidenceLevels": self.evidence_levels,
            "approvalId": self.approval_id,
            "reasons": self.reasons,
            "allowedActions": self.allowed_actions,
            "blockedActions": self.blocked_actions,
            "humanSummary": self.human_summary,
            "reason": "; ".join(self.reasons),
            "intent": self.action_key,
        }


class TrustSafetyCore:
    def __init__(
        self,
        autonomy_level: int = 1,
        permissions: TrustSafetyPermissions | None = None,
        approvals_by_source: dict[str, dict[str, Any]] | None = None,
    ) -> None:
        self.autonomy_level = max(1, min(6, int(autonomy_level)))
        self.permissions = permissions or TrustSafetyPermissions()
        self.approvals_by_source = approvals_by_source or {}

    def assess(
        self,
        decisions: list[dict[str, Any]],
        actions: list[dict[str, Any]],
        domain_events: list[dict[str, Any]],
        input_state: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        findings: list[TrustFinding] = []
        findings.extend(self.assess_decision(event) for event in decisions)
        findings.extend(self.assess_action(event) for event in actions)
        findings.extend(self.assess_domain_event(event) for event in domain_events)
        findings = [item for item in findings if item.source_id]

        operator_has_priority = bool((input_state or {}).get("operatorHasPriority")) or clean_text((input_state or {}).get("phase")) == "operator_control"
        if operator_has_priority:
            findings.append(self.operator_handoff_finding(input_state or {}))

        blocked = [item for item in findings if item.decision == "BLOCK"]
        ask_human = [item for item in findings if item.decision == "ASK_HUMAN"]
        paused = [item for item in findings if item.decision == "PAUSE_FOR_OPERATOR"]
        limited = [item for item in findings if item.decision == "ALLOW_WITH_LIMIT"]
        allowed = [item for item in findings if item.decision == "ALLOW"]
        evidence_missing = [item for item in findings if any("evidencia" in reason.lower() for reason in item.reasons)]
        approvals_applied = [item for item in findings if item.approval_id]
        critical = [item for item in findings if item.risk_level == "critical" or item.severity == "critical"]

        gate_decision = decide_gate(blocked, ask_human, paused, limited)
        status = "blocked" if gate_decision == "BLOCK" else "attention" if gate_decision in {"ASK_HUMAN", "PAUSE_FOR_OPERATOR", "ALLOW_WITH_LIMIT"} else "ok"
        human_report = self.human_report(gate_decision, findings, blocked, ask_human, paused)
        return {
            "status": status,
            "engine": "ariadgsm_trust_safety_core",
            "version": VERSION,
            "updatedAt": utc_now(),
            "policy": {
                "version": f"ariadgsm-trust-safety-{VERSION}",
                "autonomyLevel": self.autonomy_level,
                "decisions": list(DECISIONS),
                "principles": [
                    "least_privilege",
                    "human_approval_for_irreversible_actions",
                    "verify_before_continue",
                    "audit_every_permission_decision",
                    "operator_has_mouse_priority",
                    "critical_actions_need_per_action_approval",
                    "high_risk_actions_need_evidence",
                ],
            },
            "contracts": {
                "decisionSources": ["cognitive", "operating", "business_brain"],
                "inputArbiterState": "input-arbiter-state.schema.json",
                "approvalEvent": "safety-approval-event.schema.json",
                "permissionState": "trust-safety-state.schema.json",
            },
            "permissions": self.permissions.to_public(),
            "riskMatrix": {key: requirement.to_public() for key, requirement in ACTION_REQUIREMENTS.items()},
            "approvalLedger": {
                "approvalsRead": len(self.approvals_by_source),
                "approvalsApplied": len(approvals_applied),
                "ttlSeconds": APPROVAL_TTL_SECONDS,
                "applied": [
                    {
                        "sourceId": item.source_id,
                        "approvalId": item.approval_id,
                        "actionKey": item.action_key,
                    }
                    for item in approvals_applied[-12:]
                ],
            },
            "inputArbiter": summarize_input_arbiter(input_state or {}),
            "permissionGate": {
                "decision": gate_decision,
                "reason": human_report["resumenDecision"],
                "canHandsRun": gate_decision in {"ALLOW", "ALLOW_WITH_LIMIT"},
                "allowedEngines": {
                    "vision": True,
                    "perception": True,
                    "memory": True,
                    "cognitive": True,
                    "businessBrain": True,
                    "hands": gate_decision in {"ALLOW", "ALLOW_WITH_LIMIT"},
                },
            },
            "summary": {
                "decisionsRead": len(decisions),
                "actionsRead": len(actions),
                "domainEventsRead": len(domain_events),
                "approvalsRead": len(self.approvals_by_source),
                "approvalsApplied": len(approvals_applied),
                "findings": len(findings),
                "allowed": len(allowed),
                "allowedWithLimit": len(limited),
                "paused": len(paused),
                "blocked": len(blocked),
                "requiresHumanConfirmation": len(ask_human) + len(paused),
                "critical": len(critical),
                "safeNextActions": len(allowed) + len(limited),
                "irreversibleBlocked": sum(1 for item in blocked if not item.reversible),
                "evidenceMissing": len(evidence_missing),
            },
            "latestFindings": [item.to_dict() for item in findings[-40:]],
            "safeNextActions": [item.to_dict() for item in (allowed + limited)[-12:]],
            "limitedActions": [item.to_dict() for item in limited[-12:]],
            "pausedActions": [item.to_dict() for item in paused[-12:]],
            "requiresHumanActions": [item.to_dict() for item in ask_human[-12:]],
            "blockedActions": [item.to_dict() for item in blocked[-12:]],
            "humanReport": human_report,
        }

    def assess_decision(self, event: dict[str, Any]) -> TrustFinding:
        source_id = clean_text(event.get("decisionId"))
        text = " ".join(
            [
                clean_text(event.get("intent")),
                clean_text(event.get("proposedAction")),
                clean_text(event.get("reasoningSummary")),
            ]
        )
        action_key = classify_text_action(text)
        confidence = clamp(event.get("confidence"), 0.0)
        source_id = clean_text(event.get("decisionId"))
        return self.make_finding(
            source_id=source_id,
            source_type="decision_event",
            action_key=action_key,
            proposed_action=clean_text(event.get("proposedAction")) or action_key,
            confidence=confidence,
            source_requires_human=bool(event.get("requiresHumanConfirmation")),
            source_risk=read_risk_level(event),
            evidence_levels=[],
            evidence_count=count_evidence_references(event),
            approval=approval_for(self.approvals_by_source, source_id),
        )

    def assess_action(self, event: dict[str, Any]) -> TrustFinding:
        target = event.get("target") if isinstance(event.get("target"), dict) else {}
        verification = event.get("verification") if isinstance(event.get("verification"), dict) else {}
        action_type = clean_text(event.get("actionType"))
        status = clean_text(event.get("status"))
        action_key = classify_action_event(action_type, target)
        confidence = clamp(verification.get("confidence"), 0.0)
        verified = bool(verification.get("verified"))
        source_requires_human = bool(target.get("requiresHumanConfirmation"))
        finding = self.make_finding(
            source_id=clean_text(event.get("actionId")),
            source_type="action_event",
            action_key=action_key,
            proposed_action=action_type or action_key,
            confidence=confidence,
            source_requires_human=source_requires_human,
            source_risk=read_risk_level(event),
            evidence_levels=[],
            evidence_count=count_action_evidence(event),
            approval=approval_for(self.approvals_by_source, clean_text(event.get("actionId"))),
        )
        if status in {"blocked", "failed"}:
            return replace_finding(
                finding,
                decision="BLOCK",
                allowed=False,
                requires_human_confirmation=True,
                severity=max_severity(finding.severity, "blocked"),
                reasons=finding.reasons + [f"Hands reporto accion {status}: {clean_text(verification.get('summary')) or clean_text(target.get('executionSummary'))}"],
            )
        if action_type in {"open_chat", "scroll_history", "capture_conversation"} and not verified and status != "planned":
            return replace_finding(
                finding,
                decision="BLOCK",
                allowed=False,
                requires_human_confirmation=True,
                severity=max_severity(finding.severity, "blocked"),
                reasons=finding.reasons + ["La accion local no fue verificada por Perception."],
            )
        return finding

    def assess_domain_event(self, event: dict[str, Any]) -> TrustFinding:
        event_type = clean_text(event.get("eventType"))
        action_key = classify_domain_event(event_type, event)
        confidence = clamp(event.get("confidence"), 0.0)
        source_requires_human = bool(event.get("requiresHumanReview"))
        source_risk = read_risk_level(event)
        evidence_levels = evidence_levels_for(event)
        source_id = clean_text(event.get("eventId"))
        finding = self.make_finding(
            source_id=source_id,
            source_type="domain_event",
            action_key=action_key,
            proposed_action=event_type,
            confidence=confidence,
            source_requires_human=source_requires_human,
            source_risk=source_risk,
            evidence_levels=evidence_levels,
            evidence_count=count_evidence_references(event),
            approval=approval_for(self.approvals_by_source, source_id),
        )
        if event_type in {"PaymentConfirmed", "AccountingRecordConfirmed"} and "A" not in evidence_levels:
            return replace_finding(
                finding,
                decision="BLOCK",
                allowed=False,
                requires_human_confirmation=True,
                severity="critical",
                reasons=finding.reasons + ["Confirmacion contable sin evidencia nivel A."],
            )
        return finding

    def make_finding(
        self,
        *,
        source_id: str,
        source_type: str,
        action_key: str,
        proposed_action: str,
        confidence: float,
        source_requires_human: bool,
        source_risk: str,
        evidence_levels: list[str],
        evidence_count: int,
        approval: dict[str, Any] | None,
    ) -> TrustFinding:
        requirement = ACTION_REQUIREMENTS.get(action_key, ACTION_REQUIREMENTS["suggest"])
        risk_level = highest_risk(requirement.risk_level, source_risk)
        approval_id = clean_text((approval or {}).get("approvalId"))
        reasons: list[str] = []
        decision = "ALLOW"
        allowed = True
        requires_human = False

        if self.autonomy_level < requirement.required_level:
            decision = "ASK_HUMAN" if requirement.reversible and risk_level != "critical" else "BLOCK"
            allowed = False
            requires_human = True
            reasons.append(f"Nivel de autonomia {self.autonomy_level} menor que requerido {requirement.required_level}.")

        if confidence < requirement.confidence_threshold:
            decision = more_restrictive(decision, "ASK_HUMAN")
            allowed = False
            requires_human = True
            reasons.append(f"Confianza {confidence:.2f} menor que umbral {requirement.confidence_threshold:.2f}.")

        if requirement.permission and not permission_value(self.permissions, requirement.permission):
            decision = "BLOCK" if not requirement.reversible or risk_level == "critical" else "ASK_HUMAN"
            allowed = False
            requires_human = True
            reasons.append(f"Permiso explicito faltante: {requirement.permission}.")

        evidence_requirement = EVIDENCE_REQUIREMENTS.get(action_key, "none")
        if evidence_requirement == "any" and evidence_count <= 0:
            decision = more_restrictive(decision, "ASK_HUMAN")
            allowed = False
            requires_human = True
            reasons.append("La accion de alto impacto necesita evidencia enlazada.")
        elif evidence_requirement == "strong" and evidence_count <= 0 and "A" not in evidence_levels:
            decision = "BLOCK" if not requirement.reversible or risk_level == "critical" else more_restrictive(decision, "ASK_HUMAN")
            allowed = False
            requires_human = True
            reasons.append("La accion critica necesita evidencia fuerte antes de continuar.")

        if source_requires_human:
            if approval_id:
                reasons.append(f"Revision humana aplicada: {approval_id}.")
            else:
                decision = more_restrictive(decision, "ASK_HUMAN")
                allowed = False
                requires_human = True
                reasons.append("El evento fuente pide revision humana.")

        if (risk_level == "critical" or not requirement.reversible) and not approval_id:
            decision = more_restrictive(decision, "ASK_HUMAN")
            allowed = False
            requires_human = True
            reasons.append("La accion critica requiere aprobacion humana para esta ejecucion.")

        if action_key == "accounting_confirmation" and "A" not in evidence_levels:
            decision = "BLOCK"
            allowed = False
            requires_human = True
            reasons.append("Confirmar contabilidad requiere evidencia nivel A.")

        if decision == "ALLOW" and risk_level in {"medium", "high"} and requirement.reversible:
            decision = "ALLOW_WITH_LIMIT"
            reasons.append("Permitido solo dentro de limites auditables.")

        if decision in {"BLOCK", "ASK_HUMAN", "PAUSE_FOR_OPERATOR"}:
            allowed = False
        severity = severity_for(decision, risk_level)
        if not reasons:
            reasons.append("Accion permitida por politica central.")
        return TrustFinding(
            source_id=source_id,
            source_type=source_type,
            action_key=action_key,
            proposed_action=proposed_action,
            decision=decision,
            severity=severity,
            allowed=allowed,
            requires_human_confirmation=requires_human,
            risk_level=risk_level,
            confidence=confidence,
            required_level=requirement.required_level,
            permission=requirement.permission,
            reversible=requirement.reversible,
            evidence_count=evidence_count,
            evidence_levels=evidence_levels,
            approval_id=approval_id,
            reasons=reasons,
            allowed_actions=requirement.allowed_when_limited,
            blocked_actions=requirement.blocked_when_denied,
            human_summary=human_summary_for(decision, requirement, reasons),
        )

    def operator_handoff_finding(self, input_state: dict[str, Any]) -> TrustFinding:
        summary = clean_text(input_state.get("summary")) or "Bryams esta usando mouse o teclado."
        return TrustFinding(
            source_id=clean_text(input_state.get("leaseId")) or "operator-control",
            source_type="input_arbiter_state",
            action_key="operator_handoff",
            proposed_action="pause_hands",
            decision="PAUSE_FOR_OPERATOR",
            severity="review",
            allowed=False,
            requires_human_confirmation=True,
            risk_level="medium",
            confidence=1.0,
            required_level=1,
            permission=None,
            reversible=True,
            evidence_count=1,
            evidence_levels=[],
            approval_id="",
            reasons=[summary, "Las manos se pausan; ojos, memoria y cerebro siguen activos."],
            allowed_actions=["observe", "remember", "reason"],
            blocked_actions=["move_mouse", "type_text", "click_until_operator_idle"],
            human_summary="Pauso manos porque detecte control humano.",
        )

    @staticmethod
    def human_report(
        gate_decision: str,
        findings: list[TrustFinding],
        blocked: list[TrustFinding],
        ask_human: list[TrustFinding],
        paused: list[TrustFinding],
    ) -> dict[str, Any]:
        if gate_decision == "ALLOW":
            headline = "Seguridad lista"
            summary = "Trust & Safety no encontro acciones riesgosas pendientes."
        elif gate_decision == "ALLOW_WITH_LIMIT":
            headline = "Puede avanzar con limites"
            summary = "Hay acciones permitidas solo porque son reversibles, auditadas y locales."
        elif gate_decision == "PAUSE_FOR_OPERATOR":
            headline = "Te cedo el control"
            summary = "Detecte que estas usando mouse o teclado; no muevo manos hasta que sea seguro."
        elif gate_decision == "ASK_HUMAN":
            headline = "Necesito tu aprobacion"
            summary = "Hay acciones que no debo resolver sola sin confirmacion humana."
        else:
            headline = "Bloquee una accion riesgosa"
            summary = "Hay una accion irreversible, sin permiso explicito o sin evidencia suficiente."
        return {
            "headline": headline,
            "resumenDecision": summary,
            "permitidas": [item.human_summary for item in findings if item.decision in {"ALLOW", "ALLOW_WITH_LIMIT"}][-8:],
            "necesitanBryams": [item.human_summary for item in ask_human + paused][-8:],
            "bloqueadas": [item.human_summary for item in blocked][-8:],
            "riesgos": sorted({reason for item in blocked + ask_human for reason in item.reasons})[:12],
        }


def run_trust_safety_once(
    cognitive_decision_events_file: Path,
    operating_decision_events_file: Path,
    action_events_file: Path,
    domain_events_file: Path,
    state_file: Path,
    *,
    business_decision_events_file: Path | None = None,
    approval_events_file: Path | None = None,
    input_arbiter_state_file: Path | None = None,
    permissions_file: Path | None = None,
    permissions: dict[str, Any] | None = None,
    autonomy_level: int = 1,
    limit: int = 200,
) -> dict[str, Any]:
    decisions = read_jsonl_events(cognitive_decision_events_file, "decision_event", limit)
    decisions.extend(read_jsonl_events(operating_decision_events_file, "decision_event", limit))
    if business_decision_events_file is not None:
        decisions.extend(read_jsonl_events(business_decision_events_file, "decision_event", limit))
    actions = read_jsonl_events(action_events_file, "action_event", limit)
    domain_events = read_jsonl_events(domain_events_file, "", limit)
    approvals_by_source = read_approval_events(approval_events_file, limit) if approval_events_file is not None else {}
    input_state = read_json(input_arbiter_state_file) if input_arbiter_state_file else {}
    loaded_permissions = permissions if permissions is not None else read_json(permissions_file)
    core = TrustSafetyCore(
        autonomy_level=autonomy_level,
        permissions=TrustSafetyPermissions.from_dict(loaded_permissions if isinstance(loaded_permissions, dict) else None),
        approvals_by_source=approvals_by_source,
    )
    state = core.assess(
        dedupe_by(decisions, "decisionId"),
        dedupe_by(actions, "actionId"),
        dedupe_by(domain_events, "eventId"),
        input_state if isinstance(input_state, dict) else {},
    )
    state["inputs"] = {
        "cognitiveDecisionEventsFile": str(cognitive_decision_events_file),
        "operatingDecisionEventsFile": str(operating_decision_events_file),
        "businessDecisionEventsFile": str(business_decision_events_file or ""),
        "approvalEventsFile": str(approval_events_file or ""),
        "actionEventsFile": str(action_events_file),
        "domainEventsFile": str(domain_events_file),
        "inputArbiterStateFile": str(input_arbiter_state_file or ""),
        "permissionsFile": str(permissions_file or ""),
    }
    write_json(state_file, state)
    return state


def classify_text_action(text: str) -> str:
    normalized = text.lower()
    if any(token in normalized for token in ("send_message", "send ", "reply_now", "enviar", "responder ahora")):
        return "message_send"
    if any(token in normalized for token in ("confirm_payment", "payment_confirmed", "mark_paid", "close_debt", "confirmar pago")):
        return "accounting_confirmation"
    if any(token in normalized for token in ("execute_tool", "tool_action", "flash", "usb", "flasheo", "unlock_tool")):
        return "external_tool"
    if any(token in normalized for token in ("route", "transfer", "merge", "derivar", "fusionar", "otro whatsapp")):
        return "cross_channel_transfer"
    if any(token in normalized for token in ("write_text", "draft", "prepare_message", "borrador mensaje")):
        return "text_draft"
    if any(token in normalized for token in ("accounting", "payment", "debt", "refund", "record", "pago", "deuda", "reembolso")):
        return "accounting_draft"
    if any(token in normalized for token in ("open", "capture", "scroll", "price", "followup", "chat", "leer", "precio")):
        return "local_navigation"
    if any(token in normalized for token in ("suggest", "propose", "recomendar")):
        return "suggest"
    return "observe"


def classify_action_event(action_type: str, target: dict[str, Any]) -> str:
    if action_type in {"focus_window", "open_chat", "scroll_history", "capture_conversation"}:
        return "local_navigation"
    if action_type == "record_accounting":
        return "accounting_draft"
    if action_type == "write_text":
        return "text_draft"
    if action_type == "send_message":
        return "message_send"
    return classify_text_action(" ".join([action_type, clean_text(target.get("proposedAction")), clean_text(target.get("intent"))]))


def classify_domain_event(event_type: str, event: dict[str, Any]) -> str:
    if event_type in {"PaymentConfirmed", "AccountingRecordConfirmed", "DebtUpdated"}:
        return "accounting_confirmation"
    if event_type in {"PaymentDrafted", "PaymentEvidenceAttached", "AccountingEvidenceAttached", "DebtDetected", "RefundCandidate", "QuoteRecorded"}:
        return "accounting_draft"
    if event_type in {"ChannelRouteProposed", "ChannelRouteApproved", "CaseMerged"}:
        return "cross_channel_transfer"
    if event_type in {"ToolActionRequested", "ToolActionVerified", "ProcedureCandidateCreated", "ProcedureRiskAssessed"}:
        return "external_tool"
    if event_type in {"QuoteProposed", "QuoteApproved"}:
        return "text_draft"
    if event_type in {"ActionRequested", "ActionExecuted", "ActionVerified", "ActionFailed"}:
        return classify_text_action(clean_text((event.get("data") or {}).get("actionType")) if isinstance(event.get("data"), dict) else event_type)
    return classify_text_action(" ".join([event_type, clean_text(event.get("summary")), json.dumps(event.get("data") or {}, ensure_ascii=False)]))


def decide_gate(
    blocked: list[TrustFinding],
    ask_human: list[TrustFinding],
    paused: list[TrustFinding],
    limited: list[TrustFinding],
) -> str:
    if paused:
        return "PAUSE_FOR_OPERATOR"
    if blocked:
        return "BLOCK"
    if ask_human:
        return "ASK_HUMAN"
    if limited:
        return "ALLOW_WITH_LIMIT"
    return "ALLOW"


def permission_value(permissions: TrustSafetyPermissions, permission: str) -> bool:
    public = permissions.to_public()
    return bool(public.get(permission))


def read_risk_level(event: dict[str, Any]) -> str:
    risk = event.get("risk") if isinstance(event.get("risk"), dict) else {}
    raw = clean_text(risk.get("riskLevel")).lower()
    return raw if raw in RISK_ORDER else "low"


def evidence_levels_for(event: dict[str, Any]) -> list[str]:
    evidence = event.get("evidence") if isinstance(event.get("evidence"), list) else []
    return [clean_text(item.get("evidenceLevel")) for item in evidence if isinstance(item, dict) and clean_text(item.get("evidenceLevel"))]


def count_evidence_references(event: dict[str, Any]) -> int:
    evidence = event.get("evidence")
    if isinstance(evidence, list):
        return len([item for item in evidence if item])
    if clean_text(evidence):
        return 1
    data = event.get("data") if isinstance(event.get("data"), dict) else {}
    refs = data.get("evidence") if isinstance(data, dict) else None
    if isinstance(refs, list):
        return len([item for item in refs if item])
    return 0


def count_action_evidence(event: dict[str, Any]) -> int:
    target = event.get("target") if isinstance(event.get("target"), dict) else {}
    verification = event.get("verification") if isinstance(event.get("verification"), dict) else {}
    count = count_evidence_references(event)
    if bool(verification.get("verified")):
        count += 1
    for key in ("verificationPerceptionEventId", "interactionSourcePerceptionEventId", "sourceDecisionId"):
        if clean_text(target.get(key)):
            count += 1
    return count


def read_approval_events(path: Path | None, limit: int) -> dict[str, dict[str, Any]]:
    if path is None or not path.exists():
        return {}
    approvals: dict[str, dict[str, Any]] = {}
    for event in read_jsonl_events(path, "safety_approval_event", limit):
        if clean_text(event.get("decision")).upper() not in {"APPROVE", "ALLOW"}:
            continue
        target_id = clean_text(event.get("targetSourceId")) or clean_text(event.get("sourceId"))
        approval_id = clean_text(event.get("approvalId"))
        if not target_id or not approval_id:
            continue
        if is_expired_approval(event):
            continue
        approvals[target_id] = event
    return approvals


def is_expired_approval(event: dict[str, Any]) -> bool:
    expires_at = parse_dt(clean_text(event.get("expiresAt")))
    if expires_at is not None:
        return datetime.now(timezone.utc) > expires_at
    created_at = parse_dt(clean_text(event.get("createdAt")))
    if created_at is None:
        return True
    return datetime.now(timezone.utc) - created_at > timedelta(seconds=APPROVAL_TTL_SECONDS)


def parse_dt(value: str) -> datetime | None:
    if not value:
        return None
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(timezone.utc)
    except ValueError:
        return None


def approval_for(approvals_by_source: dict[str, dict[str, Any]], source_id: str) -> dict[str, Any] | None:
    return approvals_by_source.get(source_id)


def summarize_input_arbiter(input_state: dict[str, Any]) -> dict[str, Any]:
    phase = clean_text(input_state.get("phase"))
    decision = clean_text(input_state.get("decision")) or ("PAUSE_FOR_OPERATOR" if bool(input_state.get("operatorHasPriority")) else "ALLOW")
    lease = input_state.get("lease") if isinstance(input_state.get("lease"), dict) else {}
    operator = input_state.get("operator") if isinstance(input_state.get("operator"), dict) else {}
    return {
        "status": clean_text(input_state.get("status")) or "missing",
        "phase": phase or "unknown",
        "decision": decision,
        "activeOwner": clean_text(input_state.get("activeOwner")) or ("operator" if bool(input_state.get("operatorHasPriority")) else "unknown"),
        "operatorHasPriority": bool(input_state.get("operatorHasPriority")),
        "handsPausedOnly": bool(input_state.get("handsPausedOnly")),
        "operatorIdleMs": safe_int(input_state.get("operatorIdleMs"), safe_int(operator.get("idleMs"), 0)),
        "requiredIdleMs": safe_int(input_state.get("requiredIdleMs"), safe_int(operator.get("requiredIdleMs"), 0)),
        "leaseId": clean_text(input_state.get("leaseId")) or clean_text(lease.get("leaseId")),
        "summary": clean_text(input_state.get("summary")),
    }


def highest_risk(*levels: str) -> str:
    valid = [level for level in levels if level in RISK_ORDER]
    return max(valid or ["low"], key=lambda item: RISK_ORDER[item])


def more_restrictive(current: str, proposed: str) -> str:
    order = {"ALLOW": 1, "ALLOW_WITH_LIMIT": 2, "ASK_HUMAN": 3, "PAUSE_FOR_OPERATOR": 4, "BLOCK": 5}
    return proposed if order[proposed] > order[current] else current


def severity_for(decision: str, risk_level: str) -> str:
    if decision == "BLOCK" and risk_level == "critical":
        return "critical"
    if decision == "BLOCK":
        return "blocked"
    if decision in {"ASK_HUMAN", "PAUSE_FOR_OPERATOR"}:
        return "review"
    return "ok"


def max_severity(left: str, right: str) -> str:
    order = {"ok": 1, "review": 2, "blocked": 3, "critical": 4}
    return left if order.get(left, 1) >= order.get(right, 1) else right


def replace_finding(finding: TrustFinding, **updates: Any) -> TrustFinding:
    data = asdict(finding)
    data.update(updates)
    data["human_summary"] = human_summary_for(
        data["decision"],
        ACTION_REQUIREMENTS.get(data["action_key"], ACTION_REQUIREMENTS["suggest"]),
        data["reasons"],
    )
    return TrustFinding(**data)


def human_summary_for(decision: str, requirement: ActionRequirement, reasons: list[str]) -> str:
    reason = reasons[-1] if reasons else "sin motivo"
    if decision == "ALLOW":
        return f"Puedo {requirement.human_name}: {reason}"
    if decision == "ALLOW_WITH_LIMIT":
        return f"Puedo {requirement.human_name} con limite: {reason}"
    if decision == "ASK_HUMAN":
        return f"Necesito aprobacion para {requirement.human_name}: {reason}"
    if decision == "PAUSE_FOR_OPERATOR":
        return f"Pauso manos: {reason}"
    return f"Bloquee {requirement.human_name}: {reason}"


def read_jsonl_events(path: Path, event_type: str, limit: int) -> list[dict[str, Any]]:
    if not path.exists():
        return []
    events: list[dict[str, Any]] = []
    lines = [line for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()][-limit:]
    for line in lines:
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(event, dict) and (not event_type or event.get("eventType") == event_type):
            events.append(event)
    return events


def read_json(path: Path | None) -> dict[str, Any]:
    if path is None or not path.exists():
        return {}
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except json.JSONDecodeError:
        return {}
    return value if isinstance(value, dict) else {}


def dedupe_by(events: list[dict[str, Any]], key: str) -> list[dict[str, Any]]:
    seen: set[str] = set()
    result: list[dict[str, Any]] = []
    for event in reversed(events):
        value = clean_text(event.get(key))
        if not value:
            value = f"{event.get('eventType', 'event')}:{json.dumps(event, ensure_ascii=False, sort_keys=True)}"
        if value in seen:
            continue
        seen.add(value)
        result.append(event)
    return list(reversed(result))


def write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    temp.replace(path)


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def clean_text(value: Any) -> str:
    return str(value or "").strip()


def clamp(value: Any, default: float = 0.5) -> float:
    try:
        number = float(value)
    except (TypeError, ValueError):
        number = default
    return max(0.0, min(1.0, number))


def safe_int(value: Any, default: int = 0) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def resolve_runtime_path(value: str | Path) -> Path:
    path = Path(value).expanduser()
    if path.is_absolute():
        return path
    return (AGENT_ROOT / path).resolve()


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM Trust & Safety Core")
    parser.add_argument("--cognitive-decisions", default="runtime/cognitive-decision-events.jsonl")
    parser.add_argument("--operating-decisions", default="runtime/decision-events.jsonl")
    parser.add_argument("--business-decisions", default="runtime/business-decision-events.jsonl")
    parser.add_argument("--approvals", default="runtime/safety-approval-events.jsonl")
    parser.add_argument("--actions", default="runtime/action-events.jsonl")
    parser.add_argument("--domain-events", default="runtime/domain-events.jsonl")
    parser.add_argument("--input-arbiter-state", default="runtime/input-arbiter-state.json")
    parser.add_argument("--permissions-file", default="runtime/trust-safety-permissions.json")
    parser.add_argument("--state-file", default="runtime/trust-safety-state.json")
    parser.add_argument("--autonomy-level", type=int, default=1)
    parser.add_argument("--limit", type=int, default=200)
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    state = run_trust_safety_once(
        resolve_runtime_path(args.cognitive_decisions),
        resolve_runtime_path(args.operating_decisions),
        resolve_runtime_path(args.actions),
        resolve_runtime_path(args.domain_events),
        resolve_runtime_path(args.state_file),
        business_decision_events_file=resolve_runtime_path(args.business_decisions),
        approval_events_file=resolve_runtime_path(args.approvals),
        input_arbiter_state_file=resolve_runtime_path(args.input_arbiter_state),
        permissions_file=resolve_runtime_path(args.permissions_file),
        autonomy_level=args.autonomy_level,
        limit=args.limit,
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=False, indent=2))
    else:
        summary = state["summary"]
        print(
            "AriadGSM Trust & Safety: "
            f"decision={state['permissionGate']['decision']} "
            f"blocked={summary['blocked']} "
            f"human={summary['requiresHumanConfirmation']} "
            f"safe={summary['safeNextActions']}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
