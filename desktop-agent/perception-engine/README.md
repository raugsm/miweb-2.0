# Perception Engine

Final owner: C#/.NET for local perception adapters, with Python receiving understood business state later.

Purpose:

- convert Vision Engine output into objects;
- identify WhatsApp windows, chat list, active conversation, message bubbles, input box, payment proofs, audio and errors;
- combine local OCR, Windows accessibility and visual detection;
- emit `perception_event` contracts.

This engine must not leak browser cookies, tokens or hidden session data.

## Project Shape

```text
AriadGSM.Perception.slnx
global.json
Directory.Build.props

src/
  AriadGSM.Perception.Core/
  AriadGSM.Perception.Cli/
  AriadGSM.Perception.Worker/

tests/
  AriadGSM.Perception.Tests/

config/
  perception.example.json

docs/
  DESIGN.md
```

## V1 Spine

```text
VisionEventReader
  -> WhatsAppWindowDetector
  -> ChannelResolver
  -> ReaderCore slots
  -> PerceptionEvent
  -> PerceptionHealthState
```

The first hard rule is positive identity: only Chrome, Edge or Firefox windows whose visible title is WhatsApp are accepted. Everything else is ignored by design.

## CLI

```powershell
dotnet run --project src/AriadGSM.Perception.Cli -- status
dotnet run --project src/AriadGSM.Perception.Cli -- sample config/perception.example.json
dotnet run --project src/AriadGSM.Perception.Cli -- diagnose config/perception.example.json
dotnet run --project src/AriadGSM.Perception.Cli -- watch config/perception.example.json 5
```
