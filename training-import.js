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

module.exports = {
  parseWhatsAppChat,
};
