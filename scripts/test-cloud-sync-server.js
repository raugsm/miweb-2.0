const assert = require("assert");
const fs = require("fs");
const os = require("os");
const path = require("path");

process.env.DATA_DIR = fs.mkdtempSync(path.join(os.tmpdir(), "ariadgsm-cloud-sync-"));
process.env.OPERATIVA_AGENT_KEY = "test-token";
process.env.ARIADGSM_CLOUD_SYNC_RATE_LIMIT_PER_MINUTE = "2";

const { signatureForBody, verifyAriadGsmSignature, consumeCloudSyncRate } = require("../server-wrapper");
const { recordCloudSync, readCloudSyncAudit, readOperativaState } = require("../operativa-store");

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

const rawBody = JSON.stringify(payload);
const signature = signatureForBody(rawBody, process.env.OPERATIVA_AGENT_KEY);
assert.strictEqual(verifyAriadGsmSignature(rawBody, signature, process.env.OPERATIVA_AGENT_KEY), true);
assert.strictEqual(verifyAriadGsmSignature(rawBody, "sha256=bad", process.env.OPERATIVA_AGENT_KEY), false);

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

const audit = readCloudSyncAudit();
assert.deepStrictEqual(audit.map((item) => item.verdict), ["new", "duplicate"]);
assert.strictEqual(audit[0].lote_id, "cloudsync-test-batch");

const firstRate = consumeCloudSyncRate("test-token");
const secondRate = consumeCloudSyncRate("test-token");
const thirdRate = consumeCloudSyncRate("test-token");
assert.strictEqual(firstRate.allowed, true);
assert.strictEqual(secondRate.allowed, true);
assert.strictEqual(thirdRate.allowed, false);

console.log("cloud sync server OK");
