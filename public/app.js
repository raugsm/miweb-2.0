const state = {
  data: null,
  notifications: null,
  selectedCaseId: null,
  activeSidebarTab: "operations",
};

const statsGrid = document.getElementById("statsGrid");
const caseList = document.getElementById("caseList");
const caseDetail = document.getElementById("caseDetail");
const resultsMeta = document.getElementById("resultsMeta");
const newCaseForm = document.getElementById("newCaseForm");
const passwordForm = document.getElementById("passwordForm");
const styleProfileForm = document.getElementById("styleProfileForm");
const trainingImportForm = document.getElementById("trainingImportForm");
const teamUserForm = document.getElementById("teamUserForm");
const pricingOfferForm = document.getElementById("pricingOfferForm");
const pricingConfigForm = document.getElementById("pricingConfigForm");
const pricingSummary = document.getElementById("pricingSummary");
const pricingMovements = document.getElementById("pricingMovements");
const pricingPreview = document.getElementById("pricingPreview");
const pricingRawText = document.getElementById("pricingRawText");
const telegramConfigForm = document.getElementById("telegramConfigForm");
const telegramCodeForm = document.getElementById("telegramCodeForm");
const telegramPasswordForm = document.getElementById("telegramPasswordForm");
const telegramStatusBox = document.getElementById("telegramStatusBox");
const telegramSources = document.getElementById("telegramSources");
const telegramMessages = document.getElementById("telegramMessages");
const telegramRefreshStatusButton = document.getElementById("telegramRefreshStatusButton");
const telegramStartAuthButton = document.getElementById("telegramStartAuthButton");
const telegramDiscoverButton = document.getElementById("telegramDiscoverButton");
const telegramSyncButton = document.getElementById("telegramSyncButton");
const teamUsersList = document.getElementById("teamUsersList");
const createBackupButton = document.getElementById("createBackupButton");
const backupResult = document.getElementById("backupResult");
const chatFileInput = document.getElementById("chatFileInput");
const chatDropzone = document.getElementById("chatDropzone");
const chatFileName = document.getElementById("chatFileName");
const operationsTab = document.getElementById("operationsTab");
const pricingTab = document.getElementById("pricingTab");
const telegramTab = document.getElementById("telegramTab");
const trainingTab = document.getElementById("trainingTab");
const settingsTab = document.getElementById("settingsTab");
const operationsPanel = document.getElementById("operationsPanel");
const pricingPanel = document.getElementById("pricingPanel");
const telegramPanel = document.getElementById("telegramPanel");
const trainingPanel = document.getElementById("trainingPanel");
const settingsPanel = document.getElementById("settingsPanel");
const trainingSummary = document.getElementById("trainingSummary");
const trainingResultBox = document.getElementById("trainingResultBox");
const trainingProgressWrap = document.getElementById("trainingProgressWrap");
const trainingProgressBar = document.getElementById("trainingProgressBar");
const trainingStatusText = document.getElementById("trainingStatusText");
const refreshButton = document.getElementById("refreshButton");
const logoutButton = document.getElementById("logoutButton");
const notificationList = document.getElementById("notificationList");
const searchInput = document.getElementById("searchInput");
const statusFilter = document.getElementById("statusFilter");
const categoryFilter = document.getElementById("categoryFilter");
const priorityFilter = document.getElementById("priorityFilter");
const operationsWorkspace = document.getElementById("operationsWorkspace");
const trainingWorkspace = document.getElementById("trainingWorkspace");
const pageShell = document.querySelector(".page-shell");

function normalizeChip(value) {
  return String(value || "").replace(/\s+/g, "");
}

function getProcedure(procedureId) {
  return state.data?.procedures.find((item) => item.id === procedureId);
}

function getStyleProfile() {
  return state.data?.meta?.styleProfile || {};
}

function getAssigneeOptions() {
  return getStyleProfile().assignees || [];
}

function getClosureReasonOptions() {
  return getStyleProfile().closureReasons || [];
}

function setSidebarTab(tab) {
  state.activeSidebarTab = tab;
  const showOperations = tab === "operations";
  const showPricing = tab === "pricing";
  const showTelegram = tab === "telegram";
  const showTraining = tab === "training";
  const showSettings = tab === "settings";
  operationsTab.classList.toggle("active", showOperations);
  pricingTab.classList.toggle("active", showPricing);
  telegramTab.classList.toggle("active", showTelegram);
  trainingTab.classList.toggle("active", showTraining);
  settingsTab.classList.toggle("active", showSettings);
  operationsPanel.classList.toggle("active", showOperations);
  pricingPanel.classList.toggle("active", showPricing);
  telegramPanel.classList.toggle("active", showTelegram);
  trainingPanel.classList.toggle("active", showTraining);
  settingsPanel.classList.toggle("active", showSettings);
  operationsWorkspace?.classList.toggle("active", showOperations);
  trainingWorkspace?.classList.toggle("active", showTraining);
  pageShell?.classList.toggle("training-mode", showTraining);
}

function updateTrainingProgress(percent, message) {
  if (!trainingProgressWrap || !trainingProgressBar || !trainingStatusText) {
    return;
  }

  trainingProgressWrap.classList.remove("is-hidden");
  trainingProgressBar.style.width = `${Math.max(0, Math.min(100, percent))}%`;
  trainingStatusText.textContent = message || "Procesando...";
}

function resetTrainingProgress() {
  if (!trainingProgressWrap || !trainingProgressBar || !trainingStatusText) {
    return;
  }

  trainingProgressWrap.classList.add("is-hidden");
  trainingProgressBar.style.width = "0%";
  trainingStatusText.textContent = "Preparando importacion...";
}

function renderTrainingImportResult(data, errorMessage) {
  if (!trainingResultBox) {
    return;
  }

  if (!data && !errorMessage) {
    trainingResultBox.classList.add("is-hidden");
    trainingResultBox.innerHTML = "";
    return;
  }

  if (errorMessage) {
    trainingResultBox.classList.remove("is-hidden");
    trainingResultBox.innerHTML = `
      <p class="eyebrow">Importacion fallida</p>
      <p class="muted">${errorMessage}</p>
    `;
    return;
  }

  const summary = data?.summary || {};
  trainingResultBox.classList.remove("is-hidden");
  trainingResultBox.innerHTML = `
    <p class="eyebrow">Conversacion lista</p>
    <div class="preview-grid">
      <div>
        <strong>Contacto</strong>
        <p class="muted">${summary.contactName || "Sin dato"}</p>
      </div>
      <div>
        <strong>Mensajes</strong>
        <p class="muted">${summary.messageCount || 0}</p>
      </div>
      <div>
        <strong>Cliente</strong>
        <p class="muted">${summary.clientMessageCount || 0}</p>
      </div>
      <div>
        <strong>Agente</strong>
        <p class="muted">${summary.agentMessageCount || 0}</p>
      </div>
    </div>
    <p class="muted">La conversacion ya fue guardada y sumada al aprendizaje del sistema.</p>
  `;
}

function uploadJsonWithProgress(url, payload) {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open("POST", url);
    xhr.setRequestHeader("Content-Type", "application/json");

    xhr.upload.onprogress = (event) => {
      if (!event.lengthComputable) {
        updateTrainingProgress(55, "Subiendo archivo...");
        return;
      }
      const ratio = event.total ? event.loaded / event.total : 0;
      const percent = 12 + Math.round(ratio * 76);
      updateTrainingProgress(percent, "Subiendo archivo...");
    };

    xhr.onreadystatechange = () => {
      if (xhr.readyState === XMLHttpRequest.HEADERS_RECEIVED) {
        updateTrainingProgress(92, "Analizando conversacion...");
      }
    };

    xhr.onload = () => {
      let data = {};
      try {
        data = xhr.responseText ? JSON.parse(xhr.responseText) : {};
      } catch (error) {
        reject(new Error("No pude leer la respuesta del servidor"));
        return;
      }

      if (xhr.status >= 200 && xhr.status < 300) {
        updateTrainingProgress(100, "Conversacion lista");
        resolve(data);
      } else {
        reject(new Error(data.error || "No pude procesar la conversacion"));
      }
    };

    xhr.onerror = () => reject(new Error("No pude subir el archivo"));
    xhr.send(JSON.stringify(payload));
  });
}

function getFilteredCases() {
  if (!state.data) {
    return [];
  }

  const search = String(searchInput?.value || "")
    .trim()
    .toLowerCase();
  const status = statusFilter?.value || "";
  const category = categoryFilter?.value || "";
  const priority = priorityFilter?.value || "";

  return state.data.cases.filter((item) => {
    const matchesSearch =
      !search ||
      [item.id, item.clientName, item.deviceModel, item.summary, item.category, item.assignee || ""]
        .join(" ")
        .toLowerCase()
        .includes(search);

    return (
      matchesSearch &&
      (!status || item.status === status) &&
      (!category || item.category === category) &&
      (!priority || item.priority === priority)
    );
  });
}

function renderStats() {
  if (!state.data) {
    return;
  }

  const cases = state.data.cases;
  const avgAgeMinutes =
    cases.length
      ? Math.round(
          cases.reduce((sum, item) => {
            const created = item.createdAt ? new Date(item.createdAt).getTime() : Date.now();
            return sum + Math.max(0, Math.round((Date.now() - created) / 60000));
          }, 0) / cases.length
        )
      : 0;
  const stats = [
    { label: "Casos activos", value: cases.length },
    { label: "Escalados", value: cases.filter((item) => item.status === "Escalado").length },
    { label: "ADB", value: cases.filter((item) => item.category === "adb").length },
    { label: "Con IA", value: state.data.meta?.aiConfigured ? "API lista" : "Local" },
    { label: "Correo cliente", value: state.data.meta?.customerNotificationModeLabel || "Inactivo" },
    { label: "Canal entrada", value: state.data.meta?.inboundChannelLabel || "Pendiente" },
    { label: "Edad promedio", value: `${avgAgeMinutes}m` },
  ];

  statsGrid.innerHTML = stats
    .map(
      (item) => `
        <article class="stat-card">
          <strong>${item.value}</strong>
          <span>${item.label}</span>
        </article>
      `
    )
    .join("");
}

function fillStyleProfileForm() {
  const profile = getStyleProfile();
  if (!profile) {
    return;
  }

  styleProfileForm.elements.businessName.value = profile.businessName || "";
  styleProfileForm.elements.agentDisplayName.value = profile.agentDisplayName || "";
  styleProfileForm.elements.tone.value = profile.tone || "";
  styleProfileForm.elements.responseLength.value = profile.responseLength || "";
  styleProfileForm.elements.preferredPhrases.value = (profile.preferredPhrases || []).join("\n");
  styleProfileForm.elements.forbiddenPhrases.value = (profile.forbiddenPhrases || []).join("\n");
  styleProfileForm.elements.assignees.value = (profile.assignees || []).join("\n");
  styleProfileForm.elements.closureReasons.value = (profile.closureReasons || []).join("\n");
  styleProfileForm.elements.receivedReply.value = profile.stageReplies?.received || "";
  styleProfileForm.elements.inProgressReply.value = profile.stageReplies?.in_progress || "";
  styleProfileForm.elements.escalatedReply.value = profile.stageReplies?.escalated || "";
  styleProfileForm.elements.pendingCustomerReply.value =
    profile.stageReplies?.pending_customer || "";
  styleProfileForm.elements.closedReply.value = profile.stageReplies?.closed || "";
  styleProfileForm.elements.notes.value = profile.notes || "";
}

function renderTrainingSummary() {
  if (!trainingSummary || !state.data) {
    return;
  }

  const summary = state.data.meta?.trainingSummary || {};
  const insights = state.data.meta?.trainingInsights || {};
  const conversations = state.data.meta?.trainingConversations || [];
  const topConversationTypes = insights.topConversationTypes || [];
  const topOutcomes = insights.topOutcomes || [];
  const topRequestedFields = insights.topRequestedFields || [];

  trainingSummary.innerHTML = `
    <div class="training-metrics">
      <article class="stat-card">
        <strong>${summary.totalConversations || 0}</strong>
        <span>Chats importados</span>
      </article>
      <article class="stat-card">
        <strong>${summary.totalMessages || 0}</strong>
        <span>Mensajes guardados</span>
      </article>
    </div>
    <div class="preview-grid">
      <div class="preview-card">
        <p class="eyebrow">Tipos detectados</p>
        ${
          topConversationTypes.length
            ? topConversationTypes
                .map((item) => `<p><strong>${item.label}</strong> Â· ${item.total}</p>`)
                .join("")
            : '<p class="muted">Aun no hay suficientes chats para detectar tipos.</p>'
        }
      </div>
      <div class="preview-card">
        <p class="eyebrow">Resultados frecuentes</p>
        ${
          topOutcomes.length
            ? topOutcomes.map((item) => `<p><strong>${item.label}</strong> Â· ${item.total}</p>`).join("")
            : '<p class="muted">Aun no hay suficientes chats para detectar resultados.</p>'
        }
      </div>
      <div class="preview-card">
        <p class="eyebrow">Datos que sueles pedir</p>
        ${
          topRequestedFields.length
            ? topRequestedFields
                .map((item) => `<span class="chip chip-soft">${item.label} Â· ${item.total}</span>`)
                .join("")
            : '<p class="muted">Todavia no se detectan campos repetidos.</p>'
        }
      </div>
    </div>
    <div class="log-list">
      ${
        conversations.length
          ? conversations
              .map(
                (item) => `
                  <div class="log-item">
                    <div>
                      <strong>${item.contactName}</strong>
                      <p class="muted">Tipo: ${item.conversationType || "sin clasificar"} Â· Resultado: ${item.outcome || "sin detectar"} Â· Confianza: ${Math.round((Number(item.confidence || 0) || 0) * 100)}%</p>
                      ${
                        item.requestedFields?.length
                          ? `<p class="muted">Datos pedidos: ${item.requestedFields.join(", ")}</p>`
                          : ""
                      }
                      ${
                        item.learnedPattern
                          ? `<p class="muted">Patron detectado: ${item.learnedPattern}</p>`
                          : ""
                      }
                      <p class="muted">${item.source} Â· ${item.messageCount} mensajes Â· cliente ${item.clientMessageCount} Â· agente ${item.agentMessageCount}</p>
                      ${
                        item.firstClientMessage
                          ? `<p class="muted">Primer mensaje cliente: ${item.firstClientMessage.slice(0, 140)}</p>`
                          : ""
                      }
                    </div>
                    <span class="muted">${item.importedAt || ""}</span>
                  </div>
                `
              )
              .join("")
          : '<p class="muted">Todavia no has importado conversaciones de entrenamiento.</p>'
      }
    </div>
  `;
}

function renderTeamUsers() {
  if (!teamUsersList || !state.data) {
    return;
  }

  const currentUser = state.data.meta?.currentUser;
  if (!currentUser || !["owner", "admin"].includes(currentUser.role)) {
    teamUsersList.innerHTML = '<p class="muted">Tu rol actual no permite gestionar usuarios.</p>';
    if (teamUserForm) {
      teamUserForm.classList.add("is-hidden");
    }
    return;
  }

  if (teamUserForm) {
    teamUserForm.classList.remove("is-hidden");
  }

  const users = state.data.meta?.teamUsers || [];
  teamUsersList.innerHTML = users.length
    ? users
        .map(
          (user) => `
            <div class="log-item">
              <div>
                <strong>${user.displayName}</strong>
                <p class="muted">${user.username} Â· ${user.role}</p>
              </div>
              <span class="muted">${user.active ? "Activo" : "Inactivo"}</span>
            </div>
          `
        )
        .join("")
    : '<p class="muted">Todavia no hay usuarios adicionales creados.</p>';
}

function fillPricingConfigForm() {
  if (!pricingConfigForm || !state.data) {
    return;
  }

  const config = state.data.meta?.pricingConfig || {};
  pricingConfigForm.elements.defaultCurrency.value = config.defaultCurrency || "USD";
  pricingConfigForm.elements.marginPercent.value = config.marginPercent ?? 18;
  pricingConfigForm.elements.fixedMargin.value = config.fixedMargin ?? 2;
  pricingConfigForm.elements.minimumSalePrice.value = config.minimumSalePrice ?? 5;
  pricingConfigForm.elements.roundingStep.value = config.roundingStep ?? 1;
}

function fillTelegramConfigForm() {
  if (!telegramConfigForm || !state.data) {
    return;
  }

  const config = state.data.meta?.telegramConfig || {};
  telegramConfigForm.elements.apiId.value = config.apiId || "";
  telegramConfigForm.elements.apiHash.value = config.apiHash || "";
  telegramConfigForm.elements.phoneNumber.value = config.phoneNumber || "";
  telegramConfigForm.elements.tdjsonDllPath.value = config.tdjsonDllPath || "";
  telegramConfigForm.elements.dataDir.value = config.dataDir || "";
  telegramConfigForm.elements.bridgeExePath.value = config.bridgeExePath || "";
}

function renderPricingSummary() {
  if (!pricingSummary || !state.data) {
    return;
  }

  const rows = state.data.meta?.pricingSummary || [];
  pricingSummary.innerHTML = rows.length
    ? rows
        .map(
          (item) => `
            <div class="log-item">
              <div>
                <strong>${item.serviceName}${item.variant ? ` Â· ${item.variant}` : ""}</strong>
                <p class="muted">Mejor costo: ${item.bestCost} ${item.currency} con ${item.bestSupplier}</p>
                <p class="muted">Ofertas: ${item.offerCount} Â· Tope visto: ${item.highestCost} ${item.currency}</p>
              </div>
              <span class="muted">Sugerido: ${item.suggestedSale} ${item.currency}</span>
            </div>
          `
        )
        .join("")
    : '<p class="muted">Todavia no has cargado ofertas de precio.</p>';
}

function renderPricingMovements() {
  if (!pricingMovements || !state.data) {
    return;
  }

  const rows = state.data.meta?.pricingMovements || [];
  pricingMovements.innerHTML = rows.length
    ? rows
        .map((item) => {
          const movementClass = `price-movement-${item.direction || "flat"}`;
          const variantLabel = item.variant ? ` Â· ${item.variant}` : "";
          const previousLine =
            item.previousCost !== null && item.previousCost !== undefined
              ? `Antes: ${item.previousCost} ${item.currency}`
              : "Sin referencia anterior";

          return `
            <div class="log-item price-movement-card">
              <div>
                <div class="timeline-title-row">
                  <strong>${item.serviceName}${variantLabel}</strong>
                  <span class="chip ${movementClass}">${item.deltaLabel}</span>
                </div>
                <p class="muted">Ahora: ${item.latestCost} ${item.currency} con ${item.latestSupplier}</p>
                <p class="muted">${previousLine}</p>
              </div>
              <span class="muted">${item.importedAt || ""}</span>
            </div>
          `;
        })
        .join("")
    : '<p class="muted">Todavia no hay cambios suficientes para comparar precios.</p>';
}

function renderTelegramStatus() {
  if (!telegramStatusBox || !state.data) {
    return;
  }

  const status = state.data.meta?.telegramStatus || null;
  if (!status) {
    telegramStatusBox.classList.add("is-hidden");
    telegramStatusBox.innerHTML = "";
    return;
  }

  telegramStatusBox.classList.remove("is-hidden");
  telegramStatusBox.innerHTML = `
    <p class="eyebrow">Estado Telegram</p>
    <div class="preview-grid">
      <div>
        <strong>Estado</strong>
        <p class="muted">${status.state || "sin dato"}</p>
      </div>
      <div>
        <strong>Mensaje</strong>
        <p class="muted">${status.message || "Sin novedades"}</p>
      </div>
    </div>
  `;
}

function renderTelegramSources() {
  if (!telegramSources || !state.data) {
    return;
  }

  const rows = state.data.meta?.telegramSources || [];
  telegramSources.innerHTML = rows.length
    ? rows
        .map(
          (item) => `
            <div class="log-item telegram-source-card">
              <div>
                <strong>${item.title}</strong>
                <p class="muted">${item.chatType || "chat"} Â· ${item.chatId}</p>
                <p class="muted">${item.username ? `@${item.username}` : "Sin username"} Â· Ultima sync: ${item.lastSyncedAt || "Nunca"}</p>
              </div>
              <div class="source-toggle-stack">
                <label class="inline-check">
                  <input type="checkbox" data-telegram-toggle="enabled" data-chat-id="${item.chatId}" ${item.enabled ? "checked" : ""} />
                  Activo
                </label>
                <label class="inline-check">
                  <input type="checkbox" data-telegram-toggle="autoImport" data-chat-id="${item.chatId}" ${item.autoImport ? "checked" : ""} />
                  Importar ofertas
                </label>
              </div>
            </div>
          `
        )
        .join("")
    : '<p class="muted">Todavia no hay chats descubiertos. Usa "Descubrir chats" primero.</p>';

  telegramSources.querySelectorAll("[data-telegram-toggle]").forEach((input) => {
    input.addEventListener("change", async () => {
      const chatId = input.dataset.chatId;
      const field = input.dataset.telegramToggle;
      const source = rows.find((item) => String(item.chatId) === String(chatId));
      if (!source) {
        return;
      }

      await fetch("/api/telegram/sources", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          ...source,
          [field]: input.checked,
        }),
      });

      await loadDashboard();
    });
  });
}

function renderTelegramMessages() {
  if (!telegramMessages || !state.data) {
    return;
  }

  const rows = state.data.meta?.telegramMessages || [];
  telegramMessages.innerHTML = rows.length
    ? rows
        .map(
          (item) => `
            <div class="log-item">
              <div>
                <strong>${item.chatTitle || item.chatId}</strong>
                <p class="muted">${item.senderName || "sin remitente"} Â· ${item.sentAt || ""}</p>
                <p class="muted">${item.text}</p>
                ${
                  item.detectedService
                    ? `<p class="muted">Detectado: ${item.detectedService}${item.detectedVariant ? ` Â· ${item.detectedVariant}` : ""}${item.detectedCost ? ` Â· ${item.detectedCost} ${item.detectedCurrency || ""}` : ""}</p>`
                    : ""
                }
              </div>
              <span class="muted">${item.importedOfferId ? "Oferta importada" : "Solo lectura"}</span>
            </div>
          `
        )
        .join("")
    : '<p class="muted">Todavia no hay mensajes capturados desde Telegram.</p>';
}

function renderPricingPreview(preview) {
  if (!pricingPreview) {
    return;
  }

  if (!preview) {
    pricingPreview.classList.add("is-hidden");
    pricingPreview.innerHTML = "";
    return;
  }

  pricingPreview.classList.remove("is-hidden");
  pricingPreview.innerHTML = `
    <p class="eyebrow">Lectura rapida</p>
    <div class="preview-grid">
      <div>
        <strong>Proveedor</strong>
        <p class="muted">${preview.supplierName || "Sin detectar"}</p>
      </div>
      <div>
        <strong>Servicio</strong>
        <p class="muted">${preview.serviceName || "Sin detectar"}</p>
      </div>
      <div>
        <strong>Variante</strong>
        <p class="muted">${preview.variant || "Sin detectar"}</p>
      </div>
      <div>
        <strong>Precio</strong>
        <p class="muted">${preview.cost ? `${preview.cost} ${preview.currency}` : "Sin detectar"}</p>
      </div>
    </div>
  `;
}

async function previewPricingOffer(rawText) {
  const safeText = String(rawText || "").trim();
  if (!safeText) {
    renderPricingPreview(null);
    return;
  }

  try {
    const response = await fetch("/api/pricing/preview", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ rawText: safeText }),
    });

    if (!response.ok) {
      renderPricingPreview(null);
      return;
    }

    const data = await response.json();
    const preview = data.preview || null;
    renderPricingPreview(preview);

    if (preview && pricingOfferForm) {
      if (!pricingOfferForm.elements.supplierName.value.trim() && preview.supplierName) {
        pricingOfferForm.elements.supplierName.value = preview.supplierName;
      }
      if (!pricingOfferForm.elements.serviceName.value.trim() && preview.serviceName) {
        pricingOfferForm.elements.serviceName.value = preview.serviceName;
      }
      if (!pricingOfferForm.elements.variant.value.trim() && preview.variant) {
        pricingOfferForm.elements.variant.value = preview.variant;
      }
    }
  } catch (error) {
    renderPricingPreview(null);
  }
}

function updateTrainingFileLabel(file) {
  if (!chatFileName) {
    return;
  }

  chatFileName.textContent = file?.name || "Ningun archivo seleccionado";
}

function readFileAsBase64(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const result = String(reader.result || "");
      const base64 = result.includes(",") ? result.split(",")[1] : result;
      resolve(base64);
    };
    reader.onerror = () => reject(new Error("No pude leer el archivo seleccionado"));
    reader.readAsDataURL(file);
  });
}

function renderCaseList() {
  if (!state.data) {
    return;
  }

  const filteredCases = getFilteredCases();
  const availableIds = new Set(filteredCases.map((item) => item.id));

  if (!availableIds.has(state.selectedCaseId)) {
    state.selectedCaseId = filteredCases[0]?.id || null;
  }

  resultsMeta.innerHTML = filteredCases.length
    ? `<p class="muted">${filteredCases.length} caso(s) visibles de ${state.data.cases.length}</p>`
    : '<p class="muted">No hay casos que coincidan con los filtros actuales.</p>';

  caseList.innerHTML = filteredCases
    .map((item) => {
      const activeClass = item.id === state.selectedCaseId ? "active" : "";
      return `
        <article class="case-card ${activeClass}" data-case-id="${item.id}">
          <div class="case-meta">
            <strong>${item.id}</strong>
            <span class="muted">${item.deviceModel}</span>
          </div>
          <h3>${item.clientName}</h3>
          <p class="muted">${item.summary}</p>
          <div class="chips">
            <span class="chip priority-${normalizeChip(item.priority)}">${item.priority}</span>
            <span class="chip status-${normalizeChip(item.status)}">${item.status}</span>
            <span class="chip">${item.category}</span>
            ${item.assignee ? `<span class="chip">${item.assignee}</span>` : ""}
          </div>
        </article>
      `;
    })
    .join("");

  caseList.querySelectorAll(".case-card").forEach((card) => {
    card.addEventListener("click", () => {
      state.selectedCaseId = card.dataset.caseId;
      renderCaseList();
      renderCaseDetail();
    });
  });
}

async function updateCaseStatus(caseId, status, closeReason = "") {
  await fetch(`/api/cases/${caseId}/status`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ status, closeReason }),
  });
  await loadDashboard();
}

async function assignCase(caseId, assignee) {
  await fetch(`/api/cases/${caseId}/assign`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ assignee }),
  });
  await loadDashboard();
}

function buildUnifiedTimeline(selectedCase) {
  const messageItems = (selectedCase.messages || []).map((message, index) => ({
    id: `message-${index}`,
    kind: "message",
    sender: message.sender,
    title:
      message.sender === "cliente"
        ? "Mensaje del cliente"
        : message.sender === "ia"
          ? "Respuesta de la IA"
          : "Mensaje",
    text: message.text,
    time: message.time,
    details: "",
    order: index,
  }));

  const logItems = [...(selectedCase.logs || [])]
    .reverse()
    .map((log, index) => ({
      id: `log-${index}`,
      kind: "log",
      sender: log.type,
      title:
        log.type === "manual"
          ? "Cambio manual"
          : log.type === "agent"
            ? "Evento del agente"
            : "Evento de IA",
      text: log.text,
      time: log.time,
      details: log.details || "",
      order: messageItems.length + index,
    }));

  return [...messageItems, ...logItems];
}

function renderCaseDetail() {
  if (!state.data) {
    return;
  }

  const filteredCases = getFilteredCases();
  const selectedCase =
    filteredCases.find((item) => item.id === state.selectedCaseId) || filteredCases[0];

  if (!selectedCase) {
    caseDetail.innerHTML = '<div class="detail-empty">No hay casos para mostrar.</div>';
    return;
  }

  state.selectedCaseId = selectedCase.id;
  const procedure = getProcedure(selectedCase.procedureId);
  const assigneeOptions = getAssigneeOptions();
  const closureReasons = getClosureReasonOptions();
  const unifiedTimeline = buildUnifiedTimeline(selectedCase);

  caseDetail.innerHTML = `
    <div class="detail-stack">
      <section class="detail-card">
        <div class="detail-header compact-header">
          <div>
            <p class="eyebrow">Caso seleccionado</p>
            <h2>${selectedCase.clientName}</h2>
            <p class="muted">${selectedCase.summary}</p>
          </div>
          <div class="chips">
            <span class="chip">${selectedCase.id}</span>
            <span class="chip status-${normalizeChip(selectedCase.status)}">${selectedCase.status}</span>
            <span class="chip">${selectedCase.aiMode || "sin-ai"}</span>
          </div>
        </div>

        <div class="detail-grid">
          <div>
            <p class="muted"><strong>Ultima actualizacion:</strong> ${selectedCase.lastUpdate}</p>
            <p class="muted"><strong>Responsable:</strong> ${selectedCase.assignee || "Sin asignar"}</p>
            <p class="muted"><strong>Contacto:</strong> ${selectedCase.contact}</p>
            <p class="muted"><strong>Abierto hace:</strong> ${selectedCase.timings?.age || "Sin dato"}</p>
            <p class="muted"><strong>Resolucion:</strong> ${selectedCase.timings?.resolution || "Abierto"}</p>
            ${
              selectedCase.closeReason
                ? `<p class="muted"><strong>Motivo de cierre:</strong> ${selectedCase.closeReason}</p>`
                : ""
            }
          </div>
          <div>
            <div class="assign-row">
              <select id="assigneeSelect">
                <option value="">Sin responsable</option>
                ${assigneeOptions
                  .map(
                    (option) =>
                      `<option value="${option}" ${option === selectedCase.assignee ? "selected" : ""}>${option}</option>`
                  )
                  .join("")}
              </select>
              <button id="assignButton" class="ghost-button">Guardar responsable</button>
            </div>
            <div class="assign-row">
              <select id="closeReasonSelect">
                ${closureReasons
                  .map(
                    (option) =>
                      `<option value="${option}" ${option === selectedCase.closeReason ? "selected" : ""}>${option}</option>`
                  )
                  .join("")}
              </select>
              <button id="markClosedButton" class="ghost-button">Cerrar con motivo</button>
            </div>
          </div>
        </div>

        <div class="quick-actions">
          <button id="markInProgressButton" class="ghost-button">Marcar en proceso</button>
          <button id="markEscalatedButton" class="ghost-button">Escalar</button>
          <button id="copyTicketButton" class="ghost-button">Copiar ticket</button>
          <button id="openPublicTicketButton" class="ghost-button">Ver ticket publico</button>
        </div>
      </section>

      <section class="detail-card">
        <div class="detail-header">
          <div>
            <p class="eyebrow">Procedimiento</p>
            <h3>${procedure?.name || "Sin procedimiento"}</h3>
          </div>
          <span class="chip">${selectedCase.deviceModel}</span>
        </div>
        <p class="muted">${procedure?.description || ""}</p>
        <div class="step-list">
          ${(procedure?.steps || [])
            .map((step, index) => {
              const isActive = index + 1 === selectedCase.currentStep;
              return `
                <div class="step-item ${isActive ? "active-step" : ""}">
                  <strong>Paso ${index + 1}</strong>
                  <span>${step}</span>
                </div>
              `;
            })
            .join("")}
        </div>
        <div class="action-row">
          <button id="replyButton" class="ghost-button">Generar respuesta al cliente</button>
          <button id="runAgentButton">Ejecutar validacion del agente</button>
        </div>
      </section>

      <section class="detail-card">
        <div class="detail-header">
          <div>
            <p class="eyebrow">Timeline del caso</p>
            <h3>Seguimiento completo</h3>
          </div>
          <span class="chip">${unifiedTimeline.length} eventos</span>
        </div>
        <div class="timeline">
          ${unifiedTimeline.length
            ? unifiedTimeline
                .map(
                  (item) => `
                    <div class="timeline-item timeline-${normalizeChip(item.sender)}">
                      <div>
                        <div class="timeline-title-row">
                          <strong>${item.title}</strong>
                          <span class="chip">${item.sender}</span>
                        </div>
                        <p class="muted">${item.text}</p>
                        ${item.details ? `<p class="muted">${item.details}</p>` : ""}
                      </div>
                      <span class="muted">${item.time}</span>
                    </div>
                  `
                )
                .join("")
            : '<p class="muted">Todavia no hay movimiento registrado en este caso.</p>'}
        </div>
      </section>
    </div>
  `;

  document.getElementById("runAgentButton")?.addEventListener("click", async () => {
    await fetch(`/api/cases/${selectedCase.id}/run`, { method: "POST" });
    await loadDashboard();
  });

  document.getElementById("replyButton")?.addEventListener("click", async () => {
    await fetch(`/api/cases/${selectedCase.id}/reply`, { method: "POST" });
    await loadDashboard();
  });

  document.getElementById("markInProgressButton")?.addEventListener("click", async () => {
    await updateCaseStatus(selectedCase.id, "En proceso");
  });

  document.getElementById("markEscalatedButton")?.addEventListener("click", async () => {
    await updateCaseStatus(selectedCase.id, "Escalado");
  });

  document.getElementById("markClosedButton")?.addEventListener("click", async () => {
    const closeReason = document.getElementById("closeReasonSelect")?.value || "Sin motivo indicado";
    await updateCaseStatus(selectedCase.id, "Cerrado", closeReason);
  });

  document.getElementById("copyTicketButton")?.addEventListener("click", async () => {
    await navigator.clipboard.writeText(selectedCase.id);
  });

  document.getElementById("openPublicTicketButton")?.addEventListener("click", () => {
    window.open(`/consultar-ticket.html?ticket=${encodeURIComponent(selectedCase.id)}`, "_blank");
  });

  document.getElementById("assignButton")?.addEventListener("click", async () => {
    const assignee = document.getElementById("assigneeSelect")?.value || "";
    await assignCase(selectedCase.id, assignee);
  });
}

function renderNotifications() {
  if (!notificationList || !state.notifications) {
    return;
  }

  const meta = state.notifications.meta || {};
  const items = state.notifications.notifications || [];

  notificationList.innerHTML = `
    <div class="notifications-grid">
      <div class="detail-card">
        <p class="eyebrow">Metricas por responsable</p>
        <h3>Resumen operativo</h3>
        <div class="log-list">
          ${(state.data?.meta?.assigneeMetrics || []).length
            ? state.data.meta.assigneeMetrics
                .map(
                  (item) => `
                    <div class="log-item">
                      <div>
                        <strong>${item.assignee}</strong>
                        <p class="muted">Total ${item.total} Â· Abiertos ${item.open} Â· Escalados ${item.escalated} Â· Cerrados ${item.closed}</p>
                      </div>
                      <span class="muted">${item.avgResolutionLabel}</span>
                    </div>
                  `
                )
                .join("")
            : '<p class="muted">Todavia no hay responsables con actividad registrada.</p>'}
        </div>
      </div>

      <div class="detail-card">
        <p class="eyebrow">Alertas</p>
        <h3>${meta.modeLabel || "Registro local"}</h3>
        <div class="log-list">
          ${items.length
            ? items
                .slice(0, 6)
                .map(
                  (item) => `
                    <div class="log-item">
                      <div>
                        <strong>${item.title}</strong>
                        <p class="muted">${item.caseId} Â· ${item.message}</p>
                        <p class="muted">${item.mode || "local"} Â· ${item.delivery || "stored"}</p>
                      </div>
                      <span class="muted">${item.createdAt || ""}</span>
                    </div>
                  `
                )
                .join("")
            : '<p class="muted">Todavia no hay alertas registradas.</p>'}
        </div>
      </div>
    </div>
  `;
}

async function loadDashboard() {
  const [dashboardResponse, notificationsResponse] = await Promise.all([
    fetch("/api/dashboard"),
    fetch("/api/notifications-log"),
  ]);

  state.data = await dashboardResponse.json();
  state.notifications = await notificationsResponse.json();

  if (!state.selectedCaseId && state.data.cases[0]) {
    state.selectedCaseId = state.data.cases[0].id;
  }

  renderStats();
  fillStyleProfileForm();
  fillPricingConfigForm();
  fillTelegramConfigForm();
  renderCaseList();
  renderCaseDetail();
  renderNotifications();
  renderTrainingSummary();
  renderTeamUsers();
  renderPricingSummary();
  renderPricingMovements();
  renderTelegramStatus();
  renderTelegramSources();
  renderTelegramMessages();
  setSidebarTab(state.activeSidebarTab);
}

newCaseForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const formData = new FormData(newCaseForm);

  await fetch("/api/cases", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      clientName: formData.get("clientName"),
      contact: formData.get("contact"),
      deviceModel: formData.get("deviceModel"),
      summary: formData.get("summary"),
    }),
  });

  newCaseForm.reset();
  await loadDashboard();
});

passwordForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const formData = new FormData(passwordForm);

  const response = await fetch("/api/auth/change", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      currentPassword: formData.get("currentPassword"),
      newPassword: formData.get("newPassword"),
    }),
  });

  if (response.ok) {
    passwordForm.reset();
  }
});

styleProfileForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const formData = new FormData(styleProfileForm);

  await fetch("/api/style-profile", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      businessName: formData.get("businessName"),
      agentDisplayName: formData.get("agentDisplayName"),
      tone: formData.get("tone"),
      responseLength: formData.get("responseLength"),
      preferredPhrases: formData.get("preferredPhrases"),
      forbiddenPhrases: formData.get("forbiddenPhrases"),
      assignees: formData.get("assignees"),
      closureReasons: formData.get("closureReasons"),
      receivedReply: formData.get("receivedReply"),
      inProgressReply: formData.get("inProgressReply"),
      escalatedReply: formData.get("escalatedReply"),
      pendingCustomerReply: formData.get("pendingCustomerReply"),
      closedReply: formData.get("closedReply"),
      notes: formData.get("notes"),
    }),
  });

  await loadDashboard();
});

pricingOfferForm?.addEventListener("submit", async (event) => {
  event.preventDefault();
  const formData = new FormData(pricingOfferForm);

  await fetch("/api/pricing/offers", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      supplierName: formData.get("supplierName"),
      serviceName: formData.get("serviceName"),
      variant: formData.get("variant"),
      source: formData.get("source"),
      rawText: formData.get("rawText"),
      notes: formData.get("notes"),
    }),
  });

  pricingOfferForm.reset();
  renderPricingPreview(null);
  await loadDashboard();
});

pricingConfigForm?.addEventListener("submit", async (event) => {
  event.preventDefault();
  const formData = new FormData(pricingConfigForm);

  await fetch("/api/pricing/config", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      defaultCurrency: formData.get("defaultCurrency"),
      marginPercent: formData.get("marginPercent"),
      fixedMargin: formData.get("fixedMargin"),
      minimumSalePrice: formData.get("minimumSalePrice"),
      roundingStep: formData.get("roundingStep"),
    }),
  });

  await loadDashboard();
});

telegramConfigForm?.addEventListener("submit", async (event) => {
  event.preventDefault();
  const formData = new FormData(telegramConfigForm);

  await fetch("/api/telegram/config", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      apiId: formData.get("apiId"),
      apiHash: formData.get("apiHash"),
      phoneNumber: formData.get("phoneNumber"),
      tdjsonDllPath: formData.get("tdjsonDllPath"),
      dataDir: formData.get("dataDir"),
      bridgeExePath: formData.get("bridgeExePath"),
    }),
  });

  await loadDashboard();
});

telegramCodeForm?.addEventListener("submit", async (event) => {
  event.preventDefault();
  const formData = new FormData(telegramCodeForm);
  await fetch("/api/telegram/auth/code", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ code: formData.get("code") }),
  });
  telegramCodeForm.reset();
  await loadDashboard();
});

telegramPasswordForm?.addEventListener("submit", async (event) => {
  event.preventDefault();
  const formData = new FormData(telegramPasswordForm);
  await fetch("/api/telegram/auth/password", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ password: formData.get("password") }),
  });
  telegramPasswordForm.reset();
  await loadDashboard();
});

teamUserForm?.addEventListener("submit", async (event) => {
  event.preventDefault();
  const formData = new FormData(teamUserForm);

  await fetch("/api/team/users", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      username: formData.get("username"),
      displayName: formData.get("displayName"),
      role: formData.get("role"),
      password: formData.get("password"),
    }),
  });

  teamUserForm.reset();
  await loadDashboard();
});

trainingImportForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const formData = new FormData(trainingImportForm);
  const file = formData.get("chatFile");
  const hasFile = file && typeof file === "object" && file.size > 0;
  const chatText = String(formData.get("chatText") || "").trim();
  const contactName = String(formData.get("contactName") || "").trim();
  const ownerAliases = String(formData.get("ownerAliases") || "").trim();
  const payload = {
    contactName,
    ownerAliases,
    source: formData.get("source"),
    notes: formData.get("notes"),
    anonymize: formData.get("anonymize") === "on",
  };

  if (!contactName || !ownerAliases) {
    renderTrainingImportResult(
      null,
      "Completa al menos el nombre del chat y tus nombres o aliases antes de procesar."
    );
    return;
  }

  if (!hasFile && !chatText) {
    renderTrainingImportResult(
      null,
      "Sube un archivo .txt o .zip, o pega el chat en el modo avanzado antes de procesar."
    );
    return;
  }

  if (hasFile) {
    payload.fileName = file.name;
    updateTrainingProgress(8, "Preparando archivo...");
    payload.contentBase64 = await readFileAsBase64(file);
  } else {
    payload.chatText = chatText;
  }

  try {
    renderTrainingImportResult(null, null);
    updateTrainingProgress(hasFile ? 12 : 20, hasFile ? "Subiendo archivo..." : "Enviando conversacion...");
    const data = await uploadJsonWithProgress(
      hasFile ? "/api/training/import-file" : "/api/training/import",
      payload
    );
    renderTrainingImportResult(data, null);
    trainingImportForm.reset();
    updateTrainingFileLabel(null);
    await loadDashboard();
  } catch (error) {
    renderTrainingImportResult(null, error.message || "No pude importar la conversacion");
  } finally {
    window.setTimeout(() => {
      resetTrainingProgress();
    }, 900);
  }
});

createBackupButton?.addEventListener("click", async () => {
  const response = await fetch("/api/admin/backup", { method: "POST" });
  const data = await response.json();
  backupResult.textContent = response.ok
    ? `Respaldo creado en: ${data.backupFile}`
    : data.error || "No pude crear el respaldo";
});

chatDropzone?.addEventListener("click", () => {
  chatFileInput?.click();
});

chatDropzone?.addEventListener("keydown", (event) => {
  if (event.key === "Enter" || event.key === " ") {
    event.preventDefault();
    chatFileInput?.click();
  }
});

chatFileInput?.addEventListener("change", () => {
  const file = chatFileInput.files?.[0] || null;
  updateTrainingFileLabel(file);
});

["dragenter", "dragover"].forEach((eventName) => {
  chatDropzone?.addEventListener(eventName, (event) => {
    event.preventDefault();
    chatDropzone.classList.add("dragging");
  });
});

["dragleave", "dragend", "drop"].forEach((eventName) => {
  chatDropzone?.addEventListener(eventName, (event) => {
    event.preventDefault();
    if (eventName !== "drop") {
      chatDropzone.classList.remove("dragging");
    }
  });
});

chatDropzone?.addEventListener("drop", (event) => {
  event.preventDefault();
  chatDropzone.classList.remove("dragging");
  const file = event.dataTransfer?.files?.[0];
  if (!file || !chatFileInput) {
    return;
  }

  const dataTransfer = new DataTransfer();
  dataTransfer.items.add(file);
  chatFileInput.files = dataTransfer.files;
  updateTrainingFileLabel(file);
});

refreshButton.addEventListener("click", loadDashboard);
logoutButton.addEventListener("click", async () => {
  await fetch("/api/auth/logout", { method: "POST" });
  window.location.href = "/login.html";
});
operationsTab.addEventListener("click", () => setSidebarTab("operations"));
pricingTab.addEventListener("click", () => setSidebarTab("pricing"));
telegramTab.addEventListener("click", () => setSidebarTab("telegram"));
trainingTab.addEventListener("click", () => setSidebarTab("training"));
settingsTab.addEventListener("click", () => setSidebarTab("settings"));

telegramRefreshStatusButton?.addEventListener("click", async () => {
  await fetch("/api/telegram/status/refresh", { method: "POST" });
  await loadDashboard();
});

telegramStartAuthButton?.addEventListener("click", async () => {
  await fetch("/api/telegram/auth/start", { method: "POST" });
  await loadDashboard();
});

telegramDiscoverButton?.addEventListener("click", async () => {
  await fetch("/api/telegram/discover", { method: "POST" });
  await loadDashboard();
});

telegramSyncButton?.addEventListener("click", async () => {
  await fetch("/api/telegram/sync", { method: "POST" });
  await loadDashboard();
});

let pricingPreviewTimer = null;
pricingRawText?.addEventListener("input", () => {
  clearTimeout(pricingPreviewTimer);
  pricingPreviewTimer = setTimeout(() => {
    previewPricingOffer(pricingRawText.value);
  }, 280);
});

[searchInput, statusFilter, categoryFilter, priorityFilter].forEach((element) => {
  element.addEventListener("input", () => {
    renderCaseList();
    renderCaseDetail();
  });
  element.addEventListener("change", () => {
    renderCaseList();
    renderCaseDetail();
  });
});

loadDashboard();
