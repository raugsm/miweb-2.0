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
  renderReviewItems();
  renderServices();
  renderChannels();
  renderReceivers();
  renderWeeklyAccounts();
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
loadOperativa();
window.setInterval(loadOperativa, 30000);
