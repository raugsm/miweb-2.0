# AriadGSM Desktop Agent Core

This folder is the new architecture center for the local PC agent.

PowerShell remains as a temporary Windows launcher and compatibility bridge. The long-term shape is:

```text
desktop-agent/
  ariadgsm_agent/        Python core: memory, decisions, learning, service loop
  contracts/             Stable event contracts between sensors and the core
  vision-engine/         C#/.NET live eyes and temporary visual buffer
  perception-engine/     C#/.NET object reader for chats, messages, buttons
  memory-core/           Python durable memory and customer profiles
  cognitive-core/        Python reasoning, learning and customer patterns
  operating-core/        Python business cases, tasks and priorities
  hands-engine/          C#/.NET mouse, keyboard, focus, scroll and verification layer
  supervisor-core/       Python autonomy gate, confirmations, risk and audit state
  windows-app/           Future C#/.NET desktop UI and tray app
  windows-service/       Future C#/.NET background service
  local-api/             Future named-pipe or localhost contract bus
  installer/             Future installer and updater
  runtime/               Local state and SQLite memory, ignored by Git
```

## Why This Exists

The old flow grew inside PowerShell because it was the fastest way to talk to Windows. That was useful for discovery, but the agent now needs a more stable core:

- Python owns the brain, memory, decisions and learning ledger.
- C#/.NET will own deep Windows integration: live vision, accessibility, window control, mouse, keyboard, service and installer.
- PowerShell becomes only launcher, installer and emergency maintenance.

See `ARCHITECTURE.md` for the fixed design.

## Run Once

From the repo root:

```powershell
python .\desktop-agent\run_core.py --config .\desktop-agent\config.example.json --once --json
```

## Watch Mode

```powershell
python .\desktop-agent\run_core.py --config .\desktop-agent\config.example.json --watch
```

The service reads visible structured observations from:

```text
scripts\visual-agent\runtime\reader-core\structured-observations.jsonl
```

Then it stores normalized memory in SQLite:

```text
desktop-agent\runtime\agent-memory.sqlite
```

## Engine Runs

```powershell
python -m ariadgsm_agent.cognitive --json
python -m ariadgsm_agent.operating --json
python -m ariadgsm_agent.memory --json
python -m ariadgsm_agent.supervisor --json
dotnet run --project .\desktop-agent\hands-engine\src\AriadGSM.Hands.Cli -- sample .\desktop-agent\hands-engine\config\hands.example.json
```

The Cognitive Core reads `conversation-events.jsonl`, writes auditable decisions, emits learning events and updates local customer profiles. The Memory Core persists conversations, messages, signals, decisions, learning and accounting evidence into durable SQLite memory. The Hands Engine consumes decisions plus Perception evidence and emits audited action plans or verified local actions. The Supervisor Core reviews those decisions/actions and explains what is allowed, blocked or waiting for confirmation.

## Safety Contract

The Python core does not read browser cookies, tokens, local storage, WhatsApp sessions or hidden browser internals. It consumes only visible observations produced by Reader Core or future Windows bridge sensors.

## Next Migration Steps

1. Keep contracts stable in `desktop-agent/contracts`.
2. Feed Hands Engine with Perception row coordinates so it can open exact chats.
3. Move the launcher UI to `windows-app` and background execution to `windows-service`.
4. Package with `installer` so the final program does not require manual shells.
