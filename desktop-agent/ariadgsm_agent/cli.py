from __future__ import annotations

import argparse
import json
from pathlib import Path

from .config import load_config
from .service import run_service


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM Desktop Agent Core")
    parser.add_argument("--config", default=str(Path("desktop-agent") / "config.example.json"))
    parser.add_argument("--once", action="store_true")
    parser.add_argument("--watch", action="store_true")
    parser.add_argument("--interval-seconds", type=float, default=None)
    parser.add_argument("--max-cycles", type=int, default=0)
    parser.add_argument("--json", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    config = load_config(args.config)
    state = run_service(
        config,
        once=args.once or not args.watch,
        watch=args.watch,
        max_cycles=max(0, args.max_cycles),
        interval_seconds=args.interval_seconds,
    )
    if args.json:
        print(json.dumps(state, ensure_ascii=False, indent=2))
    else:
        ingested = state.get("ingested") or {}
        memory = state.get("memory") or {}
        print(
            "AriadGSM core: "
            f"seen={state.get('observationsSeen')} "
            f"new_messages={ingested.get('messages')} "
            f"memory_messages={memory.get('messages')} "
            f"db={memory.get('db')}"
        )
    return 0
