from __future__ import annotations

import hashlib
import re
from datetime import datetime, timezone
from typing import Any


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


AMOUNT_PATTERNS = (
    r"\b(?P<currency>usd|usdt|pen|mxn|cop|clp|\$|s/)\s*(?P<amount>\d+(?:[.,]\d+)?)\b",
    r"\b(?P<amount>\d+(?:[.,]\d+)?)\s*(?P<currency>usd|usdt|pen|mxn|cop|clp)\b",
)


def extract_amounts(text: str) -> list[dict[str, Any]]:
    amounts: list[dict[str, Any]] = []
    for pattern in AMOUNT_PATTERNS:
        for match in re.finditer(pattern, text, flags=re.IGNORECASE):
            try:
                amount = float(match.group("amount").replace(",", "."))
            except ValueError:
                continue
            amounts.append({"amount": amount, "currency": match.group("currency").upper()})
    return amounts


def accounting_event_from_message(conversation_id: str, client_name: str, message: dict[str, Any], kind: str) -> dict[str, Any]:
    text = str(message.get("text") or "")
    amounts = extract_amounts(text)
    first = amounts[0] if amounts else {}
    raw_id = f"{conversation_id}|{kind}|{text}|{message.get('sentAt') or ''}"
    event = {
        "eventType": "accounting_event",
        "accountingId": hashlib.sha1(raw_id.encode("utf-8")).hexdigest(),
        "createdAt": utc_now(),
        "status": "draft",
        "confidence": 0.65 if amounts else 0.45,
        "clientName": client_name,
        "conversationId": conversation_id,
        "kind": kind,
        "evidence": [message.get("messageId") or text],
    }
    if "amount" in first:
        event["amount"] = first["amount"]
    if "currency" in first:
        event["currency"] = first["currency"]
    return event
