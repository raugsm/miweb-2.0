(function () {
  function ensureBrandStyles() {
    if (document.querySelector('link[href="/brand.css"]')) return;
    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = "/brand.css";
    document.head.appendChild(link);
  }

  function ensureTelegramWorkspace() {
    const existing = document.getElementById("telegramWorkspace");
    if (existing) return existing;

    const dashboard = document.querySelector(".dashboard");
    const sourcePanel = document.getElementById("telegramPanel");
    if (!dashboard || !sourcePanel) return null;

    const workspace = document.createElement("section");
    workspace.id = "telegramWorkspace";
    workspace.className = "panel workspace-panel telegram-workspace";
    workspace.innerHTML = `
      <div class="panel-head">
        <div>
          <p class="eyebrow">Telegram</p>
          <h2>Monitor de precios y chats</h2>
          <p class="helper-copy">Conecta Telegram, descubre tus chats disponibles y activa solo los grupos o proveedores que quieras monitorear.</p>
        </div>
      </div>
      <div class="telegram-layout"></div>
    `;

    const layout = workspace.querySelector(".telegram-layout");
    [...sourcePanel.querySelectorAll(":scope > section")].forEach((section, index) => {
      section.classList.remove("form-card");
      section.classList.add("detail-card");
      if (index === 0) section.classList.add("telegram-config-card");
      if (index === 1) section.classList.add("telegram-security-card");
      if (index === 2) section.classList.add("telegram-sources-card");
      if (index === 3) section.classList.add("telegram-messages-card");
      layout.appendChild(section);
    });

    sourcePanel.innerHTML = `
      <section class="form-card">
        <h2>Telegram listo</h2>
        <p class="helper-copy">La configuracion ahora se abre en el espacio grande de la derecha para que no quede apretada.</p>
        <p class="helper-copy">Desde ahi puedes guardar credenciales, revisar estado, descubrir chats y sincronizar mensajes.</p>
      </section>
    `;

    dashboard.appendChild(workspace);
    return workspace;
  }

  function ensureXiaomiFrpBoard() {
    const pricingPanel = document.getElementById("pricingPanel");
    if (!pricingPanel || document.getElementById("xiaomiFrpBoard")) return;

    const card = document.createElement("section");
    card.className = "form-card xiaomi-frp-card";
    card.innerHTML = `
      <p class="eyebrow">Motor Xiaomi FRP</p>
      <h2>Mejor precio detectado</h2>
      <p class="helper-copy">Leo las ofertas importadas desde Telegram y te dejo el costo mas bajo, proveedor, variante/herramienta y precio sugerido para vender.</p>
      <div id="xiaomiFrpBoard" class="log-list">
        <p class="muted">Aun no hay ofertas Xiaomi FRP importadas. Activa chats de proveedores y pulsa Sincronizar ahora.</p>
      </div>
    `;

    const anchor = pricingPanel.querySelector(".panel-head")?.nextElementSibling;
    pricingPanel.insertBefore(card, anchor || pricingPanel.firstChild);
  }

  function renderXiaomiFrpBoard(dashboard) {
    const board = document.getElementById("xiaomiFrpBoard");
    const rows = dashboard?.meta?.pricingSummary || [];
    if (!board) return;

    const xiaomiRows = rows.filter((item) =>
      `${item.serviceName || ""} ${item.variant || ""}`.toLowerCase().includes("xiaomi frp")
    );

    board.innerHTML = xiaomiRows.length
      ? xiaomiRows
          .map(
            (item) => `
              <div class="log-item">
                <div>
                  <div class="timeline-title-row">
                    <strong>${item.serviceName}</strong>
                    <span class="chip">Mejor actual</span>
                  </div>
                  <p class="muted">Costo: ${item.bestCost} ${item.currency} con ${item.bestSupplier || "Proveedor sin nombre"}</p>
                  <p class="muted">Variante: ${item.variant || "General"} - Ofertas leidas: ${item.offerCount}</p>
                  <p class="muted">Ultimo visto: ${item.latestCost} ${item.currency} con ${item.latestSupplier || "Proveedor sin nombre"}</p>
                </div>
                <span class="muted">Vender desde ${item.suggestedSale} ${item.currency}</span>
              </div>
            `
          )
          .join("")
      : '<p class="muted">Aun no hay ofertas Xiaomi FRP importadas. Activa chats de proveedores y pulsa Sincronizar ahora.</p>';
  }

  async function refreshXiaomiFrpBoard() {
    try {
      const response = await fetch("/api/dashboard", { cache: "no-store" });
      if (!response.ok) return;
      renderXiaomiFrpBoard(await response.json());
    } catch (error) {
      // El dashboard principal puede seguir funcionando aunque este bloque no refresque.
    }
  }

  function showTelegramWorkspace(workspace) {
    document.querySelectorAll(".workspace-panel").forEach((panel) => panel.classList.remove("active"));
    workspace?.classList.add("active");
    document.querySelector(".page-shell")?.classList.add("training-mode");
  }

  function hideTelegramWorkspace(workspace, keepWide) {
    workspace?.classList.remove("active");
    if (!keepWide) document.querySelector(".page-shell")?.classList.remove("training-mode");
  }

  function setFeedback(box, title, message, isError) {
    if (!box) return;
    box.classList.remove("is-hidden");
    box.innerHTML = `
      <p class="eyebrow">${title}</p>
      <p class="muted${isError ? " error-copy" : ""}">${message}</p>
    `;
  }

  function init() {
    ensureBrandStyles();
    const workspace = ensureTelegramWorkspace();
    ensureXiaomiFrpBoard();
    refreshXiaomiFrpBoard();
    window.setInterval(refreshXiaomiFrpBoard, 30000);

    const telegramTab = document.getElementById("telegramTab");

    telegramTab?.addEventListener("click", () => {
      window.setTimeout(() => showTelegramWorkspace(workspace), 0);
    });

    ["operationsTab", "pricingTab", "settingsTab"].forEach((id) => {
      document.getElementById(id)?.addEventListener("click", () => {
        hideTelegramWorkspace(workspace, false);
        if (id === "pricingTab") window.setTimeout(refreshXiaomiFrpBoard, 0);
      });
    });

    document.getElementById("trainingTab")?.addEventListener("click", () => {
      hideTelegramWorkspace(workspace, true);
    });

    const form = document.getElementById("telegramConfigForm");
    const box = document.getElementById("telegramStatusBox");
    if (!form) return;

    form.addEventListener(
      "submit",
      (event) => {
        const formData = new FormData(form);
        const apiId = String(formData.get("apiId") || "").trim();
        const apiHash = String(formData.get("apiHash") || "").trim();
        const phoneNumber = String(formData.get("phoneNumber") || "").trim();

        if (!apiId || !apiHash || !phoneNumber) {
          event.preventDefault();
          event.stopImmediatePropagation();
          setFeedback(box, "Faltan datos", "Completa API ID, API Hash y numero de telefono antes de guardar.", true);
          return;
        }

        if (!/^\d+$/.test(apiId)) {
          event.preventDefault();
          event.stopImmediatePropagation();
          setFeedback(box, "API ID invalido", "El API ID debe llevar solo numeros.", true);
          return;
        }

        if (!phoneNumber.startsWith("+")) {
          event.preventDefault();
          event.stopImmediatePropagation();
          setFeedback(box, "Telefono invalido", "Escribe el telefono con prefijo internacional, por ejemplo +519...", true);
          return;
        }

        setFeedback(box, "Guardando", "Estoy validando y guardando los datos de Telegram...");
      },
      true
    );
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
