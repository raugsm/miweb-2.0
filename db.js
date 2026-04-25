const fs = require("fs");
const path = require("path");
const { DatabaseSync } = require("node:sqlite");

const DATA_DIR = process.env.DATA_DIR
  ? path.resolve(process.env.DATA_DIR)
  : path.join(__dirname, "data");
const DB_FILE = path.join(DATA_DIR, "app.db");
const LEGACY_CASES_FILE = path.join(DATA_DIR, "cases.json");
const LEGACY_STYLE_FILE = path.join(DATA_DIR, "style-profile.json");
const LEGACY_NOTIFICATIONS_FILE = path.join(DATA_DIR, "notifications-log.json");

if (!fs.existsSync(DATA_DIR)) {
  fs.mkdirSync(DATA_DIR, { recursive: true });
}

const db = new DatabaseSync(DB_FILE);

db.exec(`
  PRAGMA journal_mode = WAL;
  CREATE TABLE IF NOT EXISTS procedures (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    category TEXT NOT NULL,
    description TEXT NOT NULL
  );
  CREATE TABLE IF NOT EXISTS procedure_steps (
    procedure_id TEXT NOT NULL,
    step_order INTEGER NOT NULL,
    step_text TEXT NOT NULL,
    PRIMARY KEY (procedure_id, step_order)
  );
  CREATE TABLE IF NOT EXISTS cases (
    id TEXT PRIMARY KEY,
    client_name TEXT NOT NULL,
    contact TEXT NOT NULL,
    category TEXT NOT NULL,
    priority TEXT NOT NULL,
    status TEXT NOT NULL,
    device_model TEXT NOT NULL,
    summary TEXT NOT NULL,
    procedure_id TEXT NOT NULL,
    current_step INTEGER NOT NULL,
    last_update TEXT NOT NULL,
    ai_mode TEXT NOT NULL,
    assignee TEXT,
    close_reason TEXT,
    created_at TEXT,
    closed_at TEXT
  );
  CREATE TABLE IF NOT EXISTS case_messages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    case_id TEXT NOT NULL,
    sender TEXT NOT NULL,
    text TEXT NOT NULL,
    time TEXT NOT NULL
  );
  CREATE TABLE IF NOT EXISTS case_logs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    case_id TEXT NOT NULL,
    type TEXT NOT NULL,
    text TEXT NOT NULL,
    time TEXT NOT NULL,
    details TEXT
  );
  CREATE TABLE IF NOT EXISTS notifications (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    case_id TEXT NOT NULL,
    title TEXT NOT NULL,
    client_name TEXT NOT NULL,
    status TEXT NOT NULL,
    device_model TEXT NOT NULL,
    message TEXT NOT NULL,
    created_at TEXT NOT NULL,
    mode TEXT NOT NULL,
    delivery TEXT NOT NULL,
    error TEXT
  );
  CREATE TABLE IF NOT EXISTS users (
    username TEXT PRIMARY KEY,
    display_name TEXT NOT NULL,
    role TEXT NOT NULL,
    password_salt TEXT NOT NULL,
    password_hash TEXT NOT NULL,
    active INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
  );
  CREATE TABLE IF NOT EXISTS training_conversations (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    source TEXT NOT NULL,
    contact_name TEXT NOT NULL,
    owner_aliases TEXT NOT NULL,
    imported_at TEXT NOT NULL,
    message_count INTEGER NOT NULL,
    client_message_count INTEGER NOT NULL,
    agent_message_count INTEGER NOT NULL,
    first_client_message TEXT,
    last_agent_message TEXT,
    notes TEXT,
    conversation_type TEXT,
    service_line TEXT,
    outcome TEXT,
    requested_fields TEXT,
    learned_pattern TEXT,
    confidence REAL
  );
  CREATE TABLE IF NOT EXISTS training_messages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    conversation_id INTEGER NOT NULL,
    role TEXT NOT NULL,
    sender_name TEXT NOT NULL,
    sent_at TEXT NOT NULL,
    text TEXT NOT NULL
  );
  CREATE TABLE IF NOT EXISTS price_offers (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    source TEXT NOT NULL,
    supplier_name TEXT NOT NULL,
    service_name TEXT NOT NULL,
    variant TEXT,
    currency TEXT NOT NULL,
    cost REAL NOT NULL,
    raw_text TEXT,
    notes TEXT,
    imported_at TEXT NOT NULL
  );
  CREATE TABLE IF NOT EXISTS telegram_sources (
    chat_id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    chat_type TEXT,
    username TEXT,
    enabled INTEGER NOT NULL DEFAULT 1,
    auto_import INTEGER NOT NULL DEFAULT 1,
    notes TEXT,
    last_synced_at TEXT,
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
  );
  CREATE TABLE IF NOT EXISTS telegram_messages (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    chat_id TEXT NOT NULL,
    message_id TEXT NOT NULL,
    chat_title TEXT,
    sender_name TEXT,
    text TEXT NOT NULL,
    sent_at TEXT NOT NULL,
    imported_at TEXT NOT NULL,
    detected_supplier TEXT,
    detected_service TEXT,
    detected_variant TEXT,
    detected_currency TEXT,
    detected_cost REAL,
    imported_offer_id INTEGER,
    UNIQUE(chat_id, message_id)
  );
  CREATE TABLE IF NOT EXISTS metadata (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
  );
`);

try {
  db.exec(`ALTER TABLE cases ADD COLUMN assignee TEXT`);
} catch (error) {
  // column already exists
}

try {
  db.exec(`ALTER TABLE cases ADD COLUMN close_reason TEXT`);
} catch (error) {
  // column already exists
}

try {
  db.exec(`ALTER TABLE cases ADD COLUMN created_at TEXT`);
} catch (error) {
  // column already exists
}

try {
  db.exec(`ALTER TABLE cases ADD COLUMN closed_at TEXT`);
} catch (error) {
  // column already exists
}

[
  `ALTER TABLE training_conversations ADD COLUMN conversation_type TEXT`,
  `ALTER TABLE training_conversations ADD COLUMN service_line TEXT`,
  `ALTER TABLE training_conversations ADD COLUMN outcome TEXT`,
  `ALTER TABLE training_conversations ADD COLUMN requested_fields TEXT`,
  `ALTER TABLE training_conversations ADD COLUMN learned_pattern TEXT`,
  `ALTER TABLE training_conversations ADD COLUMN confidence REAL`,
].forEach((statement) => {
  try {
    db.exec(statement);
  } catch (error) {
    // column already exists
  }
});

function readJsonIfExists(filePath, fallback) {
  if (!fs.existsSync(filePath)) {
    return fallback;
  }

  try {
    return JSON.parse(fs.readFileSync(filePath, "utf8").replace(/^\uFEFF/, ""));
  } catch (error) {
    return fallback;
  }
}

function tableCount(tableName) {
  return db.prepare(`SELECT COUNT(*) AS count FROM ${tableName}`).get().count;
}

function saveMetadata(key, value) {
  db.prepare(
    `INSERT INTO metadata (key, value) VALUES (?, ?)
     ON CONFLICT(key) DO UPDATE SET value = excluded.value`
  ).run(key, JSON.stringify(value));
}

function getMetadata(key, fallback = null) {
  const row = db.prepare(`SELECT value FROM metadata WHERE key = ?`).get(key);
  if (!row) {
    return fallback;
  }

  try {
    return JSON.parse(row.value);
  } catch (error) {
    return fallback;
  }
}

function insertProcedure(procedure) {
  db.prepare(
    `INSERT OR REPLACE INTO procedures (id, name, category, description)
     VALUES (?, ?, ?, ?)`
  ).run(procedure.id, procedure.name, procedure.category, procedure.description);

  db.prepare(`DELETE FROM procedure_steps WHERE procedure_id = ?`).run(procedure.id);
  const insertStep = db.prepare(
    `INSERT INTO procedure_steps (procedure_id, step_order, step_text)
     VALUES (?, ?, ?)`
  );
  procedure.steps.forEach((step, index) => {
    insertStep.run(procedure.id, index + 1, step);
  });
}

function upsertCase(caseItem) {
  const existing = db.prepare(`SELECT 1 FROM cases WHERE id = ?`).get(caseItem.id);

  if (existing) {
    db.prepare(
      `UPDATE cases SET
        client_name = ?,
        contact = ?,
        category = ?,
        priority = ?,
        status = ?,
        device_model = ?,
        summary = ?,
        procedure_id = ?,
        current_step = ?,
        last_update = ?,
        ai_mode = ?,
        assignee = ?,
        close_reason = ?,
        created_at = ?,
        closed_at = ?
      WHERE id = ?`
    ).run(
      caseItem.clientName,
      caseItem.contact,
      caseItem.category,
      caseItem.priority,
      caseItem.status,
      caseItem.deviceModel,
      caseItem.summary,
      caseItem.procedureId,
      caseItem.currentStep,
      caseItem.lastUpdate,
      caseItem.aiMode || "fallback",
      caseItem.assignee || null,
      caseItem.closeReason || null,
      caseItem.createdAt || new Date().toISOString(),
      caseItem.closedAt || null,
      caseItem.id
    );
  } else {
    db.prepare(
      `INSERT INTO cases (
        id, client_name, contact, category, priority, status, device_model,
        summary, procedure_id, current_step, last_update, ai_mode, assignee, close_reason,
        created_at, closed_at
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
    ).run(
      caseItem.id,
      caseItem.clientName,
      caseItem.contact,
      caseItem.category,
      caseItem.priority,
      caseItem.status,
      caseItem.deviceModel,
      caseItem.summary,
      caseItem.procedureId,
      caseItem.currentStep,
      caseItem.lastUpdate,
      caseItem.aiMode || "fallback",
      caseItem.assignee || null,
      caseItem.closeReason || null,
      caseItem.createdAt || new Date().toISOString(),
      caseItem.closedAt || null
    );
  }

  db.prepare(`DELETE FROM case_messages WHERE case_id = ?`).run(caseItem.id);
  db.prepare(`DELETE FROM case_logs WHERE case_id = ?`).run(caseItem.id);

  const insertMessage = db.prepare(
    `INSERT INTO case_messages (case_id, sender, text, time) VALUES (?, ?, ?, ?)`
  );
  for (const message of caseItem.messages || []) {
    insertMessage.run(caseItem.id, message.sender, message.text, message.time);
  }

  const insertLog = db.prepare(
    `INSERT INTO case_logs (case_id, type, text, time, details) VALUES (?, ?, ?, ?, ?)`
  );
  for (const log of caseItem.logs || []) {
    insertLog.run(caseItem.id, log.type, log.text, log.time, log.details || null);
  }
}

function getProcedures() {
  const procedures = db.prepare(`SELECT * FROM procedures ORDER BY id`).all();
  const steps = db.prepare(`SELECT * FROM procedure_steps ORDER BY procedure_id, step_order`).all();

  return procedures.map((procedure) => ({
    id: procedure.id,
    name: procedure.name,
    category: procedure.category,
    description: procedure.description,
    steps: steps
      .filter((step) => step.procedure_id === procedure.id)
      .map((step) => step.step_text),
  }));
}

function getCases() {
  const cases = db.prepare(`SELECT * FROM cases ORDER BY id DESC`).all();
  const messages = db.prepare(`SELECT * FROM case_messages ORDER BY id ASC`).all();
  const logs = db.prepare(`SELECT * FROM case_logs ORDER BY id DESC`).all();

  return cases.map((item) => ({
    id: item.id,
    clientName: item.client_name,
    contact: item.contact,
    category: item.category,
    priority: item.priority,
    status: item.status,
    deviceModel: item.device_model,
    summary: item.summary,
    procedureId: item.procedure_id,
    currentStep: item.current_step,
    lastUpdate: item.last_update,
    aiMode: item.ai_mode,
    assignee: item.assignee,
    closeReason: item.close_reason,
    createdAt: item.created_at,
    closedAt: item.closed_at,
    messages: messages
      .filter((message) => message.case_id === item.id)
      .map((message) => ({
        sender: message.sender,
        text: message.text,
        time: message.time,
      })),
    logs: logs
      .filter((log) => log.case_id === item.id)
      .map((log) => ({
        type: log.type,
        text: log.text,
        time: log.time,
        details: log.details,
      })),
  }));
}

function getCaseById(caseId) {
  return getCases().find((item) => item.id === caseId) || null;
}

function getNextCaseId() {
  const row = db
    .prepare(`SELECT id FROM cases WHERE id LIKE 'CASE-%' ORDER BY CAST(SUBSTR(id, 6) AS INTEGER) DESC LIMIT 1`)
    .get();
  const nextNumber = row ? Number(row.id.replace("CASE-", "")) + 1 : 1;
  return `CASE-${String(nextNumber).padStart(3, "0")}`;
}

function saveNotification(entry) {
  db.prepare(
    `INSERT INTO notifications (
      case_id, title, client_name, status, device_model, message,
      created_at, mode, delivery, error
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
  ).run(
    entry.caseId,
    entry.title,
    entry.clientName,
    entry.status,
    entry.deviceModel,
    entry.message,
    entry.createdAt,
    entry.mode,
    entry.delivery,
    entry.error || null
  );
}

function getNotifications(limit = 100) {
  return db
    .prepare(`SELECT * FROM notifications ORDER BY id DESC LIMIT ?`)
    .all(limit)
    .map((row) => ({
      caseId: row.case_id,
      title: row.title,
      clientName: row.client_name,
      status: row.status,
      deviceModel: row.device_model,
      message: row.message,
      createdAt: row.created_at,
      mode: row.mode,
      delivery: row.delivery,
      error: row.error,
    }));
}

function saveUser(user) {
  db.prepare(
    `INSERT INTO users (
      username, display_name, role, password_salt, password_hash, active, created_at, updated_at
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
    ON CONFLICT(username) DO UPDATE SET
      display_name = excluded.display_name,
      role = excluded.role,
      password_salt = excluded.password_salt,
      password_hash = excluded.password_hash,
      active = excluded.active,
      updated_at = excluded.updated_at`
  ).run(
    user.username,
    user.displayName,
    user.role,
    user.passwordSalt,
    user.passwordHash,
    user.active ? 1 : 0,
    user.createdAt,
    user.updatedAt
  );
}

function getUsers() {
  return db
    .prepare(`SELECT * FROM users ORDER BY created_at ASC`)
    .all()
    .map((row) => ({
      username: row.username,
      displayName: row.display_name,
      role: row.role,
      active: Boolean(row.active),
      createdAt: row.created_at,
      updatedAt: row.updated_at,
    }));
}

function getUserByUsername(username) {
  const row = db.prepare(`SELECT * FROM users WHERE username = ?`).get(username);
  if (!row) {
    return null;
  }

  return {
    username: row.username,
    displayName: row.display_name,
    role: row.role,
    passwordSalt: row.password_salt,
    passwordHash: row.password_hash,
    active: Boolean(row.active),
    createdAt: row.created_at,
    updatedAt: row.updated_at,
  };
}

function saveTrainingConversation(conversation) {
  const result = db.prepare(
    `INSERT INTO training_conversations (
      source, contact_name, owner_aliases, imported_at, message_count,
      client_message_count, agent_message_count, first_client_message,
      last_agent_message, notes, conversation_type, service_line, outcome,
      requested_fields, learned_pattern, confidence
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
  ).run(
    conversation.source,
    conversation.contactName,
    JSON.stringify(conversation.ownerAliases || []),
    conversation.importedAt,
    conversation.messageCount,
    conversation.clientMessageCount,
    conversation.agentMessageCount,
    conversation.firstClientMessage || null,
    conversation.lastAgentMessage || null,
    conversation.notes || null,
    conversation.conversationType || null,
    conversation.serviceLine || null,
    conversation.outcome || null,
    JSON.stringify(conversation.requestedFields || []),
    conversation.learnedPattern || null,
    conversation.confidence ?? null
  );

  const conversationId = Number(result.lastInsertRowid);
  const insertMessage = db.prepare(
    `INSERT INTO training_messages (
      conversation_id, role, sender_name, sent_at, text
    ) VALUES (?, ?, ?, ?, ?)`
  );

  for (const message of conversation.messages || []) {
    insertMessage.run(
      conversationId,
      message.role,
      message.senderName,
      message.sentAt,
      message.text
    );
  }

  return conversationId;
}

function getTrainingConversations(limit = 12) {
  const conversations = db
    .prepare(`SELECT * FROM training_conversations ORDER BY id DESC LIMIT ?`)
    .all(limit);
  const messages = db
    .prepare(`SELECT * FROM training_messages WHERE conversation_id IN (
      SELECT id FROM training_conversations ORDER BY id DESC LIMIT ?
    ) ORDER BY id ASC`)
    .all(limit);

  return conversations.map((item) => ({
    id: item.id,
    source: item.source,
    contactName: item.contact_name,
    ownerAliases: JSON.parse(item.owner_aliases || "[]"),
    importedAt: item.imported_at,
    messageCount: item.message_count,
    clientMessageCount: item.client_message_count,
    agentMessageCount: item.agent_message_count,
    firstClientMessage: item.first_client_message,
    lastAgentMessage: item.last_agent_message,
    notes: item.notes,
    conversationType: item.conversation_type,
    serviceLine: item.service_line,
    outcome: item.outcome,
    requestedFields: JSON.parse(item.requested_fields || "[]"),
    learnedPattern: item.learned_pattern,
    confidence: item.confidence !== null ? Number(item.confidence) : null,
    messages: messages
      .filter((message) => message.conversation_id === item.id)
      .map((message) => ({
        role: message.role,
        senderName: message.sender_name,
        sentAt: message.sent_at,
        text: message.text,
      })),
  }));
}

function getTrainingSummary() {
  const row = db.prepare(
    `SELECT
      COUNT(*) AS total_conversations,
      COALESCE(SUM(message_count), 0) AS total_messages,
      COALESCE(SUM(client_message_count), 0) AS total_client_messages,
      COALESCE(SUM(agent_message_count), 0) AS total_agent_messages
     FROM training_conversations`
  ).get();

  return {
    totalConversations: row.total_conversations,
    totalMessages: row.total_messages,
    totalClientMessages: row.total_client_messages,
    totalAgentMessages: row.total_agent_messages,
  };
}

function getTrainingInsights() {
  const conversations = getTrainingConversations(200);
  const byType = new Map();
  const byOutcome = new Map();
  const byField = new Map();

  for (const item of conversations) {
    const typeKey = item.conversationType || "general";
    const outcomeKey = item.outcome || "abierto";
    byType.set(typeKey, (byType.get(typeKey) || 0) + 1);
    byOutcome.set(outcomeKey, (byOutcome.get(outcomeKey) || 0) + 1);

    for (const field of item.requestedFields || []) {
      byField.set(field, (byField.get(field) || 0) + 1);
    }
  }

  const sortMap = (map) =>
    [...map.entries()]
      .sort((a, b) => b[1] - a[1])
      .map(([label, total]) => ({ label, total }));

  return {
    topConversationTypes: sortMap(byType).slice(0, 5),
    topOutcomes: sortMap(byOutcome).slice(0, 5),
    topRequestedFields: sortMap(byField).slice(0, 8),
  };
}

function savePriceOffer(offer) {
  const result = db.prepare(
    `INSERT INTO price_offers (
      source, supplier_name, service_name, variant, currency, cost, raw_text, notes, imported_at
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`
  ).run(
    offer.source,
    offer.supplierName,
    offer.serviceName,
    offer.variant || null,
    offer.currency,
    Number(offer.cost),
    offer.rawText || null,
    offer.notes || null,
    offer.importedAt
  );

  return Number(result.lastInsertRowid);
}

function getPriceOffers(limit = 100) {
  return db
    .prepare(`SELECT * FROM price_offers ORDER BY imported_at DESC, id DESC LIMIT ?`)
    .all(limit)
    .map((row) => ({
      id: row.id,
      source: row.source,
      supplierName: row.supplier_name,
      serviceName: row.service_name,
      variant: row.variant,
      currency: row.currency,
      cost: Number(row.cost),
      rawText: row.raw_text,
      notes: row.notes,
      importedAt: row.imported_at,
    }));
}

function saveTelegramSource(source) {
  db.prepare(
    `INSERT INTO telegram_sources (
      chat_id, title, chat_type, username, enabled, auto_import, notes, last_synced_at, created_at, updated_at
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    ON CONFLICT(chat_id) DO UPDATE SET
      title = excluded.title,
      chat_type = excluded.chat_type,
      username = excluded.username,
      enabled = excluded.enabled,
      auto_import = excluded.auto_import,
      notes = excluded.notes,
      last_synced_at = excluded.last_synced_at,
      updated_at = excluded.updated_at`
  ).run(
    String(source.chatId),
    source.title,
    source.chatType || null,
    source.username || null,
    source.enabled ? 1 : 0,
    source.autoImport ? 1 : 0,
    source.notes || null,
    source.lastSyncedAt || null,
    source.createdAt,
    source.updatedAt
  );
}

function getTelegramSources() {
  return db
    .prepare(`SELECT * FROM telegram_sources ORDER BY updated_at DESC, title ASC`)
    .all()
    .map((row) => ({
      chatId: row.chat_id,
      title: row.title,
      chatType: row.chat_type,
      username: row.username,
      enabled: Boolean(row.enabled),
      autoImport: Boolean(row.auto_import),
      notes: row.notes,
      lastSyncedAt: row.last_synced_at,
      createdAt: row.created_at,
      updatedAt: row.updated_at,
    }));
}

function saveTelegramMessage(message) {
  const result = db.prepare(
    `INSERT OR IGNORE INTO telegram_messages (
      chat_id, message_id, chat_title, sender_name, text, sent_at, imported_at,
      detected_supplier, detected_service, detected_variant, detected_currency, detected_cost, imported_offer_id
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
  ).run(
    String(message.chatId),
    String(message.messageId),
    message.chatTitle || null,
    message.senderName || null,
    message.text,
    message.sentAt,
    message.importedAt,
    message.detectedSupplier || null,
    message.detectedService || null,
    message.detectedVariant || null,
    message.detectedCurrency || null,
    message.detectedCost ?? null,
    message.importedOfferId ?? null
  );

  return {
    inserted: Number(result.changes || 0) > 0,
    id: Number(result.lastInsertRowid || 0),
  };
}

function attachOfferToTelegramMessage(chatId, messageId, offerId) {
  db.prepare(
    `UPDATE telegram_messages
     SET imported_offer_id = ?
     WHERE chat_id = ? AND message_id = ?`
  ).run(offerId, String(chatId), String(messageId));
}

function getTelegramMessages(limit = 80) {
  return db
    .prepare(`SELECT * FROM telegram_messages ORDER BY sent_at DESC, id DESC LIMIT ?`)
    .all(limit)
    .map((row) => ({
      id: row.id,
      chatId: row.chat_id,
      messageId: row.message_id,
      chatTitle: row.chat_title,
      senderName: row.sender_name,
      text: row.text,
      sentAt: row.sent_at,
      importedAt: row.imported_at,
      detectedSupplier: row.detected_supplier,
      detectedService: row.detected_service,
      detectedVariant: row.detected_variant,
      detectedCurrency: row.detected_currency,
      detectedCost: row.detected_cost !== null ? Number(row.detected_cost) : null,
      importedOfferId: row.imported_offer_id,
    }));
}

function getTelegramSummary() {
  const row = db.prepare(
    `SELECT
      COUNT(*) AS total_messages,
      COUNT(DISTINCT chat_id) AS total_chats,
      COALESCE(SUM(CASE WHEN imported_offer_id IS NOT NULL THEN 1 ELSE 0 END), 0) AS imported_offers
     FROM telegram_messages`
  ).get();

  return {
    totalMessages: row.total_messages,
    totalChats: row.total_chats,
    importedOffers: row.imported_offers,
  };
}

function migrateLegacyData() {
  if (tableCount("procedures") === 0 || tableCount("cases") === 0) {
    const legacyCases = readJsonIfExists(LEGACY_CASES_FILE, { procedures: [], cases: [] });
    for (const procedure of legacyCases.procedures || []) {
      insertProcedure(procedure);
    }
    for (const caseItem of legacyCases.cases || []) {
      upsertCase(caseItem);
    }
  }

  if (!getMetadata("style_profile")) {
    const legacyStyle = readJsonIfExists(LEGACY_STYLE_FILE, null);
    if (legacyStyle) {
      saveMetadata("style_profile", legacyStyle);
    }
  }

  if (tableCount("notifications") === 0) {
    const legacyNotifications = readJsonIfExists(LEGACY_NOTIFICATIONS_FILE, []);
    for (const entry of legacyNotifications) {
      saveNotification(entry);
    }
  }
}

migrateLegacyData();

module.exports = {
  DATA_DIR,
  DB_FILE,
  getCaseById,
  getCases,
  getMetadata,
  getNextCaseId,
  getNotifications,
  getPriceOffers,
  getProcedures,
  getTelegramMessages,
  getTelegramSources,
  getTelegramSummary,
  getTrainingConversations,
  getTrainingInsights,
  getTrainingSummary,
  getUserByUsername,
  getUsers,
  saveMetadata,
  saveNotification,
  savePriceOffer,
  attachOfferToTelegramMessage,
  saveTelegramMessage,
  saveTelegramSource,
  saveTrainingConversation,
  saveUser,
  upsertCase,
};
