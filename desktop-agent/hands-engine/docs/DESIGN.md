# Hands Engine V1 Design

## Goal

Hands Engine turns approved decisions into auditable local actions. It is the bridge between reasoning and Windows input.

## Layers

1. Decision Reader: reads Cognitive and Operating `decision_event` records.
2. Perception Context: reads latest `perception_event` records to know what is visible.
3. Planner: maps decision intent/action into one or more `action_event` plans.
4. Safety Policy: blocks actions that exceed autonomy, require confirmation or need locked capabilities.
5. Window Targeting: maps `wa-1`, `wa-2`, `wa-3` to Edge, Chrome and Firefox.
6. Executor: dry-run by default, Win32 focus and scroll when explicitly enabled.
7. Verifier: checks whether Perception sees the target channel/conversation.
8. Action Writer: emits stable `action_event` records and deduplicates them.
9. Health State: records counts, blocked actions, verified actions and last error.

## Safety

Default config never moves the mouse. Real movement requires `executeActions=true`.
Text entry and sending require separate flags. Accounting records are blocked when the source decision still requires human confirmation.

## V1 Definition Of Done

- stable .NET 10 solution;
- reads decision events and perception events;
- plans focus, open chat, scroll/capture and noop actions;
- blocks unsafe text/send actions by default;
- emits valid `action_event` records;
- verifies visible channel/conversation when possible;
- deduplicates actions across runs;
- includes CLI, worker and tests.

## Current Limit

Hands V1 can safely focus the right WhatsApp browser window and request capture/scroll plans. Exact chat-row clicking needs Perception to provide visible row coordinates for each target conversation. That is the correct next integration point, because guessing row positions from text alone is how the old autopilot became fragile.
