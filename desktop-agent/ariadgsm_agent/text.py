from __future__ import annotations

import re
import unicodedata


_MOJIBAKE_MARKERS = ("Ã", "Â", "â", "ðŸ")


def repair_mojibake(value: object) -> str:
    text = str(value or "")
    if not any(marker in text for marker in _MOJIBAKE_MARKERS):
        return text
    try:
        candidate = text.encode("latin1").decode("utf-8")
    except UnicodeError:
        return text

    def score(raw: str) -> int:
        return sum(raw.count(marker) for marker in _MOJIBAKE_MARKERS) + raw.count("\ufffd")

    return candidate if score(candidate) <= score(text) else text


def clean_text(value: object) -> str:
    return re.sub(r"\s+", " ", repair_mojibake(value)).strip()


def normalize(value: object) -> str:
    text = clean_text(value).lower()
    normalized = unicodedata.normalize("NFD", text)
    return "".join(ch for ch in normalized if unicodedata.category(ch) != "Mn")


def looks_like_browser_ui_title(value: object) -> bool:
    normalized = normalize(value)
    if not normalized:
        return True
    blocked = (
        "whatsapp business",
        "paginas mas",
        "perfil 1",
        "anadir esta pagina a marcadores",
        "anadir pestana a la barra de tareas",
        "añadir pestaña a la barra de tareas",
        "editar marcador",
        "editar favorito",
        "ver informacion del sitio",
        "informacion del sitio",
        "leer en voz alta",
        "ctrl d",
        "google chrome",
        "microsoft edge",
        "mozilla firefox",
        "http",
    )
    return any(token in normalized for token in blocked)


def text_hash(value: object) -> str:
    import hashlib

    return hashlib.sha1(normalize(value).encode("utf-8")).hexdigest()
