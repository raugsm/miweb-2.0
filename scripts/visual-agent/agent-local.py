#!/usr/bin/env python
"""AriadGSM local live agent.

Python owns the live-cycle orchestration. The current Windows OCR and mouse
bridges still run through the existing PowerShell scripts while those pieces are
migrated gradually.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time
import unicodedata
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent.parent
RUNTIME_DIR = SCRIPT_DIR / "runtime"
STATE_FILE = RUNTIME_DIR / "agent-autopilot.state.json"
CAPTURE_SCRIPT = SCRIPT_DIR / "visual-screen-capture.ps1"
INTENT_BRIDGE_SCRIPT = SCRIPT_DIR / "visual-intent-bridge.ps1"
VISUAL_AGENT_JS = SCRIPT_DIR / "visual-agent.js"
DEFAULT_CONFIG = SCRIPT_DIR / "visual-agent.cloud.json"


def now_iso() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def log(message: str) -> None:
    stamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    print(f"[{stamp}] {message}", flush=True)


def normalize(value: str) -> str:
    text = unicodedata.normalize("NFKD", str(value or "").lower())
    text = "".join(ch for ch in text if not unicodedata.combining(ch))
    return re.sub(r"\s+", " ", text).strip()


def find_powershell() -> str:
    windir = os.environ.get("WINDIR", r"C:\Windows")
    candidate = Path(windir) / "System32" / "WindowsPowerShell" / "v1.0" / "powershell.exe"
    if candidate.exists():
        return str(candidate)
    found = shutil.which("powershell.exe") or shutil.which("powershell")
    if found:
        return found
    raise RuntimeError("No encontre powershell.exe para usar los puentes de Windows.")


def find_node() -> str:
    candidates = [
        os.environ.get("ARIADGSM_NODE", ""),
        str(Path.home() / ".cache" / "codex-runtimes" / "codex-primary-runtime" / "dependencies" / "node" / "bin" / "node.exe"),
        r"C:\Program Files\nodejs\node.exe",
    ]
    for candidate in candidates:
        if candidate and Path(candidate).exists():
            return candidate
    found = shutil.which("node.exe") or shutil.which("node")
    if found:
        return found
    raise RuntimeError("No encontre Node.js para publicar eventos a la nube.")


def quote_ps(value: Any) -> str:
    return "'" + str(value).replace("'", "''") + "'"


def ps_args(params: dict[str, Any]) -> list[str]:
    args: list[str] = []
    for key, value in params.items():
        if value is None or value is False:
            continue
        args.append(f"-{key}")
        if value is not True:
            args.append(str(value))
    return args


def run_capture_as_json(config_path: Path, max_lines: int) -> dict[str, Any]:
    parts = [
        "&",
        quote_ps(CAPTURE_SCRIPT),
        "-ConfigPath",
        quote_ps(config_path),
        "-MaxLinesPerChannel",
        str(max_lines),
        "|",
        "ConvertTo-Json",
        "-Depth",
        "12",
    ]
    command = " ".join(parts)
    result = subprocess.run(
        [find_powershell(), "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
        cwd=PROJECT_ROOT,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        raise RuntimeError((result.stderr or result.stdout).strip() or "La captura visual fallo.")
    return json.loads(result.stdout)


def extract_json(text: str) -> Any:
    decoder = json.JSONDecoder()
    best = None
    best_length = -1
    for index, char in enumerate(text):
        if char not in "[{":
            continue
        try:
            value, end = decoder.raw_decode(text[index:])
            if end > best_length:
                best = value
                best_length = end
        except json.JSONDecodeError:
            continue
    if best is None:
        raise RuntimeError("El puente no devolvio JSON util.")
    return best


def run_json_ps_script(script: Path, params: dict[str, Any]) -> dict[str, Any]:
    result = subprocess.run(
        [find_powershell(), "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script), *ps_args(params)],
        cwd=PROJECT_ROOT,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
        check=False,
    )
    if result.returncode != 0:
        raise RuntimeError((result.stderr or result.stdout).strip() or f"{script.name} fallo.")
    return extract_json(result.stdout)


def read_config(config_path: Path) -> dict[str, Any]:
    if not config_path.exists():
        raise RuntimeError(f"No encontre {config_path}.")
    return json.loads(config_path.read_text(encoding="utf-8-sig"))


def resolve_agent_path(value: str | None, fallback: str) -> Path:
    target = Path(value or fallback)
    if target.is_absolute():
        return target
    return SCRIPT_DIR / target


def publish_capture(capture_result: dict[str, Any], config: dict[str, Any], config_path: Path, send: bool, mode: str) -> dict[str, Any] | None:
    if not send or mode != "Live":
        return None

    event_file = Path(str(capture_result.get("EventFile") or ""))
    if not event_file.exists():
        return {"Published": False, "Reason": "No encontre el JSON local para publicar."}

    inbox_dir = resolve_agent_path(config.get("inboxDir"), "./cloud-inbox")
    inbox_dir.mkdir(parents=True, exist_ok=True)
    target_file = inbox_dir / f"live-{event_file.name}"
    shutil.copyfile(event_file, target_file)

    env = os.environ.copy()
    env["VISUAL_AGENT_CONFIG"] = str(config_path)
    result = subprocess.run(
        [find_node(), str(VISUAL_AGENT_JS), "--once"],
        cwd=PROJECT_ROOT,
        env=env,
        text=True,
        encoding="utf-8",
        errors="replace",
        capture_output=True,
        check=False,
    )
    logs = [line for line in (result.stdout or "").splitlines() if line.strip()]
    if result.returncode != 0:
        return {
            "Published": False,
            "EventFile": str(target_file),
            "Reason": (result.stderr or result.stdout).strip() or "visual-agent.js fallo.",
        }
    return {"Published": True, "EventFile": str(target_file), "Logs": logs}


def is_payment_group(text: str) -> bool:
    value = normalize(text)
    country = r"(mexico|mxico|mx|chile|cl|colombia|co|peru|pe)"
    return bool(re.search(rf"\bpagos?\b.*\b{country}\b", value))


def is_interface_noise(text: str) -> bool:
    value = normalize(text)
    patterns = [
        "ariadgsm agent desktop",
        "agent desktop",
        "observador:",
        "modo vivo:",
        "ultimo ciclo",
        "publicacion nube",
        "decision local",
        "busquedas probadas",
        "no movi el mouse",
        "presiona modo vivo",
        "que paso",
        "leer una vez",
        "abrir cabina",
        "panel local",
        "atender alerta",
        "aprender chats",
        "nivel actual",
    ]
    return any(pattern in value for pattern in patterns)


def add_match(matches: list[dict[str, Any]], message: dict[str, Any], intent: str, label: str, priority: str, score: int, reasons: list[str]) -> None:
    matches.append(
        {
            "Intent": intent,
            "Label": label,
            "Priority": priority,
            "Score": score,
            "Reasons": sorted(set(reasons)),
            "Message": message,
        }
    )


def build_queries(match: dict[str, Any], limit: int) -> list[str]:
    message = match["Message"]
    text = normalize(message.get("text", ""))
    queries: list[str] = []
    contact = str(message.get("contactName") or "")
    conversation_type = str(message.get("conversationType") or "")
    if conversation_type == "opened_chat" and contact and not re.match(r"^WhatsApp\s+\d+$", contact):
        queries.append(contact)

    for reason in match.get("Reasons", []):
        if len(str(reason)) >= 4:
            queries.append(str(reason))

    fallback = {
        "payment_or_receipt": ["comprobante", "transferencia", "pago"],
        "accounting_debt": ["saldo", "cuenta", "deuda"],
        "price_request": ["precio", "cuanto", "costo"],
    }
    queries.extend(fallback.get(match.get("Intent"), []))

    blocked = {"whatsapp", "lectura", "visual", "mensaje", "cliente", "estado", "comprobante", "transferencia"}
    for word in re.split(r"\s+", text):
        if len(word) >= 5 and word not in blocked:
            queries.append(word)
        if len(queries) >= limit + 4:
            break

    seen: set[str] = set()
    unique: list[str] = []
    for query in queries:
        clean = re.sub(r"\s+", " ", str(query)).strip()
        key = normalize(clean)
        if not key or key in seen:
            continue
        seen.add(key)
        unique.append(clean)
        if len(unique) >= limit:
            break
    return unique


def rule_decision(messages: list[dict[str, Any]], query_limit: int) -> dict[str, Any]:
    matches: list[dict[str, Any]] = []
    for message in messages:
        text = str(message.get("text") or "")
        value = normalize(text)
        if not value or is_payment_group(text) or is_interface_noise(text):
            continue

        score = 0
        reasons: list[str] = []
        for keyword in ["pago", "pague", "pagado", "comprobante", "transferencia", "deposito", "payment", "paid", "receipt", "yape", "plin", "nequi", "bancolombia", "banco"]:
            if keyword in value:
                score += 2
                reasons.append(keyword)
        amount = re.search(r"\b\d+(?:[.,]\d+)?\s*(usd|usdt|soles|pen|mxn|cop|clp)\b", value)
        if amount:
            score += 3
            reasons.append(amount.group(1))
        if score >= 3:
            add_match(matches, message, "payment_or_receipt", "Pago / comprobante", "alta", score, reasons)

        score = 0
        reasons = []
        for keyword in ["deuda", "debe", "saldo", "cuenta", "semanal", "reembolso", "rembolso", "devolver", "refund", "balance"]:
            if keyword in value:
                score += 2
                reasons.append(keyword)
        if score > 0:
            add_match(matches, message, "accounting_debt", "Cuenta / deuda", "alta", score, reasons)

        score = 0
        reasons = []
        has_question_context = bool(re.search(r"\?|cuanto|precio|costo|cotiza|cobras|tarifa|podras|puedes|mejor", value))
        for keyword in ["cuanto", "precio", "costo", "vale", "sale", "cotiza", "cobras", "tarifa", "price", "prices", "cost"]:
            if keyword in value:
                score += 2
                reasons.append(keyword)
        if has_question_context:
            score += 2
        if score >= 3 and has_question_context:
            add_match(matches, message, "price_request", "Pregunta precio", "media", score, reasons)

    if not matches:
        return {
            "Status": "no_local_action",
            "Source": "python_rules",
            "MessageCount": len(messages),
            "Reason": "No detecte pago, deuda o precio en la lectura local.",
        }

    priority_rank = {"alta": 0, "media": 1, "baja": 2}
    best = sorted(matches, key=lambda item: (-item["Score"], priority_rank.get(item["Priority"], 9)))[0]
    message = best["Message"]
    queries = build_queries(best, query_limit)
    return {
        "Status": "local_match",
        "Source": "python_rules",
        "MessageCount": len(messages),
        "TargetChannel": str(message.get("channelId") or ""),
        "ConversationTitle": str(message.get("contactName") or ""),
        "ConversationType": str(message.get("conversationType") or ""),
        "Text": str(message.get("text") or ""),
        "Intent": best["Intent"],
        "Label": best["Label"],
        "Priority": best["Priority"],
        "Score": best["Score"],
        "Reasons": best["Reasons"],
        "Queries": queries,
    }


def openai_decision(messages: list[dict[str, Any]], query_limit: int) -> dict[str, Any] | None:
    api_key = os.environ.get("OPENAI_API_KEY")
    if not api_key:
        return None

    safe_messages = [
        {
            "channelId": str(message.get("channelId") or ""),
            "contactName": str(message.get("contactName") or ""),
            "conversationType": str(message.get("conversationType") or "visible_screen"),
            "text": re.sub(r"\s+", " ", str(message.get("text") or "")).strip()[:260],
        }
        for message in messages[:60]
        if message.get("channelId") and message.get("text")
    ]
    schema = {
        "type": "object",
        "additionalProperties": False,
        "properties": {
            "status": {"type": "string", "enum": ["local_match", "no_local_action"]},
            "intent": {"type": "string", "enum": ["payment_or_receipt", "accounting_debt", "price_request", "no_action"]},
            "label": {"type": "string"},
            "priority": {"type": "string", "enum": ["alta", "media", "baja", "ninguna"]},
            "score": {"type": "integer"},
            "targetChannel": {"type": "string"},
            "conversationTitle": {"type": "string"},
            "conversationType": {"type": "string"},
            "text": {"type": "string"},
            "reasons": {"type": "array", "items": {"type": "string"}},
            "queries": {"type": "array", "items": {"type": "string"}},
            "notes": {"type": "string"},
        },
        "required": ["status", "intent", "label", "priority", "score", "targetChannel", "conversationTitle", "conversationType", "text", "reasons", "queries", "notes"],
    }
    payload = {
        "model": os.environ.get("OPENAI_MODEL", "gpt-5.4-nano"),
        "reasoning": {"effort": "minimal"},
        "input": [
            {
                "role": "developer",
                "content": [
                    {
                        "type": "input_text",
                        "text": "Eres el decisor local de AriadGSM en modo vivo. Revisa OCR reciente de 3 WhatsApp y decide si hay que abrir un chat ahora. Prioriza pagos, comprobantes, deudas, saldo/cuenta o preguntas de precio. Ignora interfaz del launcher y grupos Pagos Mexico/Chile/Colombia. No redactes respuestas. Devuelve no_local_action si no hay una accion clara.",
                    }
                ],
            },
            {"role": "user", "content": [{"type": "input_text", "text": json.dumps({"maxQueries": query_limit, "messages": safe_messages}, ensure_ascii=False)}]},
        ],
        "text": {"format": {"type": "json_schema", "name": "ariadgsm_python_live_decision", "schema": schema, "strict": True}},
    }
    request = urllib.request.Request(
        "https://api.openai.com/v1/responses",
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json", "Authorization": f"Bearer {api_key}"},
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=8) as response:
            response_payload = json.loads(response.read().decode("utf-8"))
    except (urllib.error.URLError, urllib.error.HTTPError, TimeoutError) as exc:
        raise RuntimeError(f"OpenAI local fallo: {exc}") from exc

    output_text = response_payload.get("output_text")
    if not output_text:
        parts: list[str] = []
        for item in response_payload.get("output", []):
            for content in item.get("content", []):
                if content.get("type") == "output_text" and content.get("text"):
                    parts.append(content["text"])
        output_text = "\n".join(parts).strip()
    if not output_text:
        raise RuntimeError("OpenAI local no devolvio texto util.")

    ai = json.loads(output_text)
    if ai.get("status") != "local_match":
        return {
            "Status": "no_local_action",
            "Source": "python_openai",
            "MessageCount": len(messages),
            "Reason": ai.get("notes") or "OpenAI local no encontro una accion clara.",
        }
    return {
        "Status": "local_match",
        "Source": "python_openai",
        "MessageCount": len(messages),
        "TargetChannel": str(ai.get("targetChannel") or ""),
        "ConversationTitle": str(ai.get("conversationTitle") or ""),
        "ConversationType": str(ai.get("conversationType") or ""),
        "Text": str(ai.get("text") or ""),
        "Intent": str(ai.get("intent") or ""),
        "Label": str(ai.get("label") or ""),
        "Priority": str(ai.get("priority") or ""),
        "Score": int(ai.get("score") or 0),
        "Reasons": list(ai.get("reasons") or []),
        "Queries": list(ai.get("queries") or [])[:query_limit],
        "AiNotes": str(ai.get("notes") or ""),
    }


def local_decision(capture: dict[str, Any], query_limit: int) -> dict[str, Any]:
    messages = [item for item in capture.get("LocalMessages", []) if isinstance(item, dict)]
    decision = rule_decision(messages, query_limit)
    if decision.get("Status") == "local_match":
        return decision
    try:
        ai = openai_decision(messages, query_limit)
        return ai or decision
    except Exception as exc:
        decision["AiError"] = str(exc)
        return decision


def invoke_intent(decision: dict[str, Any], args: argparse.Namespace) -> dict[str, Any]:
    if decision.get("Status") != "local_match":
        return run_json_ps_script(
            INTENT_BRIDGE_SCRIPT,
            {
                "ConfigPath": args.config_path,
                "MaxLinesPerChannel": args.max_lines_per_capture,
                "MaxQueries": args.intent_max_queries,
                "WaitSeconds": args.intent_wait_seconds,
                "Execute": args.execute,
                "CaptureAfterOpen": args.execute,
                "Send": bool(args.send and args.execute),
            },
        )

    attempts: list[dict[str, Any]] = []
    for query in decision.get("Queries", []):
        result = run_json_ps_script(
            INTENT_BRIDGE_SCRIPT,
            {
                "ConfigPath": args.config_path,
                "SkipCloud": True,
                "Channel": decision.get("TargetChannel") or "wa-2",
                "Query": query,
                "MaxResults": 8,
                "MaxLinesPerChannel": args.max_lines_per_capture,
                "WaitSeconds": args.intent_wait_seconds,
                "Execute": args.execute,
                "CaptureAfterOpen": args.execute,
                "Send": bool(args.send and args.execute),
            },
        )
        attempts.append(
            {
                "Query": query,
                "Status": result.get("Status"),
                "Selected": (result.get("Navigation") or {}).get("Selected") if isinstance(result.get("Navigation"), dict) else None,
                "Notes": result.get("Notes") or [],
            }
        )
        if result.get("Status") in {"preview_match", "executed", "executed_and_captured"}:
            result["Source"] = "local_decision"
            result["LocalDecisionAttempts"] = attempts
            return result

    return {
        "Status": "local_no_visible_match",
        "Source": "local_decision",
        "Execute": bool(args.execute),
        "Send": bool(args.send),
        "CaptureAfterOpen": bool(args.execute or args.send),
        "TargetChannel": decision.get("TargetChannel"),
        "CloudInsight": None,
        "SelectedQuery": None,
        "Queries": decision.get("Queries") or [],
        "Navigation": None,
        "Attempts": attempts,
        "Capture": None,
        "Notes": [
            f"Decision local detecto {decision.get('Label')}, ya publico la lectura, pero no encontre una fila visible para abrir en {decision.get('TargetChannel')}."
        ],
    }


def write_state(state: dict[str, Any]) -> None:
    RUNTIME_DIR.mkdir(parents=True, exist_ok=True)
    temp = STATE_FILE.with_suffix(".tmp")
    temp.write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding="utf-8")
    temp.replace(STATE_FILE)


def cycle(args: argparse.Namespace, cycle_number: int, config: dict[str, Any]) -> dict[str, Any]:
    notes: list[str] = []
    log(f"Ciclo {cycle_number}: captura base ({args.mode}) con motor Python.")
    capture_result = run_capture_as_json(Path(args.config_path), args.max_lines_per_capture)
    base_capture = {"Step": "base_capture", "Sent": False, "Result": capture_result, "RawCount": 1}

    decision = local_decision(capture_result, args.intent_max_queries)
    log(f"Ciclo {cycle_number}: decision local {decision.get('Status')} ({decision.get('Source')}).")
    try:
        intent = invoke_intent(decision, args)
    except Exception as exc:
        intent = None
        notes.append(f"Intent bridge fallo: {exc}")

    base_publish = publish_capture(capture_result, config, Path(args.config_path), args.send, args.mode)
    if base_publish and not base_publish.get("Published"):
        notes.append(f"Publicacion nube fallo: {base_publish.get('Reason')}")

    return {
        "Status": "ok",
        "Engine": "python",
        "Mode": args.mode,
        "Cycle": cycle_number,
        "Execute": bool(args.execute),
        "Send": bool(args.send),
        "OpenWhatsApp": False,
        "ArrangeWindows": False,
        "OpenedTargets": [],
        "ArrangedWindows": [],
        "BaseCapture": base_capture,
        "BasePublish": base_publish,
        "LocalDecision": decision,
        "Intent": intent,
        "Learning": None,
        "Notes": notes,
        "FinishedAt": now_iso(),
    }


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="AriadGSM local Python live agent")
    parser.add_argument("--config-path", default=str(DEFAULT_CONFIG))
    parser.add_argument("--mode", choices=["Live", "Full"], default="Live")
    parser.add_argument("--watch", action="store_true")
    parser.add_argument("--poll-seconds", type=int, default=30)
    parser.add_argument("--live-min-poll-seconds", type=int, default=3)
    parser.add_argument("--max-cycles", type=int, default=1)
    parser.add_argument("--learning-every-cycles", type=int, default=0)
    parser.add_argument("--max-lines-per-capture", type=int, default=20)
    parser.add_argument("--intent-max-queries", type=int, default=3)
    parser.add_argument("--intent-wait-seconds", type=float, default=0.8)
    parser.add_argument("--execute", action="store_true")
    parser.add_argument("--send", action="store_true")
    return parser.parse_args(argv)


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    args.config_path = str(Path(args.config_path).resolve())
    config = read_config(Path(args.config_path))
    RUNTIME_DIR.mkdir(parents=True, exist_ok=True)

    cycle_number = 0
    final_state: dict[str, Any] | None = None
    while True:
        cycle_number += 1
        try:
            final_state = cycle(args, cycle_number, config)
        except Exception as exc:
            final_state = {
                "Status": "error",
                "Engine": "python",
                "Mode": args.mode,
                "Cycle": cycle_number,
                "Execute": bool(args.execute),
                "Send": bool(args.send),
                "Notes": [str(exc)],
                "FinishedAt": now_iso(),
            }
            write_state(final_state)
            raise

        write_state(final_state)
        if not args.watch:
            break
        if args.max_cycles and cycle_number >= args.max_cycles:
            break
        wait_seconds = max(args.live_min_poll_seconds if args.mode == "Live" else args.poll_seconds, 1)
        time.sleep(wait_seconds)

    print(json.dumps(final_state, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except Exception as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(1)
