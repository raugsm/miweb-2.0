const { getMetadata, saveMetadata } = require("./db");

const DEFAULT_STYLE_PROFILE = {
  businessName: "Soporte Remoto",
  agentDisplayName: "Asistente de Soporte",
  tone: "confiable, claro, directo y amable",
  responseLength: "breve",
  greetingStyle: "saludar corto y pasar rapido al diagnostico",
  customerUpdates: "explicar avance real sin prometer tiempos falsos",
  escalationStyle: "avisar que el caso pasa a revision tecnica cuando una validacion falla",
  forbiddenPhrases: [
    "espere un momento por favorcito",
    "ya mismo te lo soluciono al 100%",
    "esto es super facil",
  ],
  preferredPhrases: [
    "Voy a revisarlo",
    "Te actualizo apenas termine esta validacion",
    "Ya deje el caso listo para revision tecnica",
  ],
  assignees: ["Mesa 1", "Mesa 2", "Mesa 3"],
  closureReasons: [
    "Validacion completada",
    "Cliente no respondio",
    "Pendiente de informacion del cliente",
    "Caso derivado a revision tecnica",
  ],
  stageReplies: {
    received: "Ya recibimos tu caso y estamos preparando la validacion inicial.",
    in_progress: "Ya estamos trabajando tu caso y te actualizo cuando termine esta validacion.",
    escalated: "Tu caso paso a revision tecnica porque la validacion automatica encontro una excepcion.",
    pending_customer: "Necesito un dato puntual de tu lado para poder continuar sin adivinar el resultado.",
    closed: "Tu caso ya quedo cerrado y te dejo el motivo registrado en este mismo resumen.",
  },
  askForTheseFields: [
    "modelo del equipo",
    "problema exacto",
    "si activa depuracion USB",
  ],
  notes:
    "Nunca inventar resultados tecnicos. Si el agente no confirmo algo, explicarlo como pendiente o en revision.",
};

function ensureStyleFile() {
  if (!getMetadata("style_profile")) {
    saveMetadata("style_profile", DEFAULT_STYLE_PROFILE);
  }
}

function readStyleProfile() {
  ensureStyleFile();
  const stored = getMetadata("style_profile", { ...DEFAULT_STYLE_PROFILE }) || {};
  return {
    ...DEFAULT_STYLE_PROFILE,
    ...stored,
    forbiddenPhrases: normalizeArray(stored.forbiddenPhrases || DEFAULT_STYLE_PROFILE.forbiddenPhrases),
    preferredPhrases: normalizeArray(stored.preferredPhrases || DEFAULT_STYLE_PROFILE.preferredPhrases),
    assignees: normalizeArray(stored.assignees || DEFAULT_STYLE_PROFILE.assignees),
    closureReasons: normalizeArray(stored.closureReasons || DEFAULT_STYLE_PROFILE.closureReasons),
    askForTheseFields: normalizeArray(stored.askForTheseFields || DEFAULT_STYLE_PROFILE.askForTheseFields),
    stageReplies: {
      ...DEFAULT_STYLE_PROFILE.stageReplies,
      ...(stored.stageReplies || {}),
    },
  };
}

function normalizeArray(value) {
  if (Array.isArray(value)) {
    return value.map((item) => String(item).trim()).filter(Boolean);
  }

  return String(value || "")
    .split(/\r?\n|,/)
    .map((item) => item.trim())
    .filter(Boolean);
}

function updateStyleProfile(input) {
  const nextProfile = {
    businessName: String(input.businessName || DEFAULT_STYLE_PROFILE.businessName).trim(),
    agentDisplayName: String(
      input.agentDisplayName || DEFAULT_STYLE_PROFILE.agentDisplayName
    ).trim(),
    tone: String(input.tone || DEFAULT_STYLE_PROFILE.tone).trim(),
    responseLength: String(input.responseLength || DEFAULT_STYLE_PROFILE.responseLength).trim(),
    greetingStyle: String(
      input.greetingStyle || DEFAULT_STYLE_PROFILE.greetingStyle
    ).trim(),
    customerUpdates: String(
      input.customerUpdates || DEFAULT_STYLE_PROFILE.customerUpdates
    ).trim(),
    escalationStyle: String(
      input.escalationStyle || DEFAULT_STYLE_PROFILE.escalationStyle
    ).trim(),
    forbiddenPhrases: normalizeArray(input.forbiddenPhrases),
    preferredPhrases: normalizeArray(input.preferredPhrases),
    assignees: normalizeArray(input.assignees || DEFAULT_STYLE_PROFILE.assignees),
    closureReasons: normalizeArray(input.closureReasons || DEFAULT_STYLE_PROFILE.closureReasons),
    askForTheseFields: normalizeArray(input.askForTheseFields),
    stageReplies: {
      received: String(
        input.receivedReply ||
          input?.stageReplies?.received ||
          DEFAULT_STYLE_PROFILE.stageReplies.received
      ).trim(),
      in_progress: String(
        input.inProgressReply ||
          input?.stageReplies?.in_progress ||
          DEFAULT_STYLE_PROFILE.stageReplies.in_progress
      ).trim(),
      escalated: String(
        input.escalatedReply ||
          input?.stageReplies?.escalated ||
          DEFAULT_STYLE_PROFILE.stageReplies.escalated
      ).trim(),
      pending_customer: String(
        input.pendingCustomerReply ||
          input?.stageReplies?.pending_customer ||
          DEFAULT_STYLE_PROFILE.stageReplies.pending_customer
      ).trim(),
      closed: String(
        input.closedReply ||
          input?.stageReplies?.closed ||
          DEFAULT_STYLE_PROFILE.stageReplies.closed
      ).trim(),
    },
    notes: String(input.notes || DEFAULT_STYLE_PROFILE.notes).trim(),
  };

  ensureStyleFile();
  saveMetadata("style_profile", nextProfile);

  return nextProfile;
}

module.exports = {
  DEFAULT_STYLE_PROFILE,
  readStyleProfile,
  updateStyleProfile,
};
