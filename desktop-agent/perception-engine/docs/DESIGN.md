# Perception Engine V1 Design

## Goal

The Perception Engine turns `vision_event` evidence into structured objects that later engines can trust. It is the bridge between seeing and understanding.

## Nine Layers

1. Input Layer: reads `vision_event` JSONL produced by Vision Engine.
2. Window Identity Layer: accepts only supported visible WhatsApp Web browser windows.
3. Channel Resolver: maps windows to `wa-1`, `wa-2`, `wa-3` through configuration.
4. Reader Core: reads visible text from Windows accessibility, with OCR reserved as fallback.
5. Message Extractor: converts reader output into message objects.
6. Cleaner and Normalizer: removes UI noise, standalone hours and non-message text.
7. Conversation Builder: groups messages into one timeline with a 30-day default limit.
8. Semantic Layer: will detect price, debt, payment, service, country, urgency and slang.
9. Audit and Memory Output: emits `perception_event` with confidence, source and reason.

## V1 Definition Of Done

1. Stable .NET 10 solution.
2. Reads the latest Vision Engine event.
3. Uses positive WhatsApp identity instead of blacklist filters.
4. Supports Chrome, Edge and Firefox.
5. Resolves visible WhatsApp windows to configured channels.
6. Reads visible text through Reader Core.
7. Extracts useful messages from conversation area.
8. Emits valid `perception_event` and `conversation_event` records.
9. Writes local health diagnostics.
10. Includes CLI, worker and automated tests for identity, channel resolution, extraction and conversation output.

## Boundaries

It does not move mouse, answer clients, register accounting or upload raw frames. Those jobs belong to Hands, Cognitive, Accounting and Cloud Sync.
