function normalizeText(value) {
  return String(value || "").replace(/\r/g, "").trim();
}

function anonymizeText(value) {
  return String(value || "")
    .replace(/\b[\w.+-]+@[\w.-]+\.\w+\b/g, "[correo]")
    .replace(/\b(?:\+?\d[\d\s-]{7,}\d)\b/g, "[telefono]")
    .replace(/\bIMEI\s*[:#-]?\s*[A-Z0-9-]{8,}\b/gi, "IMEI: [oculto]")
    .replace(/\bserial\s*[:#-]?\s*[A-Z0-9-]{5,}\b/gi, "serial: [oculto]")
    .replace(/\bS\/N\s*[:#-]?\s*[A-Z0-9-]{5,}\b/gi, "S/N: [oculto]")
    .replace(/https?:\/\/\S+/gi, "[enlace]");
}

function buildOwnerAliasSet(ownerAliases) {
  return new Set(
    String(ownerAliases || "")
      .split(/\r?\n|,/)
      .map((item) => item.trim().toLowerCase())
      .filter(Boolean)
  );
}

function parseHeader(line) {
  const patterns = [
    /^(\d{1,2}\/\d{1,2}\/\d{2,4}),\s+(\d{1,2}:\d{2}(?::\d{2})?\s*(?:a\.\s*m\.|p\.\s*m\.|AM|PM|am|pm)?)\s+-\s+([^:]+):\s?(.*)$/i,
    /^\[(\d{1,2}\/\d{1,2}\/\d{2,4}),\s+(\d{1,2}:\d{2}(?::\d{2})?\s*(?:a\.\s*m\.|p\.\s*m\.|AM|PM|am|pm)?)\]\s+([^:]+):\s?(.*)$/i,
  ];

  for (const pattern of patterns) {
    const match = line.match(pattern);
    if (match) {
      return {
        sentAt: `${match[1]} ${match[2]}`.trim(),
        senderName: normalizeText(match[3]),
        text: normalizeText(match[4]),
      };
    }
  }

  return null;
}

function parseWhatsAppChat(chatText, options = {}) {
  const lines = normalizeText(chatText).split("\n").filter(Boolean);
  const ownerAliasSet = buildOwnerAliasSet(options.ownerAliases);
  const messages = [];
  let currentMessage = null;

  for (const rawLine of lines) {
    const line = rawLine.trim();
    const parsed = parseHeader(line);

    if (parsed) {
      if (currentMessage) {
        messages.push(currentMessage);
      }

      const senderKey = parsed.senderName.toLowerCase();
      const messageText = options.anonymize ? anonymizeText(parsed.text || "[sin texto]") : parsed.text || "[sin texto]";
      const senderName = options.anonymize ? anonymizeText(parsed.senderName) : parsed.senderName;
      currentMessage = {
        role: ownerAliasSet.has(senderKey) ? "agent" : "client",
        senderName,
        sentAt: parsed.sentAt,
        text: messageText,
      };
      continue;
    }

    if (currentMessage) {
      currentMessage.text = `${currentMessage.text}\n${options.anonymize ? anonymizeText(line) : line}`.trim();
    }
  }

  if (currentMessage) {
    messages.push(currentMessage);
  }

  const clientMessages = messages.filter((item) => item.role === "client");
  const agentMessages = messages.filter((item) => item.role === "agent");

  return {
    source: options.source || "WhatsApp export",
    contactName: options.anonymize
      ? anonymizeText(String(options.contactName || "Chat importado").trim())
      : String(options.contactName || "Chat importado").trim(),
    ownerAliases: [...ownerAliasSet],
    importedAt: new Date().toISOString(),
    messageCount: messages.length,
    clientMessageCount: clientMessages.length,
    agentMessageCount: agentMessages.length,
    firstClientMessage: clientMessages[0]?.text || "",
    lastAgentMessage: agentMessages[agentMessages.length - 1]?.text || "",
    notes: String(options.notes || "").trim(),
    messages,
  };
}

function countMatches(text, patterns) {
  const safeText = String(text || "").toLowerCase();
  return patterns.reduce((total, pattern) => total + (pattern.test(safeText) ? 1 : 0), 0);
}

function detectRequestedFields(messages) {
  const joinedAgentText = (messages || [])
    .filter((item) => item.role === "agent")
    .map((item) => item.text)
    .join("\n")
    .toLowerCase();

  const candidates = [
    { key: "modelo", patterns: [/\bmodelo\b/, /\bequipo\b/, /\bdevice\b/] },
    { key: "problema exacto", patterns: [/\bproblema\b/, /\bque pasa\b/, /\bdetalle\b/] },
    { key: "captura", patterns: [/\bcaptura\b/, /\bfoto\b/, /\bimagen\b/, /\bscreenshot\b/] },
    { key: "serial", patterns: [/\bserial\b/, /\bs\/n\b/] },
    { key: "imei", patterns: [/\bimei\b/] },
    { key: "version", patterns: [/\bversion\b/, /\bmiui\b/, /\bhyperos\b/, /\bandroid\b/] },
    { key: "contacto", patterns: [/\bnumero\b/, /\bcontacto\b/, /\btelefono\b/, /\bwhatsapp\b/] },
    { key: "pago", patterns: [/\bpago\b/, /\bcomprobante\b/, /\btransferencia\b/, /\byape\b/, /\bplin\b/] },
  ];

  return candidates
    .filter((item) => item.patterns.some((pattern) => pattern.test(joinedAgentText)))
    .map((item) => item.key);
}

function detectConversationType(conversation) {
  const allText = (conversation.messages || []).map((item) => item.text).join("\n");
  const saleScore = countMatches(allText, [/\bprecio\b/i, /\bcuanto\b/i, /\bcosto\b/i, /\bpromo\b/i, /\bvalor\b/i]);
  const supportScore = countMatches(allText, [/\bno detecta\b/i, /\berror\b/i, /\bproblema\b/i, /\badb\b/i, /\bfastboot\b/i, /\bconexion\b/i]);
  const paymentScore = countMatches(allText, [/\bpago\b/i, /\bcomprobante\b/i, /\btransferencia\b/i, /\byape\b/i, /\bplin\b/i]);
  const followupScore = countMatches(allText, [/\bavance\b/i, /\bestado\b/i, /\bya\b/i, /\bseguimiento\b/i, /\bquedo\b/i]);

  const best = [
    { key: "venta", score: saleScore },
    { key: "soporte", score: supportScore },
    { key: "cobro", score: paymentScore },
    { key: "seguimiento", score: followupScore },
  ].sort((a, b) => b.score - a.score)[0];

  return best && best.score > 0 ? best.key : "general";
}

function detectOutcome(conversation) {
  const lastAgent = String(conversation.lastAgentMessage || "").toLowerCase();
  const allText = (conversation.messages || []).map((item) => item.text).join("\n").toLowerCase();

  if (/\brevision tecnica\b|\bescalad/i.test(allText)) {
    return "escalado";
  }
  if (/\bcomprobante\b|\bpago\b|\btransferencia\b/.test(allText)) {
    return "pago";
  }
  if (/\bcerrado\b|\blisto\b|\bsolucionado\b|\bfinalizado\b/.test(lastAgent)) {
    return "cerrado";
  }
  if (/\bnecesito\b|\benviame\b|\bmanda\b|\bfalta\b/.test(lastAgent)) {
    return "pendiente_cliente";
  }
  if (/\bprecio\b|\bcuesta\b|\bseria\b/.test(lastAgent)) {
    return "cotizado";
  }
  return "abierto";
}

function buildLearningPattern(conversation, requestedFields, conversationType, outcome) {
  const firstClient = String(conversation.firstClientMessage || "").trim();
  const firstAgent = (conversation.messages || []).find((item) => item.role === "agent")?.text || "";
  const shortAgent = String(firstAgent).replace(/\s+/g, " ").trim().slice(0, 140);
  const fieldLine = requestedFields.length ? requestedFields.join(", ") : "sin campos claros";

  return `Tipo ${conversationType}; resultado ${outcome}; el cliente suele abrir con "${firstClient.slice(
    0,
    90
  )}"; tu respuesta arranca con "${shortAgent}"; datos pedidos: ${fieldLine}.`;
}

function analyzeTrainingConversation(conversation) {
  const requestedFields = detectRequestedFields(conversation.messages || []);
  const conversationType = detectConversationType(conversation);
  const outcome = detectOutcome(conversation);
  const confidence = Math.min(
    0.96,
    0.42 + requestedFields.length * 0.08 + (conversation.messageCount > 6 ? 0.12 : 0.04)
  );

  return {
    conversationType,
    serviceLine: conversationType === "venta" ? "comercial" : "soporte",
    outcome,
    requestedFields,
    learnedPattern: buildLearningPattern(conversation, requestedFields, conversationType, outcome),
    confidence: Number(confidence.toFixed(2)),
  };
}

module.exports = {
  analyzeTrainingConversation,
  parseWhatsAppChat,
};
