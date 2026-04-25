const authStatus = document.getElementById("authStatus");
const setupForm = document.getElementById("setupForm");
const loginForm = document.getElementById("loginForm");
const authResult = document.getElementById("authResult");

function showAuthResult(message, isError = false) {
  authResult.classList.remove("is-hidden");
  authResult.classList.toggle("result-error", isError);
  authResult.classList.toggle("result-success", !isError);
  authResult.innerHTML = `<p>${message}</p>`;
}

async function loadAuthStatus() {
  const response = await fetch("/api/auth/status");
  const data = await response.json();

  if (data.authenticated) {
    window.location.href = "/";
    return;
  }

  if (data.configured) {
    authStatus.textContent = "Ingresa tu usuario y contrasena para abrir el panel interno.";
    loginForm.classList.remove("is-hidden");
  } else {
    authStatus.textContent = "Todavia no hay usuario inicial. Configuralo para proteger el panel.";
    setupForm.classList.remove("is-hidden");
  }
}

setupForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const formData = new FormData(setupForm);
  const response = await fetch("/api/auth/setup", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      username: formData.get("username"),
      displayName: formData.get("displayName"),
      password: formData.get("password"),
    }),
  });

  if (!response.ok) {
    showAuthResult("No pude guardar el usuario inicial. Usa una contrasena de al menos 8 caracteres.", true);
    return;
  }

  showAuthResult("Usuario inicial guardado. Ahora inicia sesion.");
  setupForm.classList.add("is-hidden");
  loginForm.classList.remove("is-hidden");
});

loginForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const formData = new FormData(loginForm);
  const response = await fetch("/api/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      username: formData.get("username"),
      password: formData.get("password"),
    }),
  });

  if (!response.ok) {
    showAuthResult("El usuario o la contrasena no son correctos.", true);
    return;
  }

  window.location.href = "/";
});

loadAuthStatus();
