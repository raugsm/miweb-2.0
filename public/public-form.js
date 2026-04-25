const publicSupportForm = document.getElementById("publicSupportForm");
const publicResult = document.getElementById("publicResult");
const businessName = document.getElementById("businessName");
const agentName = document.getElementById("agentName");
const publicTitle = document.getElementById("publicTitle");
const publicCopy = document.getElementById("publicCopy");

function showPublicResult(html, isError = false) {
  publicResult.classList.remove("is-hidden");
  publicResult.classList.toggle("result-error", isError);
  publicResult.classList.toggle("result-success", !isError);
  publicResult.innerHTML = html;
}

async function loadPublicConfig() {
  const response = await fetch("/api/public-config");
  const data = await response.json();

  businessName.textContent = data.businessName || "Soporte Remoto";
  agentName.textContent = data.agentDisplayName || "Asistente";
  publicTitle.textContent = data.welcomeTitle || "Solicita tu soporte remoto";
  publicCopy.textContent = data.welcomeCopy || "";
}

publicSupportForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const formData = new FormData(publicSupportForm);

  const response = await fetch("/api/public/cases", {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      clientName: formData.get("clientName"),
      contact: formData.get("contact"),
      deviceModel: formData.get("deviceModel"),
      summary: formData.get("summary"),
    }),
  });

  if (!response.ok) {
    showPublicResult(
      "<h3>No pude recibir tu solicitud</h3><p>Vuelve a intentarlo en un momento.</p>",
      true
    );
    return;
  }

  const data = await response.json();

  showPublicResult(`
    <p class="eyebrow">Solicitud enviada</p>
    <h3>Tu caso fue registrado</h3>
    <p><strong>Codigo:</strong> ${data.ticketId}</p>
    <p><strong>Estado inicial:</strong> ${data.status}</p>
    <p>${data.message || "Recibimos tu solicitud y pronto te actualizaremos."}</p>
    <p><a href="/consultar-ticket.html?ticket=${encodeURIComponent(data.ticketId)}">Consultar este ticket</a></p>
  `);

  publicSupportForm.reset();
});

loadPublicConfig();
