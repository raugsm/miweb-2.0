const crypto = require("crypto");
const {
  getMetadata,
  getUserByUsername,
  getUsers,
  saveMetadata,
  saveUser,
} = require("./db");

const SESSION_COOKIE = "support_admin_session";

function normalizeUsername(username) {
  return String(username || "")
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9._-]/g, "");
}

function createPasswordRecord(password) {
  const salt = crypto.randomBytes(16).toString("hex");
  const hash = crypto.scryptSync(password, salt, 64).toString("hex");
  return { salt, hash, createdAt: new Date().toISOString() };
}

function hasAnyUser() {
  return getUsers().length > 0;
}

function migrateLegacyAdminIfNeeded() {
  if (hasAnyUser()) {
    return;
  }

  const legacy = getMetadata("admin_auth", null);
  if (!legacy?.salt || !legacy?.hash) {
    return;
  }

  const now = new Date().toISOString();
  saveUser({
    username: "owner",
    displayName: "Owner",
    role: "owner",
    passwordSalt: legacy.salt,
    passwordHash: legacy.hash,
    active: true,
    createdAt: legacy.createdAt || now,
    updatedAt: now,
  });
}

function createUser({ username, displayName, password, role = "agent" }) {
  const safeUsername = normalizeUsername(username);
  if (!safeUsername) {
    throw new Error("Usuario invalido");
  }
  if (String(password || "").length < 8) {
    throw new Error("La contrasena debe tener al menos 8 caracteres");
  }
  if (!["owner", "admin", "agent"].includes(role)) {
    throw new Error("Rol invalido");
  }
  if (getUserByUsername(safeUsername)) {
    throw new Error("Ese usuario ya existe");
  }

  const record = createPasswordRecord(password);
  const now = new Date().toISOString();
  saveUser({
    username: safeUsername,
    displayName: String(displayName || safeUsername).trim() || safeUsername,
    role,
    passwordSalt: record.salt,
    passwordHash: record.hash,
    active: true,
    createdAt: now,
    updatedAt: now,
  });

  return getUserByUsername(safeUsername);
}

function updateUserPassword(username, password) {
  const user = getUserByUsername(username);
  if (!user) {
    throw new Error("Usuario no encontrado");
  }
  if (String(password || "").length < 8) {
    throw new Error("La contrasena debe tener al menos 8 caracteres");
  }

  const record = createPasswordRecord(password);
  saveUser({
    username: user.username,
    displayName: user.displayName,
    role: user.role,
    passwordSalt: record.salt,
    passwordHash: record.hash,
    active: user.active,
    createdAt: user.createdAt,
    updatedAt: new Date().toISOString(),
  });
}

function listUsers() {
  migrateLegacyAdminIfNeeded();
  return getUsers();
}

function hasAdminPassword() {
  migrateLegacyAdminIfNeeded();
  return hasAnyUser();
}

function verifyPassword(username, password) {
  migrateLegacyAdminIfNeeded();
  const user = getUserByUsername(normalizeUsername(username));
  if (!user || !user.active) {
    return null;
  }

  const candidate = crypto.scryptSync(password, user.passwordSalt, 64).toString("hex");
  const matches = crypto.timingSafeEqual(
    Buffer.from(candidate, "hex"),
    Buffer.from(user.passwordHash, "hex")
  );

  return matches
    ? {
        username: user.username,
        displayName: user.displayName,
        role: user.role,
      }
    : null;
}

function setAdminPassword(password, username = "owner", displayName = "Owner") {
  migrateLegacyAdminIfNeeded();
  if (hasAnyUser()) {
    throw new Error("Ya existe un usuario inicial");
  }
  return createUser({ username, displayName, password, role: "owner" });
}

function getSessionSecret() {
  let secret = getMetadata("session_secret", null);
  if (!secret) {
    secret = crypto.randomBytes(32).toString("hex");
    saveMetadata("session_secret", secret);
  }
  return secret;
}

function createSessionToken(user) {
  const payload = {
    exp: Date.now() + 1000 * 60 * 60 * 12,
    nonce: crypto.randomBytes(12).toString("hex"),
    username: user.username,
    displayName: user.displayName,
    role: user.role,
  };
  const encoded = Buffer.from(JSON.stringify(payload)).toString("base64url");
  const signature = crypto
    .createHmac("sha256", getSessionSecret())
    .update(encoded)
    .digest("base64url");
  return `${encoded}.${signature}`;
}

function verifySessionToken(token) {
  if (!token || !token.includes(".")) {
    return null;
  }

  const [encoded, signature] = token.split(".");
  const expected = crypto
    .createHmac("sha256", getSessionSecret())
    .update(encoded)
    .digest("base64url");

  if (signature !== expected) {
    return null;
  }

  try {
    const payload = JSON.parse(Buffer.from(encoded, "base64url").toString("utf8"));
    if (payload.exp <= Date.now()) {
      return null;
    }

    const user = getUserByUsername(payload.username);
    if (!user || !user.active) {
      return null;
    }

    return {
      username: user.username,
      displayName: user.displayName,
      role: user.role,
    };
  } catch (error) {
    return null;
  }
}

function parseCookies(cookieHeader) {
  return String(cookieHeader || "")
    .split(";")
    .map((item) => item.trim())
    .filter(Boolean)
    .reduce((acc, item) => {
      const index = item.indexOf("=");
      if (index === -1) {
        return acc;
      }
      const key = item.slice(0, index);
      const value = item.slice(index + 1);
      acc[key] = value;
      return acc;
    }, {});
}

function getAuthenticatedUser(req) {
  const cookies = parseCookies(req.headers.cookie);
  return verifySessionToken(cookies[SESSION_COOKIE]);
}

function isAuthenticated(req) {
  return Boolean(getAuthenticatedUser(req));
}

function buildSessionCookie(token) {
  const secure = String(process.env.SESSION_SECURE || "").toLowerCase() === "true";
  return `${SESSION_COOKIE}=${token}; HttpOnly; Path=/; Max-Age=43200; SameSite=Lax${secure ? "; Secure" : ""}`;
}

function buildClearSessionCookie() {
  const secure = String(process.env.SESSION_SECURE || "").toLowerCase() === "true";
  return `${SESSION_COOKIE}=; HttpOnly; Path=/; Expires=Thu, 01 Jan 1970 00:00:00 GMT; SameSite=Lax${secure ? "; Secure" : ""}`;
}

module.exports = {
  buildClearSessionCookie,
  buildSessionCookie,
  createSessionToken,
  createUser,
  getAuthenticatedUser,
  hasAdminPassword,
  isAuthenticated,
  listUsers,
  normalizeUsername,
  setAdminPassword,
  updateUserPassword,
  verifyPassword,
};
