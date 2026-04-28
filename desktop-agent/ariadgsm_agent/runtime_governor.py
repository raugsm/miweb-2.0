from __future__ import annotations

import argparse
import json
import os
import subprocess
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


VERSION = "0.9.2"
ENGINE = "ariadgsm_runtime_governor"
CONTRACT = "runtime_governor_state"
OWNED_PROCESS_NAMES = {
    "AriadGSM Agent",
    "AriadGSM.Vision.Worker",
    "AriadGSM.Perception.Worker",
    "AriadGSM.Interaction.Worker",
    "AriadGSM.Orchestrator.Worker",
    "AriadGSM.Hands.Worker",
    "node",
}
BROWSER_PROCESS_NAMES = {"msedge", "chrome", "firefox", "msedgewebview2"}


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def read_json(path: Path) -> dict[str, Any]:
    if not path.exists():
        return {}
    try:
        value = json.loads(path.read_text(encoding="utf-8-sig"))
    except Exception:
        return {}
    return value if isinstance(value, dict) else {}


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_suffix(path.suffix + ".tmp")
    temp.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(temp, path)


def normalize_name(value: Any) -> str:
    return str(value or "").replace(".exe", "").strip()


def is_browser_name(value: str) -> bool:
    return normalize_name(value).lower() in BROWSER_PROCESS_NAMES


def process_running(pid: int) -> bool:
    if pid <= 0:
        return False
    try:
        subprocess.run(
            ["tasklist", "/FI", f"PID eq {pid}"],
            capture_output=True,
            text=True,
            timeout=3,
            check=False,
        )
    except Exception:
        return False
    result = subprocess.run(
        ["tasklist", "/FI", f"PID eq {pid}", "/FO", "CSV", "/NH"],
        capture_output=True,
        text=True,
        timeout=3,
        check=False,
    )
    return str(pid) in result.stdout


def read_registered_processes(runtime_dir: Path) -> list[dict[str, Any]]:
    state = read_json(runtime_dir / "runtime-governor-state.json")
    rows = state.get("ownedProcesses") if isinstance(state.get("ownedProcesses"), list) else []
    result: list[dict[str, Any]] = []
    for item in rows:
        if not isinstance(item, dict):
            continue
        name = normalize_name(item.get("name"))
        if not name:
            continue
        pid = int(item.get("pid") or 0)
        role = str(item.get("role") or "unknown")
        owned = bool(item.get("owned", True))
        result.append(
            {
                **item,
                "name": name,
                "pid": pid,
                "role": role,
                "owned": owned,
                "running": process_running(pid) if pid else bool(item.get("running")),
            }
        )
    return result


def build_runtime_governor_state(
    runtime_dir: Path,
    *,
    desired_running: bool | None = None,
    force_shutdown_requested: bool = False,
    registered_processes: list[dict[str, Any]] | None = None,
) -> dict[str, Any]:
    runtime = runtime_dir.resolve()
    life = read_json(runtime / "life-controller-state.json")
    if desired_running is None:
        desired_running = bool(life.get("desiredRunning"))

    registered = registered_processes if registered_processes is not None else read_registered_processes(runtime)
    owned_processes: list[dict[str, Any]] = []
    orphaned = 0
    browsers_seen = 0
    forced_stops = 0
    for item in registered:
        name = normalize_name(item.get("name"))
        running = bool(item.get("running"))
        owned = bool(item.get("owned", True))
        role = str(item.get("role") or "unknown")
        if is_browser_name(name):
            browsers_seen += 1
            owned = False
        if owned and running and not desired_running:
            orphaned += 1
        if force_shutdown_requested and owned and running and not is_browser_name(name):
            forced_stops += 1
        owned_processes.append(
            {
                "name": name,
                "pid": int(item.get("pid") or 0),
                "role": role,
                "owned": owned,
                "running": running,
                "jobAssigned": bool(item.get("jobAssigned")),
                "startedAt": str(item.get("startedAt") or ""),
            }
        )

    running_owned = sum(1 for item in owned_processes if item["owned"] and item["running"])
    status = "ok"
    if orphaned:
        status = "attention"
    if any(item["owned"] and is_browser_name(item["name"]) for item in owned_processes):
        status = "blocked"
    if not desired_running and not running_owned:
        status = "idle"

    state = {
        "status": status,
        "engine": ENGINE,
        "version": VERSION,
        "updatedAt": utc_now(),
        "contract": CONTRACT,
        "desiredRunning": bool(desired_running),
        "policy": {
            "windowsJobObject": True,
            "killBrowsers": False,
            "gracefulShutdownFirst": True,
            "forceKillOwnedOnly": True,
            "controlLoop": "desired_state_vs_observed_state",
        },
        "ownedProcesses": owned_processes,
        "summary": {
            "ownedTotal": sum(1 for item in owned_processes if item["owned"]),
            "runningOwned": running_owned,
            "orphanedOwned": orphaned,
            "forcedStops": forced_stops,
            "browsersObservedNotOwned": browsers_seen,
            "verifiedStopped": not desired_running and running_owned == 0,
        },
        "humanReport": {
            "headline": (
                "Runtime Governor encontro procesos AriadGSM vivos sin orden de correr."
                if orphaned
                else "Runtime Governor alinea procesos con estado deseado."
            ),
            "queEstaPasando": [
                f"{running_owned} procesos AriadGSM propios siguen vivos.",
                f"{browsers_seen} navegadores observados quedan fuera de propiedad de la IA.",
            ],
            "riesgos": (
                ["Hay motores propios vivos mientras la IA esta detenida."]
                if orphaned
                else []
            ),
        },
    }
    return state


def run_runtime_governor_once(runtime_dir: Path, *, desired_running: bool | None = None) -> dict[str, Any]:
    runtime = runtime_dir.resolve()
    state = build_runtime_governor_state(runtime, desired_running=desired_running)
    write_json(runtime / "runtime-governor-state.json", state)
    write_json(runtime / "runtime-governor-report.json", state["humanReport"])
    return state


def build_arg_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="AriadGSM Runtime Governor")
    parser.add_argument("--runtime-dir", default="./runtime")
    parser.add_argument("--desired-running", action="store_true")
    parser.add_argument("--desired-stopped", action="store_true")
    parser.add_argument("--json", action="store_true")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_arg_parser().parse_args(argv)
    desired: bool | None = None
    if args.desired_running:
        desired = True
    if args.desired_stopped:
        desired = False
    state = run_runtime_governor_once(Path(args.runtime_dir), desired_running=desired)
    if args.json:
        print(json.dumps(state, ensure_ascii=False))
    else:
        print(f"{state['status']}: {state['humanReport']['headline']}")
    return 0 if state["status"] in {"ok", "attention", "idle"} else 1


if __name__ == "__main__":
    raise SystemExit(main())
