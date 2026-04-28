from __future__ import annotations

import json
import shutil
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
REPO_ROOT = ROOT.parent
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.contracts import validate_contract
from ariadgsm_agent.domain_contracts import run_domain_contracts_once


def copy_required_tree(target: Path) -> None:
    for directory in ("docs", "desktop-agent/contracts", "desktop-agent/windows-app", "desktop-agent/update"):
        source = REPO_ROOT / directory
        destination = target / directory
        ignore = shutil.ignore_patterns("bin", "obj") if directory == "desktop-agent/windows-app" else None
        shutil.copytree(source, destination, ignore=ignore)
    shutil.copytree(
        REPO_ROOT / "desktop-agent/ariadgsm_agent",
        target / "desktop-agent/ariadgsm_agent",
        ignore=shutil.ignore_patterns("__pycache__"),
    )
    shutil.copy2(REPO_ROOT / "desktop-agent/config.example.json", target / "desktop-agent/config.example.json")


def main() -> int:
    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        copy_required_tree(root)
        runtime = root / "desktop-agent/runtime"
        state = run_domain_contracts_once(root, runtime)
        assert state["stageId"] == "stage_one_domain_event_contracts"
        assert state["status"] == "ok", state
        assert state["nextStage"]["name"] == "Autonomous Cycle Orchestrator"
        assert not validate_contract(state, "domain_contracts_final_readiness")

        registry = root / "desktop-agent/contracts/domain-event-registry.json"
        registry_data = json.loads(registry.read_text(encoding="utf-8"))
        registry_data["eventTypes"].pop("HumanCorrectionReceived")
        registry.write_text(json.dumps(registry_data), encoding="utf-8")
        blocked = run_domain_contracts_once(root, runtime)
        assert blocked["status"] == "blocked"

    print("domain contracts finalization OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

