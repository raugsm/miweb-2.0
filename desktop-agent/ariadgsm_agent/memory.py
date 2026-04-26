from __future__ import annotations

import json
import sqlite3
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from .classifier import Decision
from .text import text_hash


def utc_now() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


class AgentMemory:
    def __init__(self, db_path: Path):
        self.db_path = db_path
        self.db_path.parent.mkdir(parents=True, exist_ok=True)
        self.conn = sqlite3.connect(self.db_path)
        self.conn.row_factory = sqlite3.Row
        self.init_schema()

    def close(self) -> None:
        self.conn.close()

    def init_schema(self) -> None:
        self.conn.executescript(
            """
            create table if not exists observations (
              observation_key text primary key,
              channel_id text not null,
              source text not null,
              confidence real not null,
              captured_at text,
              conversation_title text,
              raw_json text not null,
              ingested_at text not null
            );
            create table if not exists messages (
              id integer primary key autoincrement,
              observation_key text not null,
              channel_id text not null,
              text_hash text not null,
              text text not null,
              sender_name text,
              direction text,
              sent_at text,
              learning_type text,
              created_at text not null,
              unique(observation_key, text_hash)
            );
            create table if not exists decisions (
              id integer primary key autoincrement,
              observation_key text not null,
              channel_id text not null,
              intent text not null,
              label text not null,
              priority text not null,
              score integer not null,
              text text,
              reasons_json text not null,
              created_at text not null
            );
            """
        )
        self.conn.commit()

    def has_observation(self, observation_key: str) -> bool:
        row = self.conn.execute(
            "select 1 from observations where observation_key = ? limit 1",
            (observation_key,),
        ).fetchone()
        return row is not None

    def ingest_observation(self, observation: dict[str, Any], decision: Decision) -> dict[str, int]:
        observation_key = str(observation["observationKey"])
        if self.has_observation(observation_key):
            return {"observations": 0, "messages": 0, "decisions": 0}

        now = utc_now()
        with self.conn:
            self.conn.execute(
                """
                insert into observations (
                  observation_key, channel_id, source, confidence, captured_at,
                  conversation_title, raw_json, ingested_at
                ) values (?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    observation_key,
                    observation.get("channelId"),
                    observation.get("source"),
                    observation.get("confidence"),
                    observation.get("capturedAt"),
                    observation.get("conversationTitle"),
                    json.dumps(observation, ensure_ascii=False, separators=(",", ":")),
                    now,
                ),
            )
            message_count = 0
            for message in observation.get("messages") or []:
                text = str(message.get("text") or "").strip()
                if not text:
                    continue
                self.conn.execute(
                    """
                    insert or ignore into messages (
                      observation_key, channel_id, text_hash, text, sender_name,
                      direction, sent_at, learning_type, created_at
                    ) values (?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        observation_key,
                        observation.get("channelId"),
                        text_hash(text),
                        text,
                        message.get("senderName"),
                        message.get("direction"),
                        message.get("sentAt"),
                        decision.intent,
                        now,
                    ),
                )
                message_count += 1
            self.conn.execute(
                """
                insert into decisions (
                  observation_key, channel_id, intent, label, priority, score,
                  text, reasons_json, created_at
                ) values (?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                (
                    observation_key,
                    observation.get("channelId"),
                    decision.intent,
                    decision.label,
                    decision.priority,
                    decision.score,
                    decision.text,
                    json.dumps(decision.reasons, ensure_ascii=False),
                    now,
                ),
            )
        return {"observations": 1, "messages": message_count, "decisions": 1}

    def summary(self) -> dict[str, Any]:
        def scalar(sql: str) -> int:
            row = self.conn.execute(sql).fetchone()
            return int(row[0] if row else 0)

        latest = self.conn.execute(
            """
            select channel_id, intent, label, text, created_at
            from decisions
            order by id desc
            limit 1
            """
        ).fetchone()
        return {
            "observations": scalar("select count(*) from observations"),
            "messages": scalar("select count(*) from messages"),
            "decisions": scalar("select count(*) from decisions"),
            "latestDecision": dict(latest) if latest else None,
            "db": str(self.db_path),
        }
