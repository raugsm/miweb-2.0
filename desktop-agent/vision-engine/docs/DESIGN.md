# Vision Engine V1 Design

## Goal

The Vision Engine is the local eye. It captures the live desktop, keeps short-lived local evidence, detects changes, and emits `vision_event`.

## Boundaries

It does not:

- classify customer intent;
- register accounting;
- move mouse or keyboard;
- answer messages;
- upload raw video or frames to the cloud by default.

Those jobs belong to Perception, Cognitive, Accounting and Hands engines.

## V1 Definition Of Done

1. Stable .NET 10 solution.
2. Real desktop capture with Win32/GDI fallback.
3. Valid `vision_event` records.
4. Local-only raw evidence under retention and storage cap.
5. Visible window inventory through Win32.
6. Local health diagnostics with status, counters and last error.
7. Continuous mode with change detection and event throttling.
8. CLI and worker entrypoints.
9. Automated contract and pipeline tests.

## V2 Performance Track

Windows Graphics Capture and DXGI are reserved for the next performance layer. They should replace the GDI backend only after they are tested against the same event, health, retention and privacy contracts.

## Why .NET 10

.NET 10 is the current LTS target and is supported through November 2028. This avoids building the final Windows app on an older runtime that would force another migration soon.
