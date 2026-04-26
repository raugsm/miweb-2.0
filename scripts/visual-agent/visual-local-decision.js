const fs = require("fs");

const OPENAI_API_URL = "https://api.openai.com/v1/responses";
const OPENAI_MODEL = process.env.OPENAI_MODEL || "gpt-5.4-nano";

function parseArgs(argv) {
  const args = {};
  for (let i = 2; i < argv.length; i += 1) {
    const key = argv[i];
    if (key === "--input") {
      args.input = argv[i + 1];
      i += 1;
    }
  }
  return args;
}

function readInput(filePath) {
  if (!filePath) {
    throw new Error("Falta --input.");
  }
  return JSON.parse(fs.readFileSync(filePath, "utf8").replace(/^\uFEFF/, ""));
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

function sanitizeMessages(messages) {
  return (Array.isArray(messages) ? messages : [])
    .map((message) => ({
      channelId: String(message.channelId || ""),
      contactName: String(message.contactName || ""),
      conversationType: String(message.conversationType || "visible_screen"),
      text: String(message.text || "").replace(/\s+/g, " ").trim().slice(0, 260),
    }))
    .filter((message) => message.channelId && message.text)
    .slice(0, 60);
}

async function createDecision(input) {
  const schema = {
    type: "object",
    additionalProperties: false,
    properties: {
      status: { type: "string", enum: ["local_match", "no_local_action"] },
      intent: {
        type: "string",
        enum: ["payment_or_receipt", "accounting_debt", "price_request", "no_action"],
      },
      label: { type: "string" },
      priority: { type: "string", enum: ["alta", "media", "baja", "ninguna"] },
      score: { type: "integer" },
      targetChannel: { type: "string" },
      conversationTitle: { type: "string" },
      conversationType: { type: "string" },
      text: { type: "string" },
      reasons: { type: "array", items: { type: "string" } },
      queries: { type: "array", items: { type: "string" } },
      notes: { type: "string" },
    },
    required: [
      "status",
      "intent",
      "label",
      "priority",
      "score",
      "targetChannel",
      "conversationTitle",
      "conversationType",
      "text",
      "reasons",
      "queries",
      "notes",
    ],
  };

  const messages = sanitizeMessages(input.messages);
  const maxQueries = Math.max(1, Math.min(Number(input.maxQueries || 3), 6));
  const prompt = [
    {
      role: "developer",
      content: [
        {
          type: "input_text",
          text:
            "Eres el decisor local de AriadGSM para modo vivo. Revisa OCR reciente de 3 WhatsApp y decide si hay que abrir un chat ahora. Prioriza clientes con pagos, comprobantes, deudas, saldo/cuenta o preguntas de precio. Evita grupos de ruido como Pagos Mexico, Pagos Chile, Pagos Colombia y textos de interfaz. No redactes respuestas ni ordenes enviar mensajes. Devuelve no_local_action si no hay una accion clara. Las queries deben ser cortas para buscar una fila visible en WhatsApp.",
        },
      ],
    },
    {
      role: "user",
      content: [
        {
          type: "input_text",
          text: JSON.stringify({ maxQueries, messages }),
        },
      ],
    },
  ];

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
          name: "ariadgsm_local_live_decision",
          schema,
          strict: true,
        },
      },
    }),
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(`OpenAI error ${response.status}: ${body}`);
  }

  const payload = await response.json();
  const outputText = extractOutputText(payload);
  if (!outputText) {
    throw new Error("OpenAI no devolvio texto util.");
  }

  const decision = JSON.parse(outputText);
  decision.queries = (decision.queries || []).slice(0, maxQueries);
  return decision;
}

async function main() {
  if (!process.env.OPENAI_API_KEY) {
    console.log(
      JSON.stringify({
        status: "no_local_action",
        intent: "no_action",
        label: "",
        priority: "ninguna",
        score: 0,
        targetChannel: "",
        conversationTitle: "",
        conversationType: "",
        text: "",
        reasons: [],
        queries: [],
        notes: "OPENAI_API_KEY no esta configurado en esta PC.",
      })
    );
    return;
  }

  const args = parseArgs(process.argv);
  const input = readInput(args.input);
  const decision = await createDecision(input);
  console.log(JSON.stringify(decision));
}

main().catch((error) => {
  console.error(error.message);
  process.exitCode = 1;
});
