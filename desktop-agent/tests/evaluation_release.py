from __future__ import annotations

import hashlib
import json
import sys
import zipfile
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
REPO = ROOT.parent
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.contracts import sample_event, validate_contract
from ariadgsm_agent.release_evaluation import run_evaluation_release_once
from ariadgsm_agent.runtime_governor import build_runtime_governor_state


def write_text(path: Path, value: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(value, encoding="utf-8")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    digest.update(path.read_bytes())
    return digest.hexdigest()


def make_release_repo(root: Path) -> None:
    write_text(root / "docs/ARIADGSM_EXECUTION_LOCK.md", "Execution Lock\n")
    write_text(root / "docs/ARIADGSM_MASTER_EXECUTION_ROADMAP.md", "Roadmap\n")
    write_text(root / "docs/ARIADGSM_EVALUATION_RELEASE_FINAL.md", "Evaluation + Release\n")
    write_text(root / "docs/ARIADGSM_WINDOW_REALITY_RESOLVER_FINAL.md", "Window Reality Resolver\n")
    write_text(root / "desktop-agent/windows-app/VERSION", "0.9.1\n")
    write_text(
        root / "desktop-agent/windows-app/src/AriadGSM.Agent.Updater/Program.cs",
        "public const string PreviousVersion = \"0.8.18\"; // --rollback\n",
    )

    package = root / "desktop-agent/update/releases/AriadGSMAgent-0.9.1.zip"
    package.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(package, "w") as archive:
        archive.writestr("AriadGSM Agent.exe", "stub")
        archive.writestr("ariadgsm-version.json", "{\"version\":\"0.9.1\"}")
        archive.writestr("engines/vision/AriadGSM.Vision.Worker.exe", "stub")
        archive.writestr("engines/hands/AriadGSM.Hands.Worker.exe", "stub")

    manifest = {
        "version": "0.9.1",
        "packageUrl": "https://raw.githubusercontent.com/raugsm/miweb-2.0/main/desktop-agent/update/releases/AriadGSMAgent-0.9.1.zip",
        "sha256": sha256(package),
        "autoApply": True,
    }
    write_text(root / "desktop-agent/update/ariadgsm-update.json", json.dumps(manifest, indent=2))


def main() -> int:
    assert not validate_contract(sample_event("runtime_governor_state"), "runtime_governor_state")
    assert not validate_contract(sample_event("evaluation_release_state"), "evaluation_release_state")

    stopped = build_runtime_governor_state(
        REPO / "desktop-agent/runtime",
        desired_running=False,
        registered_processes=[
            {"name": "AriadGSM.Vision.Worker", "pid": 123, "role": "worker", "owned": True, "running": True},
            {"name": "chrome", "pid": 456, "role": "browser", "owned": True, "running": True},
        ],
    )
    assert stopped["status"] == "attention"
    assert stopped["summary"]["orphanedOwned"] == 1
    assert stopped["ownedProcesses"][1]["owned"] is False
    assert stopped["summary"]["browsersObservedNotOwned"] == 1
    assert not validate_contract(stopped, "runtime_governor_state")

    with TemporaryDirectory() as repo_tmp, TemporaryDirectory() as runtime_tmp:
        repo = Path(repo_tmp)
        runtime = Path(runtime_tmp)
        make_release_repo(repo)
        (runtime / "windows-app.log").write_text("ok verified\n", encoding="utf-8")
        state = run_evaluation_release_once(repo, runtime, version="0.9.1")
        assert state["status"] == "ok"
        assert state["summary"]["gatesTotal"] == 7
        assert state["summary"]["gatesOk"] == 7
        assert state["summary"]["releaseCandidate"] is True
        assert (runtime / "durable-checkpoints.jsonl").exists()
        assert (runtime / "trace-grading-state.json").exists()
        assert (runtime / "release-manager-state.json").exists()
        assert not validate_contract(state, "evaluation_release_state")

    print("evaluation release OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
