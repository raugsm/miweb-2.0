from __future__ import annotations

import hashlib
import re
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def normalize_text(value: str) -> str:
    return re.sub(r"\s+", " ", str(value or "")).strip().lower()


def message_fingerprint(channel_id: str, conversation_id: str, message: dict[str, Any]) -> str:
    raw = "|".join(
        [
            channel_id,
            conversation_id,
            str(message.get("direction") or "unknown"),
            str(message.get("sentAt") or ""),
            normalize_text(str(message.get("text") or "")),
        ]
    )
    return hashlib.sha1(raw.encode("utf-8")).hexdigest()


@dataclass
class ConversationTimeline:
    conversation_id: str
    channel_id: str
    title: str = ""
    history_limit_days: int = 30
    complete: bool = False
    messages: list[dict[str, Any]] = field(default_factory=list)
    _seen: set[str] = field(default_factory=set)

    def add_message(self, message: dict[str, Any]) -> bool:
        text = str(message.get("text") or "").strip()
        if not text:
            return False
        fingerprint = str(message.get("messageId") or "") or message_fingerprint(self.channel_id, self.conversation_id, message)
        if fingerprint in self._seen:
            return False
        self._seen.add(fingerprint)
        stored = dict(message)
        stored.setdefault("messageId", fingerprint)
        stored.setdefault("direction", "unknown")
        stored.setdefault("observedAt", utc_now())
        self.messages.append(stored)
        return True

    def merge(self, messages: list[dict[str, Any]]) -> int:
        return sum(1 for message in messages if self.add_message(message))

    def to_event(self, source: str = "live") -> dict[str, Any]:
        return {
            "eventType": "conversation_event",
            "conversationEventId": f"conversation-{self.channel_id}-{hashlib.sha1(self.conversation_id.encode('utf-8')).hexdigest()[:12]}",
            "conversationId": self.conversation_id,
            "channelId": self.channel_id,
            "observedAt": utc_now(),
            "conversationTitle": self.title,
            "source": source,
            "messages": list(self.messages),
            "timeline": {
                "historyLimitDays": self.history_limit_days,
                "complete": self.complete,
                "dedupeStrategy": "channel_conversation_direction_time_text",
            },
        }

