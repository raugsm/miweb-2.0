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

const DEFAULT_TELEGRAM_CONFIG = {
  apiId: "",
  apiHash: "",
  phoneNumber: "",
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

function readTelegramConfig() {
  const stored = getMetadata("telegram_config", null) || {};
  return {
    ...DEFAULT_TELEGRAM_CONFIG,
    ...stored,
  };
}

function updateTelegramConfig(input) {
  const nextConfig = {
    ...readTelegramConfig(),
    apiId: String(input.apiId || "").trim(),
    apiHash: String(input.apiHash || "").trim(),
    phoneNumber: String(input.phoneNumber || "").trim(),
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
    apiHashMasked: maskSecret(config.apiHash, 6),
  };
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

function readTelegramRuntimeStatus() {
  return getMetadata("telegram_runtime_status", {
    ok: false,
    state: "not_configured",
    message: "Todavia no configuraste Telegram",
  });
}

function writeTelegramRuntimeStatus(status) {
  saveMetadata("telegram_runtime_status", status);
  return status;
}

function refreshTelegramStatus() {
  const result = runTelegramBridge("status");
  return writeTelegramRuntimeStatus(result);
}

function startTelegramAuth() {
  const result = runTelegramBridge("start-auth");
  return writeTelegramRuntimeStatus(result);
}

function submitTelegramCode(code) {
  const result = runTelegramBridge("submit-code", { code: String(code || "").trim() });
  return writeTelegramRuntimeStatus(result);
}

function submitTelegramPassword(password) {
  const result = runTelegramBridge("submit-password", { password: String(password || "") });
  return writeTelegramRuntimeStatus(result);
}

function discoverTelegramChats() {
  const result = runTelegramBridge("list-chats");
  const now = new Date().toISOString();
  const chats = result.chats || [];
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
    ...result,
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

function syncTelegramSources() {
  const config = readTelegramConfig();
  const sources = getTelegramSources().filter((item) => item.enabled);
  const now = new Date().toISOString();
  let insertedMessages = 0;
  let importedOffers = 0;

  for (const source of sources) {
    const result = runTelegramBridge("fetch-history", {
      chatId: String(source.chatId),
      limit: 60,
    });
    const messages = result.messages || [];

    for (const message of messages) {
      const preview = inferOfferDetails(message.text || "", "USD");
      const saved = saveTelegramMessage({
        chatId: String(source.chatId),
        messageId: String(message.messageId),
        chatTitle: source.title,
        senderName: message.senderName || "",
        text: message.text || "",
        sentAt: message.sentAt || now,
        importedAt: now,
        detectedSupplier: preview.supplierName || "",
        detectedService: preview.serviceName || "",
        detectedVariant: preview.variant || "",
        detectedCurrency: preview.currency || "",
        detectedCost: preview.cost ?? null,
        importedOfferId: null,
      });

      if (saved.inserted) {
        insertedMessages += 1;

        if (source.autoImport && preview.cost && preview.serviceName) {
          const importedOfferId = savePriceOffer({
            source: `Telegram TDLib · ${source.title}`,
            supplierName: preview.supplierName || source.title,
            serviceName: preview.serviceName,
            variant: preview.variant || "",
            currency: preview.currency || "USD",
            cost: preview.cost,
            rawText: message.text || "",
            notes: `Chat ${source.title} (${source.chatId})`,
            importedAt: now,
          });
          attachOfferToTelegramMessage(source.chatId, message.messageId, importedOfferId);
          importedOffers += 1;
        }
      }
    }

    updateTelegramSource({
      ...source,
      lastSyncedAt: now,
    });
  }

  const status = {
    ok: true,
    state: "ready",
    message: `Sincronizacion completada. ${insertedMessages} mensajes nuevos y ${importedOffers} ofertas importadas.`,
    updatedAt: now,
  };
  writeTelegramRuntimeStatus(status);

  return {
    ...status,
    insertedMessages,
    importedOffers,
  };
}

module.exports = {
  discoverTelegramChats,
  getTelegramUiConfig,
  readTelegramRuntimeStatus,
  readTelegramConfig,
  refreshTelegramStatus,
  startTelegramAuth,
  submitTelegramCode,
  submitTelegramPassword,
  syncTelegramSources,
  updateTelegramConfig,
  updateTelegramSource,
};
