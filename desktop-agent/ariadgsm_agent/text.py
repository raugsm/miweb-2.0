from __future__ import annotations

import re
import unicodedata


def clean_text(value: object) -> str:
    return re.sub(r"\s+", " ", str(value or "")).strip()


def normalize(value: object) -> str:
    text = clean_text(value).lower()
    normalized = unicodedata.normalize("NFD", text)
    return "".join(ch for ch in normalized if unicodedata.category(ch) != "Mn")


def text_hash(value: object) -> str:
    import hashlib

    return hashlib.sha1(normalize(value).encode("utf-8")).hexdigest()
