(function () {
  function setFeedback(box, title, message, isError) {
    if (!box) return;
    box.classList.remove("is-hidden");
    box.innerHTML = `
      <p class="eyebrow">${title}</p>
      <p class="muted${isError ? " error-copy" : ""}">${message}</p>
    `;
  }

  async function postJson(url, payload) {
    const response = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });

    let data = {};
    try {
      data = await response.json();
    } catch (error) {
      data = {};
    }

    if (!response.ok) {
      throw new Error(data.error || "No pude completar la accion.");
    }

    return data;
  }

  function initTelegramCloudForm() {
    const form = document.getElementById("telegramConfigForm");
    const box = document.getElementById("telegramStatusBox");
    if (!form || !box) return;

    form.addEventListener(
      "submit",
      async (event) => {
        event.preventDefault();
        event.stopImmediatePropagation();

        const formData = new FormData(form);
        const apiId = String(formData.get("apiId") || "").trim();
        const apiHash = String(formData.get("apiHash") || "").trim();
        const phoneNumber = String(formData.get("phoneNumber") || "").trim();

        if (!apiId || !apiHash || !phoneNumber) {
          setFeedback(
            box,
            "Faltan datos",
            "Completa API ID, API Hash y numero de telefono antes de guardar.",
            true
          );
          return;
        }

        if (!/^\d+$/.test(apiId)) {
          setFeedback(box, "API ID invalido", "El API ID debe llevar solo numeros.", true);
          return;
        }

        if (!phoneNumber.startsWith("+")) {
          setFeedback(
            box,
            "Telefono invalido",
            "Escribe el telefono con prefijo internacional, por ejemplo +519...",
            true
          );
          return;
        }

        setFeedback(box, "Guardando", "Estoy guardando tus datos de Telegram...");

        try {
          await postJson("/api/telegram/config", {
            apiId,
            apiHash,
            phoneNumber,
            sessionString: formData.get("sessionString"),
            runtimeMode: formData.get("runtimeMode"),
            syncIntervalMinutes: formData.get("syncIntervalMinutes"),
            tdjsonDllPath: formData.get("tdjsonDllPath") || "",
            dataDir: formData.get("dataDir") || "",
            bridgeExePath: formData.get("bridgeExePath") || "",
          });

          const refresh = await postJson("/api/telegram/status/refresh", {});
          setFeedback(
            box,
            "Datos guardados",
            refresh.status?.message || "La configuracion de Telegram fue guardada correctamente."
          );
        } catch (error) {
          setFeedback(
            box,
            "No pude guardar",
            error.message || "No pude guardar la configuracion de Telegram.",
            true
          );
        }
      },
      true
    );
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initTelegramCloudForm);
  } else {
    initTelegramCloudForm();
  }
})();
