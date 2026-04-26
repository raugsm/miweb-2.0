# Timeline Engine

Owner: Python.

The Timeline Engine is the bridge between fast live reading and slower historical learning. It merges repeated `conversation_event` records into one current, deduplicated conversation timeline per channel/customer.

## Boundaries

It does not read the screen, classify business intent, move the mouse or write accounting records. It receives already-extracted conversation events and emits clean timeline conversation events.

## Inputs

- Live conversations: `desktop-agent/runtime/conversation-events.jsonl`
- Optional historical conversations: `desktop-agent/runtime/history-conversation-events.jsonl`

## Outputs

- Timelines: `desktop-agent/runtime/timeline-events.jsonl`
- State file: `desktop-agent/runtime/timeline-state.json`

## Run

```powershell
python -m ariadgsm_agent.timeline --json
```

Run it from `desktop-agent`, or set `PYTHONPATH=desktop-agent` from the repository root.

## V1 Rules

- groups events by `channelId + conversationId`;
- deduplicates messages by `messageId` or message fingerprint;
- preserves message source: live, history, replay, manual or timeline;
- respects a 30-day default history window;
- rewrites `timeline-events.jsonl` by default so downstream cores read the current clean timeline;
- emits contract-valid `conversation_event` records with `source: timeline`.
