# AriadGSM IA Local 0.6.0 Architecture

Version 0.6.0 is a control-center redesign. The goal is to stop adding isolated patches and make every subsystem answer to a clear owner, contract, and diagnostic trail.

## 1. Control Center

The desktop app is only the operator cockpit. It shows login, cabin status, live timeline, warnings, version and support diagnostics. It must not contain business rules for WhatsApp reading, accounting, memory or mouse execution.

## 2. Runtime Orchestrator

The runtime owns the local lifecycle:

`login -> update -> prepare_cabin -> validate_cabin -> start_engines -> monitor -> recover_or_pause`

It starts and stops AriadGSM-owned workers only. It never closes Edge, Chrome or Firefox.

## 3. Cabin Workspace Manager

The workspace manager owns the three WhatsApp sessions:

- `wa-1 = Edge`
- `wa-2 = Chrome`
- `wa-3 = Firefox`

It finds existing WhatsApp Web sessions first, opens only missing browser sessions, and reports duplicated, covered, login-required or profile-error states.

## 4. Cabin Authority

Cabin Authority is the single gate for window control. Background monitoring observes only. Window arrangement happens only during explicit setup/bootstrap. Hands may act only when Cabin Authority says the target channel is visible, ready and unblocked.

## 5. Input Arbiter

Input Arbiter decides who owns mouse and keyboard at any moment. The operator always has priority. If the operator moves mouse or types, Hands must pause only the action layer, not the whole IA.

## 6. Reader Core

Reader Core reads WhatsApp in this order:

`accessibility/visible structure -> OCR fallback -> AI verifier -> conversation contract`

The output is not loose text; it is a conversation event with channel, chat, message objects, direction, time, confidence and source.

## 7. Action Queue

The system never moves the mouse directly from a decision. A decision becomes an action plan, then passes safety, Cabin Authority, Input Arbiter, execution and verification. Every action produces an audit event.

## 8. Memory And Accounting

Memory stores business knowledge: customers, countries, services, prices, slang, payment evidence, debts, exceptions and response style. Accounting stores evidence-first drafts until autonomy level allows higher-risk action.

## 9. Diagnostic Timeline

Every important event must be understandable by the operator:

- what happened
- why it happened
- which channel/window/action was involved
- whether the IA acted, waited or needs help
- what evidence was used

Technical logs remain available, but the cockpit should tell the story without requiring JSON reading.
