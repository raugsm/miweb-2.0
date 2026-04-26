const fs = require("fs");
const path = require("path");

const SCRIPT_DIR = __dirname;
const DEFAULT_CONFIG_FILE = path.join(SCRIPT_DIR, "visual-agent.config.json");

function readJsonFile(filePath) {
  return JSON.parse(fs.readFileSync(filePath, "utf8").replace(/^\uFEFF/, ""));
}

function resolveFromScriptDir(value, fallback) {
  const target = value || fallback;
  return path.isAbsolute(target) ? target : path.join(SCRIPT_DIR, target);
}

function readConfig() {
  const configFile = process.env.VISUAL_AGENT_CONFIG || DEFAULT_CONFIG_FILE;
  if (!fs.existsSync(configFile)) {
    throw new Error(
      `No encontre ${configFile}. Copia visual-agent.config.example.json como visual-agent.config.json.`
    );
  }

  const config = readJsonFile(configFile);
  return {
    ...config,
    cloudUrl: String(config.cloudUrl || "https://ariadgsm.com").replace(/\/+$/, ""),
    inboxDir: resolveFromScriptDir(config.inboxDir, "./inbox"),
    processedDir: resolveFromScriptDir(config.processedDir, "./processed"),
    failedDir: resolveFromScriptDir(config.failedDir, "./failed"),
    pollSeconds: Math.max(2, Number(config.pollSeconds || 5)),
  };
}

function ensureDirs(config) {
  for (const target of [config.inboxDir, config.processedDir, config.failedDir]) {
    if (!fs.existsSync(target)) {
      fs.mkdirSync(target, { recursive: true });
    }
  }
}

function readEventFile(filePath) {
  const parsed = readJsonFile(filePath);
  return Array.isArray(parsed) ? parsed : [parsed];
}

async function sendEvent(config, event) {
  const token = String(config.agentToken || "").trim();
  if (!token || token === "cambia-esto-por-un-token-largo") {
    throw new Error("Configura agentToken antes de enviar eventos a la nube.");
  }

  const response = await fetch(`${config.cloudUrl}/api/operativa-v2/events`, {
    method: "POST",
    headers: {
      "Authorization": `Bearer ${token}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      actor: "visual_agent",
      ...event,
    }),
  });

  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(payload.error || `La nube respondio con estado ${response.status}`);
  }
  return payload;
}

function moveFile(source, targetDir) {
  const target = path.join(targetDir, `${Date.now()}-${path.basename(source)}`);
  fs.renameSync(source, target);
  return target;
}

async function processInboxOnce(config) {
  ensureDirs(config);
  const files = fs
    .readdirSync(config.inboxDir)
    .filter((fileName) => fileName.toLowerCase().endsWith(".json"))
    .map((fileName) => path.join(config.inboxDir, fileName));

  if (!files.length) {
    console.log("Sin eventos nuevos en inbox.");
    return { processed: 0, failed: 0 };
  }

  let processed = 0;
  let failed = 0;

  for (const filePath of files) {
    try {
      const events = readEventFile(filePath);
      for (const event of events) {
        await sendEvent(config, event);
        processed += 1;
      }
      const target = moveFile(filePath, config.processedDir);
      console.log(`Procesado: ${path.basename(filePath)} -> ${target}`);
    } catch (error) {
      failed += 1;
      const target = moveFile(filePath, config.failedDir);
      console.error(`Fallo: ${path.basename(filePath)} -> ${error.message}`);
      console.error(`Movido a: ${target}`);
    }
  }

  return { processed, failed };
}

async function watch(config) {
  console.log(`Agente visual en modo observador. Revisando ${config.inboxDir}`);
  while (true) {
    await processInboxOnce(config);
    await new Promise((resolve) => setTimeout(resolve, config.pollSeconds * 1000));
  }
}

async function main() {
  const config = readConfig();
  if (process.argv.includes("--watch")) {
    await watch(config);
    return;
  }

  await processInboxOnce(config);
}

main().catch((error) => {
  console.error(error.message);
  process.exitCode = 1;
});
