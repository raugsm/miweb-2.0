const ticketLookupForm = document.getElementById("ticketLookupForm");
const ticketResult = document.getElementById("ticketResult");

function showTicketResult(html, isError = false) {
  ticketResult.classList.remove("is-hidden");
  ticketResult.classList.toggle("result-error", isError);
  ticketResult.classList.toggle("result-success", !isError);
  ticketResult.innerHTML = html;
}

function renderTimeline(timeline) {
  return (timeline || [])
    .map(
      (item) => `
        <div class="timeline-item">
          <div>
            <strong>${item.sender}</strong>
            <p class="muted">${item.text}</p>
          </div>
          <span class="muted">${item.time}</span>
        </div>
      `
    )
    .join("");
}

async function lookupTicket(ticketId) {
  const response = await fetch(`/api/public/tickets/${encodeURIComponent(ticketId)}`);

  if (!response.ok) {
    showTicketResult(
      "<h3>No encontre ese ticket</h3><p>Revisa el codigo y vuelve a intentarlo.</p>",
      true
    );
    return;
  }

  const data = await response.json();

  showTicketResult(`
    <p class="eyebrow">Ticket encontrado</p>
    <h3>${data.ticketId}</h3>
    <div class="chips">
      <span class="chip">${data.status}</span>
      <span class="chip">${data.deviceModel}</span>
      <span class="chip">${data.category}</span>
    </div>
    <p class="ticket-copy"><strong>Resumen:</strong> ${data.summary}</p>
    <p class="ticket-copy"><strong>Procedimiento:</strong> ${data.procedureName}</p>
    <p class="ticket-copy"><strong>Paso actual:</strong> ${data.currentStep} de ${data.totalSteps || "?"}</p>
    <p class="ticket-copy"><strong>Ultima actualizacion:</strong> ${data.lastUpdate}</p>
    <p class="ticket-copy"><strong>Ultimo mensaje:</strong> ${data.latestMessage}</p>
    <div class="ticket-timeline">${renderTimeline(data.timeline)}</div>
  `);
}

ticketLookupForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const formData = new FormData(ticketLookupForm);
  await lookupTicket(String(formData.get("ticketId") || "").trim().toUpperCase());
});

const ticketIdFromUrl = new URLSearchParams(window.location.search).get("ticket");
if (ticketIdFromUrl) {
  ticketLookupForm.elements.ticketId.value = ticketIdFromUrl;
  lookupTicket(ticketIdFromUrl.toUpperCase());
}
