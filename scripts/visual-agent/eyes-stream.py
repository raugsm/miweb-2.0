#!/usr/bin/env python
"""Continuous visual eyes for AriadGSM.

This is not DOM automation. It watches the screen like a live camera, detects
changed regions, OCRs only the changed crops, and keeps a short memory buffer.
"""

from __future__ import annotations

import argparse
import html
import importlib.util
import json
import os
import re
import subprocess
import sys
import time
from collections import deque
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image, ImageGrab


SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent.parent
DEFAULT_CONFIG = SCRIPT_DIR / "visual-agent.cloud.json"
OCR_SCRIPT = SCRIPT_DIR / "visual-ocr-image.ps1"
RUNTIME_DIR = SCRIPT_DIR / "runtime"
EYES_DIR = RUNTIME_DIR / "eyes-stream"
STATE_FILE = RUNTIME_DIR / "eyes-stream.state.json"


def load_agent_local():
    module_path = SCRIPT_DIR / "agent-local.py"
    spec = importlib.util.spec_from_file_location("agent_local", module_path)
    if not spec or not spec.loader:
        raise RuntimeError("No pude cargar agent-local.py.")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


agent_local = load_agent_local()


@dataclass
class Region:
    channel_id: str
    name: str
    rect: tuple[int, int, int, int]


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def stamp() -> str:
    return datetime.now().strftime("%Y%m%d-%H%M%S-%f")[:-3]


def log(message: str) -> None:
    print(f"[{datetime.now().strftime('%Y-%m-%d %H:%M:%S')}] {message}", flush=True)


def read_config(config_path: Path) -> dict[str, Any]:
    return json.loads(config_path.read_text(encoding="utf-8-sig"))


def ratio_value(obj: dict[str, Any] | None, default: dict[str, Any] | None, name: str, fallback: float) -> float:
    value = fallback
    if default and name in default:
        value = default[name]
    if obj and name in obj:
        value = obj[name]
    return max(0.0, min(1.0, float(value)))


def build_regions(config: dict[str, Any], screen_size: tuple[int, int], full_section: bool = False) -> list[Region]:
    width, height = screen_size
    channels = list(config.get("channels") or [])
    if len(channels) < 3:
        channels = [
            {"id": "wa-1", "name": "WhatsApp 1"},
            {"id": "wa-2", "name": "WhatsApp 2"},
            {"id": "wa-3", "name": "WhatsApp 3"},
        ]
    screen_capture = config.get("screenCapture") or {}
    default_crop = screen_capture.get("defaultCrop") or {}
    channel_crops = {item.get("id"): item for item in screen_capture.get("channelCrops") or []}
    region_width = width // 3
    regions: list[Region] = []
    for index in range(3):
        base_left = region_width * index
        base_width = width - base_left if index == 2 else region_width
        channel = channels[index]
        channel_id = channel.get("id") or f"wa-{index + 1}"
        crop = channel_crops.get(channel_id)
        if full_section:
            x_start, x_end, y_start, y_end = 0.0, 1.0, 0.0, 1.0
        else:
            x_start = ratio_value(crop, default_crop, "xStartRatio", 0.42)
            x_end = ratio_value(crop, default_crop, "xEndRatio", 0.99)
            y_start = ratio_value(crop, default_crop, "yStartRatio", 0.1)
            y_end = ratio_value(crop, default_crop, "yEndRatio", 0.94)
        if x_end <= x_start:
            x_end = 1.0
        if y_end <= y_start:
            y_end = 1.0
        left = int(base_left + (base_width * x_start))
        top = int(height * y_start)
        right = int(base_left + (base_width * x_end))
        bottom = int(height * y_end)
        regions.append(Region(channel_id=str(channel_id), name=str(channel.get("name") or channel_id), rect=(left, top, right, bottom)))
    return regions


def fingerprint(image: Image.Image, size: tuple[int, int]) -> np.ndarray:
    gray = image.convert("L").resize(size)
    return np.asarray(gray, dtype=np.int16)


def change_score(previous: np.ndarray | None, current: np.ndarray) -> dict[str, float]:
    if previous is None:
        return {"mean": 255.0, "changedRatio": 1.0, "score": 255.0}
    diff = np.abs(current - previous)
    mean = float(diff.mean())
    changed_ratio = float((diff > 18).mean())
    return {"mean": mean, "changedRatio": changed_ratio, "score": mean + (changed_ratio * 60.0)}


def run_ocr(image_path: Path) -> list[str]:
    result = subprocess.run(
        [
            agent_local.find_powershell(),
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            str(OCR_SCRIPT),
            "-ImagePath",
            str(image_path),
        ],
        cwd=PROJECT_ROOT,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        raise RuntimeError((result.stderr or result.stdout).strip() or "OCR fallo.")
    payload = agent_local.extract_json(result.stdout)
    return [str(line) for line in payload.get("Lines", []) if str(line).strip()]


def time_only_line(text: str) -> bool:
    value = re.sub(r"\s+", " ", text).strip()
    if not re.search(r"\b[0-9gqloOiIl]{1,2}\s*[:;]\s*[0-9oO]{2}", value) and not re.search(r"\b[apu]\.?\s*(m|rn|nm|urn|um)\.?\b", value, re.I):
        return False
    semantic = re.sub(r"\b[0-9gqloOiIl]{1,2}\s*[:;]\s*[0-9oO]{2}", "", value, flags=re.I)
    semantic = re.sub(r"\b[apu]\.?\s*(m|rn|nm|urn|um)\.?\b", "", semantic, flags=re.I)
    semantic = re.sub(r"[^a-z0-9áéíóúñ]+", "", semantic.lower())
    return len(semantic) <= 4


def line_decision(text: str) -> dict[str, Any]:
    clean = re.sub(r"\s+", " ", str(text)).strip()
    if len(clean) < 6:
        return {"Raw": text, "Clean": clean, "Accepted": False, "Reason": "muy_corta"}
    if not re.search(r"[A-Za-z0-9ÁÉÍÓÚáéíóúÑñ]", clean):
        return {"Raw": text, "Clean": clean, "Accepted": False, "Reason": "sin_texto_util"}
    if time_only_line(clean):
        return {"Raw": text, "Clean": clean, "Accepted": False, "Reason": "hora_suelta"}
    if agent_local.is_payment_group(clean):
        return {"Raw": text, "Clean": clean, "Accepted": False, "Reason": "grupo_pagos_ignorado"}
    if agent_local.is_interface_noise(clean):
        return {"Raw": text, "Clean": clean, "Accepted": False, "Reason": "interfaz_o_ruido"}
    return {"Raw": text, "Clean": clean, "Accepted": True, "Reason": "mensaje_util"}


def blocked_section_reason(raw_lines: list[str]) -> str | None:
    text = agent_local.normalize(" ".join(raw_lines))
    patterns = {
        "codex_en_zona": ["codex", "tareas completadas", "revisar cambios", "scripts/visual-agent"],
        "launcher_en_zona": ["ariadgsm agent desktop", "modo vivo:", "que paso", "busquedas probadas"],
        "navegador_no_chat": ["youtube", "github", "railway", "deployments", "variables"],
    }
    for reason, needles in patterns.items():
        if any(needle in text for needle in needles):
            return reason
    return None


def make_messages(channel_id: str, lines: list[str]) -> list[dict[str, Any]]:
    now = now_iso()
    day_key = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    messages = []
    for index, line in enumerate(lines):
        messages.append(
            {
                "channelId": channel_id,
                "contactName": f"WhatsApp {channel_id[-1]}",
                "conversationType": "visible_screen_stream",
                "conversationId": f"stream-{channel_id}",
                "messageKey": f"stream-{channel_id}-{int(time.time() * 1000)}-{index}",
                "senderName": "Ojo vivo",
                "direction": "unknown",
                "text": line,
                "sentAt": now,
                "dayKey": day_key,
            }
        )
    return messages


def render_report(state: dict[str, Any], output_path: Path) -> None:
    def esc(value: Any) -> str:
        return html.escape("" if value is None else str(value))

    events = state.get("events") or []
    rows = []
    for event in reversed(events[-80:]):
        accepted = "<br>".join(esc(line) for line in event.get("acceptedLines", [])) or "<span class='muted'>Sin texto aceptado</span>"
        ignored = "<br>".join(f"{esc(item.get('Clean'))} <span class='muted'>({esc(item.get('Reason'))})</span>" for item in event.get("ignoredLines", [])[:8])
        image = Path(event.get("imagePath", ""))
        image_src = image.resolve().as_uri() if image.exists() else ""
        rows.append(
            f"""
            <article class="event">
              <div class="event-head">
                <strong>{esc(event.get('channelId'))}</strong>
                <span>{esc(event.get('capturedAt'))}</span>
                <span>score {esc(round(float(event.get('change', {}).get('score', 0)), 2))}</span>
              </div>
              <img src="{image_src}" alt="{esc(event.get('channelId'))}">
              <div class="cols">
                <div><h3>Aceptado</h3><p>{accepted}</p></div>
                <div><h3>Ignorado</h3><p>{ignored}</p></div>
              </div>
            </article>
            """
        )
    decision = state.get("lastDecision") or {}
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(
        f"""<!doctype html>
<html lang="es">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>AriadGSM Ojo Vivo</title>
  <style>
    :root {{ --bg:#eef4ff; --surface:#fff; --line:#d9e3f3; --ink:#101827; --muted:#61708a; --accent:#2478f2; }}
    * {{ box-sizing: border-box; }}
    body {{ margin:0; font-family:"Segoe UI",Arial,sans-serif; background:var(--bg); color:var(--ink); }}
    header {{ background:var(--surface); border-bottom:1px solid var(--line); padding:20px 26px; display:flex; justify-content:space-between; gap:20px; }}
    h1 {{ margin:0; font-size:24px; }}
    main {{ padding:22px; display:grid; gap:16px; }}
    .panel,.event {{ background:var(--surface); border:1px solid var(--line); border-radius:8px; padding:16px; box-shadow:0 10px 26px rgba(19,52,102,.08); }}
    .metrics {{ display:grid; grid-template-columns:repeat(5,minmax(120px,1fr)); gap:10px; margin-top:12px; }}
    .metric {{ background:#f4f8ff; border:1px solid var(--line); border-radius:8px; padding:10px; }}
    .metric b {{ display:block; color:var(--accent); font-size:20px; }}
    .event-head {{ display:flex; gap:12px; align-items:center; color:var(--muted); margin-bottom:10px; }}
    .event img {{ width:100%; max-height:320px; object-fit:contain; background:#0b1220; border-radius:8px; border:1px solid var(--line); }}
    .cols {{ display:grid; grid-template-columns:1fr 1fr; gap:14px; }}
    h3 {{ margin-bottom:6px; }}
    .muted {{ color:var(--muted); }}
    @media(max-width:900px) {{ .metrics,.cols {{ grid-template-columns:1fr; }} header {{ display:block; }} }}
  </style>
</head>
<body>
  <header>
    <div><h1>AriadGSM Ojo Vivo</h1><div class="muted">Streaming visual por cambios, sin DOM</div></div>
    <div class="muted">Actualizado: {esc(state.get('updatedAt'))}</div>
  </header>
  <main>
    <section class="panel">
      <h2>Estado</h2>
      <div class="metrics">
        <div class="metric"><b>{esc(state.get('frames'))}</b>frames</div>
        <div class="metric"><b>{esc(state.get('ocrRuns'))}</b>OCR</div>
        <div class="metric"><b>{esc(len(events))}</b>eventos</div>
        <div class="metric"><b>{esc(decision.get('Status'))}</b>decision</div>
        <div class="metric"><b>{esc(decision.get('TargetChannel'))}</b>canal</div>
      </div>
      <p><b>Ultima decision:</b> {esc(decision.get('Label') or decision.get('Reason'))} · {esc(decision.get('Text'))}</p>
    </section>
    {''.join(rows)}
  </main>
</body>
</html>""",
        encoding="utf-8",
    )


def write_state(state: dict[str, Any]) -> None:
    RUNTIME_DIR.mkdir(parents=True, exist_ok=True)
    temp = STATE_FILE.with_suffix(".tmp")
    temp.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
    temp.replace(STATE_FILE)
    render_report(state, EYES_DIR / "latest.html")


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM continuous visual eyes")
    parser.add_argument("--config-path", default=str(DEFAULT_CONFIG))
    parser.add_argument("--watch", action="store_true")
    parser.add_argument("--duration-seconds", type=float, default=15.0)
    parser.add_argument("--interval-ms", type=int, default=750)
    parser.add_argument("--change-threshold", type=float, default=9.0)
    parser.add_argument("--ocr-cooldown-seconds", type=float, default=2.0)
    parser.add_argument("--fingerprint-width", type=int, default=96)
    parser.add_argument("--fingerprint-height", type=int, default=96)
    parser.add_argument("--buffer-events", type=int, default=160)
    parser.add_argument("--full-section", action="store_true")
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    config_path = Path(args.config_path).resolve()
    config = json.loads(config_path.read_text(encoding="utf-8-sig"))
    session_dir = EYES_DIR / datetime.now().strftime("%Y%m%d-%H%M%S")
    crop_dir = session_dir / "crops"
    crop_dir.mkdir(parents=True, exist_ok=True)
    first = ImageGrab.grab(all_screens=True)
    regions = build_regions(config, first.size, args.full_section)
    fingerprints: dict[str, np.ndarray] = {}
    last_ocr: dict[str, float] = {}
    events: deque[dict[str, Any]] = deque(maxlen=args.buffer_events)
    frames = 0
    ocr_runs = 0
    last_decision: dict[str, Any] = {"Status": "no_local_action", "Source": "eyes_stream", "Reason": "Sin lectura todavia."}
    start = time.monotonic()
    log(f"Ojo vivo iniciado: {first.size[0]}x{first.size[1]}, {len(regions)} regiones, intervalo {args.interval_ms}ms.")

    while True:
        image = ImageGrab.grab(all_screens=True)
        frames += 1
        now_mono = time.monotonic()
        for region in regions:
            crop = image.crop(region.rect)
            fp = fingerprint(crop, (args.fingerprint_width, args.fingerprint_height))
            change = change_score(fingerprints.get(region.channel_id), fp)
            fingerprints[region.channel_id] = fp
            should_ocr = change["score"] >= args.change_threshold and (now_mono - last_ocr.get(region.channel_id, 0.0)) >= args.ocr_cooldown_seconds
            if not should_ocr:
                continue
            last_ocr[region.channel_id] = now_mono
            image_path = crop_dir / f"{stamp()}-{region.channel_id}.png"
            crop.save(image_path)
            try:
                raw_lines = run_ocr(image_path)
            except Exception as exc:
                raw_lines = []
                decisions = [{"Raw": "", "Clean": "", "Accepted": False, "Reason": f"ocr_error: {exc}"}]
                ignored = decisions
            else:
                blocked_reason = blocked_section_reason(raw_lines)
                if blocked_reason:
                    decisions = [
                        {"Raw": line, "Clean": re.sub(r"\s+", " ", line).strip(), "Accepted": False, "Reason": blocked_reason}
                        for line in raw_lines
                    ]
                else:
                    decisions = [line_decision(line) for line in raw_lines]
                ignored = [item for item in decisions if not item["Accepted"]]
            accepted = [item["Clean"] for item in decisions if item["Accepted"]]
            if accepted:
                messages = make_messages(region.channel_id, accepted)
                last_decision = agent_local.rule_decision(messages, 3)
            event = {
                "capturedAt": now_iso(),
                "channelId": region.channel_id,
                "name": region.name,
                "rect": list(region.rect),
                "imagePath": str(image_path),
                "change": change,
                "rawLines": raw_lines,
                "acceptedLines": accepted,
                "ignoredLines": ignored,
                "decision": last_decision,
            }
            events.append(event)
            ocr_runs += 1
            log(f"{region.channel_id}: cambio {change['score']:.2f}, OCR {len(raw_lines)} lineas, utiles {len(accepted)}.")

        state = {
            "Status": "running" if args.watch else "completed",
            "Engine": "eyes_stream",
            "updatedAt": now_iso(),
            "sessionDir": str(session_dir),
            "frames": frames,
            "ocrRuns": ocr_runs,
            "regions": [{"channelId": r.channel_id, "name": r.name, "rect": list(r.rect)} for r in regions],
            "events": list(events),
            "lastDecision": last_decision,
            "report": str(EYES_DIR / "latest.html"),
        }
        write_state(state)

        if not args.watch and (time.monotonic() - start) >= args.duration_seconds:
            break
        time.sleep(max(0.05, args.interval_ms / 1000.0))

    state["Status"] = "completed"
    state["updatedAt"] = now_iso()
    write_state(state)
    print(json.dumps(state, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(1)
