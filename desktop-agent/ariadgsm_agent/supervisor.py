from __future__ import annotations

import argparse
import json
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from .trust_safety import run_trust_safety_once


AGENT_ROOT = Path(__file__).resolve().parents[1]


@dataclass(frozen=True)
class SupervisorPolicy:
    autonomy_level: int = 1
    suggest_threshold: float = 0.55
    confirm_threshold: float = 0.72
    execute_threshold: float = 0.92


@dataclass(frozen=True)
class SupervisorFinding:
    source_id: str
    source_type: str
    severity: str
    allowed: bool
    requires_human_confirmation: bool
    reason: str
    intent: str = ""
    proposed_action: str = ""
    confidence: float = 0.0
    required_level: int = 1

    def to_dict(self) -> dict[str, Any]:
        return {
            "sourceId": self.source_id,
            "sourceType": self.source_type,
            "severity": self.severity,
            "allowed": self.allowed,
            "requiresHumanConfirmation": self.requires_human_confirmation,
            "reason": self.reason,
            "intent": self.intent,
            "proposedAction": self.proposed_action,
            "confidence": self.confidence,
            "requiredLevel": self.required_level,
        }


def action_permission(confidence: float, required_level: int, policy: SupervisorPolicy) -> dict[str, object]:
    allowed_by_level = policy.autonomy_level >= required_level
    if not allowed_by_level:
        return {
            "allowed": False,
            "requiresHumanConfirmation": True,
            "reason": "autonomy_level_too_low",
        }
    if confidence >= policy.execute_threshold and policy.autonomy_level >= 6:
        return {"allowed": True, "requiresHumanConfirmation": False, "reason": "high_confidence_execute"}
    if confidence >= policy.confirm_threshold:
        return {"allowed": True, "requiresHumanConfirmation": True, "reason": "needs_confirmation"}
    if confidence >= policy.suggest_threshold:
        return {"allowed": False, "requiresHumanConfirmation": True, "reason": "suggest_only"}
    return {"allowed": False, "requiresHumanConfirmation": True, "reason": "low_confidence"}


class SupervisorCore:
    def __init__(self, policy: SupervisorPolicy | None = None) -> None:
        self.policy = policy or SupervisorPolicy()

    def assess_decision(self, event: dict[str, Any]) -> SupervisorFinding:
        confidence = safe_float(event.get("confidence"), 0.0)
        proposed_action = clean_text(event.get("proposedAction"))
        intent = clean_text(event.get("intent"))
        required_level = required_level_for_decision(intent, proposed_action)
        permission = action_permission(confidence, required_level, self.policy)
        requires_confirmation = bool(permission["requiresHumanConfirmation"]) or bool(event.get("requiresHumanConfirmation"))
        allowed = bool(permission["allowed"]) and not requires_confirmation
        severity = severity_for_permission(allowed, requires_confirmation, str(permission["reason"]), required_level)
        return SupervisorFinding(
            source_id=clean_text(event.get("decisionId")),
            source_type="decision_event",
            severity=severity,
            allowed=allowed,
            requires_human_confirmation=requires_confirmation,
            reason=str(permission["reason"]),
            intent=intent,
            proposed_action=proposed_action,
            confidence=confidence,
            required_level=required_level,
        )

    def assess_action(self, event: dict[str, Any]) -> SupervisorFinding:
        target = event.get("target") if isinstance(event.get("target"), dict) else {}
        verification = event.get("verification") if isinstance(event.get("verification"), dict) else {}
        action_type = clean_text(event.get("actionType"))
        confidence = safe_float(verification.get("confidence"), 0.0)
        required_level = safe_int(target.get("requiredAutonomyLevel"), required_level_for_action(action_type))
        status = clean_text(event.get("status"))
        blocked_by_engine = status == "blocked"
        unsafe_customer_action = action_type in {"write_text", "send_message"} and self.policy.autonomy_level < 6
        allowed = not blocked_by_engine and not unsafe_customer_action and self.policy.autonomy_level >= required_level
        requires_confirmation = unsafe_customer_action or bool(target.get("requiresHumanConfirmation"))
        reason = clean_text(target.get("safetyReason")) or ("action_blocked" if blocked_by_engine else "action_reviewed")
        if unsafe_customer_action:
            reason = "customer_facing_action_needs_full_autonomy"
        severity = severity_for_permission(allowed, requires_confirmation, reason, required_level)
        return SupervisorFinding(
            source_id=clean_text(event.get("actionId")),
            source_type="action_event",
            severity=severity,
            allowed=allowed,
            requires_human_confirmation=requires_confirmation,
            reason=reason,
            intent=clean_text(target.get("intent")),
            proposed_action=action_type,
            confidence=confidence,
            required_level=required_level,
        )

    def assess(self, decisions: list[dict[str, Any]], actions: list[dict[str, Any]]) -> dict[str, Any]:
        findings = [self.assess_decision(event) for event in decisions]
        findings.extend(self.assess_action(event) for event in actions)
        blocked = [item for item in findings if not item.allowed]
        confirmations = [item for item in findings if item.requires_human_confirmation]
        critical = [item for item in findings if item.severity == "critical"]
        safe_next = [item for item in findings if item.allowed and not item.requires_human_confirmation]
        return {
            "status": "attention" if critical or confirmations else "ok",
            "engine": "ariadgsm_supervisor_core",
            "updatedAt": utc_now(),
            "policy": {
                "autonomyLevel": self.policy.autonomy_level,
                "suggestThreshold": self.policy.suggest_threshold,
                "confirmThreshold": self.policy.confirm_threshold,
                "executeThreshold": self.policy.execute_threshold,
            },
            "summary": {
                "decisionsRead": len(decisions),
                "actionsRead": len(actions),
                "findings": len(findings),
                "blocked": len(blocked),
                "requiresHumanConfirmation": len(confirmations),
                "critical": len(critical),
                "safeNextActions": len(safe_next),
            },
            "latestFindings": [item.to_dict() for item in findings[-30:]],
            "safeNextActions": [item.to_dict() for item in safe_next[-10:]],
        }


def required_level_for_decision(intent: str, proposed_action: str) -> int:
    text = f"{intent} {proposed_action}".lower()
    if "send" in text or "reply_now" in text:
        return 6
    if "write" in text or "draft" in text or "prepare_message" in text:
        return 5
    if "accounting" in text or "payment" in text or "debt" in text or "record" in text:
        return 4
    if "open" in text or "capture" in text or "price" in text or "followup" in text:
        return 3
    if "suggest" in text:
        return 2
    return 1


def required_level_for_action(action_type: str) -> int:
    return {
        "noop": 1,
        "focus_window": 3,
        "open_chat": 3,
        "scroll_history": 3,
        "capture_conversation": 3,
        "record_accounting": 4,
        "write_text": 5,
        "send_message": 6,
    }.get(action_type, 1)


def severity_for_permission(allowed: bool, requires_confirmation: bool, reason: str, required_level: int) -> str:
    if "send" in reason or required_level >= 6:
        return "critical"
    if requires_confirmation:
        return "review"
    if not allowed:
        return "blocked"
    return "ok"


def run_supervisor_once(
    cognitive_decision_events_file: Path,
    operating_decision_events_file: Path,
    action_events_file: Path,
    state_file: Path,
    domain_events_file: Path | None = None,
    input_arbiter_state_file: Path | None = None,
    permissions_file: Path | None = None,
    trust_safety_state_file: Path | None = None,
    autonomy_level: int = 1,
    limit: int = 200,
) -> dict[str, Any]:
    domain_events_file = domain_events_file or (AGENT_ROOT / "runtime" / "domain-events.jsonl")
    input_arbiter_state_file = input_arbiter_state_file or (AGENT_ROOT / "runtime" / "input-arbiter-state.json")
    permissions_file = permissions_file or (AGENT_ROOT / "runtime" / "trust-safety-permissions.json")
    trust_safety_state_file = trust_safety_state_file or (AGENT_ROOT / "runtime" / "trust-safety-state.json")
    state = run_trust_safety_once(
        cognitive_decision_events_file,
        operating_decision_events_file,
        action_events_file,
        domain_events_file,
        trust_safety_state_file,
        input_arbiter_state_file=input_arbiter_state_file,
        permissions_file=permissions_file,
        autonomy_level=autonomy_level,
        limit=limit,
    )
    supervisor_state = dict(state)
    supervisor_state["engine"] = "ariadgsm_supervisor_core"
    supervisor_state["trustSafetyStateFile"] = str(trust_safety_state_file)
    write_json(state_file, supervisor_state)
    return supervisor_state


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
        if isinstance(event, dict) and event.get("eventType") == event_type:
            events.append(event)
    return events


def dedupe_by(events: list[dict[str, Any]], key: str) -> list[dict[str, Any]]:
    seen: set[str] = set()
    result: list[dict[str, Any]] = []
    for event in reversed(events):
        value = clean_text(event.get(key))
        if not value or value in seen:
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


def safe_float(value: Any, default: float) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def safe_int(value: Any, default: int) -> int:
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
    parser = argparse.ArgumentParser(description="AriadGSM Supervisor Core")
    parser.add_argument("--cognitive-decisions", default="runtime/cognitive-decision-events.jsonl")
    parser.add_argument("--operating-decisions", default="runtime/decision-events.jsonl")
    parser.add_argument("--actions", default="runtime/action-events.jsonl")
    parser.add_argument("--domain-events", default="runtime/domain-events.jsonl")
    parser.add_argument("--input-arbiter-state", default="runtime/input-arbiter-state.json")
    parser.add_argument("--permissions-file", default="runtime/trust-safety-permissions.json")
    parser.add_argument("--trust-safety-state-file", default="runtime/trust-safety-state.json")
    parser.add_argument("--state-file", default="runtime/supervisor-state.json")
    parser.add_argument("--autonomy-level", type=int, default=1)
    parser.add_argument("--limit", type=int, default=200)
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    state = run_supervisor_once(
        resolve_runtime_path(args.cognitive_decisions),
        resolve_runtime_path(args.operating_decisions),
        resolve_runtime_path(args.actions),
        resolve_runtime_path(args.state_file),
        domain_events_file=resolve_runtime_path(args.domain_events),
        input_arbiter_state_file=resolve_runtime_path(args.input_arbiter_state),
        permissions_file=resolve_runtime_path(args.permissions_file),
        trust_safety_state_file=resolve_runtime_path(args.trust_safety_state_file),
        autonomy_level=args.autonomy_level,
        limit=args.limit,
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=False, indent=2))
    else:
        summary = state["summary"]
        print(
            "AriadGSM Supervisor Core: "
            f"status={state['status']} "
            f"decisions={summary['decisionsRead']} "
            f"actions={summary['actionsRead']} "
            f"blocked={summary['blocked']} "
            f"confirm={summary['requiresHumanConfirmation']} "
            f"safe={summary['safeNextActions']}"
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
