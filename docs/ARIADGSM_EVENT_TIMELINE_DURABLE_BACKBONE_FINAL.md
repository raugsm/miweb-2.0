# AriadGSM Event, Timeline & Durable State Backbone

Version: 0.9.9
Layer: 5
Status: consolidated

## External sources used

- Python `io` documentation: streams support `seek`, `tell`, `readline` and binary file positioning. This is the basis for offset/checkpoint ingestion instead of loading entire JSONL files.
  https://docs.python.org/3.11/library/io.html
- Python `sqlite3` documentation: transactions and context-managed commits/rollbacks are the supported local durability mechanism available in the runtime.
  https://docs.python.org/3/library/sqlite3.html
- SQLite WAL documentation: WAL appends commits, supports concurrent readers/writers, and checkpoints keep the database performant.
  https://www.sqlite.org/wal.html
- SQLite checkpoint API documentation: checkpointing transfers WAL content into the database and can reset the WAL.
  https://www.sqlite.org/c3ref/wal_checkpoint.html
- Microsoft .NET OpenTelemetry observability guidance: useful systems emit logs, metrics and traces with agreed semantics.
  https://learn.microsoft.com/es-es/dotnet/core/diagnostics/observability-with-otel

## Why this layer was not finished before

Reader Core and Timeline were correct logically but not operationally. They used:

```text
path.read_text(...).splitlines()[-limit:]
```

That pattern reads the whole file into memory before taking the last lines. With
`perception-events.jsonl` above 300 MB, the IA could see WhatsApp but the final
reader state arrived too late or not at all. Capa 4 then blocked Capa 7 because
fresh reading was missing.

## Final responsibility

Capa 5 owns the live event pipe:

- event ingestion by offset/checkpoint;
- SQLite durable state for Reader Core and Timeline;
- JSONL only as human projection and exchange format;
- safe retention/compaction for Python-owned projections;
- metrics that explain lag, bytes read, backlog and cycle time;
- unified Timeline for live and learning events.

## Final design

### 1. Incremental JSONL ingestion

`desktop-agent/ariadgsm_agent/event_backbone.py` reads each source file from the
last committed byte offset. On first run with a huge source file, it bootstraps
from the tail, records skipped backlog and checkpoints to the current end.

This prevents the IA from rereading yesterday's or last hour's full file when it
only needs the newest WhatsApp evidence.

### 2. Durable checkpoints

State file:

```text
desktop-agent/runtime/event-backbone-state.json
```

The state tracks every source by namespace and absolute path:

- previous offset;
- current file size;
- bytes read;
- backlog bytes;
- skipped backlog bytes;
- partial line handling;
- cycle duration.

### 3. SQLite as live truth

Reader Core keeps accepted visible messages in:

```text
desktop-agent/runtime/reader-core.sqlite
```

Timeline keeps unified conversation messages in:

```text
desktop-agent/runtime/timeline.sqlite
```

Both use SQLite WAL mode so read/write activity does not force every module to
wait for a full file rewrite.

### 4. JSON as projection

These files remain for debugging, cloud sync and human review:

- `reader-visible-messages.jsonl`
- `conversation-events.jsonl`
- `timeline-events.jsonl`
- `reader-core-state.json`
- `timeline-state.json`

They are not the primary truth anymore.

### 5. Retention and compaction

The backbone does not rewrite active source logs such as `perception-events.jsonl`
because that file can be written by the perception worker. Instead it skips old
backlog through offsets.

Python-owned projections are compacted by tail when they exceed safe limits.
Before replacing a projection, a local archive copy is created.

### 6. Human metrics

The app now exposes:

- `sourceBytesRead`;
- `sourceBacklogBytes`;
- `sourceSkippedBacklogBytes`;
- `cycleDurationMs`;
- Event Backbone bytes/backlog/skipped in the human report.

This means the next time the IA says "no actuo", we can see whether it is
waiting for WhatsApp, Reader Core, Timeline, or backlog ingestion.

## Contracts

New contracts:

- `desktop-agent/contracts/event-backbone-state.schema.json`
- `desktop-agent/contracts/timeline-state.schema.json`

Updated contracts registry:

- `event_backbone_state`
- `timeline_state`

## Integration points

### Capa 2: AI Runtime Control Plane

The Python loop still owns the execution order. The difference is that Reader
Core and Timeline now return measurable freshness/lag instead of blocking on
full-file reads.

### Capa 4: Perception & Reader Core

Reader Core consumes Perception output incrementally and publishes fresh channel
readiness only from the current batch.

### Capa 7: Action, Tools & Verification

Hands still wait for fresh reader confirmation. This layer only makes that
confirmation arrive fast enough to operate live.

### Capa 8: Trust, Telemetry, Evaluation & Cloud

Telemetry now has concrete ingestion metrics and a durable checkpoint file to
explain failures.

## Definition of done

Capa 5 is complete when:

- Reader Core no longer loads whole JSONL source files;
- Timeline no longer rebuilds only from raw JSONL as its truth;
- huge source logs can be consumed from tail in a bounded cycle;
- event offsets survive restart;
- state contracts validate;
- automated tests simulate a large JSONL and prove bounded reads;
- Windows app surfaces Event Backbone status;
- release package and manifest are versioned.

## Remaining operational validation

The code can prove bounded ingestion locally. The real-world validation is to
leave the app running with the 3 WhatsApps open and confirm:

- `reader-core-state.json` updates to version 0.9.9;
- `sourceBytesRead` stays bounded;
- `sourceSkippedBacklogBytes` appears only during first catch-up;
- Window Reality leaves `READY_PENDING_READER` once Reader Core receives a fresh
  Perception batch.
