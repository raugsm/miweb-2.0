const OPENAI_API_URL = "https://api.openai.com/v1/responses";
const OPENAI_MODEL = process.env.OPENAI_MODEL || "gpt-5.4-nano";
const { readStyleProfile } = require("./style-profile");

function isAiConfigured() {
  return Boolean(process.env.OPENAI_API_KEY);
}

function getAiModeLabel() {
  return isAiConfigured() ? `OpenAI (${OPENAI_MODEL})` : "Reglas locales";
}

function getDefaultClassification(summary) {
  const text = String(summary || "").toLowerCase();

  if (text.includes("fastboot")) {
    return {
      category: "fastboot",
      priority: "alta",
      response:
        "Voy a validar si el equipo responde en fastboot y te aviso si necesito apoyo manual.",
    };
  }

  if (text.includes("adb") || text.includes("depuracion") || text.includes("usb debugging")) {
    return {
      category: "adb",
      priority: "alta",
      response:
        "Voy a revisar la conexion ADB, la autorizacion del equipo y el siguiente paso permitido.",
    };
  }

  return {
    category: "conexion",
    priority: "media",
    response:
      "Voy a comprobar la deteccion basica del dispositivo en Windows y te mantengo al tanto.",
  };
}

function extractOutputText(payload) {
  if (typeof payload?.output_text === "string" && payload.output_text) {
    return payload.output_text;
  }

  const parts = [];
  for (const item of payload?.output || []) {
    for (const content of item?.content || []) {
      if (content?.type === "output_text" && content?.text) {
        parts.push(content.text);
      }
    }
  }

  return parts.join("\n").trim();
}

async function createStructuredResponse(prompt, schemaName, schema) {
  const response = await fetch(OPENAI_API_URL, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${process.env.OPENAI_API_KEY}`,
    },
    body: JSON.stringify({
      model: OPENAI_MODEL,
      reasoning: { effort: "minimal" },
      input: prompt,
      text: {
        format: {
          type: "json_schema",
          name: schemaName,
          schema,
          strict: true,
        },
      },
    }),
  });

  if (!response.ok) {
    const errorText = await response.text();
    throw new Error(`OpenAI error ${response.status}: ${errorText}`);
  }

  const payload = await response.json();
  const outputText = extractOutputText(payload);

  if (!outputText) {
    throw new Error("OpenAI no devolvio texto util.");
  }

  return JSON.parse(outputText);
}

async function classifyCaseWithAi(input) {
  const styleProfile = readStyleProfile();
  const schema = {
    type: "object",
    additionalProperties: false,
    properties: {
      category: {
        type: "string",
        enum: ["conexion", "adb", "fastboot"],
      },
      priority: {
        type: "string",
        enum: ["alta", "media", "baja"],
      },
      response: {
        type: "string",
      },
      summary: {
        type: "string",
      },
    },
    required: ["category", "priority", "response", "summary"],
  };

  const prompt = [
    {
      role: "developer",
      content: [
        {
          type: "input_text",
          text:
            `Eres ${styleProfile.agentDisplayName} de ${styleProfile.businessName}. Tu tono debe ser ${styleProfile.tone}. Clasifica el caso en una de estas categorias exactas: conexion, adb o fastboot. La respuesta al cliente debe ser ${styleProfile.responseLength}, sonar humana y seguir estas preferencias: saludo ${styleProfile.greetingStyle}; actualizaciones ${styleProfile.customerUpdates}; evitar frases ${styleProfile.forbiddenPhrases.join(" | ")}; usar cuando encaje ${styleProfile.preferredPhrases.join(" | ")}.`,
        },
      ],
    },
    {
      role: "user",
      content: [
        {
          type: "input_text",
          text: JSON.stringify(input),
        },
      ],
    },
  ];

  return createStructuredResponse(prompt, "support_case_classification", schema);
}

async function generateReplyWithAi(input) {
  const styleProfile = readStyleProfile();
  const schema = {
    type: "object",
    additionalProperties: false,
    properties: {
      reply: {
        type: "string",
      },
      next_step: {
        type: "string",
      },
    },
    required: ["reply", "next_step"],
  };

  const prompt = [
    {
      role: "developer",
      content: [
        {
          type: "input_text",
          text:
            `Redacta respuestas para clientes de soporte remoto con este perfil de marca. Negocio: ${styleProfile.businessName}. Firma visible: ${styleProfile.agentDisplayName}. Tono: ${styleProfile.tone}. Extension: ${styleProfile.responseLength}. Al saludar: ${styleProfile.greetingStyle}. Al actualizar: ${styleProfile.customerUpdates}. Al escalar: ${styleProfile.escalationStyle}. Nunca uses estas frases: ${styleProfile.forbiddenPhrases.join(" | ")}. Prefiere estas frases si son naturales: ${styleProfile.preferredPhrases.join(" | ")}. Si faltan datos, pide solo lo necesario de esta lista: ${styleProfile.askForTheseFields.join(" | ")}. Nota operativa: ${styleProfile.notes}`,
        },
      ],
    },
    {
      role: "user",
      content: [
        {
          type: "input_text",
          text: JSON.stringify(input),
        },
      ],
    },
  ];

  return createStructuredResponse(prompt, "support_case_reply", schema);
}

async function classifyCase(input) {
  const fallback = getDefaultClassification(input.summary);
  const styleProfile = readStyleProfile();
  const styledFallbackResponse = `${styleProfile.preferredPhrases[0] || "Voy a revisarlo"}. ${
    fallback.response
  }`;

  if (!isAiConfigured()) {
    return {
      ...fallback,
      response: styledFallbackResponse,
      summary: input.summary || "Sin resumen",
      source: "fallback",
    };
  }

  try {
    const aiResult = await classifyCaseWithAi(input);
    return {
      ...aiResult,
      source: "openai",
    };
  } catch (error) {
    return {
      ...fallback,
      response: styledFallbackResponse,
      summary: input.summary || "Sin resumen",
      source: "fallback",
      error: error.message,
    };
  }
}

async function generateSuggestedReply(input) {
  const styleProfile = readStyleProfile();
  const defaultReply =
    input.status === "Escalado"
      ? `${styleProfile.preferredPhrases[2] || "Ya deje el caso listo para revision tecnica"}. ${
          styleProfile.customerUpdates
        }.`
      : `${styleProfile.preferredPhrases[0] || "Voy a revisarlo"}. ${
          styleProfile.preferredPhrases[1] ||
          "Te actualizo apenas termine esta validacion"
        }.`;

  if (!isAiConfigured()) {
    return {
      reply: defaultReply,
      next_step: "Continuar con el flujo local hasta configurar la integracion real.",
      source: "fallback",
    };
  }

  try {
    const aiResult = await generateReplyWithAi(input);
    return {
      ...aiResult,
      source: "openai",
    };
  } catch (error) {
    return {
      reply: defaultReply,
      next_step: "La integracion IA fallo y se uso una respuesta local.",
      source: "fallback",
      error: error.message,
    };
  }
}

function generateStageReply(input) {
  const styleProfile = readStyleProfile();
  const stageReplies = styleProfile.stageReplies || {};
  const stageMap = {
    received: stageReplies.received,
    in_progress: stageReplies.in_progress,
    escalated: stageReplies.escalated,
    pending_customer: stageReplies.pending_customer,
    closed: stageReplies.closed,
  };

  const baseReply = stageMap[input.stage] || stageReplies.in_progress || "Te actualizo en breve.";
  const greeting = styleProfile.preferredPhrases[0] || "Voy a revisarlo";
  const closeReasonLine =
    input.stage === "closed" && input.closeReason ? ` Motivo registrado: ${input.closeReason}.` : "";

  return `${greeting}. ${baseReply}${closeReasonLine}`.trim();
}

module.exports = {
  classifyCase,
  generateStageReply,
  generateSuggestedReply,
  getAiModeLabel,
  isAiConfigured,
};
