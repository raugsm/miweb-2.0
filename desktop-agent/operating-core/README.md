# Operating Core

Owner: Python.

The Operating Core turns `conversation_event` records into business state: cases, tasks, priority queue and operating `decision_event` output.

## Boundaries

It does not read the screen, move the mouse, answer customers, or register final accounting entries. It opens operational tasks that the Cognitive Core, Accounting Core and Hands Engine can act on.

## Inputs

- `desktop-agent/runtime/conversation-events.jsonl`

## Outputs

- SQLite state: `desktop-agent/runtime/operating-core.sqlite`
- State file: `desktop-agent/runtime/operating-state.json`
- Decisions: `desktop-agent/runtime/decision-events.jsonl`

## Run

```powershell
python -m ariadgsm_agent.operating --json
```

Run it from `desktop-agent`, or set `PYTHONPATH=desktop-agent` from the repository root. Runtime paths are resolved against the `desktop-agent` folder.

## Business Rules V1

- price requests become price response tasks;
- payment/debt messages become accounting-risk tasks;
- service/equipment context becomes active service case;
- customer/unknown latest messages stay in the waiting queue;
- groups named Pagos Mexico, Pagos Chile or Pagos Colombia are tracked as ignored groups and do not create action decisions.
