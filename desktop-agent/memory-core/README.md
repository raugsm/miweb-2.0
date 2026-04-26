# Memory Core

Owner: Python.

The Memory Core is AriadGSM's durable local memory. It stores what the system has seen and understood: conversations, messages, semantic signals, decisions, learning events, accounting drafts and customer profiles.

## Boundaries

It does not read the screen, move the mouse, decide actions or write final accounting records. It receives already-understood events from Vision, Perception, Cognitive, Operating and Accounting layers.

## Inputs

- Conversations: `desktop-agent/runtime/timeline-events.jsonl`
- Cognitive decisions: `desktop-agent/runtime/cognitive-decision-events.jsonl`
- Operating decisions: `desktop-agent/runtime/decision-events.jsonl`
- Learning events: `desktop-agent/runtime/learning-events.jsonl`
- Accounting drafts: `desktop-agent/runtime/accounting-events.jsonl`

## Outputs

- SQLite memory: `desktop-agent/runtime/memory-core.sqlite`
- State file: `desktop-agent/runtime/memory-state.json`

## Run

```powershell
python -m ariadgsm_agent.memory --json
```

Run it from `desktop-agent`, or set `PYTHONPATH=desktop-agent` from the repository root.

## V1 Rules

- deduplicates every source event by stable event id;
- stores messages separately from conversations;
- stores Perception semantic signals per message;
- stores decisions from Cognitive and Operating cores;
- stores learning events as searchable knowledge items;
- stores accounting draft evidence;
- keeps customer profiles with country, service, language, message count, decision count and accounting count;
- preserves the old `AgentMemory` API so the earlier desktop service still works.
