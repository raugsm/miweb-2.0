# Operating Core

Owner: Python.

The Operating Core turns `conversation_event` records into business state: cases, tasks, priority queue, accounting drafts and operating `decision_event` output.

## Boundaries

It does not read the screen, move the mouse, answer customers, or register final accounting entries. It opens operational tasks and draft accounting events that the Cognitive Core, Accounting Core and Hands Engine can act on.

## Inputs

- `desktop-agent/runtime/conversation-events.jsonl`

## Outputs

- SQLite state: `desktop-agent/runtime/operating-core.sqlite`
- State file: `desktop-agent/runtime/operating-state.json`
- Decisions: `desktop-agent/runtime/decision-events.jsonl`
- Accounting drafts: `desktop-agent/runtime/accounting-events.jsonl`

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
- groups named Pagos Mexico, Pagos Chile or Pagos Colombia are tracked as ignored groups and do not create action decisions;
- source conversation events are deduplicated so repeated passes do not create duplicate work;
- repeated tasks for the same case/action are updated in place, and older duplicate open tasks are superseded;
- resolved conversations close open tasks for that case;
- payment, debt and price-with-amount signals create draft accounting events for later confirmation.
