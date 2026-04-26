from __future__ import annotations

from dataclasses import dataclass, field


PRIORITY_ORDER = {
    "customer_waiting": 10,
    "accounting_risk": 9,
    "price_request": 8,
    "active_case": 7,
    "history_learning": 3,
    "ignored_group": 1,
}


@dataclass(order=True)
class WorkItem:
    sort_priority: int
    kind: str = field(compare=False)
    channel_id: str = field(compare=False)
    conversation_id: str = field(compare=False)
    summary: str = field(compare=False)


def make_work_item(kind: str, channel_id: str, conversation_id: str, summary: str) -> WorkItem:
    return WorkItem(
        sort_priority=-PRIORITY_ORDER.get(kind, 5),
        kind=kind,
        channel_id=channel_id,
        conversation_id=conversation_id,
        summary=summary,
    )

