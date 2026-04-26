# AriadGSM Desktop Agent Core

This folder is the new architecture center for the local PC agent.

PowerShell remains as a temporary Windows launcher and compatibility bridge. The long-term shape is:

```text
desktop-agent/
  ariadgsm_agent/        Python core: memory, decisions, learning, service loop
  contracts/             Stable event contracts between sensors and the core
  windows-bridge/        Future C#/.NET Windows bridge for UIA, mouse, capture
  runtime/               Local state and SQLite memory, ignored by Git
```

## Why This Exists

The old flow grew inside PowerShell because it was the fastest way to talk to Windows. That was useful for discovery, but the agent now needs a more stable core:

- Python owns the brain, memory, decisions and learning ledger.
- C#/.NET will own deep Windows integration: accessibility, window control, mouse and fast capture.
- PowerShell becomes only launcher, installer and emergency maintenance.

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

1. Move the classifier from `scripts/visual-agent/agent-local.py` into this core.
2. Replace PowerShell UI Automation with a C# Windows bridge that writes the same visible observation contract.
3. Move the launcher UI to WPF/WinUI or a Python desktop UI after the core is stable.
