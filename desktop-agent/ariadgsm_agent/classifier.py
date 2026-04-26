from __future__ import annotations

from dataclasses import dataclass, asdict
from typing import Any

from .text import normalize


@dataclass(frozen=True)
class Decision:
    status: str
    intent: str
    label: str
    priority: str
    score: int
    text: str
    reasons: list[str]

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


INTENTS = [
    (
        "accounting_payment",
        "Pago / comprobante",
        "alta",
        ["pago", "pague", "pagado", "comprobante", "transferencia", "deposito", "yape", "plin", "nequi", "zelle", "pix", "banco"],
    ),
    (
        "accounting_debt",
        "Cuenta / deuda",
        "alta",
        ["deuda", "debe", "saldo", "cuenta", "reembolso", "devolver", "refund", "balance"],
    ),
    (
        "price_request",
        "Pregunta precio",
        "media",
        ["precio", "costo", "cuanto", "vale", "cotiza", "cobras", "tarifa", "price", "prices", "cost"],
    ),
    (
        "service_context",
        "Servicio / equipo",
        "media",
        ["samsung", "huawei", "xiaomi", "honor", "tecno", "infinix", "iphone", "frp", "mdm", "imei", "liberar", "unlock"],
    ),
]


def classify_text(text: str) -> Decision:
    value = normalize(text)
    best: Decision | None = None
    for intent, label, priority, keywords in INTENTS:
        reasons = [keyword for keyword in keywords if keyword in value]
        if not reasons:
            continue
        score = len(reasons) * 2
        if intent in {"accounting_payment", "accounting_debt", "price_request"}:
            score += 4
        if intent == "service_context":
            score = len(reasons)
        decision = Decision(
            status="match",
            intent=intent,
            label=label,
            priority=priority,
            score=score,
            text=text,
            reasons=reasons[:6],
        )
        if not best or decision.score > best.score:
            best = decision
    return best or Decision(
        status="context",
        intent="conversation_context",
        label="Contexto de cliente",
        priority="baja",
        score=0,
        text=text,
        reasons=[],
    )


def classify_messages(messages: list[dict[str, Any]]) -> Decision:
    best: Decision | None = None
    for message in messages:
        text = str(message.get("text") or "")
        if not text.strip():
            continue
        decision = classify_text(text)
        if not best or decision.score > best.score:
            best = decision
    return best or Decision(
        status="empty",
        intent="no_signal",
        label="Sin lectura util",
        priority="baja",
        score=0,
        text="",
        reasons=[],
    )
