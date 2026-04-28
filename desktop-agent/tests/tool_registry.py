from __future__ import annotations

import json
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.contracts import sample_event, validate_contract
from ariadgsm_agent.tool_registry import run_tool_registry_once


def append_jsonl(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n")


def test_contract_sample() -> None:
    state = sample_event("tool_registry_state")
    assert not validate_contract(state, "tool_registry_state")


def test_default_catalog_and_usb_request() -> None:
    with TemporaryDirectory() as temporary:
        root = Path(temporary)
        recommendations = root / "business-recommendations.jsonl"
        decisions = root / "business-decision-events.jsonl"
        domain_events = root / "domain-events.jsonl"
        state_file = root / "tool-registry-state.json"
        report_file = root / "tool-registry-report.json"
        append_jsonl(
            recommendations,
            {
                "recommendationId": "business-tool-1",
                "caseId": "case-usb-1",
                "channelId": "wa-3",
                "conversationId": "wa-3-client",
                "conversationTitle": "Cliente USB",
                "intent": "service_triage",
                "proposedAction": "prepare_service_next_step",
                "confidence": 0.86,
                "rationale": "Cliente necesita revisar USB Redirector y driver para detectar dispositivo.",
            },
        )

        state = run_tool_registry_once(
            root / "catalog.json",
            recommendations,
            decisions,
            domain_events,
            root / "hands-verification-state.json",
            root / "trust-safety-state.json",
            state_file,
            report_file,
        )

        assert not validate_contract(state, "tool_registry_state")
        assert state["summary"]["toolsRegistered"] >= 6
        assert state["summary"]["requestsRead"] == 1
        assert state["summary"]["matchedRequests"] == 1
        assert state["summary"]["plansNeedHuman"] == 1
        assert state["policy"]["executionMode"] == "plan_only_no_direct_execution"
        assert state["handsIntegration"]["externalExecutionAllowedByRegistry"] is False
        assert state_file.exists()
        assert report_file.exists()

        plan = state["toolPlans"][0]
        assert plan["selectedToolId"] == "usb-remote-session"
        assert "driver-package-manager" in plan["fallbackToolIds"]
        assert plan["requiresHumanApproval"] is True

        emitted = state["emittedDecisionEvents"]
        assert len(emitted) == 1
        assert emitted[0]["intent"] == "external_tool_plan"
        assert emitted[0]["requiresHumanConfirmation"] is True
        assert not validate_contract(emitted[0], "decision_event")

        second = run_tool_registry_once(
            root / "catalog.json",
            recommendations,
            decisions,
            domain_events,
            root / "hands-verification-state.json",
            root / "trust-safety-state.json",
            state_file,
            report_file,
        )
        assert second["summary"]["emittedDecisionEvents"] == 0


def test_no_match_keeps_human_fallback() -> None:
    with TemporaryDirectory() as temporary:
        root = Path(temporary)
        catalog = root / "catalog.json"
        catalog.write_text(
            json.dumps(
                {
                    "tools": [
                        {
                            "toolId": "manual-only",
                            "name": "Manual only",
                            "category": "human",
                            "status": "ready",
                            "riskLevel": "low",
                            "capabilities": ["human_override"],
                            "inputsNeeded": ["summary"],
                            "outputsProduced": ["correction"],
                            "verifiers": ["human_feedback_event"],
                            "failureSignals": [],
                            "alternatives": [],
                            "requiresHumanApproval": False,
                        }
                    ]
                },
                ensure_ascii=False,
            ),
            encoding="utf-8",
        )
        recommendations = root / "business-recommendations.jsonl"
        append_jsonl(
            recommendations,
            {
                "recommendationId": "business-price-1",
                "intent": "quote_or_price",
                "proposedAction": "prepare_quote_draft",
                "rationale": "Necesito cotizar precio de un servicio no registrado.",
            },
        )
        state = run_tool_registry_once(
            catalog,
            recommendations,
            root / "business-decision-events.jsonl",
            root / "domain-events.jsonl",
            root / "hands-verification-state.json",
            root / "trust-safety-state.json",
            root / "tool-registry-state.json",
            root / "tool-registry-report.json",
        )
        assert state["summary"]["unmatchedRequests"] == 1
        assert state["toolPlans"][0]["status"] == "no_match"
        assert state["toolPlans"][0]["requiresHumanApproval"] is True


def main() -> int:
    test_contract_sample()
    test_default_catalog_and_usb_request()
    test_no_match_keeps_human_fallback()
    print("tool registry OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
