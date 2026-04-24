const net = require("net");
const tls = require("tls");
const { getNotifications, saveNotification } = require("./db");

function isNotificationConfigured() {
  return Boolean(
    process.env.SMTP_HOST &&
      process.env.SMTP_PORT &&
      process.env.SMTP_FROM &&
      process.env.NOTIFY_TO
  );
}

function getCustomerNotificationModeLabel() {
  return isNotificationConfigured() ? "Correo al cliente disponible" : "Correo al cliente inactivo";
}

function getNotificationModeLabel() {
  return isNotificationConfigured() ? "SMTP activo" : "Registro local";
}

function readNotificationLog() {
  return getNotifications(100);
}

function formatEmailMessage({ from, to, subject, body }) {

  return [
    `From: ${from}`,
    `To: ${to}`,
    `Subject: ${subject}`,
    "MIME-Version: 1.0",
    "Content-Type: text/plain; charset=utf-8",
    "",
    body,
  ].join("\r\n");
}

function waitForCode(socket, expectedStartsWith) {
  return new Promise((resolve, reject) => {
    let buffer = "";

    function cleanup() {
      socket.off("data", onData);
      socket.off("error", onError);
    }

    function onError(error) {
      cleanup();
      reject(error);
    }

    function onData(chunk) {
      buffer += chunk.toString("utf8");
      const lines = buffer.split(/\r?\n/).filter(Boolean);
      const lastLine = lines[lines.length - 1] || "";

      if (/^\d{3} /.test(lastLine)) {
        cleanup();
        if (lastLine.startsWith(expectedStartsWith)) {
          resolve(buffer);
          return;
        }

        reject(new Error(`SMTP respondio con ${lastLine}`));
      }
    }

    socket.on("data", onData);
    socket.on("error", onError);
  });
}

function sendCommand(socket, command) {
  socket.write(`${command}\r\n`);
}

async function sendSmtpMail({ subject, body }) {
  const host = process.env.SMTP_HOST;
  const port = Number(process.env.SMTP_PORT || "587");
  const secure = String(process.env.SMTP_SECURE || "").toLowerCase() === "true" || port === 465;
  const username = process.env.SMTP_USER || "";
  const password = process.env.SMTP_PASS || "";
  const from = process.env.SMTP_FROM;
  const to = process.env.NOTIFY_TO;
  return sendSmtpMailTo({ from, to, subject, body, host, port, secure, username, password });
}

async function sendSmtpMailTo({ from, to, subject, body, host, port, secure, username, password }) {

  const socket = secure
    ? tls.connect({ host, port, servername: host })
    : net.createConnection({ host, port });

  await waitForCode(socket, "220");
  sendCommand(socket, `EHLO ${host}`);
  await waitForCode(socket, "250");

  if (!secure) {
    sendCommand(socket, "STARTTLS");
    await waitForCode(socket, "220");
    const tlsSocket = tls.connect({ socket, servername: host });
    await waitForCode(tlsSocket, "220").catch(() => Promise.resolve());
    sendCommand(tlsSocket, `EHLO ${host}`);
    await waitForCode(tlsSocket, "250");

    if (username && password) {
      sendCommand(tlsSocket, "AUTH LOGIN");
      await waitForCode(tlsSocket, "334");
      sendCommand(tlsSocket, Buffer.from(username).toString("base64"));
      await waitForCode(tlsSocket, "334");
      sendCommand(tlsSocket, Buffer.from(password).toString("base64"));
      await waitForCode(tlsSocket, "235");
    }

    sendCommand(tlsSocket, `MAIL FROM:<${from}>`);
    await waitForCode(tlsSocket, "250");
    sendCommand(tlsSocket, `RCPT TO:<${to}>`);
    await waitForCode(tlsSocket, "250");
    sendCommand(tlsSocket, "DATA");
    await waitForCode(tlsSocket, "354");
    tlsSocket.write(`${formatEmailMessage({ from, to, subject, body })}\r\n.\r\n`);
    await waitForCode(tlsSocket, "250");
    sendCommand(tlsSocket, "QUIT");
    tlsSocket.end();
    return;
  }

  if (username && password) {
    sendCommand(socket, "AUTH LOGIN");
    await waitForCode(socket, "334");
    sendCommand(socket, Buffer.from(username).toString("base64"));
    await waitForCode(socket, "334");
    sendCommand(socket, Buffer.from(password).toString("base64"));
    await waitForCode(socket, "235");
  }

  sendCommand(socket, `MAIL FROM:<${from}>`);
  await waitForCode(socket, "250");
  sendCommand(socket, `RCPT TO:<${to}>`);
  await waitForCode(socket, "250");
  sendCommand(socket, "DATA");
  await waitForCode(socket, "354");
  socket.write(`${formatEmailMessage({ from, to, subject, body })}\r\n.\r\n`);
  await waitForCode(socket, "250");
  sendCommand(socket, "QUIT");
  socket.end();
}

function looksLikeEmail(value) {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(String(value || "").trim());
}

async function notifyEvent(event) {
  const entry = {
    ...event,
    createdAt: new Date().toISOString(),
  };

  if (!isNotificationConfigured()) {
    saveNotification({
      ...entry,
      mode: "local",
      delivery: "stored",
    });

    return { mode: "local", delivered: false };
  }

  const subject = `[${event.caseId}] ${event.title}`;
  const body = [
    `Caso: ${event.caseId}`,
    `Evento: ${event.title}`,
    `Cliente: ${event.clientName}`,
    `Estado: ${event.status}`,
    `Modelo: ${event.deviceModel}`,
    "",
    event.message,
  ].join("\n");

  try {
    await sendSmtpMail({ subject, body });
    saveNotification({
      ...entry,
      mode: "smtp",
      delivery: "sent",
    });
    return { mode: "smtp", delivered: true };
  } catch (error) {
    saveNotification({
      ...entry,
      mode: "smtp-fallback",
      delivery: "failed",
      error: error.message,
    });
    return { mode: "smtp-fallback", delivered: false, error: error.message };
  }
}

async function notifyCustomer(caseItem, subject, body) {
  const to = String(caseItem?.contact || "").trim();

  if (!looksLikeEmail(to) || !isNotificationConfigured()) {
    return { mode: "skipped", delivered: false };
  }

  const host = process.env.SMTP_HOST;
  const port = Number(process.env.SMTP_PORT || "587");
  const secure = String(process.env.SMTP_SECURE || "").toLowerCase() === "true" || port === 465;
  const username = process.env.SMTP_USER || "";
  const password = process.env.SMTP_PASS || "";
  const from = process.env.CUSTOMER_SMTP_FROM || process.env.SMTP_FROM;

  try {
    await sendSmtpMailTo({
      from,
      to,
      subject,
      body,
      host,
      port,
      secure,
      username,
      password,
    });
    return { mode: "customer-smtp", delivered: true };
  } catch (error) {
    saveNotification({
      caseId: caseItem.id,
      title: "Correo al cliente no enviado",
      clientName: caseItem.clientName,
      status: caseItem.status,
      deviceModel: caseItem.deviceModel,
      message: error.message,
      createdAt: new Date().toISOString(),
      mode: "customer-smtp-fallback",
      delivery: "failed",
      error: error.message,
    });
    return { mode: "customer-smtp-fallback", delivered: false, error: error.message };
  }
}

module.exports = {
  getCustomerNotificationModeLabel,
  getNotificationModeLabel,
  isNotificationConfigured,
  notifyEvent,
  notifyCustomer,
  readNotificationLog,
};
