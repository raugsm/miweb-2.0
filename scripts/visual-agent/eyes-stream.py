#!/usr/bin/env python
"""Continuous visual eyes for AriadGSM.

This is not DOM automation. It watches the screen like a live camera, detects
changed regions, OCRs only the changed crops, and keeps a short memory buffer.
"""

from __future__ import annotations

import argparse
import hashlib
import html
import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys
import time
from collections import deque
from concurrent.futures import Future, ThreadPoolExecutor
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image, ImageEnhance, ImageGrab, ImageOps

import reader_core


SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent.parent
DEFAULT_CONFIG = SCRIPT_DIR / "visual-agent.cloud.json"
OCR_SCRIPT = SCRIPT_DIR / "visual-ocr-image.ps1"
RUNTIME_DIR = SCRIPT_DIR / "runtime"
EYES_DIR = RUNTIME_DIR / "eyes-stream"
STATE_FILE = RUNTIME_DIR / "eyes-stream.state.json"
DEFAULT_VISION_STORAGE = Path("D:/AriadGSM/vision-buffer")
LEARNING_DIR = RUNTIME_DIR / "learning-ledger"
LEARNING_LEDGER_FILE = LEARNING_DIR / "learning-ledger.jsonl"
LEARNING_REPORT_FILE = LEARNING_DIR / "latest.html"
LEARNING_SUMMARY_FILE = LEARNING_DIR / "summary.json"


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


def resolve_vision_storage_root(config: dict[str, Any]) -> Path:
    storage = config.get("storage") or {}
    configured = os.environ.get("ARIADGSM_VISION_STORAGE_DIR") or storage.get("visionStorageDir")
    if configured:
        return Path(str(configured)).expanduser().resolve()
    if DEFAULT_VISION_STORAGE.drive and Path(DEFAULT_VISION_STORAGE.drive + "/").exists():
        return DEFAULT_VISION_STORAGE.resolve()
    return (RUNTIME_DIR / "vision-buffer").resolve()


def path_is_inside(path: Path, root: Path) -> bool:
    try:
        path.resolve().relative_to(root.resolve())
    except ValueError:
        return False
    return True


def directory_size(path: Path) -> int:
    total = 0
    for item in path.rglob("*"):
        if item.is_file():
            try:
                total += item.stat().st_size
            except OSError:
                pass
    return total


def cleanup_vision_storage(root: Path, retention_hours: float, max_storage_gb: float) -> dict[str, Any]:
    stream_root = root / "eyes-stream"
    stream_root.mkdir(parents=True, exist_ok=True)
    deleted: list[str] = []
    now = time.time()
    retention_seconds = max(1.0, retention_hours * 3600.0)

    sessions = [item for item in stream_root.iterdir() if item.is_dir()]
    for session in sessions:
        try:
            age_seconds = now - session.stat().st_mtime
        except OSError:
            continue
        if age_seconds > retention_seconds and path_is_inside(session, stream_root):
            shutil.rmtree(session, ignore_errors=True)
            deleted.append(session.name)

    max_bytes = int(max_storage_gb * 1024 * 1024 * 1024)
    sessions = sorted([item for item in stream_root.iterdir() if item.is_dir()], key=lambda item: item.stat().st_mtime)
    total = directory_size(stream_root)
    for session in sessions:
        if total <= max_bytes:
            break
        if not path_is_inside(session, stream_root):
            continue
        size = directory_size(session)
        shutil.rmtree(session, ignore_errors=True)
        total = max(0, total - size)
        deleted.append(session.name)

    return {
        "root": str(root),
        "streamRoot": str(stream_root),
        "deletedSessions": deleted,
        "usedBytes": total,
        "maxBytes": max_bytes,
    }


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


def run_ocr(image_path: Path, languages: list[str], max_engines: int) -> list[str]:
    command = [
        agent_local.find_powershell(),
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        str(OCR_SCRIPT),
        "-ImagePath",
        str(image_path),
    ]
    if languages:
        command.extend(["-Languages", ",".join(languages)])
    command.extend(["-MaxEngines", str(max(1, max_engines))])
    result = subprocess.run(
        command,
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


def save_recorded_frame(image: Image.Image, image_path: Path, quality: int) -> str:
    image_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(image_path, format="JPEG", quality=quality, optimize=True)
    return str(image_path)


def prepare_ocr_image(image: Image.Image, output_path: Path, scale: float, enhance: bool) -> str:
    output_path.parent.mkdir(parents=True, exist_ok=True)
    work = image.convert("RGB")
    if scale and scale != 1:
        width = max(1, int(work.width * scale))
        height = max(1, int(work.height * scale))
        work = work.resize((width, height), Image.Resampling.LANCZOS)
    if enhance:
        work = ImageOps.autocontrast(work)
        work = ImageEnhance.Contrast(work).enhance(1.35)
        work = ImageEnhance.Sharpness(work).enhance(1.55)
    work.save(output_path, format="PNG", optimize=True)
    return str(output_path)


def run_ocr_crop(image: Image.Image, image_path: Path, ocr_path: Path, scale: float, enhance: bool, languages: list[str], max_engines: int) -> dict[str, Any]:
    image_path.parent.mkdir(parents=True, exist_ok=True)
    image.save(image_path, format="PNG", optimize=True)
    prepare_ocr_image(image, ocr_path, scale, enhance)
    return {
        "lines": run_ocr(ocr_path, languages, max_engines),
        "imagePath": str(image_path),
        "ocrImagePath": str(ocr_path),
    }


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
    value = agent_local.normalize(clean)
    if len(clean) < 6:
        return {"Raw": text, "Clean": clean, "Accepted": False, "Reason": "muy_corta"}
    exact_ui_noise = {
        "buscar",
        "nombre",
        "ncmbre",
        "copiar",
        "copiar ruta acceso",
        "copiar ruta de acceso",
    }
    if value in exact_ui_noise:
        return {"Raw": text, "Clean": clean, "Accepted": False, "Reason": "interfaz_o_ruido"}
    if not re.search(r"[A-Za-z0-9ÁÉÍÓÚáéíóúÑñ]", clean):
        return {"Raw": text, "Clean": clean, "Accepted": False, "Reason": "sin_texto_util"}
    if time_only_line(clean):
        return {"Raw": text, "Clean": clean, "Accepted": False, "Reason": "hora_suelta"}
    tokens = re.findall(r"[A-Za-z0-9ÃÃ‰ÃÃ“ÃšÃ¡Ã©Ã­Ã³ÃºÃ‘Ã±]+", clean)
    tokens = re.findall(r"[^\W_]+", clean, flags=re.UNICODE)
    known_upper_words = {"HUAWEI", "SAMSUNG", "XIAOMI", "HONOR", "TECNO", "INFINIX", "IPHONE", "FRP", "MDM"}
    meaningful_words = [
        token
        for token in tokens
        if len(token) >= 3 and (any(ch.islower() for ch in token) or token.upper() in known_upper_words)
    ]
    if len(tokens) >= 3 and not meaningful_words:
        return {"Raw": text, "Clean": clean, "Accepted": False, "Reason": "ocr_garbage"}
    if time_only_line(clean):
        return {"Raw": text, "Clean": clean, "Accepted": False, "Reason": "hora_suelta"}
    if agent_local.is_payment_group(clean):
        return {"Raw": text, "Clean": clean, "Accepted": False, "Reason": "grupo_pagos_ignorado"}
    if agent_local.is_interface_noise(clean):
        return {"Raw": text, "Clean": clean, "Accepted": False, "Reason": "interfaz_o_ruido"}
    return {"Raw": text, "Clean": clean, "Accepted": True, "Reason": "mensaje_util"}


def blocked_section_reason(raw_lines: list[str]) -> str | None:
    signature = agent_local.whatsapp_signature(raw_lines)
    if not signature["accepted"]:
        return "no_whatsapp_signature"

    text = agent_local.normalize(" ".join(raw_lines))
    patterns = {
        "codex_en_zona": ["codex", "tareas completadas", "revisar cambios", "scripts/visual-agent"],
        "launcher_en_zona": ["ariadgsm agent desktop", "modo vivo:", "que paso", "busquedas probadas"],
        "navegador_no_chat": ["youtube", "github", "railway", "deployments", "variables"],
        "whatsapp_welcome": ["whatsapp business en la web", "organiza", "cuenta de empresa"],
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


def extract_amounts(text: str) -> list[dict[str, Any]]:
    values: list[dict[str, Any]] = []
    patterns = [
        r"\b(?P<currency>usd|usdt|soles|pen|mxn|cop|clp|\$|s/)\s*(?P<amount>\d+(?:[.,]\d+)?)\b",
        r"\b(?P<amount>\d+(?:[.,]\d+)?)\s*(?P<currency>usd|usdt|soles|pen|mxn|cop|clp)\b",
    ]
    for pattern in patterns:
        for match in re.finditer(pattern, text, flags=re.I):
            amount_text = match.group("amount").replace(",", ".")
            try:
                amount = float(amount_text)
            except ValueError:
                continue
            values.append({"amount": amount, "currency": match.group("currency").upper()})
    return values


def classify_learning_text(text: str, decision: dict[str, Any]) -> tuple[str, str]:
    value = agent_local.normalize(text)
    if any(keyword in value for keyword in ["pago", "pague", "pagado", "comprobante", "transferencia", "deposito", "yape", "plin", "nequi", "banco"]):
        return "accounting_payment", "Pago / comprobante"
    if any(keyword in value for keyword in ["deuda", "debe", "saldo", "cuenta", "reembolso", "devolver", "refund", "balance"]):
        return "accounting_debt", "Cuenta / deuda"
    if any(keyword in value for keyword in ["precio", "costo", "cuanto", "cotiza", "cobras", "tarifa", "price", "prices", "cost"]):
        return "price_request", "Pregunta precio"
    if decision.get("Status") == "local_match" and agent_local.normalize(decision.get("Text", "")) == value:
        return str(decision.get("Intent") or "business_signal"), str(decision.get("Label") or "Senal de negocio")
    return "conversation_context", "Contexto de cliente"


def detect_language_profile(text: str) -> dict[str, Any]:
    value = agent_local.normalize(text)
    tokens = set(re.findall(r"[a-z0-9]+", value))
    profiles = {
        "es": {
            "label": "Espanol",
            "terms": {"que", "cuando", "debe", "pago", "precio", "cuanto", "ayuda", "solucionar", "me", "con", "por", "favor"},
        },
        "en": {
            "label": "Ingles",
            "terms": {"the", "you", "price", "prices", "check", "tool", "friend", "please", "unlock", "payment", "paid", "cost"},
        },
        "pt": {
            "label": "Portugues",
            "terms": {"voce", "voces", "preco", "pagamento", "obrigado", "quanto", "ajuda", "conta", "cliente", "pode"},
        },
    }
    scores: dict[str, int] = {}
    for code, profile in profiles.items():
        scores[code] = len(tokens & profile["terms"])
    best_code = max(scores, key=scores.get) if scores else "unknown"
    if scores.get(best_code, 0) == 0:
        best_code = "unknown"
    return {
        "language": best_code,
        "languageLabel": profiles.get(best_code, {}).get("label", "Desconocido"),
        "scores": scores,
    }


def detect_regional_profile(text: str) -> dict[str, Any]:
    value = agent_local.normalize(text)
    country_terms = {
        "Colombia": ["colombia", "colombiano", "parce", "manes", "nequi", "bancolombia", "daviplata", "pelao"],
        "Mexico": ["mexico", "mx", "compa", "wey", "mande", "ahorita", "banorte", "oxxo", "bbva mxn"],
        "Chile": ["chile", "cl", "weon", "lucas", "transferencia chile", "bci", "mach"],
        "Peru": ["peru", "pe", "yape", "plin", "soles", "bcp", "interbank"],
        "Venezuela": ["venezuela", "ve", "pana", "chamo", "zelle", "bolivares", "mercantil"],
        "Brasil": ["brasil", "brazil", "pix", "obrigado", "preco", "reais"],
        "USA": ["usa", "united states", "zelle", "cashapp", "paypal", "usd"],
    }
    matches: dict[str, list[str]] = {}
    for country, terms in country_terms.items():
        found = [term for term in terms if term in value]
        if found:
            matches[country] = found
    primary = max(matches, key=lambda country: len(matches[country])) if matches else "Unknown"
    slang_terms = sorted({term for terms in matches.values() for term in terms if term not in {"colombia", "mexico", "chile", "peru", "venezuela", "brasil", "brazil", "usa"}})
    return {
        "countryHint": primary,
        "countryMatches": matches,
        "slangTerms": slang_terms,
    }


def build_language_profile(text: str) -> dict[str, Any]:
    language = detect_language_profile(text)
    regional = detect_regional_profile(text)
    return {
        **language,
        **regional,
    }


def learning_id(channel_id: str, text: str, learning_type: str) -> str:
    raw = f"{channel_id}|{learning_type}|{agent_local.normalize(text)}"
    return hashlib.sha1(raw.encode("utf-8")).hexdigest()


def build_learning_items(event: dict[str, Any], decision: dict[str, Any]) -> list[dict[str, Any]]:
    items: list[dict[str, Any]] = []
    for text in event.get("acceptedLines") or []:
        clean = re.sub(r"\s+", " ", str(text)).strip()
        if not clean:
            continue
        learning_type, label = classify_learning_text(clean, decision)
        language_profile = build_language_profile(clean)
        item = {
            "id": learning_id(str(event.get("channelId") or ""), clean, learning_type),
            "learnedAt": now_iso(),
            "channelId": event.get("channelId"),
            "conversationName": event.get("name"),
            "learningType": learning_type,
            "label": label,
            "text": clean,
            "amounts": extract_amounts(clean),
            "languageProfile": language_profile,
            "language": language_profile.get("language"),
            "countryHint": language_profile.get("countryHint"),
            "slangTerms": language_profile.get("slangTerms"),
            "decisionLinked": agent_local.normalize(decision.get("Text", "")) == agent_local.normalize(clean),
            "decisionIntent": decision.get("Intent"),
            "decisionLabel": decision.get("Label"),
            "sourceImagePath": event.get("imagePath"),
            "ocrImagePath": event.get("ocrImagePath"),
            "capturedAt": event.get("capturedAt"),
        }
        items.append(item)
    return items


def apply_reading_event(
    event: dict[str, Any],
    last_decision: dict[str, Any],
    seen_learning_ids: set[str],
) -> tuple[dict[str, Any], dict[str, Any], int]:
    selected_lines = [str(line) for line in event.get("acceptedLines") or [] if str(line).strip()]
    if selected_lines:
        messages = make_messages(str(event.get("channelId") or ""), selected_lines)
        last_decision = agent_local.rule_decision(messages, 3)
        event_decision = last_decision
    else:
        event_decision = {
            "Status": "no_local_action",
            "Source": "reader_core",
            "TargetChannel": event.get("channelId"),
            "Reason": "Sin lineas utiles para decidir.",
        }
    event["decision"] = event_decision
    fresh_learning = append_learning_items(build_learning_items(event, event_decision), seen_learning_ids)
    return event, last_decision, len(fresh_learning)


def event_from_structured_observation(observation: dict[str, Any]) -> dict[str, Any]:
    channel_id = str(observation.get("channelId") or "")
    base_event = {
        "capturedAt": observation.get("capturedAt") or now_iso(),
        "processedAt": now_iso(),
        "channelId": channel_id,
        "name": observation.get("conversationTitle") or channel_id,
        "rect": [],
        "imagePath": "",
        "ocrImagePath": "",
        "ocrScale": None,
        "ocrEnhanced": False,
        "change": {"mean": 0.0, "changedRatio": 0.0, "score": 0.0},
        "rawLines": [],
        "acceptedLines": [],
        "ignoredLines": [],
        "decision": {},
    }
    return reader_core.observation_to_event(observation, base_event)


def load_recent_learning_ids(limit: int = 3000) -> set[str]:
    if not LEARNING_LEDGER_FILE.exists():
        return set()
    ids: set[str] = set()
    try:
        lines = LEARNING_LEDGER_FILE.read_text(encoding="utf-8").splitlines()[-limit:]
    except OSError:
        return ids
    for line in lines:
        try:
            item = json.loads(line)
        except json.JSONDecodeError:
            continue
        if item.get("id"):
            ids.add(str(item["id"]))
    return ids


def append_learning_items(items: list[dict[str, Any]], seen_ids: set[str]) -> list[dict[str, Any]]:
    fresh = [item for item in items if item.get("id") and item["id"] not in seen_ids]
    if not fresh:
        return []
    LEARNING_DIR.mkdir(parents=True, exist_ok=True)
    with LEARNING_LEDGER_FILE.open("a", encoding="utf-8") as handle:
        for item in fresh:
            handle.write(json.dumps(item, ensure_ascii=False, separators=(",", ":")) + "\n")
            seen_ids.add(str(item["id"]))
    return fresh


def read_learning_tail(limit: int = 300) -> list[dict[str, Any]]:
    if not LEARNING_LEDGER_FILE.exists():
        return []
    try:
        lines = LEARNING_LEDGER_FILE.read_text(encoding="utf-8").splitlines()[-limit:]
    except OSError:
        return []
    items: list[dict[str, Any]] = []
    for line in lines:
        try:
            items.append(json.loads(line))
        except json.JSONDecodeError:
            continue
    return items


def render_learning_report() -> dict[str, Any]:
    items = read_learning_tail(500)
    counts: dict[str, int] = {}
    languages: dict[str, int] = {}
    countries: dict[str, int] = {}
    for item in items:
        key = str(item.get("learningType") or "unknown")
        counts[key] = counts.get(key, 0) + 1
        language = str(item.get("language") or "unknown")
        languages[language] = languages.get(language, 0) + 1
        country = str(item.get("countryHint") or "Unknown")
        countries[country] = countries.get(country, 0) + 1
    accounting = [item for item in items if str(item.get("learningType") or "").startswith("accounting_")]

    def esc(value: Any) -> str:
        return html.escape("" if value is None else str(value))

    rows = []
    for item in reversed(items[-120:]):
        amounts = ", ".join(f"{value.get('amount')} {value.get('currency')}" for value in item.get("amounts") or [])
        slang = ", ".join(item.get("slangTerms") or [])
        rows.append(
            f"<tr><td>{esc(item.get('learnedAt'))}</td><td>{esc(item.get('channelId'))}</td><td>{esc(item.get('label'))}</td><td>{esc(item.get('text'))}</td><td>{esc(item.get('language'))}</td><td>{esc(item.get('countryHint'))}</td><td>{esc(slang)}</td><td>{esc(amounts)}</td></tr>"
        )
    LEARNING_DIR.mkdir(parents=True, exist_ok=True)
    LEARNING_REPORT_FILE.write_text(
        f"""<!doctype html>
<html lang="es">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>AriadGSM Aprendizaje Local</title>
  <style>
    body {{ margin:0; font-family:"Segoe UI",Arial,sans-serif; background:#eef4ff; color:#101827; }}
    header {{ background:white; border-bottom:1px solid #d9e3f3; padding:20px 26px; }}
    main {{ padding:22px; display:grid; gap:16px; }}
    section {{ background:white; border:1px solid #d9e3f3; border-radius:8px; padding:16px; }}
    .metrics {{ display:grid; grid-template-columns:repeat(6,minmax(130px,1fr)); gap:10px; }}
    .metric {{ background:#f4f8ff; border:1px solid #d9e3f3; border-radius:8px; padding:10px; }}
    .metric b {{ display:block; color:#2478f2; font-size:22px; }}
    table {{ width:100%; border-collapse:collapse; font-size:13px; }}
    th,td {{ border-bottom:1px solid #d9e3f3; padding:8px; text-align:left; vertical-align:top; }}
    th {{ background:#f4f8ff; }}
  </style>
</head>
<body>
  <header><h1>AriadGSM Aprendizaje Local</h1><p>Registro visible de lo que el agente esta aprendiendo de WhatsApp.</p></header>
  <main>
    <section>
      <div class="metrics">
        <div class="metric"><b>{len(items)}</b>ultimos aprendizajes</div>
        <div class="metric"><b>{len(accounting)}</b>contabilidad</div>
        <div class="metric"><b>{counts.get('accounting_payment', 0)}</b>pagos</div>
        <div class="metric"><b>{counts.get('accounting_debt', 0)}</b>deudas</div>
        <div class="metric"><b>{languages.get('en', 0)}</b>ingles</div>
        <div class="metric"><b>{sum(1 for item in items if item.get('slangTerms'))}</b>jerga</div>
      </div>
    </section>
    <section>
      <h2>Ultimos registros</h2>
      <table><thead><tr><th>Hora</th><th>Canal</th><th>Tipo</th><th>Texto aprendido</th><th>Idioma</th><th>Pais</th><th>Jerga</th><th>Monto</th></tr></thead><tbody>{''.join(rows)}</tbody></table>
    </section>
  </main>
</body>
</html>""",
        encoding="utf-8",
    )
    summary = {
        "updatedAt": now_iso(),
        "itemsInReport": len(items),
        "counts": counts,
        "languages": languages,
        "countries": countries,
        "accountingItems": len(accounting),
        "report": str(LEARNING_REPORT_FILE),
        "ledger": str(LEARNING_LEDGER_FILE),
    }
    LEARNING_SUMMARY_FILE.write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
    return summary


def render_report(state: dict[str, Any], output_path: Path) -> None:
    def esc(value: Any) -> str:
        return html.escape("" if value is None else str(value))

    events = state.get("events") or []
    rows = []
    for event in reversed(events[-80:]):
        accepted = "<br>".join(esc(line) for line in event.get("acceptedLines", [])) or "<span class='muted'>Sin texto aceptado</span>"
        ignored = "<br>".join(f"{esc(item.get('Clean'))} <span class='muted'>({esc(item.get('Reason'))})</span>" for item in event.get("ignoredLines", [])[:8])
        image_path = str(event.get("imagePath") or "")
        image = Path(image_path) if image_path else None
        image_src = image.resolve().as_uri() if image and image.is_file() else ""
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
    .metrics {{ display:grid; grid-template-columns:repeat(7,minmax(120px,1fr)); gap:10px; margin-top:12px; }}
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
    <div><h1>AriadGSM Ojo Vivo</h1><div class="muted">Reader Core estructurado + OCR de respaldo</div></div>
    <div class="muted">Actualizado: {esc(state.get('updatedAt'))}</div>
  </header>
  <main>
    <section class="panel">
      <h2>Estado</h2>
      <div class="metrics">
        <div class="metric"><b>{esc(state.get('frames'))}</b>frames</div>
        <div class="metric"><b>{esc(state.get('recordedFrames'))}</b>grabados</div>
        <div class="metric"><b>{esc(state.get('ocrRuns'))}</b>OCR</div>
        <div class="metric"><b>{esc(state.get('learnedItems'))}</b>aprendidos</div>
        <div class="metric"><b>{esc(len(events))}</b>eventos</div>
        <div class="metric"><b>{esc(decision.get('Status'))}</b>decision</div>
        <div class="metric"><b>{esc(decision.get('TargetChannel'))}</b>canal</div>
      </div>
      <p><b>Almacen visual:</b> {esc(state.get('visionStorageRoot'))}</p>
      <p><b>Reporte aprendizaje:</b> {esc((state.get('learningSummary') or {}).get('report'))}</p>
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
    parser.add_argument("--live", action="store_true", help="Preset rapido para operacion en vivo.")
    parser.add_argument("--duration-seconds", type=float, default=15.0)
    parser.add_argument("--interval-ms", type=int, default=750)
    parser.add_argument("--change-threshold", type=float, default=9.0)
    parser.add_argument("--ocr-cooldown-seconds", type=float, default=2.0)
    parser.add_argument("--ocr-workers", type=int, default=1)
    parser.add_argument("--max-pending-ocr", type=int, default=4)
    parser.add_argument("--ocr-scale", type=float, default=1.0)
    parser.add_argument("--ocr-languages", default="es-MX,en-US,pt-BR")
    parser.add_argument("--ocr-max-engines", type=int, default=3)
    parser.add_argument("--ocr-enhance", dest="ocr_enhance", action="store_true")
    parser.add_argument("--no-ocr-enhance", dest="ocr_enhance", action="store_false")
    parser.set_defaults(ocr_enhance=None)
    parser.add_argument("--state-interval-ms", type=int, default=750)
    parser.add_argument("--record-frames", dest="record_frames", action="store_true")
    parser.add_argument("--no-record-frames", dest="record_frames", action="store_false")
    parser.set_defaults(record_frames=None)
    parser.add_argument("--record-interval-ms", type=int, default=500)
    parser.add_argument("--record-quality", type=int, default=55)
    parser.add_argument("--retention-hours", type=float, default=168.0)
    parser.add_argument("--max-storage-gb", type=float, default=500.0)
    parser.add_argument("--cloud-raw-frames", action="store_true", help="Reservado. El modo normal nunca sube frames crudos.")
    parser.add_argument("--understood-cloud-only", dest="understood_cloud_only", action="store_true")
    parser.add_argument("--no-understood-cloud-only", dest="understood_cloud_only", action="store_false")
    parser.set_defaults(understood_cloud_only=True)
    parser.add_argument("--vision-storage-dir", default="")
    parser.add_argument("--fingerprint-width", type=int, default=96)
    parser.add_argument("--fingerprint-height", type=int, default=96)
    parser.add_argument("--buffer-events", type=int, default=160)
    parser.add_argument("--full-section", action="store_true")
    parser.add_argument("--reader-core-max-age-seconds", type=float, default=10.0)
    parser.add_argument("--reader-core-min-structured-confidence", type=float, default=0.86)
    args = parser.parse_args(argv)
    if args.live:
        if args.interval_ms == 750:
            args.interval_ms = 100
        if args.change_threshold == 9.0:
            args.change_threshold = 4.5
        if args.ocr_cooldown_seconds == 2.0:
            args.ocr_cooldown_seconds = 0.35
        if args.ocr_workers == 1:
            args.ocr_workers = 2
        if args.max_pending_ocr == 4:
            args.max_pending_ocr = 6
        if args.ocr_scale == 1.0:
            args.ocr_scale = 2.0
        if args.ocr_enhance is None:
            args.ocr_enhance = True
        if args.state_interval_ms == 750:
            args.state_interval_ms = 500
        if args.record_frames is None:
            args.record_frames = True
        if args.record_interval_ms == 500:
            args.record_interval_ms = args.interval_ms
        if args.retention_hours == 168.0:
            args.retention_hours = 1.0
        if args.max_storage_gb == 500.0:
            args.max_storage_gb = 40.0
    if args.record_frames is None:
        args.record_frames = False
    if args.ocr_enhance is None:
        args.ocr_enhance = True
    args.ocr_scale = max(1.0, min(3.0, args.ocr_scale))
    args.ocr_max_engines = max(1, min(5, args.ocr_max_engines))
    args.ocr_language_list = [item.strip() for item in str(args.ocr_languages or "").split(",") if item.strip()]
    args.record_quality = max(25, min(90, args.record_quality))
    args.record_interval_ms = max(100, args.record_interval_ms)
    args.retention_hours = max(0.1, args.retention_hours)
    args.max_storage_gb = max(1.0, args.max_storage_gb)
    args.reader_core_max_age_seconds = max(1.0, args.reader_core_max_age_seconds)
    args.reader_core_min_structured_confidence = max(0.0, min(1.0, args.reader_core_min_structured_confidence))
    return args


def apply_storage_config(args: argparse.Namespace, config: dict[str, Any]) -> None:
    storage = config.get("storage") or {}
    if args.vision_storage_dir == "" and storage.get("visionStorageDir"):
        args.vision_storage_dir = str(storage.get("visionStorageDir"))
    if args.live:
        if args.retention_hours == 1.0 and storage.get("liveRetentionHours") is not None:
            args.retention_hours = max(0.1, float(storage.get("liveRetentionHours")))
        if args.max_storage_gb == 40.0 and storage.get("liveMaxStorageGb") is not None:
            args.max_storage_gb = max(1.0, float(storage.get("liveMaxStorageGb")))
        if args.record_interval_ms == args.interval_ms and storage.get("liveRecordIntervalMs") is not None:
            args.record_interval_ms = max(100, int(storage.get("liveRecordIntervalMs")))
        if storage.get("cloudRawFrames") is not None:
            args.cloud_raw_frames = bool(storage.get("cloudRawFrames"))
        if storage.get("understoodCloudOnly") is not None:
            args.understood_cloud_only = bool(storage.get("understoodCloudOnly"))


def build_state(
    args: argparse.Namespace,
    session_dir: Path,
    frames: int,
    ocr_runs: int,
    regions: list[Region],
    events: deque[dict[str, Any]],
    last_decision: dict[str, Any],
    pending_ocr: dict[Future[dict[str, Any]], dict[str, Any]],
    pending_frames: dict[Future[str], str],
    vision_storage_root: Path,
    storage_cleanup: dict[str, Any],
    recorded_frames: int,
    learned_items: int,
    learning_summary: dict[str, Any] | None,
    reader_core_sources: dict[str, int],
    completed: bool = False,
) -> dict[str, Any]:
    return {
        "Status": "completed" if completed else ("running" if args.watch else "completed"),
        "Engine": "eyes_stream",
        "Mode": "live" if args.live else "sample",
        "updatedAt": now_iso(),
        "sessionDir": str(session_dir),
        "frames": frames,
        "ocrRuns": ocr_runs,
        "pendingOcr": len(pending_ocr),
        "pendingFrameSaves": len(pending_frames),
        "recordedFrames": recorded_frames,
        "recordFrames": bool(args.record_frames),
        "learnedItems": learned_items,
        "learningSummary": learning_summary or {},
        "captureIntervalMs": args.interval_ms,
        "recordIntervalMs": args.record_interval_ms,
        "ocrCooldownSeconds": args.ocr_cooldown_seconds,
        "ocrWorkers": args.ocr_workers,
        "ocrScale": args.ocr_scale,
        "ocrEnhance": bool(args.ocr_enhance),
        "ocrLanguages": list(getattr(args, "ocr_language_list", [])),
        "ocrMaxEngines": args.ocr_max_engines,
        "readerCore": {
            "priority": ["structured", "ocr", "ai_verifier_pending"],
            "maxStructuredAgeSeconds": args.reader_core_max_age_seconds,
            "minStructuredConfidence": args.reader_core_min_structured_confidence,
            "sources": reader_core_sources,
            "stateFile": str(reader_core.STATE_FILE),
        },
        "localVisionPolicy": {
            "rawFrames": "local_temp_buffer",
            "rawFramesUploadedToCloud": bool(args.cloud_raw_frames),
            "cloudPayload": "understood_conversation_only" if args.understood_cloud_only else "events_and_understanding",
            "retentionHours": args.retention_hours,
            "maxStorageGb": args.max_storage_gb,
            "frameProcessing": "continuous_local_capture",
            "decisionPath": ["browser_reader", "reader_core", "ocr_fallback", "core_ia_python"],
        },
        "visionStorageRoot": str(vision_storage_root),
        "storageCleanup": storage_cleanup,
        "regions": [{"channelId": r.channel_id, "name": r.name, "rect": list(r.rect)} for r in regions],
        "events": list(events),
        "lastDecision": last_decision,
        "report": str(EYES_DIR / "latest.html"),
    }


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    config_path = Path(args.config_path).resolve()
    config = json.loads(config_path.read_text(encoding="utf-8-sig"))
    apply_storage_config(args, config)
    vision_storage_root = Path(args.vision_storage_dir).expanduser().resolve() if args.vision_storage_dir else resolve_vision_storage_root(config)
    storage_cleanup = cleanup_vision_storage(vision_storage_root, args.retention_hours, args.max_storage_gb)
    session_dir = vision_storage_root / "eyes-stream" / datetime.now().strftime("%Y%m%d-%H%M%S")
    crop_dir = session_dir / "crops"
    frame_dir = session_dir / "frames"
    crop_dir.mkdir(parents=True, exist_ok=True)
    first = ImageGrab.grab(all_screens=True)
    regions = build_regions(config, first.size, args.full_section)
    fingerprints: dict[str, np.ndarray] = {}
    last_ocr: dict[str, float] = {}
    pending_ocr: dict[Future[dict[str, Any]], dict[str, Any]] = {}
    pending_frames: dict[Future[str], str] = {}
    pending_channels: set[str] = set()
    events: deque[dict[str, Any]] = deque(maxlen=args.buffer_events)
    frames = 0
    ocr_runs = 0
    recorded_frames = 0
    learned_items = 0
    reader_core_sources: dict[str, int] = {}
    learning_summary = render_learning_report()
    seen_learning_ids = load_recent_learning_ids()
    seen_structured_observation_keys: set[str] = set()
    last_decision: dict[str, Any] = {"Status": "no_local_action", "Source": "eyes_stream", "Reason": "Sin lectura todavia."}
    start = time.monotonic()
    last_state_write = 0.0
    last_record_write = 0.0
    log(
        f"Ojo vivo iniciado: {first.size[0]}x{first.size[1]}, {len(regions)} regiones, "
        f"intervalo {args.interval_ms}ms, OCR workers {args.ocr_workers}, storage {vision_storage_root}."
    )

    with ThreadPoolExecutor(max_workers=max(1, args.ocr_workers)) as executor:
        with ThreadPoolExecutor(max_workers=1) as frame_executor:
            while True:
                now_mono = time.monotonic()
                event_added = False
                structured_by_channel = reader_core.latest_by_channel(
                    reader_core.read_structured_observations(args.reader_core_max_age_seconds)
                )

                for observation in structured_by_channel.values():
                    if float(observation.get("confidence") or 0.0) < args.reader_core_min_structured_confidence:
                        continue
                    observation_key = reader_core.observation_key(observation)
                    if observation_key in seen_structured_observation_keys:
                        continue
                    seen_structured_observation_keys.add(observation_key)
                    event = event_from_structured_observation(observation)
                    event, last_decision, fresh_count = apply_reading_event(event, last_decision, seen_learning_ids)
                    if fresh_count:
                        learned_items += fresh_count
                        learning_summary = render_learning_report()
                    events.append(event)
                    selected_source = str((event.get("readerCore") or {}).get("selectedSource") or "structured")
                    reader_core_sources[selected_source] = reader_core_sources.get(selected_source, 0) + 1
                    event_added = True
                    log(
                        f"{event.get('channelId')}: lector {selected_source} "
                        f"({(event.get('readerCore') or {}).get('confidence')}) directo, "
                        f"utiles {len(event.get('acceptedLines') or [])}."
                    )

                for future in [item for item in pending_frames if item.done()]:
                    pending_frames.pop(future, None)
                    try:
                        future.result()
                    except Exception as exc:
                        log(f"No pude guardar frame visual: {exc}")

                for future in [item for item in pending_ocr if item.done()]:
                    job = pending_ocr.pop(future)
                    pending_channels.discard(job["region"].channel_id)
                    try:
                        ocr_payload = future.result()
                        raw_lines = [str(line) for line in ocr_payload.get("lines", [])]
                    except Exception as exc:
                        ocr_payload = {}
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
                    ocr_event = {
                        "capturedAt": job["capturedAt"],
                        "processedAt": now_iso(),
                        "channelId": job["region"].channel_id,
                        "name": job["region"].name,
                        "rect": list(job["region"].rect),
                        "imagePath": str(ocr_payload.get("imagePath") or job["imagePath"]),
                        "ocrImagePath": str(ocr_payload.get("ocrImagePath") or job["ocrImagePath"]),
                        "ocrScale": args.ocr_scale,
                        "ocrEnhanced": bool(args.ocr_enhance),
                        "change": job["change"],
                        "rawLines": raw_lines,
                        "acceptedLines": accepted,
                        "ignoredLines": ignored,
                        "decision": last_decision,
                    }
                    candidate_structured = dict(structured_by_channel)
                    structured_for_channel = candidate_structured.get(job["region"].channel_id)
                    if structured_for_channel and reader_core.observation_key(structured_for_channel) in seen_structured_observation_keys:
                        candidate_structured.pop(job["region"].channel_id, None)
                    event = reader_core.select_reading(
                        ocr_event,
                        candidate_structured,
                        args.reader_core_min_structured_confidence,
                    )
                    selected_source = str((event.get("readerCore") or {}).get("selectedSource") or "ocr")
                    reader_core_sources[selected_source] = reader_core_sources.get(selected_source, 0) + 1
                    event, last_decision, fresh_count = apply_reading_event(event, last_decision, seen_learning_ids)
                    selected_lines = [str(line) for line in event.get("acceptedLines") or [] if str(line).strip()]
                    if fresh_count:
                        learned_items += fresh_count
                        learning_summary = render_learning_report()
                    events.append(event)
                    ocr_runs += 1
                    event_added = True
                    confidence = (event.get("readerCore") or {}).get("confidence")
                    log(
                        f"{job['region'].channel_id}: cambio {job['change']['score']:.2f}, "
                        f"lector {selected_source} ({confidence}), OCR {len(raw_lines)} lineas, utiles {len(selected_lines)}."
                    )

                expired = (not args.watch) and (now_mono - start) >= args.duration_seconds
                if not expired:
                    image = ImageGrab.grab(all_screens=True)
                    frames += 1
                    if args.record_frames and (now_mono - last_record_write) >= (args.record_interval_ms / 1000.0) and len(pending_frames) < 2:
                        frame_path = frame_dir / f"{stamp()}-screen.jpg"
                        pending_frames[frame_executor.submit(save_recorded_frame, image.copy(), frame_path, args.record_quality)] = str(frame_path)
                        recorded_frames += 1
                        last_record_write = now_mono

                    candidates: list[tuple[float, Region, Image.Image, dict[str, float]]] = []
                    for region in regions:
                        crop = image.crop(region.rect)
                        fp = fingerprint(crop, (args.fingerprint_width, args.fingerprint_height))
                        change = change_score(fingerprints.get(region.channel_id), fp)
                        fingerprints[region.channel_id] = fp
                        should_ocr = (
                            change["score"] >= args.change_threshold
                            and (now_mono - last_ocr.get(region.channel_id, 0.0)) >= args.ocr_cooldown_seconds
                            and region.channel_id not in pending_channels
                        )
                        if should_ocr:
                            candidates.append((change["score"], region, crop, change))

                    slots = max(0, args.max_pending_ocr - len(pending_ocr))
                    for _, region, crop, change in sorted(candidates, key=lambda item: item[0], reverse=True)[:slots]:
                        last_ocr[region.channel_id] = now_mono
                        image_path = crop_dir / f"{stamp()}-{region.channel_id}.png"
                        ocr_path = crop_dir / f"{stamp()}-{region.channel_id}-ocr.png"
                        future = executor.submit(
                            run_ocr_crop,
                            crop.copy(),
                            image_path,
                            ocr_path,
                            args.ocr_scale,
                            bool(args.ocr_enhance),
                            list(args.ocr_language_list),
                            args.ocr_max_engines,
                        )
                        pending_ocr[future] = {
                            "region": region,
                            "imagePath": image_path,
                            "ocrImagePath": ocr_path,
                            "change": change,
                            "capturedAt": now_iso(),
                        }
                        pending_channels.add(region.channel_id)

                should_write_state = event_added or (now_mono - last_state_write) >= (args.state_interval_ms / 1000.0)
                if should_write_state:
                    state = build_state(
                        args,
                        session_dir,
                        frames,
                        ocr_runs,
                        regions,
                        events,
                        last_decision,
                        pending_ocr,
                        pending_frames,
                        vision_storage_root,
                        storage_cleanup,
                        recorded_frames,
                        learned_items,
                        learning_summary,
                        reader_core_sources,
                    )
                    write_state(state)
                    last_state_write = now_mono

                if expired and not pending_ocr and not pending_frames:
                    break
                time.sleep(max(0.02, args.interval_ms / 1000.0))

    state = build_state(
        args,
        session_dir,
        frames,
        ocr_runs,
        regions,
        events,
        last_decision,
        pending_ocr,
        pending_frames,
        vision_storage_root,
        storage_cleanup,
        recorded_frames,
        learned_items,
        learning_summary,
        reader_core_sources,
        completed=True,
    )
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
