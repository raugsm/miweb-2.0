#!/usr/bin/env python
"""Build a local visual debugger report for AriadGSM eyes."""

from __future__ import annotations

import argparse
import html
import importlib.util
import json
import os
import subprocess
import sys
from datetime import datetime
from pathlib import Path
from typing import Any


SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent.parent
DEFAULT_CONFIG = SCRIPT_DIR / "visual-agent.cloud.json"
CAPTURE_SCRIPT = SCRIPT_DIR / "visual-screen-capture.ps1"
REPORT_ROOT = SCRIPT_DIR / "runtime" / "visual-debugger"


def load_agent_local():
    module_path = SCRIPT_DIR / "agent-local.py"
    spec = importlib.util.spec_from_file_location("agent_local", module_path)
    if not spec or not spec.loader:
        raise RuntimeError("No pude cargar agent-local.py.")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


agent_local = load_agent_local()


def quote_ps(value: Any) -> str:
    return "'" + str(value).replace("'", "''") + "'"


def run_capture(config_path: Path, max_lines: int, full_section: bool) -> dict[str, Any]:
    parts = [
        "&",
        quote_ps(CAPTURE_SCRIPT),
        "-ConfigPath",
        quote_ps(config_path),
        "-MaxLinesPerChannel",
        str(max_lines),
        "-DebugDetails",
    ]
    if full_section:
        parts.append("-FullSection")
    parts += ["|", "ConvertTo-Json", "-Depth", "14"]
    result = subprocess.run(
        [agent_local.find_powershell(), "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", " ".join(parts)],
        cwd=PROJECT_ROOT,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        raise RuntimeError((result.stderr or result.stdout).strip() or "La captura debug fallo.")
    return json.loads(result.stdout)


def esc(value: Any) -> str:
    return html.escape("" if value is None else str(value))


def file_uri(value: Any) -> str:
    path = Path(str(value))
    if not path.exists():
        return ""
    return path.resolve().as_uri()


def class_for_reason(reason: str) -> str:
    if reason == "mensaje_util":
        return "accepted"
    if reason in {"seccion_bloqueada", "grupo_pagos_ignorado"}:
        return "blocked"
    return "ignored"


def render_lines_table(lines: list[dict[str, Any]]) -> str:
    if not lines:
        return '<p class="empty">Sin lineas OCR para mostrar.</p>'
    rows = []
    for item in lines:
        reason = str(item.get("Reason") or "")
        css = class_for_reason(reason)
        rows.append(
            "<tr class='{css}'>"
            "<td>{state}</td>"
            "<td>{clean}</td>"
            "<td>{reason}</td>"
            "<td><code>{pattern}</code></td>"
            "</tr>".format(
                css=css,
                state="Aceptada" if item.get("Accepted") else "Ignorada",
                clean=esc(item.get("Clean") or item.get("Raw") or ""),
                reason=esc(reason),
                pattern=esc(item.get("MatchedPattern") or ""),
            )
        )
    return (
        "<table><thead><tr><th>Estado</th><th>Texto OCR</th><th>Motivo</th><th>Patron</th></tr></thead>"
        f"<tbody>{''.join(rows)}</tbody></table>"
    )


def render_region(region: dict[str, Any]) -> str:
    accepted = [line for line in region.get("DebugLines", []) if line.get("Accepted")]
    ignored = [line for line in region.get("DebugLines", []) if not line.get("Accepted")]
    image = file_uri(region.get("Path"))
    blocked = bool(region.get("Blocked"))
    return f"""
      <section class="region {'blocked-region' if blocked else ''}">
        <div class="region-head">
          <div>
            <h2>{esc(region.get('ChannelId'))} · {esc(region.get('Name'))}</h2>
            <p>{'Bloqueada: parece otra ventana/interfaz' if blocked else 'Zona leida como WhatsApp'}</p>
          </div>
          <div class="stats">
            <span>{esc(region.get('RawLineCount'))} OCR</span>
            <span>{esc(region.get('UsefulLineCount'))} utiles</span>
          </div>
        </div>
        <img class="capture" src="{image}" alt="Captura {esc(region.get('ChannelId'))}">
        <div class="split">
          <div>
            <h3>Aceptadas ({len(accepted)})</h3>
            {render_lines_table(accepted)}
          </div>
          <div>
            <h3>Ignoradas ({len(ignored)})</h3>
            {render_lines_table(ignored)}
          </div>
        </div>
      </section>
    """


def decision_summary(decision: dict[str, Any]) -> str:
    if decision.get("Status") == "local_match":
        return f"""
          <div class="decision good">
            <strong>{esc(decision.get('Label'))}</strong>
            <span>Canal: {esc(decision.get('TargetChannel'))}</span>
            <span>Fuente: {esc(decision.get('Source'))}</span>
            <span>Score: {esc(decision.get('Score'))}</span>
            <p>{esc(decision.get('Text'))}</p>
            <p><b>Busquedas:</b> {esc(', '.join(decision.get('Queries') or []))}</p>
          </div>
        """
    return f"""
      <div class="decision neutral">
        <strong>Sin accion clara</strong>
        <span>Fuente: {esc(decision.get('Source'))}</span>
        <p>{esc(decision.get('Reason'))}</p>
      </div>
    """


def render_report(capture: dict[str, Any], decision: dict[str, Any], output_dir: Path, config_path: Path) -> Path:
    output_dir.mkdir(parents=True, exist_ok=True)
    report_file = output_dir / "index.html"
    data_file = output_dir / "debug-data.json"
    payload = {"capture": capture, "decision": decision}
    data_file.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")

    regions_html = "\n".join(render_region(region) for region in capture.get("Regions", []))
    generated_at = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    html_text = f"""<!doctype html>
<html lang="es">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>AriadGSM Visual Debugger</title>
  <style>
    :root {{
      --bg: #eef4ff;
      --surface: #ffffff;
      --surface-strong: #f4f8ff;
      --ink: #101827;
      --muted: #61708a;
      --line: #d9e3f3;
      --accent: #2478f2;
      --accent-strong: #0f5fd4;
      --good: #087f5b;
      --bad: #b42318;
      --warn: #b54708;
    }}
    * {{ box-sizing: border-box; }}
    body {{
      margin: 0;
      font-family: "Segoe UI", Arial, sans-serif;
      background: var(--bg);
      color: var(--ink);
    }}
    header {{
      padding: 22px 28px;
      background: var(--surface);
      border-bottom: 1px solid var(--line);
      display: flex;
      justify-content: space-between;
      gap: 18px;
      align-items: center;
    }}
    .brand {{
      display: flex;
      align-items: center;
      gap: 14px;
    }}
    .brand-mark {{
      background: var(--accent);
      color: white;
      font-family: "Arial Black", Arial, sans-serif;
      font-style: italic;
      font-size: 24px;
      padding: 6px 10px;
      line-height: 1;
    }}
    .brand strong {{
      display: block;
      font-size: 22px;
    }}
    .brand span, .meta {{
      color: var(--muted);
      font-size: 13px;
    }}
    main {{
      padding: 24px;
      display: grid;
      gap: 20px;
    }}
    .overview, .region {{
      background: var(--surface);
      border: 1px solid var(--line);
      border-radius: 8px;
      padding: 18px;
      box-shadow: 0 10px 26px rgba(19, 52, 102, 0.08);
    }}
    .overview-grid {{
      display: grid;
      grid-template-columns: repeat(4, minmax(140px, 1fr));
      gap: 12px;
      margin-top: 16px;
    }}
    .metric {{
      background: var(--surface-strong);
      border: 1px solid var(--line);
      padding: 12px;
      border-radius: 8px;
    }}
    .metric b {{
      display: block;
      font-size: 20px;
      color: var(--accent-strong);
    }}
    .decision {{
      margin-top: 14px;
      border-radius: 8px;
      padding: 14px;
      display: grid;
      gap: 6px;
      border: 1px solid var(--line);
    }}
    .decision.good {{
      background: #ecfdf3;
      border-color: #abefc6;
    }}
    .decision.neutral {{
      background: #f8fafc;
    }}
    .decision strong {{ font-size: 18px; }}
    .region-head {{
      display: flex;
      justify-content: space-between;
      gap: 16px;
      align-items: flex-start;
      margin-bottom: 14px;
    }}
    h1, h2, h3, p {{ margin-top: 0; }}
    h2 {{ margin-bottom: 4px; }}
    h3 {{ margin-bottom: 10px; }}
    .stats {{
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }}
    .stats span {{
      background: var(--surface-strong);
      border: 1px solid var(--line);
      padding: 6px 10px;
      border-radius: 999px;
      font-size: 12px;
      color: var(--muted);
    }}
    .capture {{
      width: 100%;
      max-height: 480px;
      object-fit: contain;
      background: #0b1220;
      border: 1px solid var(--line);
      border-radius: 8px;
      margin-bottom: 16px;
    }}
    .split {{
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }}
    table {{
      width: 100%;
      border-collapse: collapse;
      font-size: 13px;
    }}
    th, td {{
      border-bottom: 1px solid var(--line);
      padding: 8px;
      text-align: left;
      vertical-align: top;
    }}
    th {{
      background: var(--surface-strong);
      color: var(--muted);
      font-size: 12px;
    }}
    tr.accepted td:first-child {{ color: var(--good); font-weight: 700; }}
    tr.ignored td:first-child {{ color: var(--muted); }}
    tr.blocked td:first-child {{ color: var(--warn); font-weight: 700; }}
    code {{
      white-space: normal;
      color: var(--muted);
      font-size: 12px;
    }}
    .blocked-region {{
      border-color: #fedf89;
    }}
    .empty {{
      color: var(--muted);
      font-size: 13px;
    }}
    @media (max-width: 980px) {{
      .overview-grid, .split {{ grid-template-columns: 1fr; }}
      header {{ display: block; }}
    }}
  </style>
</head>
<body>
  <header>
    <div class="brand">
      <div class="brand-mark">ARIAD</div>
      <div>
        <strong>Visual Debugger</strong>
        <span>Lo que el agente ve, acepta, ignora y decide</span>
      </div>
    </div>
    <div class="meta">
      <div>Generado: {esc(generated_at)}</div>
      <div>Config: {esc(config_path.name)}</div>
      <div>Datos: {esc(data_file.name)}</div>
    </div>
  </header>
  <main>
    <section class="overview">
      <h1>Resumen de ojos</h1>
      <p>Este reporte no mueve el mouse ni envia mensajes. Solo captura, lee OCR y muestra el razonamiento visual.</p>
      <div class="overview-grid">
        <div class="metric"><b>{esc(capture.get('Lines'))}</b>lineas utiles</div>
        <div class="metric"><b>{esc(len(capture.get('Regions', [])))}</b>zonas</div>
        <div class="metric"><b>{esc(sum(1 for r in capture.get('Regions', []) if r.get('Blocked')))}</b>bloqueadas</div>
        <div class="metric"><b>{esc(len(capture.get('LocalMessages', [])))}</b>mensajes locales</div>
      </div>
      {decision_summary(decision)}
    </section>
    {regions_html}
  </main>
</body>
</html>
"""
    report_file.write_text(html_text, encoding="utf-8")
    return report_file


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM visual debugger")
    parser.add_argument("--config-path", default=str(DEFAULT_CONFIG))
    parser.add_argument("--max-lines-per-capture", type=int, default=40)
    parser.add_argument("--intent-max-queries", type=int, default=3)
    parser.add_argument("--full-section", action="store_true")
    parser.add_argument("--open", action="store_true")
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    config_path = Path(args.config_path).resolve()
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    output_dir = REPORT_ROOT / stamp
    capture = run_capture(config_path, args.max_lines_per_capture, args.full_section)
    decision = agent_local.local_decision(capture, args.intent_max_queries)
    report_file = render_report(capture, decision, output_dir, config_path)
    latest_file = REPORT_ROOT / "latest.html"
    REPORT_ROOT.mkdir(parents=True, exist_ok=True)
    latest_file.write_text(report_file.read_text(encoding="utf-8"), encoding="utf-8")
    result = {
        "Status": "ok",
        "Report": str(report_file),
        "Latest": str(latest_file),
        "Lines": capture.get("Lines"),
        "Decision": decision,
    }
    print(json.dumps(result, ensure_ascii=False, indent=2))
    if args.open:
        os.startfile(str(report_file))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except Exception as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(1)
