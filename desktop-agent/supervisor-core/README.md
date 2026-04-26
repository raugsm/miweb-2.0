# Supervisor Core

Owner: Python.

The Supervisor Core is AriadGSM's autonomy gate. It reviews decisions and action events before the system is allowed to move from observation into navigation, accounting, prepared replies or customer-facing execution.

## Boundaries

It does not read the screen, move the mouse, type, send messages or create accounting records. It decides whether a decision/action is allowed, blocked, or requires human confirmation.

## Inputs

- Cognitive decisions: `desktop-agent/runtime/cognitive-decision-events.jsonl`
- Operating decisions: `desktop-agent/runtime/decision-events.jsonl`
- Hands actions: `desktop-agent/runtime/action-events.jsonl`

## Outputs

- State file: `desktop-agent/runtime/supervisor-state.json`

## Run

```powershell
python -m ariadgsm_agent.supervisor --json
```

Run it from `desktop-agent`, or set `PYTHONPATH=desktop-agent` from the repository root.

## V1 Rules

- maps each decision/action to autonomy levels 1-6;
- blocks anything above the configured autonomy level;
- treats customer-facing write/send as critical unless full autonomy is explicitly reached;
- keeps payment/debt/accounting actions behind confirmation;
- writes a concise audit state for the Windows App dashboard.
