# AriadGSM Autonomous Agent Architecture

This is the fixed architecture for the local AriadGSM agent. The goal is to avoid growing into a fragile bot made of scripts and filters.

## Non-Negotiable Rules

1. Local first: the PC sees, reasons and reacts quickly. The cloud receives understood business state, not raw screen video.
2. PowerShell is temporary: launcher, compatibility and emergency only.
3. C#/.NET owns Windows: desktop app, service, live capture, UI Automation, mouse, keyboard and installer.
4. Python owns intelligence: cognition, memory, accounting, learning, planning and business reasoning.
5. Every module talks through contracts in `desktop-agent/contracts`.
6. Raw frames are temporary local evidence. Understood conversations, accounting and learning are long-term memory.
7. Autonomy is graduated: observe, suggest, confirm, act on safe cases.
8. Every decision and action must be auditable.

## Layer Map

```text
Vision Engine
  Sees the live desktop and stores short-lived local visual evidence.

Perception Engine
  Converts pixels/accessibility/OCR into objects: windows, chats, messages,
  buttons, payment proofs, audio, errors.

Timeline Engine
  Unifies live messages and historical learning into one conversation timeline.

Memory Core
  Stores clients, conversations, services, slang, procedures, failures and facts.

Operating Core
  Keeps the business state: cases, tasks, priorities, payments, debts.

Accounting Core
  Converts evidence into payment/debt records with confidence and status.

Cognitive Core
  Understands, plans, decides, learns and asks for confirmation when needed.

Hands Engine
  Moves mouse, keyboard, focus and scroll, then verifies the result.

Supervisor
  Enforces safety, confidence thresholds, autonomy level and audit rules.

Windows App / Service / Installer
  Runs the local agent without visible shells and starts it with Windows.

Cloud Sync
  Syncs understood business state to ariadgsm.com.
```

## Stable Contracts

Each stage emits one contract:

- `vision-event.schema.json`
- `perception-event.schema.json`
- `conversation-event.schema.json`
- `decision-event.schema.json`
- `action-event.schema.json`
- `accounting-event.schema.json`
- `learning-event.schema.json`

The code can evolve, but these contracts are the spine. If WhatsApp changes, the adapter changes. The Cognitive Core should not be rewritten.

## Runtime Policy

- Raw frames live under `D:\AriadGSM\vision-buffer` when available.
- Live raw visual retention defaults to 1 hour.
- Default raw storage cap is 40 GB.
- Raw frames are not uploaded to the cloud by default.
- The cloud receives conversation summaries, accounting evidence, learning events and decisions.

## Autonomy Levels

```text
1 observe: read and learn only
2 suggest: propose actions
3 navigate: open chats, scroll and read
4 record: create accounting drafts and case records
5 prepare: draft customer responses for human confirmation
6 execute: send/respond only in high-confidence safe cases
```

## Build Order

1. Freeze contracts and folder structure.
2. Vision Engine v1: live local buffer, change detection, no cloud raw frames.
3. Perception Engine v1: objects, not loose OCR lines.
4. Timeline Engine: live plus one-month history in the same timeline.
5. Memory and Accounting Core.
6. Cognitive Core v1.
7. Hands Engine with verification.
8. Windows App and service.
9. Installer and updater.

