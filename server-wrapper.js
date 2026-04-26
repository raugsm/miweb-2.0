const http = require("http");
const crypto = require("crypto");
const { URL } = require("url");
const { hasAdminPassword, isAuthenticated } = require("./admin-auth");
const {
  buildOperativaSnapshot,
  ingestOperativaEvent,
  resolveAllReviewItems,
  resolveReviewItem,
} = require("./operativa-store");

const originalCreateServer = http.createServer.bind(http);

function sendJson(res, statusCode, payload) {
  res.writeHead(statusCode, {
    "Content-Type": "application/json; charset=utf-8",
    "X-Content-Type-Options": "nosniff",
    "X-Frame-Options": "DENY",
    "Referrer-Policy": "same-origin",
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
        resolve(body ? JSON.parse(body) : {});
      } catch (error) {
        reject(error);
      }
    });
  });
}

function safeTokenEquals(left, right) {
  const leftBuffer = Buffer.from(String(left || ""));
  const rightBuffer = Buffer.from(String(right || ""));

  if (!leftBuffer.length || leftBuffer.length !== rightBuffer.length) {
    return false;
  }

  return crypto.timingSafeEqual(leftBuffer, rightBuffer);
}

function getAgentToken(req) {
  const authHeader = String(req.headers.authorization || "");
  if (authHeader.toLowerCase().startsWith("bearer ")) {
    return authHeader.slice(7).trim();
  }
  return String(req.headers["x-operativa-agent-token"] || "").trim();
}

function isAgentTokenAllowed(req) {
  const expectedToken = String(
    process.env.OPERATIVA_AGENT_KEY || process.env.OPERATIVA_AGENT_TOKEN || ""
  ).trim();
  return Boolean(expectedToken && safeTokenEquals(getAgentToken(req), expectedToken));
}

function isSessionAllowed(req) {
  return hasAdminPassword() && isAuthenticated(req);
}

function isApiAllowed(req) {
  return isSessionAllowed(req) || isAgentTokenAllowed(req);
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

async function handleOperativaApi(req, res, requestUrl) {
  const isOperativaRoute =
    requestUrl.pathname === "/api/operativa-v2" ||
    requestUrl.pathname === "/api/operativa-v2/events" ||
    requestUrl.pathname.startsWith("/api/operativa-v2/reviews");

  if (!isOperativaRoute) {
    return false;
  }

  if (!isApiAllowed(req)) {
    sendJson(res, 401, { error: "No autorizado" });
    return true;
  }

  if (req.method === "GET" && requestUrl.pathname === "/api/operativa-v2") {
    sendJson(res, 200, buildOperativaSnapshot());
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
        snapshot: payload.snapshot,
      });
    } catch (error) {
      sendJson(res, 400, { error: error.message || "No pude registrar el evento operativo" });
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

    if (protectOperativaPage(req, res, requestUrl)) {
      return;
    }

    if (await handleOperativaApi(req, res, requestUrl)) {
      return;
    }

    listener(req, res);
  });
};

require("./server");
