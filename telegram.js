const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");
const {
  getMetadata,
  saveMetadata,
  getTelegramSources,
  saveTelegramSource,
  saveTelegramMessage,
  savePriceOffer,
  attachOfferToTelegramMessage,
} = require("./db");
const { inferOfferDetails } = require("./pricing");

let TelegramClient;
let StringSession;

try {
  ({ TelegramClient } = require("telegram"));
  ({ StringSession } = require("telegram/sessions"));
} catch (error) {
  TelegramClient = null;
  StringSession = null;
}

const DEFAULT_TELEGRAM_CONFIG = {
  runtimeMode: "cloud",
  apiId: "",
  apiHash: "",
  phoneNumber: "",
  sessionString: "",
  syncIntervalMinutes: 15,
  tdjsonDllPath: path.join(__dirname, "bin", "tdjson.dll"),
  dataDir: path.join(__dirname, "data", "telegram-tdlib"),
  bridgeExePath: path.join(
    __dirname,
    "scripts",
    "TelegramTdlibBridge",
    "bin",
    "Release",
    "net8.0",
    "TelegramTdlibBridge.exe"
  ),
};

let activeCloudAuth = null;

function readTelegramConfig() {
  const stored = getMetadata("telegram_config", null) || {};
  return {
    ...DEFAULT_TELEGRAM_CONFIG,
    ...stored,
  };
}

function updateTelegramConfig(input) {
  const currentConfig = readTelegramConfig();
  const nextConfig = {
    ...currentConfig,
    runtimeMode: String(input.runtimeMode || "cloud").trim() || "cloud",
    apiId: String(input.apiId || "").trim(),
    apiHash: String(input.apiHash || "").trim() || currentConfig.apiHash,
    phoneNumber: String(input.phoneNumber || "").trim(),
    sessionString: String(input.sessionString || "").trim() || currentConfig.sessionString,
    syncIntervalMinutes: Number(input.syncIntervalMinutes || 15) || 15,
    tdjsonDllPath: String(input.tdjsonDllPath || DEFAULT_TELEGRAM_CONFIG.tdjsonDllPath).trim(),
    dataDir: String(input.dataDir || DEFAULT_TELEGRAM_CONFIG.dataDir).trim(),
    bridgeExePath: String(
      input.bridgeExePath || DEFAULT_TELEGRAM_CONFIG.bridgeExePath
    ).trim(),
  };

  saveMetadata("telegram_config", nextConfig);
  return nextConfig;
}

function maskSecret(value, visible = 4) {
  const safe = String(value || "");
  if (!safe) {
    return "";
  }
  if (safe.length <= visible) {
    return "*".repeat(safe.length);
  }
  return `${"*".repeat(Math.max(4, safe.length - visible))}${safe.slice(-visible)}`;
}

function getTelegramUiConfig() {
  const config = readTelegramConfig();
  return {
    ...config,
    apiHash: "",
    sessionString: "",
    apiHashMasked: maskSecret(config.apiHash, 6),
    sessionStringMasked: maskSecret(config.sessionString, 8),
  };
}

function writeTelegramRuntimeStatus(status) {
  const nextStatus = {
    ...status,
    updatedAt: status.updatedAt || new Date().toISOString(),
  };
  saveMetadata("telegram_runtime_status", nextStatus);
  return nextStatus;
}

function readTelegramRuntimeStatus() {
  return getMetadata("telegram_runtime_status", {
    ok: false,
    state: "cloud_pending",
    message: "Telegram esta esperando credenciales para iniciar la conexion en nube.",
  });
}

function validateCloudConfig(config) {
  if (!config.apiId || !config.apiHash || !config.phoneNumber) {
    throw new Error("Completa API ID, API Hash y numero de telefono antes de conectar Telegram.");
  }

  if (!String(config.phoneNumber).startsWith("+")) {
    throw new Error("El numero de Telegram debe incluir prefijo internacional, por ejemplo +51...");
  }

  if (!TelegramClient || !StringSession) {
    throw new Error(
      "Falta instalar el cliente Telegram en el servidor. Railway lo instalara al redeplegar con la nueva dependencia."
    );
  }
}

function createCloudClient(config) {
  validateCloudConfig(config);
  return new TelegramClient(
    new StringSession(config.sessionString || ""),
    Number(config.apiId),
    config.apiHash,
    {
      connectionRetries: 5,
      useWSS: false,
    }
  );
}

function saveSessionFromClient(client) {
  const sessionString = client.session.save();
  const config = readTelegramConfig();
  updateTelegramConfig({
    ...config,
    sessionString,
    runtimeMode: "cloud",
  });
  return sessionString;
}

function wait(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function waitForAuthState() {
  for (let index = 0; index < 20; index += 1) {
    const status = readTelegramRuntimeStatus();
    if (
      status.state === "code_required" ||
      status.state === "password_required" ||
      status.state === "ready" ||
      status.state === "error"
    ) {
      return status;
    }
    await wait(250);
  }
  return readTelegramRuntimeStatus();
}

async function withCloudClient(callback) {
  const config = readTelegramConfig();
  const client = createCloudClient(config);

  await client.connect();
  const authorized = await client.isUserAuthorized();
  if (!authorized) {
    await client.disconnect();
    throw new Error("Telegram aun no esta autorizado. Pulsa Preparar conexion y envia el codigo recibido.");
  }

  try {
    return await callback(client, config);
  } finally {
    await client.disconnect();
  }
}

function ensureTelegramDataDir(config) {
  const dir = path.resolve(config.dataDir || DEFAULT_TELEGRAM_CONFIG.dataDir);
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
  }
  return dir;
}

function ensureBridgeAvailable(config) {
  const exePath = path.resolve(config.bridgeExePath || DEFAULT_TELEGRAM_CONFIG.bridgeExePath);
  if (!fs.existsSync(exePath)) {
    throw new Error(
      "No encontre el puente de Telegram. Compila primero scripts/TelegramTdlibBridge."
    );
  }
  return exePath;
}

function ensureTdjsonAvailable(config) {
  const dllPath = path.resolve(config.tdjsonDllPath || DEFAULT_TELEGRAM_CONFIG.tdjsonDllPath);
  if (!fs.existsSync(dllPath)) {
    throw new Error("No encontre tdjson.dll. Copialo a la ruta configurada antes de conectar Telegram.");
  }
  return dllPath;
}

function runTelegramBridge(action, payload = {}) {
  const config = readTelegramConfig();

  if (config.runtimeMode !== "local") {
    throw new Error("El puente local solo se usa en modo local. En nube usa el conector cloud.");
  }

  ensureTelegramDataDir(config);
  const bridgeExePath = ensureBridgeAvailable(config);
  const tdjsonDllPath = ensureTdjsonAvailable(config);

  const request = {
    action,
    apiId: config.apiId,
    apiHash: config.apiHash,
    phoneNumber: config.phoneNumber,
    tdjsonDllPath,
    dataDir: config.dataDir,
    ...payload,
  };

  const output = execFileSync(bridgeExePath, {
    cwd: __dirname,
    input: JSON.stringify(request),
    encoding: "utf8",
    windowsHide: true,
    timeout: 120000,
  });

  return JSON.parse(String(output || "{}").trim() || "{}");
}

async function refreshTelegramStatus() {
  const config = readTelegramConfig();

  if (config.runtimeMode === "local") {
    const result = runTelegramBridge("status");
    return writeTelegramRuntimeStatus(result);
  }

  try {
    validateCloudConfig(config);
  } catch (error) {
    return writeTelegramRuntimeStatus({
      ok: false,
      state: "cloud_pending",
      message: error.message,
    });
  }

  if (activeCloudAuth) {
    return readTelegramRuntimeStatus();
  }

  if (!config.sessionString) {
    return writeTelegramRuntimeStatus({
      ok: true,
      state: "credentials_saved",
      message: "Credenciales guardadas. Pulsa Preparar conexion para recibir el codigo de Telegram.",
    });
  }

  try {
    const client = createCloudClient(config);
    await client.connect();
    const authorized = await client.isUserAuthorized();
    await client.disconnect();
    return writeTelegramRuntimeStatus({
      ok: authorized,
      state: authorized ? "ready" : "code_required",
      message: authorized
        ? "Telegram esta conectado en la nube y listo para descubrir chats."
        : "La sesion guardada no esta autorizada. Pulsa Preparar conexion otra vez.",
    });
  } catch (error) {
    return writeTelegramRuntimeStatus({
      ok: false,
      state: "error",
      message: error.message || "No pude revisar la conexion de Telegram.",
    });
  }
}

async function startTelegramAuth() {
  const config = readTelegramConfig();

  if (config.runtimeMode === "local") {
    const result = runTelegramBridge("start-auth");
    return writeTelegramRuntimeStatus(result);
  }

  validateCloudConfig(config);

  if (activeCloudAuth) {
    return readTelegramRuntimeStatus();
  }

  const client = createCloudClient({
    ...config,
    sessionString: config.sessionString || "",
  });

  activeCloudAuth = {
    client,
    codeResolver: null,
    passwordResolver: null,
    done: false,
  };

  writeTelegramRuntimeStatus({
    ok: true,
    state: "starting",
    message: "Estoy pidiendo el codigo a Telegram. Espera unos segundos.",
  });

  activeCloudAuth.promise = client
    .start({
      phoneNumber: async () => config.phoneNumber,
      phoneCode: async () => {
        writeTelegramRuntimeStatus({
          ok: true,
          state: "code_required",
          message: "Telegram envio un codigo. Escribelo en Codigo recibido y pulsa Enviar codigo.",
        });
        return new Promise((resolve) => {
          activeCloudAuth.codeResolver = resolve;
        });
      },
      password: async () => {
        writeTelegramRuntimeStatus({
          ok: true,
          state: "password_required",
          message: "Telegram pidio contrasena 2FA. Escribela y pulsa Enviar contrasena.",
        });
        return new Promise((resolve) => {
          activeCloudAuth.passwordResolver = resolve;
        });
      },
      onError: (error) => {
        writeTelegramRuntimeStatus({
          ok: false,
          state: "error",
          message: error.message || "Telegram devolvio un error durante la autorizacion.",
        });
      },
    })
    .then(async () => {
      saveSessionFromClient(client);
      await client.disconnect();
      activeCloudAuth = null;
      return writeTelegramRuntimeStatus({
        ok: true,
        state: "ready",
        message: "Telegram quedo conectado. Ya puedes descubrir chats.",
      });
    })
    .catch(async (error) => {
      try {
        await client.disconnect();
      } catch (_) {
        // ignore disconnect failures during auth cleanup
      }
      activeCloudAuth = null;
      return writeTelegramRuntimeStatus({
        ok: false,
        state: "error",
        message: error.message || "No pude completar la autorizacion de Telegram.",
      });
    });

  return waitForAuthState();
}

async function submitTelegramCode(code) {
  const safeCode = String(code || "").trim();
  if (!safeCode) {
    throw new Error("Escribe el codigo recibido en Telegram.");
  }

  const config = readTelegramConfig();
  if (config.runtimeMode === "local") {
    const result = runTelegramBridge("submit-code", { code: safeCode });
    return writeTelegramRuntimeStatus(result);
  }

  if (!activeCloudAuth || !activeCloudAuth.codeResolver) {
    throw new Error("Primero pulsa Preparar conexion para pedir un codigo nuevo.");
  }

  activeCloudAuth.codeResolver(safeCode);
  activeCloudAuth.codeResolver = null;
  await Promise.race([activeCloudAuth.promise, wait(2500)]);
  return readTelegramRuntimeStatus();
}

async function submitTelegramPassword(password) {
  const safePassword = String(password || "");
  if (!safePassword) {
    throw new Error("Escribe la contrasena 2FA de Telegram.");
  }

  const config = readTelegramConfig();
  if (config.runtimeMode === "local") {
    const result = runTelegramBridge("submit-password", { password: safePassword });
    return writeTelegramRuntimeStatus(result);
  }

  if (!activeCloudAuth || !activeCloudAuth.passwordResolver) {
    throw new Error("Telegram no esta esperando contrasena 2FA en este momento.");
  }

  activeCloudAuth.passwordResolver(safePassword);
  activeCloudAuth.passwordResolver = null;
  await Promise.race([activeCloudAuth.promise, wait(2500)]);
  return readTelegramRuntimeStatus();
}

function normalizeChat(dialog) {
  const entity = dialog.entity || {};
  const chatId = String(dialog.id ?? entity.id ?? "");
  const chatType = dialog.isChannel
    ? "channel"
    : dialog.isGroup
      ? "group"
      : dialog.isUser
        ? "user"
        : "unknown";

  return {
    chatId,
    title: dialog.title || entity.title || entity.username || entity.firstName || `Chat ${chatId}`,
    chatType,
    username: entity.username || "",
  };
}

async function discoverTelegramChats() {
  const config = readTelegramConfig();
  if (config.runtimeMode === "local") {
    const result = runTelegramBridge("list-chats");
    return saveDiscoveredChats(result.chats || [], result);
  }

  return withCloudClient(async (client) => {
    const dialogs = await client.getDialogs({ limit: 100 });
    const chats = dialogs.map(normalizeChat).filter((chat) => chat.chatId);
    return saveDiscoveredChats(chats, {
      ok: true,
      message: `Encontre ${chats.length} chats. Activa los que quieras monitorear.`,
    });
  });
}

function saveDiscoveredChats(chats, baseResult = {}) {
  const now = new Date().toISOString();
  const existing = new Map(getTelegramSources().map((item) => [String(item.chatId), item]));

  chats.forEach((chat) => {
    const previous = existing.get(String(chat.chatId));
    saveTelegramSource({
      chatId: String(chat.chatId),
      title: chat.title || `Chat ${chat.chatId}`,
      chatType: chat.chatType || "unknown",
      username: chat.username || null,
      enabled: previous ? previous.enabled : false,
      autoImport: previous ? previous.autoImport : true,
      notes: previous?.notes || "",
      lastSyncedAt: previous?.lastSyncedAt || null,
      createdAt: previous?.createdAt || now,
      updatedAt: now,
    });
  });

  return {
    ...baseResult,
    ok: baseResult.ok ?? true,
    chats,
  };
}

function updateTelegramSource(input) {
  const previous = getTelegramSources().find((item) => String(item.chatId) === String(input.chatId));
  const now = new Date().toISOString();
  saveTelegramSource({
    chatId: String(input.chatId),
    title: String(input.title || previous?.title || `Chat ${input.chatId}`).trim(),
    chatType: String(input.chatType || previous?.chatType || "unknown").trim(),
    username: String(input.username || previous?.username || "").trim(),
    enabled: input.enabled !== undefined ? Boolean(input.enabled) : previous?.enabled ?? true,
    autoImport:
      input.autoImport !== undefined ? Boolean(input.autoImport) : previous?.autoImport ?? true,
    notes: String(input.notes || previous?.notes || "").trim(),
    lastSyncedAt: input.lastSyncedAt || previous?.lastSyncedAt || null,
    createdAt: previous?.createdAt || now,
    updatedAt: now,
  });
}

function messageToPlainObject(message, source, now) {
  const sentAt = message.date
    ? new Date(Number(message.date) * 1000).toISOString()
    : now;

  return {
    chatId: String(source.chatId),
    messageId: String(message.id),
    chatTitle: source.title,
    senderName: message.senderId ? String(message.senderId) : "",
    text: message.message || "",
    sentAt,
  };
}

function importTelegramMessages(source, messages, now) {
  let insertedMessages = 0;
  let importedOffers = 0;

  for (const message of messages) {
    const text = message.text || "";
    if (!text.trim()) {
      continue;
    }

    const preview = inferOfferDetails(text, "USD");
    const saved = saveTelegramMessage({
      ...message,
      importedAt: now,
      detectedSupplier: preview.supplierName || "",
      detectedService: preview.serviceName || "",
      detectedVariant: preview.variant || "",
      detectedCurrency: preview.currency || "",
      detectedCost: preview.cost ?? null,
      importedOfferId: null,
    });

    if (!saved.inserted) {
      continue;
    }

    insertedMessages += 1;

    if (source.autoImport && preview.cost && preview.serviceName) {
      const importedOfferId = savePriceOffer({
        source: `Telegram cloud - ${source.title}`,
        supplierName: preview.supplierName || source.title,
        serviceName: preview.serviceName,
        variant: preview.variant || "",
        currency: preview.currency || "USD",
        cost: preview.cost,
        rawText: text,
        notes: `Chat ${source.title} (${source.chatId})`,
        importedAt: now,
      });
      attachOfferToTelegramMessage(source.chatId, message.messageId, importedOfferId);
      importedOffers += 1;
    }
  }

  return {
    insertedMessages,
    importedOffers,
  };
}

async function syncTelegramSources() {
  const config = readTelegramConfig();
  if (config.runtimeMode === "local") {
    return syncLocalTelegramSources();
  }

  const sources = getTelegramSources().filter((item) => item.enabled);
  if (!sources.length) {
    const status = writeTelegramRuntimeStatus({
      ok: true,
      state: "ready",
      message: "No hay fuentes activas. Primero descubre chats y activa al menos uno.",
    });
    return {
      ...status,
      insertedMessages: 0,
      importedOffers: 0,
    };
  }

  return withCloudClient(async (client) => {
    const now = new Date().toISOString();
    let insertedMessages = 0;
    let importedOffers = 0;

    for (const source of sources) {
      const entityRef = source.username || source.chatId;
      const messages = await client.getMessages(entityRef, { limit: 60 });
      const plainMessages = messages.map((message) => messageToPlainObject(message, source, now));
      const result = importTelegramMessages(source, plainMessages, now);
      insertedMessages += result.insertedMessages;
      importedOffers += result.importedOffers;
      updateTelegramSource({
        ...source,
        lastSyncedAt: now,
      });
    }

    const status = writeTelegramRuntimeStatus({
      ok: true,
      state: "ready",
      message: `Sincronizacion completada. ${insertedMessages} mensajes nuevos y ${importedOffers} ofertas importadas.`,
    });

    return {
      ...status,
      insertedMessages,
      importedOffers,
    };
  });
}

function syncLocalTelegramSources() {
  const sources = getTelegramSources().filter((item) => item.enabled);
  const now = new Date().toISOString();
  let insertedMessages = 0;
  let importedOffers = 0;

  for (const source of sources) {
    const result = runTelegramBridge("fetch-history", {
      chatId: String(source.chatId),
      limit: 60,
    });
    const messages = (result.messages || []).map((message) => ({
      chatId: String(source.chatId),
      messageId: String(message.messageId),
      chatTitle: source.title,
      senderName: message.senderName || "",
      text: message.text || "",
      sentAt: message.sentAt || now,
    }));
    const imported = importTelegramMessages(source, messages, now);
    insertedMessages += imported.insertedMessages;
    importedOffers += imported.importedOffers;
    updateTelegramSource({
      ...source,
      lastSyncedAt: now,
    });
  }

  const status = writeTelegramRuntimeStatus({
    ok: true,
    state: "ready",
    message: `Sincronizacion completada. ${insertedMessages} mensajes nuevos y ${importedOffers} ofertas importadas.`,
  });

  return {
    ...status,
    insertedMessages,
    importedOffers,
  };
}

function rememberAsyncError(promise, fallbackMessage) {
  Promise.resolve(promise).catch((error) => {
    writeTelegramRuntimeStatus({
      ok: false,
      state: "error",
      message: error.message || fallbackMessage,
    });
  });
}

function refreshTelegramStatusCompat() {
  const config = readTelegramConfig();
  if (config.runtimeMode === "local") {
    return refreshTelegramStatus();
  }

  try {
    validateCloudConfig(config);
  } catch (error) {
    return writeTelegramRuntimeStatus({
      ok: false,
      state: "cloud_pending",
      message: error.message,
    });
  }

  if (!config.sessionString || activeCloudAuth) {
    rememberAsyncError(refreshTelegramStatus(), "No pude revisar la conexion de Telegram.");
    return readTelegramRuntimeStatus();
  }

  writeTelegramRuntimeStatus({
    ok: true,
    state: "checking",
    message: "Estoy revisando la sesion de Telegram en la nube.",
  });
  rememberAsyncError(refreshTelegramStatus(), "No pude revisar la conexion de Telegram.");
  return readTelegramRuntimeStatus();
}

function startTelegramAuthCompat() {
  writeTelegramRuntimeStatus({
    ok: true,
    state: "starting",
    message: "Estoy pidiendo el codigo a Telegram. Espera unos segundos.",
  });
  rememberAsyncError(startTelegramAuth(), "No pude iniciar la autorizacion de Telegram.");
  return readTelegramRuntimeStatus();
}

function submitTelegramCodeCompat(code) {
  rememberAsyncError(submitTelegramCode(code), "No pude enviar el codigo a Telegram.");
  return readTelegramRuntimeStatus();
}

function submitTelegramPasswordCompat(password) {
  rememberAsyncError(submitTelegramPassword(password), "No pude enviar la contrasena de Telegram.");
  return readTelegramRuntimeStatus();
}

function discoverTelegramChatsCompat() {
  writeTelegramRuntimeStatus({
    ok: true,
    state: "discovering",
    message: "Estoy buscando chats disponibles en Telegram.",
  });
  rememberAsyncError(
    discoverTelegramChats().then((result) => {
      writeTelegramRuntimeStatus({
        ok: Boolean(result.ok),
        state: result.ok ? "ready" : "error",
        message: result.message || `Encontre ${(result.chats || []).length} chats.`,
      });
      return result;
    }),
    "No pude descubrir chats de Telegram."
  );
  return {
    ok: true,
    chats: [],
    message: "Busqueda iniciada. Actualiza el panel en unos segundos.",
  };
}

function syncTelegramSourcesCompat() {
  writeTelegramRuntimeStatus({
    ok: true,
    state: "syncing",
    message: "Estoy sincronizando mensajes de Telegram.",
  });
  rememberAsyncError(syncTelegramSources(), "No pude sincronizar Telegram.");
  return {
    ok: true,
    insertedMessages: 0,
    importedOffers: 0,
    message: "Sincronizacion iniciada. Actualiza el panel en unos segundos.",
  };
}

module.exports = {
  discoverTelegramChats: discoverTelegramChatsCompat,
  getTelegramUiConfig,
  readTelegramRuntimeStatus,
  readTelegramConfig,
  refreshTelegramStatus: refreshTelegramStatusCompat,
  startTelegramAuth: startTelegramAuthCompat,
  submitTelegramCode: submitTelegramCodeCompat,
  submitTelegramPassword: submitTelegramPasswordCompat,
  syncTelegramSources: syncTelegramSourcesCompat,
  updateTelegramConfig,
  updateTelegramSource,
};
