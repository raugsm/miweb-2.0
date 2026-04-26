# Vision Engine

Final owner: C#/.NET.

This is the real local eye of AriadGSM. It must see the desktop, preserve short-lived local visual evidence, detect changes, and emit `vision_event` contracts. It must not reason about the business, move the mouse, answer customers, or upload raw frames to the cloud by default.

## Project Shape

```text
AriadGSM.Vision.sln
global.json
Directory.Build.props

src/
  AriadGSM.Vision.Core/
  AriadGSM.Vision.Worker/
  AriadGSM.Vision.Cli/

tests/
  AriadGSM.Vision.Tests/

config/
  vision.example.json

docs/
  DESIGN.md
```

## Runtime Policy

- raw frames are local temporary evidence;
- default storage root: `D:\AriadGSM\vision-buffer`;
- default retention: 1 hour;
- default cap: 40 GB;
- cloud raw frame upload: disabled;
- output contract: `desktop-agent/contracts/vision-event.schema.json`.
- CLI diagnostics:
  - `dotnet run --project src/AriadGSM.Vision.Cli -- sample config/vision.example.json`
  - `dotnet run --project src/AriadGSM.Vision.Cli -- diagnose config/vision.example.json`
  - `dotnet run --project src/AriadGSM.Vision.Cli -- windows`

Python `scripts/visual-agent/eyes-stream.py` remains only as a temporary prototype until this .NET engine replaces it.
