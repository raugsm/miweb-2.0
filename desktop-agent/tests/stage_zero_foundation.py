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
from ariadgsm_agent.stage_zero import run_stage_zero_once


def copy_required_tree(target: Path) -> None:
    for directory in ("docs", "desktop-agent/contracts", "desktop-agent/windows-app", "desktop-agent/update"):
        source = REPO_ROOT / directory
        destination = target / directory
        ignore = None
        if directory == "desktop-agent/windows-app":
            ignore = shutil.ignore_patterns("bin", "obj")
        shutil.copytree(source, destination, ignore=ignore)
    shutil.copy2(REPO_ROOT / "desktop-agent/config.example.json", target / "desktop-agent/config.example.json")


def main() -> int:
    with TemporaryDirectory() as tmp:
        root = Path(tmp)
        copy_required_tree(root)
        runtime = root / "desktop-agent/runtime"
        state = run_stage_zero_once(root, runtime)

        assert state["stageId"] == "stage_zero_product_foundation"
        assert state["status"] == "ok", state
        assert state["nextStage"]["name"] == "Domain Event Contracts"
        assert len(state["checks"]) >= 5
        assert not validate_contract(state, "stage_zero_readiness")
        assert (runtime / "stage-zero-state.json").exists()
        assert (runtime / "stage-zero-report.json").exists()

        manifest = root / "desktop-agent/update/ariadgsm-update.json"
        manifest_data = json.loads(manifest.read_text(encoding="utf-8"))
        manifest_data["sha256"] = ""
        manifest.write_text(json.dumps(manifest_data), encoding="utf-8")
        attention = run_stage_zero_once(root, runtime)
        assert attention["status"] == "attention"

    print("stage zero foundation OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

