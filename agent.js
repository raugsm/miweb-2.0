const fs = require("fs");
const path = require("path");

const REPORT_FILE = path.join(__dirname, "data", "agent-report.json");

function nowTime() {
  return new Date().toLocaleTimeString("es-PE", {
    hour: "2-digit",
    minute: "2-digit",
  });
}

function getMissingReportResult() {
  return {
    action: "agent_report",
    status: "needs_human",
    summary: "No hay un reporte reciente del agente local.",
    details:
      "Ejecuta scripts\\collect-agent-diagnostics.ps1 para generar chequeos reales antes de validar el caso.",
  };
}

function readReportFile() {
  if (!fs.existsSync(REPORT_FILE)) {
    return null;
  }

  try {
    const raw = fs.readFileSync(REPORT_FILE, "utf8").replace(/^\uFEFF/, "");
    return JSON.parse(raw);
  } catch (error) {
    return {
      adb: {
        action: "agent_report",
        status: "needs_human",
        summary: "El reporte del agente local no se pudo leer.",
        details: error.message,
      },
      conexion: {
        action: "agent_report",
        status: "needs_human",
        summary: "El reporte del agente local no se pudo leer.",
        details: error.message,
      },
      fastboot: {
        action: "agent_report",
        status: "needs_human",
        summary: "El reporte del agente local no se pudo leer.",
        details: error.message,
      },
    };
  }
}

async function runCaseDiagnostics(category) {
  const report = readReportFile();

  if (!report) {
    return getMissingReportResult();
  }

  return report[category] || report.conexion || getMissingReportResult();
}

module.exports = {
  nowTime,
  runCaseDiagnostics,
};
