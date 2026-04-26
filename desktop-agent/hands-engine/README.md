# Hands Engine

Final owner: C#/.NET.

Purpose:

- focus windows;
- move mouse and keyboard;
- open chats;
- scroll history;
- capture conversation context;
- verify every action through perception before reporting success.

It emits `action_event` contracts and is always supervised by autonomy policy.

## Project Shape

```text
AriadGSM.Hands.slnx
global.json
Directory.Build.props

src/
  AriadGSM.Hands.Core/
  AriadGSM.Hands.Cli/
  AriadGSM.Hands.Worker/

tests/
  AriadGSM.Hands.Tests/

config/
  hands.example.json

docs/
  DESIGN.md
```

## V1 Spine

```text
DecisionEventReader
  -> PerceptionContextReader
  -> ActionPlanner
  -> HandsSafetyPolicy
  -> IHandsExecutor
  -> ActionVerifier
  -> ActionEventWriter
  -> HandsHealthState
```

Hands V1 is safe by default. With `executeActions=false`, it does not move the mouse or keyboard; it plans and writes auditable `action_event` records. Real window focus/scroll execution is enabled only when `executeActions=true`. Writing text and sending messages remain separately locked by `allowTextInput` and `allowSendMessage`.

## CLI

```powershell
dotnet run --project src/AriadGSM.Hands.Cli -- status
dotnet run --project src/AriadGSM.Hands.Cli -- sample config/hands.example.json
dotnet run --project src/AriadGSM.Hands.Cli -- diagnose config/hands.example.json
dotnet run --project src/AriadGSM.Hands.Cli -- watch config/hands.example.json 5
```

## V1 Closed

- reads Cognitive and Operating `decision_event` files;
- reads latest Perception context to verify visible WhatsApp channels;
- consumes Perception `chat_row` coordinates for exact `open_chat` targeting;
- plans focus, open-chat, scroll-history, capture-conversation, accounting, text and send actions;
- infers `wa-1`, `wa-2`, `wa-3` from decision targets or message evidence;
- blocks unsafe actions by autonomy level and explicit send/text flags;
- emits contract-valid `action_event` records;
- deduplicates stable action ids across cycles;
- writes health state for the future Windows App dashboard.
