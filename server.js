const http = require("http");
const fs = require("fs");
const path = require("path");
const { URL } = require("url");
const AdmZip = require("adm-zip");
const { nowTime, runCaseDiagnostics } = require("./agent");
const {
  classifyCase,
  generateStageReply,
  generateSuggestedReply,
  getAiModeLabel,
  isAiConfigured,
} = require("./ai");
const { readStyleProfile, updateStyleProfile } = require("./style-profile");
const {
  DATA_DIR,
  DB_FILE,
  getCaseById,
  getCases,
  getNextCaseId,
  getProcedures,
  getTrainingConversations,
  getTrainingInsights,
  getTrainingSummary,
  getPriceOffers,
  getTelegramMessages,
  getTelegramSources,
  getTelegramSummary,
  saveTrainingConversation,
  savePriceOffer,
  saveTelegramSource,
  upsertCase,
} = require("./db");
const {
  buildClearSessionCookie,
  buildSessionCookie,
  createSessionToken,
  createUser,
  getAuthenticatedUser,
  hasAdminPassword,
  isAuthenticated,
  listUsers,
  normalizeUsername,
  setAdminPassword,
  updateUserPassword,
  verifyPassword,
} = require("./admin-auth");
const {
  getCustomerNotificationModeLabel,
  getNotificationModeLabel,
  isNotificationConfigured,
  notifyEvent,
  notifyCustomer,
  readNotificationLog,
} = require("./notifications");
const { analyzeTrainingConversation, parseWhatsAppChat } = require("./training-import");
const {
  discoverTelegramChats,
  getTelegramUiConfig,
  readTelegramRuntimeStatus,
  refreshTelegramStatus,
  startTelegramAuth,
  submitTelegramCode,
  submitTelegramPassword,
  syncTelegramSources,
  updateTelegramConfig,
  updateTelegramSource,
} = require("./telegram");
const {
  buildPricingMovements,
  buildPricingSummary,
  inferOfferDetails,
  parsePriceText,
  readPricingConfig,
  updatePricingConfig,
} = require("./pricing");

const PORT = process.env.PORT || 3000;
const PUBLIC_DIR = path.join(__dirname, "public");
const TRAINING_UPLOAD_DIR = path.join(DATA_DIR, "training-imports");

const MIME_TYPES = {
  ".css": "text/css; charset=utf-8",
  ".html": "text/html; charset=utf-8",
  ".js": "application/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".svg": "image/svg+xml",
};

function sendJson(res, statusCode, payload) {
  res.writeHead(statusCode, { "Content-Type": MIME_TYPES[".json"] });
  res.end(JSON.stringify(payload));
}

function sendJsonWithHeaders(res, statusCode, payload, headers = {}) {
  res.writeHead(statusCode, { "Content-Type": MIME_TYPES[".json"] , ...headers });
  res.end(JSON.stringify(payload));
}

function sendSecurityHeaders(res) {
  res.setHeader("X-Frame-Options", "DENY");
  res.setHeader("X-Content-Type-Options", "nosniff");
  res.setHeader("Referrer-Policy", "same-origin");
}

function maybeRedirectToHttps(req, res, requestUrl) {
  if (String(process.env.FORCE_HTTPS || "").toLowerCase() !== "true") {
    return false;
  }

  const forwardedProto = String(req.headers["x-forwarded-proto"] || "").toLowerCase();
  if (forwardedProto === "https") {
    return false;
  }

  const host = req.headers.host;
  res.writeHead(301, { Location: `https://${host}${requestUrl.pathname}${requestUrl.search}` });
  res.end();
  return true;
}

function serveStaticFile(filePath, res) {
  const ext = path.extname(filePath);
  const contentType = MIME_TYPES[ext] || "application/octet-stream";

  fs.readFile(filePath, (error, content) => {
    if (error) {
      sendJson(res, 404, { error: "Archivo no encontrado" });
      return;
    }

    res.writeHead(200, { "Content-Type": contentType });
    res.end(content);
  });
}

function getSuggestedProcedure(category) {
  const map = {
    adb: "proc-adb",
    fastboot: "proc-fastboot",
    conexion: "proc-win-detect",
  };
  return map[category] || "proc-win-detect";
}

function ensureTrainingUploadDir() {
  if (!fs.existsSync(TRAINING_UPLOAD_DIR)) {
    fs.mkdirSync(TRAINING_UPLOAD_DIR, { recursive: true });
  }
}

function extractChatTextFromUpload(fileName, contentBase64) {
  ensureTrainingUploadDir();
  const safeName = path.basename(String(fileName || "chat.txt"));
  const extension = path.extname(safeName).toLowerCase();
  const fileBuffer = Buffer.from(String(contentBase64 || ""), "base64");
  const maxUploadBytes = 2 * 1024 * 1024;

  if (!fileBuffer.length) {
    throw new Error("El archivo llego vacio");
  }
  if (fileBuffer.length > maxUploadBytes) {
    throw new Error("El archivo supera el limite de 2 MB");
  }

  if (extension === ".txt") {
    return fileBuffer.toString("utf8");
  }

  if (extension !== ".zip") {
    throw new Error("Por ahora solo puedo importar archivos .txt o .zip");
  }

  try {
    const zip = new AdmZip(fileBuffer);
    const txtEntry = zip
      .getEntries()
      .find((entry) => !entry.isDirectory && path.extname(entry.entryName).toLowerCase() === ".txt");

    if (!txtEntry) {
      throw new Error("No encontre un .txt dentro del .zip");
    }

    return zip.readAsText(txtEntry, "utf8");
  } catch (error) {
    throw new Error("No pude abrir el archivo .zip. Prueba con el .txt directo o con otro .zip.");
  }
}

function sanitizeImportedChatText(chatText) {
  const maxLength = 250000;
  const safeText = String(chatText || "").replace(/\u0000/g, "").trim();
  if (!safeText) {
    throw new Error("No encontre texto util en el archivo");
  }
  if (safeText.length > maxLength) {
    throw new Error("El chat importado es demasiado grande para esta primera version");
  }
  return safeText;
}

function createBackupSnapshot() {
  const backupsDir = path.join(DATA_DIR, "backups");
  if (!fs.existsSync(backupsDir)) {
    fs.mkdirSync(backupsDir, { recursive: true });
  }

  const stamp = new Date().toISOString().replace(/[:.]/g, "-");
  const backupFile = path.join(backupsDir, `app-${stamp}.db`);
  fs.copyFileSync(DB_FILE, backupFile);
  return backupFile;
}

function buildCustomerSubject(caseItem, title) {
  return `[${caseItem.id}] ${title}`;
}

function buildCustomerBody(caseItem, title, message) {
  return [
    `Hola ${caseItem.clientName},`,
    "",
    `${title}.`,
    message,
    "",
    `Ticket: ${caseItem.id}`,
    `Estado: ${caseItem.status}`,
    `Equipo: ${caseItem.deviceModel}`,
    caseItem.closeReason ? `Motivo de cierre: ${caseItem.closeReason}` : "",
    "",
    "Puedes consultar tu ticket desde el enlace de seguimiento si lo compartimos contigo.",
  ]
    .filter(Boolean)
    .join("\n");
}

function getNowIso() {
  return new Date().toISOString();
}

function formatDuration(fromIso, toIso = getNowIso()) {
  if (!fromIso) {
    return "Sin dato";
  }

  const diffMs = Math.max(0, new Date(toIso).getTime() - new Date(fromIso).getTime());
  const totalMinutes = Math.round(diffMs / 60000);
  const days = Math.floor(totalMinutes / 1440);
  const hours = Math.floor((totalMinutes % 1440) / 60);
  const minutes = totalMinutes % 60;
  const parts = [];

  if (days) parts.push(`${days}d`);
  if (hours) parts.push(`${hours}h`);
  if (minutes || !parts.length) parts.push(`${minutes}m`);

  return parts.join(" ");
}

function getStageKeyFromStatus(status) {
  if (status === "Nuevo") return "received";
  if (status === "En proceso") return "in_progress";
  if (status === "Escalado") return "escalated";
  if (status === "Cerrado") return "closed";
  return "pending_customer";
}

function buildCaseTimings(caseItem) {
  return {
    createdAt: caseItem.createdAt,
    closedAt: caseItem.closedAt,
    age: formatDuration(caseItem.createdAt),
    resolution:
      caseItem.closedAt && caseItem.createdAt
        ? formatDuration(caseItem.createdAt, caseItem.closedAt)
        : "Abierto",
  };
}

function formatMinutes(minutes) {
  const safeMinutes = Math.max(0, Number(minutes || 0));
  const days = Math.floor(safeMinutes / 1440);
  const hours = Math.floor((safeMinutes % 1440) / 60);
  const remainingMinutes = safeMinutes % 60;
  const parts = [];

  if (days) parts.push(`${days}d`);
  if (hours) parts.push(`${hours}h`);
  if (remainingMinutes || !parts.length) parts.push(`${remainingMinutes}m`);

  return parts.join(" ");
}

function buildAssigneeMetrics(cases) {
  const metricsMap = new Map();

  for (const item of cases) {
    const key = item.assignee || "Sin responsable";
    if (!metricsMap.has(key)) {
      metricsMap.set(key, {
        assignee: key,
        total: 0,
        open: 0,
        escalated: 0,
        closed: 0,
        avgResolutionMinutes: 0,
        avgResolutionLabel: "Sin cierres",
      });
    }

    const target = metricsMap.get(key);
    target.total += 1;
    if (item.status !== "Cerrado") {
      target.open += 1;
    }
    if (item.status === "Escalado") {
      target.escalated += 1;
    }
    if (item.status === "Cerrado") {
      target.closed += 1;
      if (item.createdAt && item.closedAt) {
        target.avgResolutionMinutes += Math.max(
          0,
          Math.round((new Date(item.closedAt).getTime() - new Date(item.createdAt).getTime()) / 60000)
        );
      }
    }
  }

  return [...metricsMap.values()].map((item) => {
    const avgMinutes = item.closed ? Math.round(item.avgResolutionMinutes / item.closed) : 0;
    return {
      assignee: item.assignee,
      total: item.total,
      open: item.open,
      escalated: item.escalated,
      closed: item.closed,
      avgResolutionMinutes: avgMinutes,
      avgResolutionLabel: item.closed ? formatMinutes(avgMinutes) : "Sin cierres",
    };
  });
}

function isInboundEmailConfigured() {
  return Boolean(process.env.INBOUND_EMAIL_TOKEN);
}

function getInboundChannelLabel() {
  return isInboundEmailConfigured() ? "Correo de entrada listo" : "Correo de entrada pendiente";
}

function isValidInboundToken(req) {
  const token = req.headers["x-inbound-token"];
  return Boolean(token && token === process.env.INBOUND_EMAIL_TOKEN);
}

async function createCase(body) {
  const classification = await classifyCase({
    clientName: body.clientName,
    deviceModel: body.deviceModel,
    summary: body.summary || "",
  });
  const nextId = getNextCaseId();
  const procedureId = getSuggestedProcedure(classification.category);

  const newCase = {
    id: nextId,
    clientName: body.clientName || "Cliente sin nombre",
    contact: body.contact || "Sin contacto",
    category: classification.category,
    priority: classification.priority,
    status: "Nuevo",
    deviceModel: body.deviceModel || "Modelo no indicado",
    summary: classification.summary || body.summary || "Sin resumen",
    procedureId,
    currentStep: 1,
    lastUpdate: `Caso creado y listo para validacion inicial. Fuente de clasificacion: ${classification.source}.`,
    aiMode: classification.source,
    createdAt: getNowIso(),
    closedAt: null,
    closeReason: null,
    messages: [
      {
        sender: "cliente",
        text: body.summary || "Sin descripcion del problema.",
        time: nowTime(),
      },
      {
        sender: "ia",
        text: `${generateStageReply({ stage: "received" })} ${classification.response}`.trim(),
        time: nowTime(),
      },
    ],
    logs: [],
  };

  if (classification.error) {
    newCase.logs.unshift({
      type: "ai",
      text: "La clasificacion por OpenAI fallo y se uso el modo local.",
      time: nowTime(),
      details: classification.error,
    });
  }

  upsertCase(newCase);

  await notifyCustomer(
    newCase,
    buildCustomerSubject(newCase, "Caso recibido"),
    buildCustomerBody(
      newCase,
      "Recibimos tu solicitud",
      "Ya abrimos tu caso y estamos preparando la validacion inicial."
    )
  );

  return { created: newCase, procedures: getProcedures() };
}

async function createPublicCase(body) {
  const payload = await createCase({
    clientName: body.clientName,
    contact: body.contact || body.phone || "Formulario web publico",
    deviceModel: body.deviceModel,
    summary: body.summary,
  });

  await notifyEvent({
    caseId: payload.created.id,
    title: "Nuevo caso recibido",
    clientName: payload.created.clientName,
    status: payload.created.status,
    deviceModel: payload.created.deviceModel,
    message: `Entro un nuevo caso publico. Resumen: ${payload.created.summary}`,
  });

  return {
    ticketId: payload.created.id,
    message: payload.created.messages[payload.created.messages.length - 1]?.text,
    status: payload.created.status,
  };
}

function getPublicTicket(caseId) {
  const targetCase = getCaseById(caseId);

  if (!targetCase) {
    return null;
  }

  const procedure = getProcedures().find((item) => item.id === targetCase.procedureId);

  return {
    ticketId: targetCase.id,
    clientName: targetCase.clientName,
    status: targetCase.status,
    deviceModel: targetCase.deviceModel,
    summary: targetCase.summary,
    category: targetCase.category,
    currentStep: targetCase.currentStep,
    procedureName: procedure?.name || "Sin procedimiento",
    totalSteps: procedure?.steps?.length || 0,
    lastUpdate: targetCase.lastUpdate,
    timings: buildCaseTimings(targetCase),
    latestMessage: targetCase.messages[targetCase.messages.length - 1]?.text || "",
    timeline: targetCase.messages.slice(-5),
  };
}

async function createInboundEmailCase(body) {
  const payload = await createCase({
    clientName: body.clientName || body.fromName || body.from || "Cliente por correo",
    contact: body.from || body.contact || "Sin correo",
    deviceModel: body.deviceModel || body.subject || "Equipo no indicado",
    summary: body.text || body.summary || body.subject || "Correo sin contenido",
  });

  await notifyEvent({
    caseId: payload.created.id,
    title: "Nuevo caso por correo",
    clientName: payload.created.clientName,
    status: payload.created.status,
    deviceModel: payload.created.deviceModel,
    message: `Ingreso por canal correo. Asunto: ${body.subject || "Sin asunto"}`,
  });

  return payload;
}

async function runAgent(caseId) {
  const targetCase = getCaseById(caseId);

  if (!targetCase) {
    return null;
  }

  const previousStatus = targetCase.status;
  const procedure = getProcedures().find((item) => item.id === targetCase.procedureId);
  const currentAction = procedure?.steps[targetCase.currentStep - 1] || "Validacion general";
  const diagnostic = await runCaseDiagnostics(targetCase.category);
  const now = nowTime();

  targetCase.logs.unshift({
    type: "agent",
    text: `${diagnostic.action} -> ${diagnostic.summary}`,
    time: now,
    details: diagnostic.details,
  });

  if (diagnostic.status === "ok") {
    targetCase.status = "En proceso";
    targetCase.closedAt = null;
    targetCase.currentStep = Math.min(targetCase.currentStep + 1, procedure.steps.length);
    targetCase.lastUpdate = `Agente completo: ${currentAction}. ${diagnostic.summary}`;
    targetCase.messages.push({
      sender: "ia",
      text: `${generateStageReply({ stage: "in_progress" })} ${diagnostic.summary}`.trim(),
      time: now,
    });
  } else {
    targetCase.status = "Escalado";
    targetCase.closedAt = null;
    targetCase.lastUpdate = `Agente pausado: ${currentAction}. ${diagnostic.summary}`;
    targetCase.messages.push({
      sender: "ia",
      text: `${generateStageReply({ stage: "escalated" })} ${diagnostic.summary}`.trim(),
      time: now,
    });
  }

  upsertCase(targetCase);

  if (targetCase.status !== previousStatus) {
    await notifyEvent({
      caseId: targetCase.id,
      title: `Cambio de estado: ${targetCase.status}`,
      clientName: targetCase.clientName,
      status: targetCase.status,
      deviceModel: targetCase.deviceModel,
      message: targetCase.lastUpdate,
    });

    await notifyCustomer(
      targetCase,
      buildCustomerSubject(targetCase, `Estado actualizado a ${targetCase.status}`),
      buildCustomerBody(targetCase, "Actualizamos tu caso", targetCase.lastUpdate)
    );
  }

  return { updated: targetCase, procedure, diagnostic };
}

async function createReplySuggestion(caseId) {
  const targetCase = getCaseById(caseId);

  if (!targetCase) {
    return null;
  }

  const procedure = getProcedures().find((item) => item.id === targetCase.procedureId);
  const suggestion = await generateSuggestedReply({
    clientName: targetCase.clientName,
    deviceModel: targetCase.deviceModel,
    summary: targetCase.summary,
    status: targetCase.status,
    lastUpdate: targetCase.lastUpdate,
    procedure: procedure?.name || "Sin procedimiento",
    lastMessages: targetCase.messages.slice(-4),
  });

  const now = nowTime();
  targetCase.messages.push({
    sender: "ia",
    text: suggestion.reply,
    time: now,
  });
  targetCase.lastUpdate = `Respuesta sugerida generada con ${suggestion.source}.`;
  targetCase.logs.unshift({
    type: "ai",
    text: `reply_suggestion -> ${suggestion.next_step}`,
    time: now,
    details: suggestion.error || `Modo: ${suggestion.source}`,
  });
  targetCase.aiMode = suggestion.source;

  upsertCase(targetCase);
  return { updated: targetCase, suggestion };
}

async function updateCaseStatus(caseId, nextStatus, closeReason) {
  const targetCase = getCaseById(caseId);

  if (!targetCase) {
    return null;
  }

  const allowedStatuses = new Set(["Nuevo", "En proceso", "Escalado", "Cerrado"]);
  if (!allowedStatuses.has(nextStatus)) {
    throw new Error("Estado no valido");
  }

  const previousStatus = targetCase.status;
  const cleanReason = String(closeReason || "").trim();
  targetCase.status = nextStatus;
  targetCase.closeReason = nextStatus === "Cerrado" ? cleanReason || "Sin motivo indicado" : null;
  targetCase.closedAt = nextStatus === "Cerrado" ? getNowIso() : null;
  targetCase.lastUpdate =
    nextStatus === "Cerrado"
      ? `Caso cerrado manualmente. Motivo: ${targetCase.closeReason}.`
      : `Estado actualizado manualmente a ${nextStatus}.`;
  targetCase.logs.unshift({
    type: "manual",
    text:
      nextStatus === "Cerrado"
        ? `status_update -> ${nextStatus} (${targetCase.closeReason})`
        : `status_update -> ${nextStatus}`,
    time: nowTime(),
    details: "Cambio manual desde el panel",
  });
  targetCase.messages.push({
    sender: "ia",
    text: generateStageReply({
      stage: getStageKeyFromStatus(nextStatus),
      closeReason: targetCase.closeReason,
    }),
    time: nowTime(),
  });

  upsertCase(targetCase);

  if (previousStatus !== nextStatus) {
    await notifyEvent({
      caseId: targetCase.id,
      title: `Cambio de estado: ${nextStatus}`,
      clientName: targetCase.clientName,
      status: targetCase.status,
      deviceModel: targetCase.deviceModel,
      message: targetCase.lastUpdate,
    });

    await notifyCustomer(
      targetCase,
      buildCustomerSubject(targetCase, `Estado actualizado a ${nextStatus}`),
      buildCustomerBody(targetCase, "Actualizamos tu caso", targetCase.lastUpdate)
    );
  }

  return { updated: targetCase };
}

async function assignCase(caseId, assignee) {
  const targetCase = getCaseById(caseId);

  if (!targetCase) {
    return null;
  }

  const cleanAssignee = String(assignee || "").trim();
  targetCase.assignee = cleanAssignee || null;
  targetCase.lastUpdate = cleanAssignee
    ? `Caso asignado a ${cleanAssignee}.`
    : "Caso sin responsable asignado.";
  targetCase.logs.unshift({
    type: "manual",
    text: cleanAssignee ? `assignment -> ${cleanAssignee}` : "assignment -> sin responsable",
    time: nowTime(),
    details: "Asignacion manual desde el panel",
  });

  upsertCase(targetCase);
  return { updated: targetCase };
}

function parseBody(req, options = {}) {
  return new Promise((resolve, reject) => {
    const maxBytes = options.maxBytes || 4 * 1024 * 1024;
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

const server = http.createServer(async (req, res) => {
  const requestUrl = new URL(req.url, `http://${req.headers.host}`);
  sendSecurityHeaders(res);
  if (maybeRedirectToHttps(req, res, requestUrl)) {
    return;
  }
  const protectedApiPrefixes = [
    "/api/dashboard",
    "/api/style-profile",
    "/api/cases",
    "/api/notifications-log",
    "/api/pricing",
    "/api/training",
    "/api/team",
    "/api/admin",
  ];
  const isProtectedApi = protectedApiPrefixes.some((prefix) =>
    requestUrl.pathname === prefix || requestUrl.pathname.startsWith(`${prefix}/`)
  );
  const protectedPages = ["/", "/index.html"];

  if (isProtectedApi && (!hasAdminPassword() || !isAuthenticated(req))) {
    sendJson(res, 401, { error: "No autorizado" });
    return;
  }

  if (protectedPages.includes(requestUrl.pathname) && (!hasAdminPassword() || !isAuthenticated(req))) {
    res.writeHead(302, { Location: "/login.html" });
    res.end();
    return;
  }

  if (req.method === "GET" && requestUrl.pathname === "/api/auth/status") {
    const user = getAuthenticatedUser(req);
    sendJson(res, 200, {
      configured: hasAdminPassword(),
      authenticated: Boolean(user),
      user,
    });
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/auth/setup") {
    try {
      const body = await parseBody(req);
      if (hasAdminPassword()) {
        sendJson(res, 409, { error: "La contraseÃ±a ya fue configurada" });
        return;
      }
      if (!body.password || String(body.password).length < 8) {
        sendJson(res, 400, { error: "La contraseÃ±a debe tener al menos 8 caracteres" });
        return;
      }
      setAdminPassword(
        String(body.password),
        normalizeUsername(body.username || "owner"),
        String(body.displayName || body.username || "Owner")
      );
      sendJson(res, 200, { ok: true });
    } catch (error) {
      sendJson(res, 400, { error: "No pude guardar la contraseÃ±a" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/auth/login") {
    try {
      const body = await parseBody(req);
      const user = verifyPassword(String(body.username || ""), String(body.password || ""));
      if (!user) {
        sendJson(res, 401, { error: "Credenciales invalidas" });
        return;
      }
      const token = createSessionToken(user);
      sendJsonWithHeaders(res, 200, { ok: true, user }, { "Set-Cookie": buildSessionCookie(token) });
    } catch (error) {
      sendJson(res, 400, { error: "No pude iniciar sesion" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/auth/logout") {
    sendJsonWithHeaders(res, 200, { ok: true }, { "Set-Cookie": buildClearSessionCookie() });
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/auth/change") {
    const sessionUser = getAuthenticatedUser(req);
    if (!sessionUser) {
      sendJson(res, 401, { error: "No autorizado" });
      return;
    }
    try {
      const body = await parseBody(req);
      if (!verifyPassword(sessionUser.username, String(body.currentPassword || ""))) {
        sendJson(res, 401, { error: "La contraseÃ±a actual no coincide" });
        return;
      }
      if (!body.newPassword || String(body.newPassword).length < 8) {
        sendJson(res, 400, { error: "La nueva contraseÃ±a debe tener al menos 8 caracteres" });
        return;
      }
      updateUserPassword(sessionUser.username, String(body.newPassword));
      sendJson(res, 200, { ok: true });
    } catch (error) {
      sendJson(res, 400, { error: "No pude cambiar la contraseÃ±a" });
    }
    return;
  }

  if (req.method === "GET" && requestUrl.pathname === "/api/team/users") {
    const sessionUser = getAuthenticatedUser(req);
    if (!sessionUser || !["owner", "admin"].includes(sessionUser.role)) {
      sendJson(res, 403, { error: "No autorizado para ver el equipo" });
      return;
    }

    sendJson(res, 200, { users: listUsers() });
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/team/users") {
    const sessionUser = getAuthenticatedUser(req);
    if (!sessionUser || !["owner", "admin"].includes(sessionUser.role)) {
      sendJson(res, 403, { error: "No autorizado para crear usuarios" });
      return;
    }

    try {
      const body = await parseBody(req);
      const user = createUser({
        username: body.username,
        displayName: body.displayName,
        password: body.password,
        role: body.role || "agent",
      });
      sendJson(res, 201, {
        ok: true,
        user: {
          username: user.username,
          displayName: user.displayName,
          role: user.role,
        },
      });
    } catch (error) {
      sendJson(res, 400, { error: error.message || "No pude crear el usuario" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/admin/backup") {
    const sessionUser = getAuthenticatedUser(req);
    if (!sessionUser || !["owner", "admin"].includes(sessionUser.role)) {
      sendJson(res, 403, { error: "No autorizado para crear respaldos" });
      return;
    }

    try {
      const backupFile = createBackupSnapshot();
      sendJson(res, 200, { ok: true, backupFile });
    } catch (error) {
      sendJson(res, 500, { error: "No pude crear el respaldo" });
    }
    return;
  }

  if (req.method === "GET" && requestUrl.pathname === "/api/dashboard") {
    const cases = getCases();
    const sessionUser = getAuthenticatedUser(req);
    const priceOffers = getPriceOffers(120);
    const pricingConfig = readPricingConfig();
    sendJson(res, 200, {
      procedures: getProcedures(),
      cases: cases.map((item) => ({
        ...item,
        timings: buildCaseTimings(item),
      })),
      meta: {
        aiConfigured: isAiConfigured(),
        aiModeLabel: getAiModeLabel(),
        styleProfile: readStyleProfile(),
        notificationsConfigured: isNotificationConfigured(),
        notificationModeLabel: getNotificationModeLabel(),
        customerNotificationModeLabel: getCustomerNotificationModeLabel(),
        inboundChannelLabel: getInboundChannelLabel(),
        assigneeMetrics: buildAssigneeMetrics(cases),
        trainingSummary: getTrainingSummary(),
        trainingInsights: getTrainingInsights(),
        trainingConversations: getTrainingConversations(8),
        pricingConfig,
        pricingOffers: priceOffers.slice(0, 20),
        pricingSummary: buildPricingSummary(priceOffers, pricingConfig),
        pricingMovements: buildPricingMovements(priceOffers).slice(0, 12),
        telegramConfig: getTelegramUiConfig(),
        telegramStatus: readTelegramRuntimeStatus(),
        telegramSources: getTelegramSources().slice(0, 30),
        telegramMessages: getTelegramMessages(16),
        telegramSummary: getTelegramSummary(),
        currentUser: sessionUser,
        sessionSecure: String(process.env.SESSION_SECURE || "").toLowerCase() === "true",
        teamUsers: sessionUser && ["owner", "admin"].includes(sessionUser.role) ? listUsers() : [],
      },
    });
    return;
  }

  if (req.method === "GET" && requestUrl.pathname === "/api/public-config") {
    const styleProfile = readStyleProfile();
    sendJson(res, 200, {
      businessName: styleProfile.businessName,
      agentDisplayName: styleProfile.agentDisplayName,
      welcomeTitle: "Solicita tu soporte remoto",
      welcomeCopy:
        "Cuentanos el modelo del equipo y el problema. Te abrimos un caso y te damos una primera respuesta de inmediato.",
    });
    return;
  }

  if (req.method === "GET" && requestUrl.pathname === "/api/notifications-log") {
    sendJson(res, 200, {
      notifications: readNotificationLog(),
      meta: {
        configured: isNotificationConfigured(),
        modeLabel: getNotificationModeLabel(),
      },
    });
    return;
  }

  if (req.method === "GET" && requestUrl.pathname.startsWith("/api/public/tickets/")) {
    const caseId = requestUrl.pathname.split("/")[4];
    const payload = getPublicTicket(caseId);

    if (!payload) {
      sendJson(res, 404, { error: "Ticket no encontrado" });
      return;
    }

    sendJson(res, 200, payload);
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/style-profile") {
    try {
      const body = await parseBody(req);
      const profile = updateStyleProfile(body);
      sendJson(res, 200, { profile });
    } catch (error) {
      sendJson(res, 400, { error: "No pude guardar el perfil de estilo" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/pricing/config") {
    try {
      const body = await parseBody(req);
      const config = updatePricingConfig(body);
      sendJson(res, 200, { config });
    } catch (error) {
      sendJson(res, 400, { error: "No pude guardar la configuracion de precios" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/telegram/config") {
    try {
      const body = await parseBody(req);
      const config = updateTelegramConfig(body);
      sendJson(res, 200, { config: getTelegramUiConfig(config) });
    } catch (error) {
      sendJson(res, 400, { error: error.message || "No pude guardar la configuracion de Telegram" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/telegram/status/refresh") {
    try {
      const status = refreshTelegramStatus();
      sendJson(res, 200, { status });
    } catch (error) {
      sendJson(res, 400, { error: error.message || "No pude revisar el estado de Telegram" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/telegram/auth/start") {
    try {
      const status = startTelegramAuth();
      sendJson(res, 200, { status });
    } catch (error) {
      sendJson(res, 400, { error: error.message || "No pude iniciar la autorizacion de Telegram" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/telegram/auth/code") {
    try {
      const body = await parseBody(req);
      const status = submitTelegramCode(body.code);
      sendJson(res, 200, { status });
    } catch (error) {
      sendJson(res, 400, { error: error.message || "No pude enviar el codigo a Telegram" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/telegram/auth/password") {
    try {
      const body = await parseBody(req);
      const status = submitTelegramPassword(body.password);
      sendJson(res, 200, { status });
    } catch (error) {
      sendJson(res, 400, { error: error.message || "No pude enviar la contrasena de Telegram" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/telegram/discover") {
    try {
      const result = discoverTelegramChats();
      sendJson(res, 200, result);
    } catch (error) {
      sendJson(res, 400, { error: error.message || "No pude descubrir chats de Telegram" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/telegram/sources") {
    try {
      const body = await parseBody(req);
      updateTelegramSource(body);
      sendJson(res, 200, { ok: true });
    } catch (error) {
      sendJson(res, 400, { error: error.message || "No pude guardar la fuente de Telegram" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/telegram/sync") {
    try {
      const result = syncTelegramSources();
      sendJson(res, 200, result);
    } catch (error) {
      sendJson(res, 400, { error: error.message || "No pude sincronizar Telegram" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/pricing/preview") {
    try {
      const body = await parseBody(req);
      const pricingConfig = readPricingConfig();
      const preview = inferOfferDetails(body.rawText || "", pricingConfig.defaultCurrency);
      sendJson(res, 200, { preview });
    } catch (error) {
      sendJson(res, 400, { error: "No pude interpretar esa oferta" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/pricing/offers") {
    try {
      const body = await parseBody(req);
      const pricingConfig = readPricingConfig();
      const parsedPrice = parsePriceText(body.rawText || body.cost, pricingConfig.defaultCurrency);
      const inferred = inferOfferDetails(body.rawText || "", pricingConfig.defaultCurrency);
      const offer = {
        source: String(body.source || "Telegram manual").trim() || "Telegram manual",
        supplierName: String(body.supplierName || inferred.supplierName || "").trim(),
        serviceName: String(body.serviceName || inferred.serviceName || "").trim(),
        variant: String(body.variant || inferred.variant || "").trim(),
        currency: String(body.currency || parsedPrice.currency || pricingConfig.defaultCurrency)
          .trim()
          .toUpperCase(),
        cost: Number(body.cost || parsedPrice.cost),
        rawText: String(body.rawText || "").trim(),
        notes: String(body.notes || "").trim(),
        importedAt: new Date().toISOString(),
      };

      if (!offer.supplierName || !offer.serviceName || !offer.cost) {
        sendJson(res, 400, { error: "Faltan datos para guardar la oferta" });
        return;
      }

      const offerId = savePriceOffer(offer);
      sendJson(res, 201, { ok: true, offerId });
    } catch (error) {
      sendJson(res, 400, { error: error.message || "No pude guardar la oferta de precio" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/training/import") {
    try {
      const body = await parseBody(req, { maxBytes: 3 * 1024 * 1024 });
      const parsedConversation = parseWhatsAppChat(sanitizeImportedChatText(body.chatText), {
        source: body.source || "WhatsApp export",
        contactName: body.contactName,
        ownerAliases: body.ownerAliases,
        notes: body.notes,
        anonymize: Boolean(body.anonymize),
      });
      Object.assign(parsedConversation, analyzeTrainingConversation(parsedConversation));

      if (!parsedConversation.messageCount) {
        sendJson(res, 400, { error: "No pude detectar mensajes validos en el chat importado" });
        return;
      }

      const conversationId = saveTrainingConversation(parsedConversation);
      sendJson(res, 201, {
        ok: true,
        conversationId,
        summary: {
          contactName: parsedConversation.contactName,
          messageCount: parsedConversation.messageCount,
          clientMessageCount: parsedConversation.clientMessageCount,
          agentMessageCount: parsedConversation.agentMessageCount,
        },
      });
    } catch (error) {
      sendJson(res, 400, { error: "No pude importar el chat de entrenamiento" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/training/import-file") {
    try {
      const body = await parseBody(req, { maxBytes: 4 * 1024 * 1024 });
      const chatText = sanitizeImportedChatText(
        extractChatTextFromUpload(body.fileName, body.contentBase64)
      );
      const parsedConversation = parseWhatsAppChat(chatText, {
        source: body.source || "WhatsApp export",
        contactName: body.contactName,
        ownerAliases: body.ownerAliases,
        notes: body.notes,
        anonymize: Boolean(body.anonymize),
      });
      Object.assign(parsedConversation, analyzeTrainingConversation(parsedConversation));

      if (!parsedConversation.messageCount) {
        sendJson(res, 400, { error: "No pude detectar mensajes validos en el archivo importado" });
        return;
      }

      const conversationId = saveTrainingConversation(parsedConversation);
      sendJson(res, 201, {
        ok: true,
        conversationId,
        summary: {
          contactName: parsedConversation.contactName,
          messageCount: parsedConversation.messageCount,
          clientMessageCount: parsedConversation.clientMessageCount,
          agentMessageCount: parsedConversation.agentMessageCount,
        },
      });
    } catch (error) {
      sendJson(res, 400, { error: error.message || "No pude importar el archivo de entrenamiento" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/cases") {
    try {
      const body = await parseBody(req);
      const payload = await createCase(body);
      sendJson(res, 201, payload);
    } catch (error) {
      sendJson(res, 400, { error: "No pude crear el caso" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/public/cases") {
    try {
      const body = await parseBody(req);
      const payload = await createPublicCase(body);
      sendJson(res, 201, payload);
    } catch (error) {
      sendJson(res, 400, { error: "No pude recibir la solicitud publica" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname === "/api/inbound/email") {
    try {
      if (!isValidInboundToken(req)) {
        sendJson(res, 401, { error: "Token de entrada invalido" });
        return;
      }

      const body = await parseBody(req);
      const payload = await createInboundEmailCase(body);
      sendJson(res, 201, {
        ok: true,
        caseId: payload.created.id,
      });
    } catch (error) {
      sendJson(res, 400, { error: "No pude recibir el correo de entrada" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname.startsWith("/api/cases/") && requestUrl.pathname.endsWith("/reply")) {
    const caseId = requestUrl.pathname.split("/")[3];
    const payload = await createReplySuggestion(caseId);

    if (!payload) {
      sendJson(res, 404, { error: "Caso no encontrado" });
      return;
    }

    sendJson(res, 200, payload);
    return;
  }

  if (req.method === "POST" && requestUrl.pathname.startsWith("/api/cases/") && requestUrl.pathname.endsWith("/status")) {
    try {
      const caseId = requestUrl.pathname.split("/")[3];
      const body = await parseBody(req);
      const payload = await updateCaseStatus(caseId, body.status, body.closeReason);

      if (!payload) {
        sendJson(res, 404, { error: "Caso no encontrado" });
        return;
      }

      sendJson(res, 200, payload);
    } catch (error) {
      sendJson(res, 400, { error: "No pude actualizar el estado" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname.startsWith("/api/cases/") && requestUrl.pathname.endsWith("/assign")) {
    try {
      const caseId = requestUrl.pathname.split("/")[3];
      const body = await parseBody(req);
      const payload = await assignCase(caseId, body.assignee);

      if (!payload) {
        sendJson(res, 404, { error: "Caso no encontrado" });
        return;
      }

      sendJson(res, 200, payload);
    } catch (error) {
      sendJson(res, 400, { error: "No pude asignar el caso" });
    }
    return;
  }

  if (req.method === "POST" && requestUrl.pathname.startsWith("/api/cases/") && requestUrl.pathname.endsWith("/run")) {
    const caseId = requestUrl.pathname.split("/")[3];
    const payload = await runAgent(caseId);

    if (!payload) {
      sendJson(res, 404, { error: "Caso no encontrado" });
      return;
    }

    sendJson(res, 200, payload);
    return;
  }

  const filePath =
    requestUrl.pathname === "/"
      ? path.join(PUBLIC_DIR, "index.html")
      : path.join(PUBLIC_DIR, requestUrl.pathname);

  serveStaticFile(filePath, res);
});

server.listen(PORT, () => {
  console.log(`MVP listo en http://localhost:${PORT}`);
});
