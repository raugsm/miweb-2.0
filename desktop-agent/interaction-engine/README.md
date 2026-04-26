# AriadGSM Interaction Engine

Interaction Engine converts raw Perception objects into verified UI targets for AriadGSM.

It does not read cookies, tokens, browser internals, or WhatsApp sessions. It only consumes local Perception events already produced by the visible WhatsApp windows and emits auditable interaction targets for Hands.

Flow:

1. Read `perception-events.jsonl`.
2. Accept only WhatsApp chat rows with a channel, title, bounds, and click coordinates.
3. Reject browser UI, generic WhatsApp titles, and low-value payment/admin groups as navigation targets.
4. Write `interaction-events.jsonl` and `interaction-state.json`.
5. Hands uses those verified targets before moving the mouse.
