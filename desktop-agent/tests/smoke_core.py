from __future__ import annotations

import json
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from ariadgsm_agent.config import load_config
from ariadgsm_agent.service import run_service


def main() -> int:
    with tempfile.TemporaryDirectory() as temp_dir:
        temp = Path(temp_dir)
        observations = temp / "observations.jsonl"
        observations.write_text(
            json.dumps(
                {
                    "channelId": "wa-1",
                    "source": "accessibility",
                    "confidence": 0.91,
                    "capturedAt": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
                    "conversationTitle": "Cliente prueba",
                    "visibleOnly": True,
                    "messages": [{"text": "Cuanto vale liberar Samsung por IMEI?", "direction": "client"}],
                },
                ensure_ascii=False,
            )
            + "\n",
            encoding="utf-8",
        )
        config_path = temp / "config.json"
        config_path.write_text(
            json.dumps(
                {
                    "runtimeDir": str(temp / "runtime"),
                    "memoryDb": str(temp / "runtime" / "memory.sqlite"),
                    "stateFile": str(temp / "runtime" / "state.json"),
                    "readerCore": {"observationsFile": str(observations), "maxAgeSeconds": 60, "limit": 20},
                    "service": {"intervalSeconds": 0.1},
                }
            ),
            encoding="utf-8",
        )
        state = run_service(load_config(config_path), once=True, watch=False)
        assert state["ingested"]["messages"] == 1, state
        assert state["decisions"][0]["decision"]["intent"] == "price_request", state
        print("desktop-agent smoke OK")
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
