const http = require("http");
const crypto = require("crypto");
const { URL } = require("url");
const { hasAdminPassword, isAuthenticated } = require("./admin-auth");
const {
  appendCloudSyncAudit,
  buildCloudReport,
  buildOperativaSnapshot,
  createOperativaBackupSnapshot,
  ingestOperativaEvent,
  listOperativaBackups,
  recordCloudSync,
  readOperativaState,
  writeOperativaState,
} = require("./operativa-store");

const originalCreateServer = http.createServer.bind(http);
const WEB_HARDENING_VERSION = "0.9.10";
const SESSION_HEADER = "x-ariadgsm-session";
const CSRF_HEADER = "x-ariadgsm-csrf";
const SIGNATURE_HEADER = "x-ariadgsm-signature";
const BOOT_SESSION_TOKEN =
  process.env.ARIADGSM_PANEL_SESSION_TOKEN || crypto.randomBytes(32).toString("base64url");
const CSRF_TOKEN = process.env.ARIADGSM_PANEL_CSRF_TOKEN || crypto.randomBytes(32).toString("base64url");
const CLOUD_RATE_LIMIT_PER_MINUTE = Math.max(
  1,
  Number(process.env.ARIADGSM_CLOUD_SYNC_RATE_LIMIT_PER_MINUTE || 60)
);
const CLOUD_RATE_WINDOW_MS = 60 * 1000;
const cloudRateBuckets = new Map();

const STRICT_CSP = [
  "default-src 'self'",
  "script-src 'self'",
  "style-src 'self'",
  "img-src 'self' data:",
  "connect-src 'self'",
  "frame-ancestors 'none'",
  "base-uri 'none'",
  "form-action 'self'",
].join("; ");

const COMMON_SECURITY_HEADERS = {
  "Content-Security-Policy": STRICT_CSP,
  "X-Content-Type-Options": "nosniff",
  "Referrer-Policy": "no-referrer",
  "Permissions-Policy": "camera=(), microphone=(), geolocation=(), payment=(), usb=(), fullscreen=(self)",
  "X-Frame-Options": "DENY",
  "Strict-Transport-Security": "max-age=31536000; includeSubDomains; preload",
};

function applySecurityHeaders(res) {
  for (const [name, value] of Object.entries(COMMON_SECURITY_HEADERS)) {
    res.setHeader(name, value);
  }
}

function buildCsrfCookie() {
  const secure = String(process.env.SESSION_SECURE || "").toLowerCase() === "true" ? "; Secure" : "";
  return `ariadgsm_csrf=${CSRF_TOKEN}; HttpOnly; Path=/; SameSite=Strict${secure}`;
}

function injectPanelTokens(html) {
  return String(html).replace(
    "</head>",
    `    <meta name="ariadgsm-session-token" content="${BOOT_SESSION_TOKEN}" />\n` +
      `    <meta name="ariadgsm-csrf-token" content="${CSRF_TOKEN}" />\n` +
      `</head>`
  );
}

global.__ARIADGSM_WEB_HARDENING__ = {
  version: WEB_HARDENING_VERSION,
  sessionHeader: SESSION_HEADER,
  csrfHeader: CSRF_HEADER,
  bootSessionToken: BOOT_SESSION_TOKEN,
  csrfToken: CSRF_TOKEN,
  csp: STRICT_CSP,
  commonSecurityHeaders: COMMON_SECURITY_HEADERS,
  applySecurityHeaders,
  buildCsrfCookie,
  injectPanelTokens,
};

function sendJson(res, statusCode, payload) {
  applySecurityHeaders(res);
  res.writeHead(statusCode, {
    "Content-Type": "application/json; charset=utf-8",
  });
  res.end(JSON.stringify(payload));
}

function parseBody(req, options = {}) {
  return new Promise((resolve, reject) => {
    const maxBytes = options.maxBytes || 2 * 1024 * 1024;
    let body = "";
    let bodyLength = 0;

    req.on("data", (chunk) => {
      bodyLength += chunk.length;
      if (bodyLength > maxBytes) {
        reject(new Error("Payload demasiado grande"));
        req.destroy();
        return;
      }
      body += chunk.toString();
    });

    req.on("end", () => {
      try {
        const parsed = body ? JSON.parse(body) : {};
        resolve(options.returnRaw ? { body: parsed, rawBody: body } : parsed);
      } catch (error) {
        reject(error);
      }
    });
  });
}

function nowIso() {
  return new Date().toISOString();
}

function createAuditId() {
  return `audit-${Date.now().toString(36)}-${crypto.randomBytes(3).toString("hex")}`;
}

function filterPendingSnapshot(snapshot) {
  return {
    ...snapshot,
    reviewItems: Array.isArray(snapshot.reviewItems)
      ? snapshot.reviewItems.filter((item) => item.status === "pendiente")
      : [],
  };
}

function buildCleanOperativaSnapshot(stateOverride = null, options = {}) {
  return filterPendingSnapshot(buildOperativaSnapshot(stateOverride, options));
}

function pushReviewAudit(state, entry) {
  state.auditLog = Array.isArray(state.auditLog) ? state.auditLog : [];
  state.auditLog.unshift({
    id: createAuditId(),
    actor: entry.actor || "panel",
    action: entry.action,
    entityType: "review_item",
    entityId: entry.entityId,
    before: entry.before || null,
    after: entry.after || null,
    createdAt: nowIso(),
  });
  state.auditLog = state.auditLog.slice(0, 500);
}

function resolveReviewItem(reviewId, actor = "panel", note = "") {
  const state = readOperativaState();
  const review = (state.reviewItems || []).find((item) => item.id === reviewId);

  if (!review) {
    throw new Error("No encontre esa duda pendiente");
  }

  const before = { ...review };
  review.status = "revisado";
  review.resolvedAt = nowIso();
  review.resolvedBy = actor;
  review.resolutionNote = note || "Marcado como revisado desde la cabina";
  review.updatedAt = nowIso();

  pushReviewAudit(state, {
    actor,
    action: "review:resolve",
    entityId: review.id,
    before,
    after: review,
  });

  return {
    review,
    snapshot: buildCleanOperativaSnapshot(writeOperativaState(state)),
  };
}

function resolveAllReviewItems(actor = "panel", note = "") {
  const state = readOperativaState();
  const now = nowIso();
  const pending = (state.reviewItems || []).filter((item) => item.status === "pendiente");

  pending.forEach((review) => {
    review.status = "revisado";
    review.resolvedAt = now;
    review.resolvedBy = actor;
    review.resolutionNote = note || "Limpieza manual del tablero";
    review.updatedAt = now;
  });

  if (pending.length) {
    pushReviewAudit(state, {
      actor,
      action: "review:resolve_all",
      entityId: "all_pending",
      after: { resolvedCount: pending.length },
    });
  }

  return {
    resolvedCount: pending.length,
    snapshot: buildCleanOperativaSnapshot(writeOperativaState(state)),
  };
}

function safeTokenEquals(left, right) {
  const leftBuffer = Buffer.from(String(left || ""));
  const rightBuffer = Buffer.from(String(right || ""));

  if (!leftBuffer.length || leftBuffer.length !== rightBuffer.length) {
    return false;
  }

  return crypto.timingSafeEqual(leftBuffer, rightBuffer);
}

function parseCookies(cookieHeader) {
  return String(cookieHeader || "")
    .split(";")
    .map((item) => item.trim())
    .filter(Boolean)
    .reduce((acc, item) => {
      const index = item.indexOf("=");
      if (index === -1) {
        return acc;
      }
      acc[item.slice(0, index)] = item.slice(index + 1);
      return acc;
    }, {});
}

function getAgentToken(req) {
  const authHeader = String(req.headers.authorization || "");
  if (authHeader.toLowerCase().startsWith("bearer ")) {
    return authHeader.slice(7).trim();
  }
  return String(req.headers["x-operativa-agent-token"] || "").trim();
}

function getExpectedAgentToken() {
  return String(process.env.OPERATIVA_AGENT_KEY || process.env.OPERATIVA_AGENT_TOKEN || "").trim();
}

function isAgentTokenAllowed(req) {
  const expectedToken = getExpectedAgentToken();
  return Boolean(expectedToken && safeTokenEquals(getAgentToken(req), expectedToken));
}

function isSessionAllowed(req) {
  return hasAdminPassword() && isAuthenticated(req);
}

function getPresentedSessionToken(req, requestUrl) {
  return String(req.headers[SESSION_HEADER] || requestUrl.searchParams.get("sessionToken") || "").trim();
}

function isBootSessionAllowed(req, requestUrl) {
  return safeTokenEquals(getPresentedSessionToken(req, requestUrl), BOOT_SESSION_TOKEN);
}

function isPanelSessionAllowed(req, requestUrl) {
  return isSessionAllowed(req) && isBootSessionAllowed(req, requestUrl);
}

function normalizeOrigin(value) {
  if (!value) {
    return "";
  }
  try {
    const url = new URL(value);
    return `${url.protocol}//${url.host}`;
  } catch (error) {
    return "";
  }
}

function getAllowedOrigins(req) {
  const host = String(req.headers.host || "").trim();
  const forwardedProto = String(req.headers["x-forwarded-proto"] || "").toLowerCase();
  const scheme = forwardedProto === "https" ? "https" : "http";
  const origins = new Set();
  if (host) {
    origins.add(`${scheme}://${host}`);
    origins.add(`http://${host}`);
    origins.add(`https://${host}`);
  }
  for (const value of [process.env.PUBLIC_URL, process.env.ARIADGSM_CLOUD_URL, "https://ariadgsm.com"]) {
    const normalized = normalizeOrigin(value);
    if (normalized) {
      origins.add(normalized);
    }
  }
  return origins;
}

function isOriginAllowed(req) {
  const origin = String(req.headers.origin || "").trim();
  if (!origin) {
    return true;
  }
  const normalized = normalizeOrigin(origin);
  return Boolean(normalized && getAllowedOrigins(req).has(normalized));
}

function isStateChanging(req) {
  return ["POST", "PUT", "PATCH", "DELETE"].includes(req.method);
}

function requiresPanelCsrf(req, requestUrl) {
  if (!isStateChanging(req)) {
    return false;
  }
  if (requestUrl.pathname === "/api/operativa-v2/cloud/sync") {
    return false;
  }
  if (requestUrl.pathname === "/api/operativa-v2/events") {
    return false;
  }
  return (
    requestUrl.pathname === "/api/operativa-v2/cloud/backups" ||
    requestUrl.pathname.startsWith("/api/operativa-v2/reviews")
  );
}

function isCsrfAllowed(req) {
  const cookies = parseCookies(req.headers.cookie);
  const cookieToken = cookies.ariadgsm_csrf || "";
  const headerToken = String(req.headers[CSRF_HEADER] || "").trim();
  return (
    safeTokenEquals(cookieToken, CSRF_TOKEN) &&
    safeTokenEquals(headerToken, CSRF_TOKEN) &&
    safeTokenEquals(cookieToken, headerToken)
  );
}

function signatureForBody(rawBody, secret) {
  return `sha256=${crypto.createHmac("sha256", secret).update(rawBody).digest("hex")}`;
}

function verifyAriadGsmSignature(rawBody, signatureHeader, secret) {
  const signature = String(signatureHeader || "").trim();
  if (!secret || !signature.startsWith("sha256=")) {
    return false;
  }
  return safeTokenEquals(signature, signatureForBody(rawBody, secret));
}

function consumeCloudSyncRate(agentToken) {
  const now = Date.now();
  const key = crypto.createHash("sha256").update(String(agentToken || "anonymous")).digest("hex");
  const bucket = cloudRateBuckets.get(key);
  if (!bucket || now >= bucket.resetAt) {
    cloudRateBuckets.set(key, { count: 1, resetAt: now + CLOUD_RATE_WINDOW_MS });
    return { allowed: true, remaining: CLOUD_RATE_LIMIT_PER_MINUTE - 1, resetAt: now + CLOUD_RATE_WINDOW_MS };
  }
  if (bucket.count >= CLOUD_RATE_LIMIT_PER_MINUTE) {
    return { allowed: false, remaining: 0, resetAt: bucket.resetAt };
  }
  bucket.count += 1;
  return { allowed: true, remaining: CLOUD_RATE_LIMIT_PER_MINUTE - bucket.count, resetAt: bucket.resetAt };
}

function appendRejectedCloudSyncAudit(req, rawBody, verdict, reason) {
  appendCloudSyncAudit({
    batchId: "",
    agentId: getAgentToken(req) ? "desktop_agent" : "unknown",
    payloadHash: crypto.createHash("sha256").update(String(rawBody || "")).digest("hex"),
    verdict,
    reason,
  });
}

function protectOperativaPage(req, res, requestUrl) {
  if (requestUrl.pathname !== "/operativa-v2.html") {
    return false;
  }

  if (isSessionAllowed(req)) {
    return false;
  }

  res.writeHead(302, { Location: "/login.html" });
  res.end();
  return true;
}

function parsePaginationOptions(requestUrl) {
  const pageSize = Math.max(1, Math.min(200, Number(requestUrl.searchParams.get("pageSize") || 80)));
  return {
    pageSize,
    cursors: {
      conversations: requestUrl.searchParams.get("conversationsCursor") || "",
      messages: requestUrl.searchParams.get("messagesCursor") || "",
      signals: requestUrl.searchParams.get("signalsCursor") || "",
      learnings: requestUrl.searchParams.get("learningsCursor") || "",
    },
  };
}

function buildSnapshotForRequest(requestUrl, stateOverride = null) {
  return buildCleanOperativaSnapshot(stateOverride, parsePaginationOptions(requestUrl));
}

function writeSse(res, eventName, payload) {
  res.write(`event: ${eventName}\n`);
  res.write(`data: ${JSON.stringify(payload)}\n\n`);
}

function handleRuntimeStream(req, res, requestUrl) {
  if (req.method !== "GET" || requestUrl.pathname !== "/api/operativa-v2/runtime/stream") {
    return false;
  }

  if (!isPanelSessionAllowed(req, requestUrl)) {
    sendJson(res, 401, { error: "No autorizado" });
    return true;
  }

  applySecurityHeaders(res);
  res.writeHead(200, {
    "Content-Type": "text/event-stream; charset=utf-8",
    "Cache-Control": "no-store",
    Connection: "keep-alive",
  });

  const send = () => {
    writeSse(res, "runtime", buildSnapshotForRequest(requestUrl));
  };
  send();
  const interval = setInterval(send, 10000);
  req.on("close", () => clearInterval(interval));
  return true;
}

async function handleCloudSync(req, res, requestUrl) {
  if (req.method !== "POST" || requestUrl.pathname !== "/api/operativa-v2/cloud/sync") {
    return false;
  }

  let parsed;
  try {
    parsed = await parseBody(req, { maxBytes: 5 * 1024 * 1024, returnRaw: true });
  } catch (error) {
    appendRejectedCloudSyncAudit(req, "", "rejected", "invalid_json");
    sendJson(res, 400, { error: error.message || "Payload invalido" });
    return true;
  }

  const body = parsed.body;
  const rawBody = parsed.rawBody;
  const expectedToken = getExpectedAgentToken();
  const presentedToken = getAgentToken(req);
  if (!expectedToken || !presentedToken || !safeTokenEquals(presentedToken, expectedToken)) {
    appendRejectedCloudSyncAudit(req, rawBody, "rejected", "missing_or_invalid_agent_token");
    sendJson(res, 401, { error: "No autorizado" });
    return true;
  }

  const rate = consumeCloudSyncRate(presentedToken);
  res.setHeader("X-RateLimit-Limit", String(CLOUD_RATE_LIMIT_PER_MINUTE));
  res.setHeader("X-RateLimit-Remaining", String(rate.remaining));
  if (!rate.allowed) {
    appendRejectedCloudSyncAudit(req, rawBody, "rejected", "rate_limited");
    sendJson(res, 429, { error: "Rate limit excedido" });
    return true;
  }

  if (!verifyAriadGsmSignature(rawBody, req.headers[SIGNATURE_HEADER], expectedToken)) {
    appendRejectedCloudSyncAudit(req, rawBody, "rejected", "missing_or_invalid_signature");
    sendJson(res, 401, { error: "Firma invalida" });
    return true;
  }

  try {
    const actor = body.actor || "desktop_agent";
    const idempotencyKey = body.idempotencyKey || req.headers["idempotency-key"] || body.id || "";
    if (idempotencyKey) {
      const currentState = readOperativaState();
      const syncBatches = Array.isArray(currentState.syncBatches) ? currentState.syncBatches : [];
      const existing = syncBatches.find(
        (item) => item.idempotencyKey === idempotencyKey || item.id === idempotencyKey
      );
      if (existing) {
        const duplicatePayload = recordCloudSync({ ...body, idempotencyKey, id: body.id || idempotencyKey }, actor);
        sendJson(res, 200, {
          ok: true,
          duplicate: true,
          batch: duplicatePayload.batch,
          eventErrors: [],
          snapshot: buildSnapshotForRequest(requestUrl, duplicatePayload.snapshot),
        });
        return true;
      }
    }
    const events = Array.isArray(body.events) ? body.events.slice(0, 250) : [];
    let eventsIngested = 0;
    let eventsRejected = 0;
    const eventErrors = [];
    for (const event of events) {
      try {
        ingestOperativaEvent(event, actor);
        eventsIngested++;
      } catch (error) {
        eventsRejected++;
        eventErrors.push({
          type: event?.type || event?.eventType || "unknown",
          error: error.message || "Evento rechazado",
        });
      }
    }
    const syncStatus = body.status === "ok" && eventsRejected > 0 ? "attention" : body.status;
    const payload = recordCloudSync(
      {
        ...body,
        idempotencyKey,
        status: syncStatus,
        eventsIngested,
        eventsRejected,
        eventErrors,
        error: eventsRejected > 0 ? `${eventsRejected} eventos rechazados por el panel cloud.` : body.error,
      },
      actor
    );
    sendJson(res, 201, {
      ok: true,
      duplicate: Boolean(payload.duplicate),
      batch: payload.batch,
      eventErrors,
      snapshot: buildSnapshotForRequest(requestUrl, payload.snapshot),
    });
  } catch (error) {
    appendRejectedCloudSyncAudit(req, rawBody, "rejected", error.message || "cloud_sync_error");
    sendJson(res, 400, { error: error.message || "No pude registrar la sincronizacion cloud" });
  }
  return true;
}

async function handleOperativaApi(req, res, requestUrl) {
  const isOperativaRoute =
    requestUrl.pathname === "/api/operativa-v2" ||
    requestUrl.pathname === "/api/operativa-v2/runtime/stream" ||
    requestUrl.pathname === "/api/operativa-v2/events" ||
    requestUrl.pathname.startsWith("/api/operativa-v2/cloud") ||
    requestUrl.pathname.startsWith("/api/operativa-v2/reviews");

  if (!isOperativaRoute) {
    return false;
  }

  if (!isOriginAllowed(req)) {
    sendJson(res, 403, { error: "Origin no permitido" });
    return true;
  }

  if (await handleCloudSync(req, res, requestUrl)) {
    return true;
  }

  const panelAllowed = isPanelSessionAllowed(req, requestUrl);
  const agentAllowed = isAgentTokenAllowed(req);
  if (!panelAllowed && !agentAllowed) {
    sendJson(res, 401, { error: "No autorizado" });
    return true;
  }

  if (panelAllowed && requiresPanelCsrf(req, requestUrl) && !isCsrfAllowed(req)) {
    sendJson(res, 403, { error: "CSRF invalido" });
    return true;
  }

  if (req.method === "GET" && requestUrl.pathname === "/api/operativa-v2") {
    sendJson(res, 200, buildSnapshotForRequest(requestUrl));
    return true;
  }

  if (req.method === "GET" && requestUrl.pathname === "/api/operativa-v2/cloud") {
    const snapshot = buildSnapshotForRequest(requestUrl);
    sendJson(res, 200, {
      cloudStatus: snapshot.cloudStatus,
      reportSummary: snapshot.reportSummary,
      syncBatches: snapshot.syncBatches,
    });
    return true;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/operativa-v2/events") {
    try {
      const body = await parseBody(req);
      const payload = ingestOperativaEvent(body, body.actor || "visual_agent");
      sendJson(res, 201, {
        ok: true,
        event: payload.event,
        entity: payload.entity,
        snapshot: buildSnapshotForRequest(requestUrl, payload.snapshot),
      });
    } catch (error) {
      sendJson(res, 400, { error: error.message || "No pude registrar el evento operativo" });
    }
    return true;
  }

  if (req.method === "GET" && requestUrl.pathname === "/api/operativa-v2/cloud/report") {
    sendJson(res, 200, buildCloudReport());
    return true;
  }

  if (req.method === "GET" && requestUrl.pathname === "/api/operativa-v2/cloud/backups") {
    sendJson(res, 200, { backups: listOperativaBackups(30) });
    return true;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/operativa-v2/cloud/backups") {
    try {
      const body = await parseBody(req);
      const backup = createOperativaBackupSnapshot(body.actor || "panel", body.note || "");
      sendJson(res, 201, {
        ok: true,
        backup,
        backups: listOperativaBackups(30),
        snapshot: buildSnapshotForRequest(requestUrl),
      });
    } catch (error) {
      sendJson(res, 400, { error: error.message || "No pude crear el respaldo operativo" });
    }
    return true;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/operativa-v2/reviews/clear") {
    try {
      const body = await parseBody(req);
      const result = resolveAllReviewItems(body.actor || "panel", body.note || "");
      sendJson(res, 200, { ok: true, ...result });
    } catch (error) {
      sendJson(res, 400, { error: error.message || "No pude limpiar la bandeja" });
    }
    return true;
  }

  const reviewMatch = requestUrl.pathname.match(/^\/api\/operativa-v2\/reviews\/([^/]+)\/resolve$/);
  if (req.method === "POST" && reviewMatch) {
    try {
      const body = await parseBody(req);
      const result = resolveReviewItem(decodeURIComponent(reviewMatch[1]), body.actor || "panel", body.note || "");
      sendJson(res, 200, { ok: true, ...result });
    } catch (error) {
      sendJson(res, 400, { error: error.message || "No pude marcar la duda como revisada" });
    }
    return true;
  }

  sendJson(res, 405, { error: "Metodo no permitido" });
  return true;
}

http.createServer = function createWrappedServer(listener) {
  return originalCreateServer(async (req, res) => {
    const requestUrl = new URL(req.url, `http://${req.headers.host || "localhost"}`);
    applySecurityHeaders(res);

    if (protectOperativaPage(req, res, requestUrl)) {
      return;
    }

    if (handleRuntimeStream(req, res, requestUrl)) {
      return;
    }

    if (await handleOperativaApi(req, res, requestUrl)) {
      return;
    }

    listener(req, res);
  });
};

function startWrappedServer() {
  require("./server");
}

if (require.main === module) {
  startWrappedServer();
}

module.exports = {
  WEB_HARDENING_VERSION,
  STRICT_CSP,
  applySecurityHeaders,
  buildCsrfCookie,
  consumeCloudSyncRate,
  getAgentToken,
  getExpectedAgentToken,
  injectPanelTokens,
  isOriginAllowed,
  safeTokenEquals,
  signatureForBody,
  startWrappedServer,
  verifyAriadGsmSignature,
};
