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

## V1 Strategy

1. Compile stable .NET 10 skeleton.
2. Emit valid `vision_event` records.
3. Keep raw local evidence under retention policy.
4. Enumerate visible windows through Win32.
5. Replace synthetic capture with Windows Graphics Capture.
6. Add DXGI capture later for high-FPS performance.

## Why .NET 10

.NET 10 is the current LTS target and is supported through November 2028. This avoids building the final Windows app on an older runtime that would force another migration soon.

