const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");

process.env.DATA_DIR = fs.mkdtempSync(path.join(os.tmpdir(), "ariadgsm-cloud-sync-"));

const { recordCloudSync, readOperativaState } = require("../operativa-store");

const payload = {
  id: "cloudsync-test-batch",
  idempotencyKey: "cloudsync-test-batch",
  payloadHash: "hash-test",
  schemaVersion: "cloud_sync_payload_v1",
  actor: "desktop_agent",
  source: "ariadgsm_local_agent",
  mode: "local_ia",
  status: "ok",
  runtimeKernel: {
    status: "ok",
    canAct: false,
    canSync: true,
  },
  summary: {
    incidentsOpen: 0,
  },
  conversations: 1,
  messages: 2,
  accountingEvents: 0,
  learningEvents: 0,
  eventsIngested: 2,
  eventsRejected: 0,
};

const first = recordCloudSync(payload, "test");
assert.strictEqual(first.batch.duplicate, false);
assert.strictEqual(first.batch.idempotencyKey, "cloudsync-test-batch");

const second = recordCloudSync(payload, "test");
assert.strictEqual(second.duplicate, true);
assert.strictEqual(second.batch.duplicate, true);

const state = readOperativaState();
assert.strictEqual(state.syncBatches.length, 1);
assert.strictEqual(state.syncBatches[0].idempotencyKey, "cloudsync-test-batch");
assert.strictEqual(state.cloud.status, "sincronizado");

console.log("cloud sync server OK");
