# Cognitive Core

Owner: Python.

The Cognitive Core turns `conversation_event` records into business reasoning: intent, confidence, proposed action, learning events and customer memory.

## Boundaries

It does not read the screen, move the mouse, send replies or register final accounting entries. It decides what the system believes is happening and what should happen next, under Supervisor autonomy rules.

## Inputs

- Conversations: `desktop-agent/runtime/timeline-events.jsonl`

## Outputs

- Decisions: `desktop-agent/runtime/cognitive-decision-events.jsonl`
- Learning events: `desktop-agent/runtime/learning-events.jsonl`
- SQLite memory: `desktop-agent/runtime/cognitive-core.sqlite`
- State file: `desktop-agent/runtime/cognitive-state.json`

## Run

```powershell
python -m ariadgsm_agent.cognitive --json
```

Run it from `desktop-agent`, or set `PYTHONPATH=desktop-agent` from the repository root.

## V1 Rules

- combines classifier signals with Perception semantic `signals`;
- prioritizes payment/debt over price/service when both appear;
- applies Supervisor autonomy levels before allowing actions;
- emits auditable `decision_event` records;
- emits `learning_event` records for pricing, accounting, services, countries, urgency and language hints;
- maintains lightweight client profiles by conversation.
