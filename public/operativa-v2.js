const state = { data: null };

const moneyLabels = [
  ["receivedUsd", "Ingresos recibidos", "USD"],
  ["providerCostsUsd", "Costos proveedor", "USD"],
  ["realizedProfitUsd", "Ganancia realizada", "USD"],
  ["projectedProfitUsd", "Ganancia proyectada", "USD"],
  ["refundsUsd", "Devoluciones", "USD"],
  ["pendingValidationUsd", "Pagos por validar", "USD"],
  ["retainedUsd", "Dinero retenido", "USD"],
];

function $(id) {
  return document.getElementById(id);
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"]/g, (match) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    "\"": "&quot;",
  }[match]));
}

function createEmptyData(message) {
  const generatedAt = new Date().toISOString();
  return {
    generatedAt,
    channels: [
      { name: "WhatsApp 1", serviceScope: "Samsung, senal Claro y servicios remotos", status: "sin_conexion", unread: 0 },
      { name: "WhatsApp 2", serviceScope: "Motorola, Honor, FRP y Unlock", status: "sin_conexion", unread: 0 },
      { name: "WhatsApp 3", serviceScope: "Xiaomi, Tecno, Infinix, iPhone y creditos/licencias", status: "sin_conexion", unread: 0 },
    ],
    summary: { openAlerts: 0, reviewPending: 0, servicesInProgress: 0, receiverPendingCount: 0 },
    agent: {
      mode: "observador",
      autonomyLevel: 1,
      connected: false,
      message: message || "Cabina lista. Esperando datos reales del agente visual.",
    },
    nowQueue: [],
    messageInsights: [],
    messageIntentSummary: {},
    cashbox: {},
    reviewItems: [],
    serviceOrders: [],
    receivers: [
      { name: "Receptor Peru", country: "Peru", currency: "PEN", received: 0, sentToOwner: 0, pending: 0, status: "cerrado" },
      { name: "Receptor Chile", country: "Chile", currency: "CLP", received: 0, sentToOwner: 0, pending: 0, status: "cerrado" },
      { name: "Receptor Colombia", country: "Colombia", currency: "COP", received: 0, sentToOwner: 0, pending: 0, status: "cerrado" },
      { name: "Receptor Mexico", country: "Mexico", currency: "MXN", received: 0, sentToOwner: 0, pending: 0, status: "cerrado" },
    ],
    weeklyAccounts: [],
    cloudStatus: {
      status: "pendiente",
      message: "Esperando la primera sincronizacion del agente local.",
      lastSyncAt: null,
      lastBackupAt: null,
      backups: [],
      totals: {},
    },
    reportSummary: {
      generatedAt,
      totals: {},
      channels: [],
    },
    syncBatches: [],
  };
}

function priorityClass(priority) {
  if (priority === "alta") return "high";
  if (priority === "media") return "medium";
  return "";
}

function formatConfidence(value) {
  return `${Math.round(Number(value || 0) * 100)}% confianza`;
}

function formatDate(value) {
  if (!value) return "Sin actualizar";
  return new Date(value).toLocaleString("es-PE", {
    dateStyle: "short",
    timeStyle: "short",
  });
}

function formatNumber(value) {
  return Number(value || 0).toLocaleString("es-PE");
}

function setText(id, value) {
  const element = $(id);
  if (element) element.textContent = value;
}

function renderSummary() {
  const summary = state.data?.summary || {};
  setText("openAlerts", summary.openAlerts ?? 0);
  setText("reviewPending", summary.reviewPending ?? 0);
  setText("servicesInProgress", summary.servicesInProgress ?? 0);
  setText("receiverPendingCount", summary.receiverPendingCount ?? 0);
  setText("actionableMessages", summary.actionableMessages ?? 0);
  setText("lastUpdate", `Ultima lectura: ${formatDate(state.data?.generatedAt)}`);
}

function renderAgent() {
  const agent = state.data?.agent || {};
  setText("agentMode", agent.connected ? "Conectado" : "Pendiente");
  setText("agentMessage", agent.message || "Sin novedades.");
  setText("agentLevel", `Nivel ${agent.autonomyLevel || 1} - ${agent.mode || "observador"}`);
}

function renderNowQueue() {
  const container = $("nowQueue");
  const rows = state.data?.nowQueue || [];
  if (!container) return;

  container.innerHTML = rows.length
    ? rows.map((item) => `
      <article class="event-card">
        <div>
          <h4>${escapeHtml(item.title)}</h4>
          <p>${escapeHtml(item.detail || item.description)}</p>
          <div class="event-meta">
            <span class="tag">${escapeHtml(item.customer || item.relatedEntityType || "Sistema")}</span>
            <span class="tag">${escapeHtml(item.channel || "Cabina")}</span>
            <span class="tag ${priorityClass(item.priority)}">${escapeHtml(item.priority || "media")}</span>
            <span class="tag">${formatConfidence(item.confidence)}</span>
          </div>
        </div>
        <span class="chip">${escapeHtml(item.action || "Revisar")}</span>
      </article>
    `).join("")
    : "<p>No hay alertas por ahora. Cuando el agente visual envie eventos, apareceran aqui.</p>";
}

function renderCashbox() {
  const container = $("cashbox");
  const cashbox = state.data?.cashbox || {};
  if (!container) return;

  container.innerHTML = moneyLabels.map(([key, label, currency]) => `
    <div class="cash-item">
      <span>${label}</span>
      <strong>${formatNumber(cashbox[key])} ${currency}</strong>
    </div>
  `).join("");
}

function renderList(id, rows, emptyMessage, mapper) {
  const container = $(id);
  if (!container) return;
  container.innerHTML = rows.length ? rows.map(mapper).join("") : `<p>${emptyMessage}</p>`;
}

function renderReviewItems() {
  renderList("reviewItems", state.data?.reviewItems || [], "No hay dudas pendientes.", (item) => `
    <article class="list-row">
      <h4>${escapeHtml(item.title)}</h4>
      <p>${escapeHtml(item.detail || item.description)}</p>
      <div class="row-meta">
        <span class="tag">${escapeHtml(item.status || "pendiente")}</span>
        <span class="tag medium">${formatConfidence(item.confidence)}</span>
      </div>
    </article>
  `);
}

function renderMessageInsights() {
  renderList("messageInsights", state.data?.messageInsights || [], "Todavia no hay mensajes clasificados.", (item) => {
    const classification = item.classification || {};
    const extracted = classification.extracted || {};
    const chips = [
      item.channel,
      item.conversationTitle,
      classification.label,
      classification.priority,
      formatConfidence(classification.confidence),
      ...(extracted.amounts || []).slice(0, 2),
      ...(extracted.devices || []).slice(0, 2),
    ].filter(Boolean);

    return `
      <article class="list-row">
        <h4>${escapeHtml(classification.action || item.suggestedAction || "Revisar")}</h4>
        <p>${escapeHtml(item.text)}</p>
        <div class="row-meta">
          ${chips.map((chip) => `<span class="tag ${priorityClass(chip)}">${escapeHtml(chip)}</span>`).join("")}
        </div>
      </article>
    `;
  });
}

function renderIntentSummary() {
  const labels = {
    payment_or_receipt: "Pagos",
    accounting_debt: "Cuentas",
    price_request: "Precios",
    device_or_imei: "Modelos / IMEI",
    technical_process: "Tecnico",
    provider_offer: "Ofertas",
    procedure_reference: "Procedimientos",
    process_update: "Estados",
  };
  const rows = Object.entries(state.data?.messageIntentSummary || {})
    .sort((left, right) => right[1] - left[1])
    .map(([intent, count]) => ({ intent, count, label: labels[intent] || intent }));

  renderList("intentSummary", rows, "Sin categorias todavia.", (item) => `
    <article class="list-row">
      <h4>${escapeHtml(item.label)}</h4>
      <p>${formatNumber(item.count)} mensajes detectados</p>
    </article>
  `);
}

function renderServices() {
  renderList("serviceOrders", state.data?.serviceOrders || [], "No hay servicios activos.", (item) => `
    <article class="list-row">
      <h4>${escapeHtml(item.customer || item.customerId)} - ${escapeHtml(item.service || item.serviceName)}</h4>
      <p>${escapeHtml(item.channel || item.channelId)} | Cobrado ${escapeHtml(item.charged)} | Costo ${escapeHtml(item.cost)}</p>
      <div class="row-meta">
        <span class="tag">${escapeHtml(item.status)}</span>
        <span class="tag">${escapeHtml(item.financialState || "pendiente")}</span>
      </div>
    </article>
  `);
}

function renderChannels() {
  renderList("channels", state.data?.channels || [], "No hay canales configurados.", (item) => `
    <article class="list-row">
      <h4>${escapeHtml(item.name)}</h4>
      <p>${escapeHtml(item.serviceScope)}</p>
      <div class="row-meta">
        <span class="tag">${escapeHtml(item.status)}</span>
        <span class="tag">${formatNumber(item.unread)} no leidos</span>
      </div>
    </article>
  `);
}

function renderReceivers() {
  const container = $("receivers");
  const rows = state.data?.receivers || [];
  if (!container) return;

  container.innerHTML = rows.length
    ? rows.map((item) => `
      <article class="receiver-card">
        <span>${escapeHtml(item.country)} | ${escapeHtml(item.name)}</span>
        <strong>${formatNumber(item.pending)} ${escapeHtml(item.currency)}</strong>
        <p>Recibido ${formatNumber(item.received)} | Enviado ${formatNumber(item.sentToOwner)}</p>
        <span class="tag">${escapeHtml(item.status)}</span>
      </article>
    `).join("")
    : "<p>No hay receptores configurados.</p>";
}

function renderWeeklyAccounts() {
  renderList("weeklyAccounts", state.data?.weeklyAccounts || [], "No hay cuentas semanales abiertas.", (item) => `
    <article class="list-row">
      <h4>${escapeHtml(item.customer)}</h4>
      <p>${formatNumber(item.completed)} completados | ${formatNumber(item.pending)} pendientes | ${formatNumber(item.failed)} fallidos</p>
      <div class="row-meta">
        <span class="tag">${escapeHtml(item.total)}</span>
        <span class="tag">${escapeHtml(item.status)}</span>
      </div>
    </article>
  `);
}

function renderCloudStatus() {
  const cloud = state.data?.cloudStatus || {};
  const totals = cloud.totals || {};
  setText("cloudStatusPill", cloud.status || "Pendiente");
  const container = $("cloudStatus");
  if (!container) return;
  const rows = [
    ["Estado", cloud.status || "pendiente"],
    ["Ultima sincronizacion", formatDate(cloud.lastSyncAt)],
    ["Ultimo respaldo", formatDate(cloud.lastBackupAt)],
    ["Conversaciones", formatNumber(totals.conversations)],
    ["Mensajes aprendidos", formatNumber(totals.messages)],
    ["Eventos contables", formatNumber(totals.accountingEntries)],
  ];
  container.innerHTML = `
    <article class="cloud-message">
      <h4>${escapeHtml(cloud.message || "Nube lista.")}</h4>
      <p>${escapeHtml(cloud.publicUrl || "https://ariadgsm.com")}</p>
    </article>
    ${rows.map(([label, value]) => `
      <div class="cash-item">
        <span>${escapeHtml(label)}</span>
        <strong>${escapeHtml(value)}</strong>
      </div>
    `).join("")}
  `;
}

function renderBackups() {
  const backups = state.data?.cloudStatus?.backups || [];
  renderList("backupList", backups, "Todavia no hay respaldos operativos.", (item) => `
    <article class="list-row">
      <h4>${escapeHtml(item.fileName)}</h4>
      <p>${formatDate(item.updatedAt || item.createdAt)} | ${formatNumber(item.bytes)} bytes</p>
    </article>
  `);
}

function renderCloudReport() {
  const report = state.data?.reportSummary || {};
  const totals = report.totals || {};
  const rows = [
    ["Mensajes hoy", totals.todayMessages],
    ["Dudas pendientes", totals.pendingReviews],
    ["Pagos pendientes", totals.pendingPayments],
    ["Servicios activos", totals.activeServices],
    ["Sincronizaciones", totals.syncBatches],
    ["Respaldos", totals.backups],
  ];
  const container = $("cloudReport");
  if (!container) return;
  container.innerHTML = rows.map(([label, value]) => `
    <div class="cash-item">
      <span>${escapeHtml(label)}</span>
      <strong>${formatNumber(value)}</strong>
    </div>
  `).join("");
}

function renderSyncBatches() {
  renderList("syncBatches", state.data?.syncBatches || [], "No hay lotes de sincronizacion todavia.", (item) => `
    <article class="list-row">
      <h4>${escapeHtml(item.source || "desktop_agent")} - ${escapeHtml(item.status || "ok")}${item.duplicate ? " (duplicado)" : ""}</h4>
      <p>${formatDate(item.receivedAt)} | ${formatNumber(item.messages)} mensajes | ${formatNumber(item.conversations)} conversaciones</p>
      <p>Kernel: ${escapeHtml(item.runtimeKernelStatus || "-")} | enviados ${formatNumber(item.eventsIngested)} | rechazados ${formatNumber(item.eventsRejected)}</p>
      <div class="row-meta">
        <span class="tag">${escapeHtml(item.mode || "observador")}</span>
        <span class="tag">${escapeHtml(item.channelId || "todos")}</span>
        <span class="tag">${escapeHtml(item.idempotencyKey || item.id || "sin-idempotencia")}</span>
      </div>
    </article>
  `);
}

function buildDataFromDashboard(payload) {
  const cases = payload?.cases || [];
  const meta = payload?.meta || {};
  const training = meta.trainingSummary || {};
  const escalated = cases.filter((item) => item.status === "Escalado");
  const active = cases.filter((item) => item.status !== "Cerrado");

  return {
    generatedAt: new Date().toISOString(),
    channels: [
      { name: "WhatsApp 1", serviceScope: "Canal pendiente de conectar al agente visual", status: "panel_actual", unread: 0 },
      { name: "WhatsApp 2", serviceScope: "Canal pendiente de conectar al agente visual", status: "panel_actual", unread: 0 },
      { name: "WhatsApp 3", serviceScope: "Canal pendiente de conectar al agente visual", status: "panel_actual", unread: 0 },
    ],
    summary: {
      openAlerts: escalated.length,
      reviewPending: escalated.length,
      servicesInProgress: active.length,
      receiverPendingCount: 0,
    },
    agent: {
      mode: "puente_panel_actual",
      autonomyLevel: 1,
      connected: false,
      message: "Mostrando datos reales del panel actual. Falta conectar el agente visual de WhatsApp para pagos y comprobantes.",
    },
    nowQueue: escalated.slice(0, 8).map((item) => ({
      title: `Caso escalado: ${item.clientName}`,
      detail: item.lastUpdate || item.summary,
      customer: item.clientName,
      channel: item.contact || "Panel actual",
      confidence: 0.7,
      priority: "alta",
      action: "Revisar caso",
    })),
    cashbox: {},
    reviewItems: escalated.map((item) => ({
      title: item.clientName,
      description: item.lastUpdate || item.summary,
      confidence: 0.7,
      status: "pendiente",
    })),
    serviceOrders: active.map((item) => ({
      customer: item.clientName,
      service: item.category || "Soporte",
      channel: item.contact || "Panel actual",
      charged: "Sin dato",
      cost: "Sin dato",
      status: item.status,
      financialState: "pendiente_contabilidad",
    })),
    messageInsights: [],
    messageIntentSummary: {},
    receivers: createEmptyData().receivers,
    weeklyAccounts: [
      {
        customer: "Entrenamiento importado",
        completed: Number(training.totalConversations || 0),
        pending: 0,
        failed: 0,
        total: `${Number(training.totalMessages || 0)} mensajes`,
        status: "base_aprendizaje",
      },
    ],
  };
}

function renderAll() {
  renderSummary();
  renderAgent();
  renderNowQueue();
  renderCashbox();
  renderMessageInsights();
  renderIntentSummary();
  renderReviewItems();
  renderServices();
  renderChannels();
  renderReceivers();
  renderWeeklyAccounts();
  renderCloudStatus();
  renderBackups();
  renderCloudReport();
  renderSyncBatches();
}

async function createBackup() {
  const button = $("createBackupButton");
  if (button) {
    button.disabled = true;
    button.textContent = "Creando...";
  }
  try {
    const response = await fetch("/api/operativa-v2/cloud/backups", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ actor: "panel", note: "Respaldo manual desde AriadGSM Control" }),
    });
    const payload = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(payload.error || "No pude crear el respaldo");
    }
    state.data = payload.snapshot || state.data;
    renderAll();
  } catch (error) {
    window.alert(error.message || "No pude crear el respaldo");
  } finally {
    if (button) {
      button.disabled = false;
      button.textContent = "Crear respaldo";
    }
  }
}

async function loadOperativa() {
  const button = $("refreshButton");
  if (button) button.disabled = true;
  try {
    const response = await fetch("/api/operativa-v2", { cache: "no-store" });
    if (!response.ok) {
      throw new Error("No pude cargar la cabina operativa");
    }
    state.data = await response.json();
  } catch (error) {
    try {
      const fallbackResponse = await fetch("/api/dashboard", { cache: "no-store" });
      if (!fallbackResponse.ok) {
        throw new Error("dashboard no disponible");
      }
      state.data = buildDataFromDashboard(await fallbackResponse.json());
    } catch (fallbackError) {
      state.data = createEmptyData("No pude conectar con el motor de datos. Si acabas de desplegar, espera un momento y actualiza.");
    }
  } finally {
    renderAll();
    if (button) button.disabled = false;
  }
}

$("refreshButton")?.addEventListener("click", loadOperativa);
$("createBackupButton")?.addEventListener("click", createBackup);
loadOperativa();
window.setInterval(loadOperativa, 30000);
