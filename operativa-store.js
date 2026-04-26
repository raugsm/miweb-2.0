const { DATA_DIR } = require("./db");
const fs = require("fs");
const crypto = require("crypto");
const path = require("path");

const OPERATIVA_FILE = path.join(DATA_DIR, "operativa-v2.json");

const DEFAULT_CHANNELS = [
  {
    id: "wa-1",
    name: "WhatsApp 1",
    type: "whatsapp",
    serviceScope: "Samsung, senal Claro y servicios remotos",
    status: "ready",
    unread: 0,
    active: true,
  },
  {
    id: "wa-2",
    name: "WhatsApp 2",
    type: "whatsapp",
    serviceScope: "Motorola, Honor, FRP y Unlock",
    status: "ready",
    unread: 0,
    active: true,
  },
  {
    id: "wa-3",
    name: "WhatsApp 3",
    type: "whatsapp",
    serviceScope: "Xiaomi, Tecno, Infinix, iPhone y creditos/licencias",
    status: "ready",
    unread: 0,
    active: true,
  },
];

const DEFAULT_RECEIVERS = [
  { id: "receiver-pe", name: "Receptor Peru", country: "Peru", currency: "PEN", active: true },
  { id: "receiver-cl", name: "Receptor Chile", country: "Chile", currency: "CLP", active: true },
  { id: "receiver-co", name: "Receptor Colombia", country: "Colombia", currency: "COP", active: true },
  { id: "receiver-mx", name: "Receptor Mexico", country: "Mexico", currency: "MXN", active: true },
];

const MESSAGE_CLASSIFIERS = [
  {
    id: "payment_or_receipt",
    label: "Pago / comprobante",
    title: "Validar pago o comprobante",
    action: "Validar pago",
    priority: "alta",
    keywords: [
      "pago",
      "pague",
      "pagué",
      "pagado",
      "comprobante",
      "transferencia",
      "deposito",
      "depósito",
      "yape",
      "plin",
      "nequi",
      "bancolombia",
      "nacion",
      "banco",
    ],
    patterns: [/\b\d+(?:[.,]\d+)?\s*(usd|usdt|soles|pen|mxn|cop|clp)\b/i],
  },
  {
    id: "accounting_debt",
    label: "Cuenta / deuda",
    title: "Revisar cuenta o deuda",
    action: "Registrar cuenta",
    priority: "alta",
    keywords: [
      "deuda",
      "nueva deuda",
      "debe",
      "saldo",
      "cuenta",
      "semanal",
      "reembolso",
      "rembolso",
      "devolver",
      "devolucion",
      "devolución",
    ],
    patterns: [],
  },
  {
    id: "price_request",
    label: "Pregunta precio",
    title: "Cliente pide precio",
    action: "Responder precio",
    priority: "media",
    keywords: ["cuanto", "cuánto", "vale", "sale", "precio", "costo", "cotiza", "cobras", "tarifa"],
    patterns: [/\b(cu[aá]nto|cuanto)\b.*\b(sale|vale|cuesta|es)\b/i, /\b(fr[pi]|unlock|liberaci[oó]n|reset)\b.*\?/i],
  },
  {
    id: "device_or_imei",
    label: "Modelo / IMEI",
    title: "Completar datos del equipo",
    action: "Completar ficha",
    priority: "media",
    keywords: ["imei", "brand", "model", "modelo", "motorola", "samsung", "xiaomi", "huawei", "honor", "tecno", "infinix", "iphone", "moto"],
    patterns: [/\b\d{14,16}\b/, /\b(g\d{1,3}|a\d{2,3}|p\d{2,3}|redmi|note\s*\d+|iphone\s*\d+)\b/i],
  },
  {
    id: "technical_process",
    label: "Proceso tecnico",
    title: "Abrir o actualizar proceso tecnico",
    action: "Abrir proceso",
    priority: "media",
    keywords: [
      "frp",
      "unlock",
      "liberacion",
      "liberación",
      "factory reset",
      "reset",
      "mdm",
      "bypass",
      "firmware",
      "flash",
      "flahs",
      "rom",
      "brom",
      "bootloader",
      "oem",
      "root",
      "kg",
      "knox",
      "icloud",
      "mi account",
      "conecta",
    ],
    patterns: [],
  },
  {
    id: "provider_offer",
    label: "Oferta proveedor",
    title: "Guardar oferta de proveedor",
    action: "Guardar oferta",
    priority: "baja",
    keywords: ["proveedor", "service price update", "level", "instant success", "lista blanca", "subio", "subió", "api"],
    patterns: [],
  },
  {
    id: "procedure_reference",
    label: "Procedimiento",
    title: "Guardar procedimiento o archivo tecnico",
    action: "Guardar procedimiento",
    priority: "media",
    keywords: ["youtube", "youtu.be", "drive.google", "mifirm", "descargar", "archivo", "archivos", "procedimiento", "pasos"],
    patterns: [/https?:\/\//i],
  },
  {
    id: "process_update",
    label: "Estado proceso",
    title: "Actualizar estado del proceso",
    action: "Actualizar proceso",
    priority: "media",
    keywords: ["done", "listo", "completado", "salio", "salió", "ya esta", "ya está", "pendiente", "en proceso"],
    patterns: [/\bard\d{2,}/i],
  },
];

function getDayKey(date = new Date()) {
  return date.toISOString().slice(0, 10);
}

function nowIso() {
  return new Date().toISOString();
}

function ensureOperativaFile() {
  if (!fs.existsSync(DATA_DIR)) {
    fs.mkdirSync(DATA_DIR, { recursive: true });
  }

  if (!fs.existsSync(OPERATIVA_FILE)) {
    fs.writeFileSync(OPERATIVA_FILE, JSON.stringify(createSeedState(), null, 2));
  }
}

function hashText(value) {
  return crypto.createHash("sha256").update(String(value || "").trim().toLowerCase()).digest("hex");
}

function createId(prefix) {
  return `${prefix}-${Date.now().toString(36)}-${crypto.randomBytes(3).toString("hex")}`;
}

function normalizeAmount(value) {
  const number = Number(value);
  return Number.isFinite(number) ? number : 0;
}

function normalizeCurrency(value, fallback = "USD") {
  return String(value || fallback).trim().toUpperCase();
}

function normalizeForMatch(value) {
  return String(value || "")
    .toLowerCase()
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "");
}

function extractMessageSignals(text) {
  const raw = String(text || "");
  const lower = normalizeForMatch(raw);
  const amounts = raw.match(/\b\d+(?:[.,]\d+)?\s*(?:usd|usdt|soles|pen|mxn|cop|clp)\b/gi) || [];
  const imeis = raw.match(/\b\d{14,16}\b/g) || [];
  const links = raw.match(/https?:\/\/\S+|(?:drive|wa|youtu)\.[^\s]+/gi) || [];
  const devices = [];

  for (const match of raw.matchAll(/\b(?:moto\s*)?(?:g\d{1,3}|a\d{2,3}|p\d{2,3}|redmi\s*[\w\s-]{0,12}|note\s*\d+|iphone\s*\d+|samsung\s*[\w-]{1,12}|xiaomi\s*[\w-]{1,12})\b/gi)) {
    devices.push(match[0].trim());
  }

  return {
    lower,
    amounts: [...new Set(amounts)],
    imeis: [...new Set(imeis)],
    links: [...new Set(links)],
    devices: [...new Set(devices)].slice(0, 5),
  };
}

function classifyWhatsappMessage(data) {
  const text = String(data.text || "");
  const signals = extractMessageSignals(text);
  const matches = [];

  for (const classifier of MESSAGE_CLASSIFIERS) {
    let score = 0;
    const reasons = [];

    for (const keyword of classifier.keywords || []) {
      if (signals.lower.includes(normalizeForMatch(keyword))) {
        score += keyword.length > 8 ? 2 : 1;
        reasons.push(keyword);
      }
    }

    for (const pattern of classifier.patterns || []) {
      if (pattern.test(text)) {
        score += 2;
        reasons.push(pattern.toString());
      }
    }

    if (classifier.id === "device_or_imei") {
      score += signals.imeis.length * 2 + signals.devices.length;
    }

    if (classifier.id === "procedure_reference") {
      score += signals.links.length;
    }

    if (classifier.id === "provider_offer" && /(service price update|proveedor|instant success|lista blanca|subio|subió)/i.test(signals.lower)) {
      score += 3;
    }

    if (classifier.id === "technical_process" && /(operation|factory reset|frp|unlock|brom|firmware|flash|rom)/i.test(signals.lower)) {
      score += 4;
    }

    if (score > 0) {
      matches.push({
        id: classifier.id,
        label: classifier.label,
        title: classifier.title,
        action: classifier.action,
        priority: classifier.priority,
        score,
        confidence: Math.min(0.95, 0.48 + score * 0.08),
        reasons: [...new Set(reasons)].slice(0, 5),
      });
    }
  }

  matches.sort((left, right) => {
    const priorityWeight = { alta: 3, media: 2, baja: 1 };
    return right.score - left.score || (priorityWeight[right.priority] || 0) - (priorityWeight[left.priority] || 0);
  });

  const primary = matches[0] || {
    id: "no_action",
    label: "Sin accion clara",
    title: "Mensaje observado",
    action: "Observar",
    priority: "baja",
    score: 0,
    confidence: 0.35,
    reasons: [],
  };

  return {
    intent: primary.id,
    label: primary.label,
    action: primary.action,
    priority: primary.priority,
    confidence: primary.confidence,
    requiresHumanReview: primary.id !== "no_action",
    reasons: primary.reasons,
    matches: matches.slice(0, 3).map((item) => ({
      intent: item.id,
      label: item.label,
      confidence: item.confidence,
    })),
    extracted: {
      amounts: signals.amounts,
      imeis: signals.imeis,
      links: signals.links,
      devices: signals.devices,
    },
  };
}

function normalizeState(rawState) {
  if (!rawState || typeof rawState !== "object" || rawState.version < 3) {
    return createSeedState();
  }

  return {
    ...createSeedState(),
    ...rawState,
    version: 3,
    channels: Array.isArray(rawState.channels) && rawState.channels.length ? rawState.channels : DEFAULT_CHANNELS,
    receivers: Array.isArray(rawState.receivers) && rawState.receivers.length ? rawState.receivers : DEFAULT_RECEIVERS,
    conversations: rawState.conversations || [],
    messages: rawState.messages || [],
    attachments: rawState.attachments || [],
    customers: rawState.customers || [],
    paymentEvidences: rawState.paymentEvidences || [],
    serviceOrders: rawState.serviceOrders || [],
    ledgerEntries: rawState.ledgerEntries || [],
    customerBalances: rawState.customerBalances || [],
    customerIdentities: rawState.customerIdentities || [],
    crossChannelPaymentLinks: rawState.crossChannelPaymentLinks || [],
    receiverSettlements: rawState.receiverSettlements || [],
    reviewItems: rawState.reviewItems || [],
    weeklyAccounts: rawState.weeklyAccounts || [],
    events: rawState.events || [],
    agentCheckpoints: rawState.agentCheckpoints || [],
    auditLog: rawState.auditLog || [],
    agent: {
      ...createSeedState().agent,
      ...(rawState.agent || {}),
    },
  };
}

function readOperativaState() {
  ensureOperativaFile();
  try {
    return normalizeState(JSON.parse(fs.readFileSync(OPERATIVA_FILE, "utf8")));
  } catch (error) {
    const seed = createSeedState();
    fs.writeFileSync(OPERATIVA_FILE, JSON.stringify(seed, null, 2));
    return seed;
  }
}

function writeOperativaState(nextState) {
  ensureOperativaFile();
  const normalized = normalizeState(nextState);
  fs.writeFileSync(OPERATIVA_FILE, JSON.stringify(normalized, null, 2));
  return normalized;
}

function createSeedState() {
  return {
    version: 3,
    dayKey: getDayKey(),
    channels: DEFAULT_CHANNELS,
    receivers: DEFAULT_RECEIVERS,
    conversations: [],
    messages: [],
    attachments: [],
    customers: [],
    paymentEvidences: [],
    serviceOrders: [],
    ledgerEntries: [],
    customerBalances: [],
    customerIdentities: [],
    crossChannelPaymentLinks: [],
    receiverSettlements: [],
    reviewItems: [],
    weeklyAccounts: [],
    events: [],
    agentCheckpoints: [],
    auditLog: [],
    agent: {
      mode: "observador",
      autonomyLevel: 1,
      connected: false,
      lastHeartbeat: null,
      message:
        "Cabina lista. Falta que el agente visual empiece a enviar eventos reales desde los 3 WhatsApp.",
    },
  };
}

function pushAudit(state, entry) {
  state.auditLog.unshift({
    id: createId("audit"),
    actor: entry.actor || "system",
    action: entry.action,
    entityType: entry.entityType,
    entityId: entry.entityId,
    before: entry.before || null,
    after: entry.after || null,
    createdAt: nowIso(),
  });
  state.auditLog = state.auditLog.slice(0, 500);
}

function addReviewItem(state, item) {
  const dedupeKey = item.dedupeKey || hashText(`${item.type}:${item.relatedEntityType}:${item.relatedEntityId}:${item.title}`);
  const existing = state.reviewItems.find(
    (review) => review.status === "pendiente" && review.dedupeKey === dedupeKey
  );

  if (existing) {
    existing.description = item.description || existing.description;
    existing.confidence = Math.max(existing.confidence || 0, item.confidence || 0);
    existing.updatedAt = nowIso();
    return existing;
  }

  const review = {
    id: item.id || createId("rev"),
    type: item.type || "general",
    title: item.title || "Revision pendiente",
    description: item.description || "",
    confidence: item.confidence ?? 0.5,
    priority: item.priority || "media",
    relatedEntityType: item.relatedEntityType || null,
    relatedEntityId: item.relatedEntityId || null,
    status: item.status || "pendiente",
    dedupeKey,
    createdAt: nowIso(),
    resolvedAt: null,
  };
  state.reviewItems.unshift(review);
  return review;
}

function upsertById(collection, item) {
  const index = collection.findIndex((entry) => entry.id === item.id);
  if (index >= 0) {
    collection[index] = { ...collection[index], ...item, updatedAt: nowIso() };
    return collection[index];
  }
  collection.unshift(item);
  return item;
}

function upsertConversation(state, data) {
  const channelId = data.channelId || data.channel || "wa-1";
  const title = data.title || data.contactName || data.customerName || "Chat sin nombre";
  const phoneOrGroupId = data.phoneOrGroupId || data.phone || data.groupId || title;
  const conversationId = data.conversationId || hashText(`${channelId}:${phoneOrGroupId}`).slice(0, 16);
  const existing = state.conversations.find((item) => item.id === conversationId);
  const conversation = {
    id: conversationId,
    channelId,
    title,
    phoneOrGroupId,
    conversationType: data.conversationType || "direct",
    customerId: data.customerId || existing?.customerId || null,
    lastSeenMessageKey: data.lastSeenMessageKey || existing?.lastSeenMessageKey || null,
    lastScannedAt: data.lastScannedAt || nowIso(),
    createdAt: existing?.createdAt || nowIso(),
    updatedAt: nowIso(),
  };
  upsertById(state.conversations, conversation);
  return conversation;
}

function upsertCustomer(state, data) {
  const name = data.customerName || data.name || data.contactName || "Cliente sin nombre";
  const identityKey = hashText(`${name}:${data.phone || data.alias || ""}`).slice(0, 16);
  const id = data.customerId || `cus-${identityKey}`;
  const existing = state.customers.find((item) => item.id === id);
  const customer = {
    id,
    displayName: name,
    phone: data.phone || existing?.phone || null,
    country: data.country || existing?.country || null,
    notes: data.notes || existing?.notes || null,
    createdAt: existing?.createdAt || nowIso(),
    updatedAt: nowIso(),
  };
  upsertById(state.customers, customer);
  return customer;
}

function ingestWhatsappMessage(state, data) {
  const conversation = upsertConversation(state, data);
  const messageKey = String(data.messageKey || data.messageId || hashText(`${conversation.id}:${data.sentAt || nowIso()}:${data.text}`));
  const existing = state.messages.find((item) => item.conversationId === conversation.id && item.messageKey === messageKey);

  if (existing) {
    if (!existing.classification) {
      existing.classification = classifyWhatsappMessage(existing);
    }
    return existing;
  }

  const classification = classifyWhatsappMessage({ ...data, channelId: conversation.channelId });
  const message = {
    id: createId("msg"),
    conversationId: conversation.id,
    channelId: conversation.channelId,
    messageKey,
    senderName: data.senderName || "",
    text: data.text || "",
    sentAt: data.sentAt || nowIso(),
    capturedAt: nowIso(),
    direction: data.direction || "unknown",
    dayKey: data.dayKey || getDayKey(data.sentAt ? new Date(data.sentAt) : new Date()),
    hash: hashText(`${conversation.id}:${messageKey}:${data.text || ""}`),
    processed: Boolean(data.processed),
    classification,
  };

  state.messages.unshift(message);
  conversation.lastSeenMessageKey = messageKey;
  conversation.lastScannedAt = nowIso();

  if (message.direction === "client") {
    const channel = state.channels.find((item) => item.id === conversation.channelId);
    if (channel) {
      channel.unread = Number(channel.unread || 0) + 1;
    }
  }

  const lowerText = message.text.toLowerCase();
  if (lowerText.includes("otro whatsapp") || lowerText.includes("otro numero")) {
    addReviewItem(state, {
      type: "cross_channel_hint",
      title: "Cliente menciona otro WhatsApp",
      description: message.text,
      confidence: 0.74,
      priority: "media",
      relatedEntityType: "message",
      relatedEntityId: message.id,
    });
  }

  if (lowerText.includes("pague") || lowerText.includes("pago") || lowerText.includes("comprobante")) {
    addReviewItem(state, {
      type: "payment_text_hint",
      title: "Mensaje parece hablar de un pago",
      description: message.text,
      confidence: 0.62,
      priority: "media",
      relatedEntityType: "message",
      relatedEntityId: message.id,
    });
  }

  if (classification.requiresHumanReview) {
    const classifier = MESSAGE_CLASSIFIERS.find((item) => item.id === classification.intent);
    addReviewItem(state, {
      type: `message_${classification.intent}`,
      title: classifier?.title || classification.label,
      description: message.text,
      confidence: classification.confidence,
      priority: classification.priority,
      relatedEntityType: "message",
      relatedEntityId: message.id,
      dedupeKey: `message-classification:${message.hash}:${classification.intent}`,
    });
  }

  return message;
}

function buildReceiptHash(data) {
  return hashText(
    [
      data.operationCode,
      data.amount,
      normalizeCurrency(data.currency, ""),
      data.paymentDatetime,
      data.receiverId,
      data.ocrText,
      data.rawText,
    ].join("|")
  );
}

function ingestPaymentEvidence(state, data) {
  const customer = upsertCustomer(state, data);
  const conversation = upsertConversation(state, { ...data, customerId: customer.id });
  const amount = normalizeAmount(data.amount);
  const currency = normalizeCurrency(data.currency, "USD");
  const receiptHash = data.receiptHash || buildReceiptHash({ ...data, amount, currency });
  const duplicate = state.paymentEvidences.find((item) => item.receiptHash === receiptHash);
  const detectedAt = nowIso();
  const paymentDate = data.paymentDatetime || data.sentAt || detectedAt;
  const dayKey = data.dayKey || getDayKey(new Date(paymentDate));
  const isOld = dayKey < getDayKey();

  const status =
    duplicate ? "duplicate_suspect" : data.status || (data.evidenceType === "text" ? "text_only" : isOld ? "old_reusable" : "detected");

  const payment = {
    id: data.id || createId("pay"),
    customerId: customer.id,
    conversationId: conversation.id,
    channelId: conversation.channelId,
    sourceMessageId: data.sourceMessageId || null,
    evidenceType: data.evidenceType || "text",
    amount,
    currency,
    country: data.country || customer.country || null,
    paymentMethod: data.paymentMethod || null,
    receiverId: data.receiverId || null,
    operationCode: data.operationCode || null,
    paymentDatetime: paymentDate,
    confidence: data.confidence ?? 0.65,
    duplicateScore: duplicate ? 1 : data.duplicateScore || 0,
    status,
    receiptHash,
    rawText: data.rawText || data.ocrText || null,
    createdAt: detectedAt,
    updatedAt: detectedAt,
  };

  upsertById(state.paymentEvidences, payment);

  if (["verified", "applied"].includes(payment.status)) {
    state.ledgerEntries.unshift({
      id: createId("led"),
      dayKey,
      type: "income",
      amount,
      currency,
      relatedPaymentId: payment.id,
      receiverId: payment.receiverId,
      status: "confirmed",
      createdAt: detectedAt,
    });
  }

  if (payment.receiverId) {
    upsertReceiverSettlement(state, {
      receiverId: payment.receiverId,
      dayKey,
      amount,
      currency,
      type: "received",
    });
  }

  if (duplicate) {
    addReviewItem(state, {
      type: "duplicate_receipt",
      title: "Posible comprobante repetido",
      description: `Coincide con un pago anterior de ${duplicate.amount} ${duplicate.currency}.`,
      confidence: 0.95,
      priority: "alta",
      relatedEntityType: "payment_evidence",
      relatedEntityId: payment.id,
    });
  } else if (payment.status === "text_only") {
    addReviewItem(state, {
      type: "text_payment",
      title: "Pago por texto sin comprobante",
      description: payment.rawText || "El cliente menciona pago, pero no hay imagen adjunta.",
      confidence: payment.confidence,
      priority: "media",
      relatedEntityType: "payment_evidence",
      relatedEntityId: payment.id,
    });
  } else if (payment.status === "old_reusable") {
    addReviewItem(state, {
      type: "old_payment",
      title: "Pago de dia anterior",
      description: `Pago detectado con fecha ${dayKey}. Validar si es saldo disponible, consumido o devolucion.`,
      confidence: payment.confidence,
      priority: "media",
      relatedEntityType: "payment_evidence",
      relatedEntityId: payment.id,
    });
  }

  return payment;
}

function ingestServiceOrder(state, data) {
  const customer = upsertCustomer(state, data);
  const conversation = upsertConversation(state, { ...data, customerId: customer.id });
  const order = {
    id: data.id || createId("srv"),
    customerId: customer.id,
    customer: customer.displayName,
    conversationId: conversation.id,
    channel: state.channels.find((item) => item.id === conversation.channelId)?.name || conversation.channelId,
    channelId: conversation.channelId,
    service: data.serviceName || data.service || "Servicio sin clasificar",
    serviceName: data.serviceName || data.service || "Servicio sin clasificar",
    deviceModel: data.deviceModel || null,
    charged: data.charged || `${normalizeAmount(data.chargedAmount)} ${normalizeCurrency(data.chargedCurrency, "USD")}`,
    cost: data.cost || `${normalizeAmount(data.providerCost)} ${normalizeCurrency(data.providerCurrency, "USD")}`,
    chargedAmount: normalizeAmount(data.chargedAmount),
    chargedCurrency: normalizeCurrency(data.chargedCurrency, "USD"),
    providerCost: normalizeAmount(data.providerCost),
    providerCurrency: normalizeCurrency(data.providerCurrency, "USD"),
    status: data.status || "quoted",
    financialState: data.financialState || "pendiente",
    paymentEvidenceId: data.paymentEvidenceId || null,
    weeklyBillable: Boolean(data.weeklyBillable),
    createdAt: data.createdAt || nowIso(),
    completedAt: data.completedAt || null,
    updatedAt: nowIso(),
  };

  upsertById(state.serviceOrders, order);

  if (order.providerCost > 0 && ["in_process", "completed", "closed"].includes(order.status)) {
    state.ledgerEntries.unshift({
      id: createId("led"),
      dayKey: getDayKey(new Date(order.completedAt || order.createdAt)),
      type: "cost",
      amount: order.providerCost,
      currency: order.providerCurrency,
      relatedServiceId: order.id,
      status: "projected",
      createdAt: nowIso(),
    });
  }

  return order;
}

function upsertReceiverSettlement(state, data) {
  const receiverId = data.receiverId;
  const receiver = state.receivers.find((item) => item.id === receiverId);
  if (!receiver) {
    return null;
  }

  const dayKey = data.dayKey || getDayKey();
  const currency = normalizeCurrency(data.currency, receiver.currency);
  const settlementId = `${receiverId}-${dayKey}-${currency}`;
  const existing = state.receiverSettlements.find((item) => item.id === settlementId);
  const settlement = existing || {
    id: settlementId,
    receiverId,
    dayKey,
    currency,
    totalReceived: 0,
    totalSentToOwner: 0,
    pendingAmount: 0,
    status: "pendiente",
    createdAt: nowIso(),
  };

  if (data.type === "sent_to_owner") {
    settlement.totalSentToOwner += normalizeAmount(data.amount);
  } else {
    settlement.totalReceived += normalizeAmount(data.amount);
  }
  settlement.pendingAmount = settlement.totalReceived - settlement.totalSentToOwner;
  settlement.status = settlement.pendingAmount <= 0 ? "cerrado" : settlement.totalSentToOwner > 0 ? "parcial" : "pendiente";
  settlement.updatedAt = nowIso();

  upsertById(state.receiverSettlements, settlement);
  return settlement;
}

function ingestCrossChannelPayment(state, data) {
  const link = {
    id: data.id || createId("xpay"),
    sourceChannelId: data.sourceChannelId,
    targetChannelId: data.targetChannelId,
    paymentEvidenceId: data.paymentEvidenceId || null,
    sourceServiceId: data.sourceServiceId || null,
    targetServiceId: data.targetServiceId || null,
    amountApplied: normalizeAmount(data.amountApplied || data.amount),
    currency: normalizeCurrency(data.currency, "USD"),
    reason: data.reason || "Cliente solicita usar pago entre WhatsApps",
    status: data.status || "pendiente_revision",
    approvedBy: data.approvedBy || null,
    createdAt: nowIso(),
    approvedAt: data.approvedAt || null,
  };

  upsertById(state.crossChannelPaymentLinks, link);
  addReviewItem(state, {
    type: "cross_channel_payment",
    title: "Pago cruzado entre WhatsApps",
    description: link.reason,
    confidence: data.confidence ?? 0.78,
    priority: "media",
    relatedEntityType: "cross_channel_payment_link",
    relatedEntityId: link.id,
  });
  return link;
}

function ingestCustomerIdentity(state, data) {
  const customer = upsertCustomer(state, data);
  const conversation = upsertConversation(state, { ...data, customerId: customer.id });
  const identity = {
    id: data.id || createId("iden"),
    customerId: customer.id,
    channelId: conversation.channelId,
    conversationId: conversation.id,
    phoneOrAlias: data.phoneOrAlias || data.phone || conversation.title,
    confidence: data.confidence ?? 0.7,
    status: data.status || (data.confidence >= 0.9 ? "confirmado" : "pendiente_revision"),
    linkedAt: nowIso(),
  };

  upsertById(state.customerIdentities, identity);
  if (identity.status !== "confirmado") {
    addReviewItem(state, {
      type: "identity_match",
      title: "Posible mismo cliente en otro WhatsApp",
      description: `Coincidencia detectada para ${customer.displayName}.`,
      confidence: identity.confidence,
      priority: "media",
      relatedEntityType: "customer_identity",
      relatedEntityId: identity.id,
    });
  }
  return identity;
}

function ingestWeeklyAccount(state, data) {
  const weekly = {
    id: data.id || createId("week"),
    customer: data.customer || data.customerName || "Cliente semanal",
    customerId: data.customerId || null,
    completed: Number(data.completed || 0),
    pending: Number(data.pending || 0),
    failed: Number(data.failed || 0),
    total: data.total || `${normalizeAmount(data.totalAmount)} ${normalizeCurrency(data.currency, "USD")}`,
    status: data.status || "acumulando",
    weekKey: data.weekKey || getDayKey(),
    updatedAt: nowIso(),
  };
  upsertById(state.weeklyAccounts, weekly);
  return weekly;
}

function updateAgentStatus(state, data) {
  state.agent = {
    ...state.agent,
    mode: data.mode || state.agent.mode,
    autonomyLevel: Number(data.autonomyLevel || state.agent.autonomyLevel || 1),
    connected: data.connected ?? state.agent.connected,
    lastHeartbeat: data.lastHeartbeat || nowIso(),
    message: data.message || state.agent.message,
  };
  return state.agent;
}

function updateCheckpoint(state, data) {
  const checkpoint = {
    id: data.id || `${data.channelId || "wa"}-${data.conversationId || "global"}-${data.dayKey || getDayKey()}`,
    channelId: data.channelId || null,
    conversationId: data.conversationId || null,
    dayKey: data.dayKey || getDayKey(),
    lastMessageKey: data.lastMessageKey || null,
    lastCaptureHash: data.lastCaptureHash || null,
    lastScannedAt: data.lastScannedAt || nowIso(),
  };
  upsertById(state.agentCheckpoints, checkpoint);
  return checkpoint;
}

function ingestOperativaEvent(payload, actor = "visual_agent") {
  const state = readOperativaState();
  const type = payload.type || payload.eventType;
  const data = payload.data || payload;
  let entity = null;
  let entityType = type;

  switch (type) {
    case "whatsapp_message":
      entity = ingestWhatsappMessage(state, data);
      entityType = "message";
      break;
    case "payment_evidence":
      entity = ingestPaymentEvidence(state, data);
      entityType = "payment_evidence";
      break;
    case "service_order":
      entity = ingestServiceOrder(state, data);
      entityType = "service_order";
      break;
    case "receiver_settlement":
      entity = upsertReceiverSettlement(state, data);
      entityType = "receiver_settlement";
      break;
    case "cross_channel_payment":
      entity = ingestCrossChannelPayment(state, data);
      entityType = "cross_channel_payment";
      break;
    case "customer_identity":
      entity = ingestCustomerIdentity(state, data);
      entityType = "customer_identity";
      break;
    case "weekly_account":
      entity = ingestWeeklyAccount(state, data);
      entityType = "weekly_account";
      break;
    case "agent_status":
      entity = updateAgentStatus(state, data);
      entityType = "agent";
      break;
    case "checkpoint":
      entity = updateCheckpoint(state, data);
      entityType = "agent_checkpoint";
      break;
    case "review_item":
      entity = addReviewItem(state, data);
      entityType = "review_item";
      break;
    default:
      throw new Error(`Tipo de evento no soportado: ${type}`);
  }

  const event = {
    id: createId("evt"),
    type,
    actor,
    entityType,
    entityId: entity?.id || null,
    receivedAt: nowIso(),
    payload: data,
  };
  state.events.unshift(event);
  state.events = state.events.slice(0, 1000);
  pushAudit(state, {
    actor,
    action: `ingest:${type}`,
    entityType,
    entityId: entity?.id || null,
    after: entity,
  });

  return {
    event,
    entity,
    snapshot: buildOperativaSnapshot(writeOperativaState(state)),
  };
}

function sumLedger(state, type, currencies = ["USD", "USDT"]) {
  return state.ledgerEntries
    .filter((item) => item.type === type && currencies.includes(normalizeCurrency(item.currency)))
    .reduce((sum, item) => sum + normalizeAmount(item.amount), 0);
}

function buildCashbox(state) {
  const payments = state.paymentEvidences.filter((item) => ["verified", "applied"].includes(item.status));
  const pendingPayments = state.paymentEvidences.filter((item) =>
    ["detected", "text_only", "old_reusable", "duplicate_suspect"].includes(item.status)
  );
  const services = state.serviceOrders;
  const completed = services.filter((item) => ["completed", "closed"].includes(item.status));
  const projected = services.filter((item) => ["payment_verified", "in_process", "pending_provider"].includes(item.status));

  return {
    dayKey: state.dayKey || getDayKey(),
    receivedUsd: sumLedger(state, "income"),
    providerCostsUsd: sumLedger(state, "cost"),
    realizedProfitUsd:
      completed.reduce((sum, item) => sum + normalizeAmount(item.chargedCurrency === "USD" ? item.chargedAmount : 0), 0) -
      completed.reduce((sum, item) => sum + normalizeAmount(item.providerCurrency === "USD" || item.providerCurrency === "USDT" ? item.providerCost : 0), 0),
    projectedProfitUsd:
      projected.reduce((sum, item) => sum + normalizeAmount(item.chargedCurrency === "USD" ? item.chargedAmount : 0), 0) -
      projected.reduce((sum, item) => sum + normalizeAmount(item.providerCurrency === "USD" || item.providerCurrency === "USDT" ? item.providerCost : 0), 0),
    refundsUsd: sumLedger(state, "refund"),
    pendingValidationUsd: pendingPayments.reduce(
      (sum, item) => sum + (["USD", "USDT"].includes(item.currency) ? normalizeAmount(item.amount) : 0),
      0
    ),
    retainedUsd: payments.reduce((sum, item) => sum + (["USD", "USDT"].includes(item.currency) ? normalizeAmount(item.amount) : 0), 0),
  };
}

function buildChannels(state) {
  return state.channels.map((channel) => {
    const channelMessages = state.messages.filter((message) => message.channelId === channel.id);
    return {
      ...channel,
      unread: channelMessages.filter((message) => message.direction === "client" && !message.processed).length,
      lastScannedAt:
        state.agentCheckpoints.find((checkpoint) => checkpoint.channelId === channel.id)?.lastScannedAt || null,
    };
  });
}

function buildReceivers(state) {
  return state.receivers.map((receiver) => {
    const settlements = state.receiverSettlements.filter((item) => item.receiverId === receiver.id);
    const received = settlements.reduce((sum, item) => sum + normalizeAmount(item.totalReceived), 0);
    const sentToOwner = settlements.reduce((sum, item) => sum + normalizeAmount(item.totalSentToOwner), 0);
    return {
      ...receiver,
      received,
      sentToOwner,
      pending: received - sentToOwner,
      status: received - sentToOwner <= 0 ? "cerrado" : sentToOwner > 0 ? "parcial" : "pendiente",
    };
  });
}

function mapReviewToQueue(item) {
  return {
    id: item.id,
    type: item.type,
    title: item.title,
    customer: item.relatedEntityType || "Sistema",
    channel: "Cabina operativa",
    detail: item.description,
    confidence: item.confidence,
    priority: item.priority || "media",
    action: "Revisar",
  };
}

function mapMessageToInsight(state, message) {
  const classification = message.classification || classifyWhatsappMessage(message);
  const conversation = state.conversations.find((item) => item.id === message.conversationId);
  const channel = state.channels.find((item) => item.id === message.channelId);
  return {
    id: message.id,
    messageKey: message.messageKey,
    channelId: message.channelId,
    channel: channel?.name || message.channelId,
    conversationId: message.conversationId,
    conversationTitle: conversation?.title || message.senderName || "Chat visible",
    text: message.text,
    capturedAt: message.capturedAt,
    sentAt: message.sentAt,
    classification,
    suggestedAction: classification.action,
  };
}

function buildMessageInsights(state) {
  const insights = state.messages
    .map((message) => mapMessageToInsight(state, message))
    .filter((item) => item.classification.intent !== "no_action");

  const byIntent = insights.reduce((acc, item) => {
    acc[item.classification.intent] = (acc[item.classification.intent] || 0) + 1;
    return acc;
  }, {});

  return {
    rows: insights.slice(0, 40),
    byIntent,
    actionable: insights.length,
  };
}

function buildOperativaSnapshot(stateOverride = null) {
  const state = stateOverride ? normalizeState(stateOverride) : readOperativaState();
  const pendingReviews = state.reviewItems.filter((item) => item.status === "pendiente");
  const servicesInProgress = state.serviceOrders.filter((item) =>
    ["payment_verified", "in_process", "pending_provider", "weekly_billable"].includes(item.status)
  );
  const receivers = buildReceivers(state);
  const messageInsights = buildMessageInsights(state);

  return {
    ...state,
    generatedAt: nowIso(),
    channels: buildChannels(state),
    receivers,
    messageInsights: messageInsights.rows,
    messageIntentSummary: messageInsights.byIntent,
    cashbox: buildCashbox(state),
    nowQueue: pendingReviews.slice(0, 12).map(mapReviewToQueue),
    summary: {
      openAlerts: pendingReviews.filter((item) => item.priority === "alta").length,
      reviewPending: pendingReviews.length,
      servicesInProgress: servicesInProgress.length,
      receiverPendingCount: receivers.filter((item) => item.pending > 0).length,
      actionableMessages: messageInsights.actionable,
    },
  };
}

module.exports = {
  buildOperativaSnapshot,
  classifyWhatsappMessage,
  createSeedState,
  getDayKey,
  ingestOperativaEvent,
  readOperativaState,
  writeOperativaState,
};
