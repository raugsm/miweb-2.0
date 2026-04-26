# AriadGSM Desktop Agent Core

This folder is the new architecture center for the local PC agent.

PowerShell remains as a temporary Windows launcher and compatibility bridge. The long-term shape is:

```text
desktop-agent/
  ariadgsm_agent/        Python core: memory, decisions, learning, service loop
  contracts/             Stable event contracts between sensors and the core
  vision-engine/         Future C#/.NET live eyes and temporary visual buffer
  perception-engine/     Future object detector for chats, messages, buttons
  hands-engine/          Future C#/.NET mouse, keyboard and verification layer
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

## Safety Contract

The Python core does not read browser cookies, tokens, local storage, WhatsApp sessions or hidden browser internals. It consumes only visible observations produced by Reader Core or future Windows bridge sensors.

## Next Migration Steps

1. Keep contracts stable in `desktop-agent/contracts`.
2. Replace PowerShell vision and UI Automation with `vision-engine`, `perception-engine` and `hands-engine`.
3. Move the launcher UI to `windows-app` and background execution to `windows-service`.
4. Package with `installer` so the final program does not require manual shells.
