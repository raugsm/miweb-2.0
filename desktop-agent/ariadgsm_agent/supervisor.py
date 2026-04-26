from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class SupervisorPolicy:
    autonomy_level: int = 1
    suggest_threshold: float = 0.55
    confirm_threshold: float = 0.72
    execute_threshold: float = 0.92


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

