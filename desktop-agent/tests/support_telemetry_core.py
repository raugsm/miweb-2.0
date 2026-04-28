from __future__ import annotations

import json
import sys
import zipfile
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.contracts import sample_event, validate_contract
from ariadgsm_agent.support_telemetry import run_support_telemetry_once


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False), encoding="utf-8")


def main() -> int:
    assert not validate_contract(sample_event("support_telemetry_event"), "support_telemetry_event")
    assert not validate_contract(sample_event("support_telemetry_state"), "support_telemetry_state")

    with TemporaryDirectory() as tmp:
        runtime = Path(tmp)
        kernel = sample_event("runtime_kernel_state")
        kernel["status"] = "attention"
        kernel["incidents"][0]["detail"] = (
            "token=1234567890abcdef1234567890abcdef "
            "C:\\Users\\Bryams\\AppData\\Local\\AriadGSM\\secret.txt "
            "cliente +51999999999"
        )
        write_json(runtime / "runtime-kernel-state.json", kernel)
        write_json(runtime / "runtime-governor-state.json", sample_event("runtime_governor_state"))
        write_json(runtime / "window-reality-state.json", sample_event("window_reality_state"))
        write_json(runtime / "cloud-sync-state.json", sample_event("cloud_sync_state"))
        (runtime / "windows-app.log").write_text(
            "2026-04-28 error UnauthorizedAccessException token=abcdef1234567890abcdef123456 C:\\Users\\Bryams\\x\n",
            encoding="utf-8",
        )

        state = run_support_telemetry_once(runtime, simulate=["crash", "hang", "privacy"])
        assert state["engine"] == "ariadgsm_support_telemetry_core"
        assert state["status"] == "blocked"
        assert state["summary"]["incidentsOpen"] >= 3
        assert state["summary"]["redactionsApplied"] >= 3
        assert state["summary"]["bundleReady"] is True
        assert not validate_contract(state, "support_telemetry_state")

        event_lines = [
            json.loads(line)
            for line in (runtime / "support-telemetry-events.jsonl").read_text(encoding="utf-8").splitlines()
            if line.strip()
        ]
        assert event_lines
        for event in event_lines:
            assert not validate_contract(event, "support_telemetry_event")

        combined_events = "\n".join(json.dumps(event, ensure_ascii=False) for event in event_lines)
        assert "1234567890abcdef1234567890abcdef" not in combined_events
        assert "C:\\Users\\Bryams" not in combined_events
        assert "+51999999999" not in combined_events

        bundle = Path(state["supportBundle"]["path"])
        assert bundle.exists()
        with zipfile.ZipFile(bundle) as archive:
            names = set(archive.namelist())
            assert "manifest.json" in names
            assert "support-telemetry-state.json" in names
            assert not any("screenshot" in name.lower() or "frame" in name.lower() for name in names)
            text = "\n".join(
                archive.read(name).decode("utf-8", errors="replace")
                for name in names
                if name.endswith(".json") or name.endswith(".jsonl")
            )
            assert "1234567890abcdef1234567890abcdef" not in text
            assert "C:\\Users\\Bryams" not in text
            assert "+51999999999" not in text
            manifest = json.loads(archive.read("manifest.json").decode("utf-8"))
            assert manifest["privacy"]["containsRawScreenshots"] is False
            assert manifest["privacy"]["containsFullChats"] is False
            assert manifest["privacy"]["containsSecrets"] is False

    print("support telemetry core OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
