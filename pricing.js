const { getMetadata, getPriceOffers, saveMetadata } = require("./db");

const DEFAULT_PRICING_CONFIG = {
  marginPercent: 18,
  fixedMargin: 2,
  minimumSalePrice: 5,
  roundingStep: 1,
  defaultCurrency: "USD",
};

function readPricingConfig() {
  const stored = getMetadata("pricing_config", null) || {};
  return {
    ...DEFAULT_PRICING_CONFIG,
    ...stored,
  };
}

function normalizeNumericString(value) {
  const raw = String(value || "").trim();
  if (!raw) {
    return "";
  }

  const cleaned = raw.replace(/[^\d.,-]/g, "");
  if (!cleaned) {
    return "";
  }

  const lastComma = cleaned.lastIndexOf(",");
  const lastDot = cleaned.lastIndexOf(".");

  if (lastComma > -1 && lastDot > -1) {
    if (lastComma > lastDot) {
      return cleaned.replace(/\./g, "").replace(",", ".");
    }
    return cleaned.replace(/,/g, "");
  }

  if (lastComma > -1) {
    const decimalDigits = cleaned.length - lastComma - 1;
    if (decimalDigits <= 2) {
      return cleaned.replace(/\./g, "").replace(",", ".");
    }
    return cleaned.replace(/,/g, "");
  }

  return cleaned;
}

function updatePricingConfig(input) {
  const nextConfig = {
    marginPercent: Number(input.marginPercent ?? DEFAULT_PRICING_CONFIG.marginPercent),
    fixedMargin: Number(input.fixedMargin ?? DEFAULT_PRICING_CONFIG.fixedMargin),
    minimumSalePrice: Number(
      input.minimumSalePrice ?? DEFAULT_PRICING_CONFIG.minimumSalePrice
    ),
    roundingStep: Math.max(0.01, Number(input.roundingStep ?? DEFAULT_PRICING_CONFIG.roundingStep)),
    defaultCurrency: String(input.defaultCurrency || DEFAULT_PRICING_CONFIG.defaultCurrency)
      .trim()
      .toUpperCase(),
  };

  saveMetadata("pricing_config", nextConfig);
  return nextConfig;
}

function roundUp(value, step) {
  const safeStep = Math.max(0.01, Number(step || 1));
  return Math.ceil(Number(value || 0) / safeStep) * safeStep;
}

function calculateSuggestedSale(cost, config = readPricingConfig()) {
  const percentTarget = Number(cost || 0) * (1 + Number(config.marginPercent || 0) / 100);
  const fixedTarget = Number(cost || 0) + Number(config.fixedMargin || 0);
  const base = Math.max(percentTarget, fixedTarget, Number(config.minimumSalePrice || 0));
  return roundUp(base, config.roundingStep);
}

function formatPriceDelta(delta) {
  const safeDelta = Number(delta || 0);
  if (!safeDelta) {
    return "sin cambios";
  }
  return `${safeDelta > 0 ? "+" : ""}${safeDelta.toFixed(2)}`;
}

function buildPricingSummary(offers = getPriceOffers(200), config = readPricingConfig()) {
  const grouped = new Map();

  for (const offer of offers) {
    const key = `${offer.serviceName}||${offer.variant || ""}||${offer.currency}`;
    if (!grouped.has(key)) {
      grouped.set(key, []);
    }
    grouped.get(key).push(offer);
  }

  return [...grouped.entries()]
    .map(([key, group]) => {
      const [serviceName, variant, currency] = key.split("||");
      const sorted = [...group].sort((a, b) => a.cost - b.cost);
      const newestFirst = [...group].sort(
        (a, b) => new Date(b.importedAt).getTime() - new Date(a.importedAt).getTime()
      );
      const bestOffer = sorted[0];
      const highestOffer = sorted[sorted.length - 1];
      const latestOffer = newestFirst[0];
      const previousOffer = newestFirst[1] || null;
      const latestDelta = previousOffer ? latestOffer.cost - previousOffer.cost : 0;
      return {
        serviceName,
        variant: variant || "",
        currency,
        bestCost: bestOffer.cost,
        bestSupplier: bestOffer.supplierName,
        offerCount: group.length,
        highestCost: highestOffer.cost,
        suggestedSale: calculateSuggestedSale(bestOffer.cost, config),
        latestImportedAt: latestOffer?.importedAt || "",
        latestCost: latestOffer?.cost || bestOffer.cost,
        latestSupplier: latestOffer?.supplierName || bestOffer.supplierName,
        latestDelta,
        latestDeltaLabel: formatPriceDelta(latestDelta),
      };
    })
    .sort((a, b) => a.serviceName.localeCompare(b.serviceName));
}

function buildPricingMovements(offers = getPriceOffers(200)) {
  const grouped = new Map();

  for (const offer of offers) {
    const key = `${offer.serviceName}||${offer.variant || ""}||${offer.currency}`;
    if (!grouped.has(key)) {
      grouped.set(key, []);
    }
    grouped.get(key).push(offer);
  }

  return [...grouped.entries()]
    .map(([key, group]) => {
      const [serviceName, variant, currency] = key.split("||");
      const newestFirst = [...group].sort(
        (a, b) => new Date(b.importedAt).getTime() - new Date(a.importedAt).getTime()
      );
      const latest = newestFirst[0];
      const previous = newestFirst[1] || null;
      const delta = previous ? latest.cost - previous.cost : 0;

      return {
        serviceName,
        variant: variant || "",
        currency,
        latestCost: latest.cost,
        latestSupplier: latest.supplierName,
        previousCost: previous?.cost ?? null,
        delta,
        deltaLabel: formatPriceDelta(delta),
        direction: delta < 0 ? "down" : delta > 0 ? "up" : "flat",
        importedAt: latest.importedAt,
      };
    })
    .sort((a, b) => new Date(b.importedAt).getTime() - new Date(a.importedAt).getTime());
}

function parsePriceText(rawText, fallbackCurrency = "USD") {
  const safeText = String(rawText || "").replace(/,/g, ".");
  const patterns = [
    /(?:\b(usd|usdt|eur|mxn|cop|clp|pen)\b|\$|s\/|€)\s*([\d.,]+)/gi,
    /([\d.,]+)\s*(usd|usdt|eur|mxn|cop|clp|pen)\b/gi,
  ];
  const candidates = [];

  for (const pattern of patterns) {
    let match;
    while ((match = pattern.exec(safeText))) {
      const first = match[1];
      const second = match[2];
      const currency = /[a-z]/i.test(String(first || "")) ? first : second;
      const rawNumber = /[a-z]/i.test(String(first || "")) ? second : first;
      const normalizedNumber = normalizeNumericString(rawNumber);
      const cost = Number(normalizedNumber);
      if (cost) {
        candidates.push({
          cost,
          currency: String(currency || fallbackCurrency)
            .replace("$", "USD")
            .replace("€", "EUR")
            .replace(/^S\/$/i, "PEN")
            .toUpperCase(),
        });
      }
    }
  }

  if (!candidates.length) {
    const plainNumber = safeText.match(/([\d.,]+)/);
    if (!plainNumber) {
      throw new Error("No pude detectar un precio en el texto");
    }

    const normalizedNumber = normalizeNumericString(plainNumber[1]);
    const cost = Number(normalizedNumber);
    if (!cost) {
      throw new Error("No pude detectar un precio en el texto");
    }

    return {
      cost,
      currency: String(fallbackCurrency || "USD").toUpperCase(),
    };
  }

  const bestCandidate = candidates.sort((a, b) => a.cost - b.cost)[0];
  if (!bestCandidate?.cost) {
    throw new Error("No pude detectar un precio en el texto");
  }
  return {
    cost: bestCandidate.cost,
    currency: bestCandidate.currency || String(fallbackCurrency || "USD").toUpperCase(),
  };
}

function cleanOfferLine(text) {
  return String(text || "")
    .replace(/\s+/g, " ")
    .replace(/^[\-\*\u2022\s]+/, "")
    .trim();
}

function extractVariant(text) {
  const safeText = String(text || "");
  const patterns = [
    /\b(?:frp\s*)?(?:sideload|side\s*load)\b/i,
    /\b(?:auth|autorizaci[oó]n|autorizacion)\b/i,
    /\bslot\s+[a-z0-9]+\b/i,
    /\btool\s+[a-z0-9._-]+\b/i,
    /\b(?:unlocktool|unlock\s*tool|umt|chimera|hydra|pandora|dft|miko|miflash|mi\s*flash|xiaomi\s*tool)\b/i,
    /\bserver\s+[a-z0-9._-]+\b/i,
    /\bmodalidad\s+[a-z0-9._-]+\b/i,
  ];

  for (const pattern of patterns) {
    const match = safeText.match(pattern);
    if (match) {
      return cleanOfferLine(match[0]);
    }
  }

  return "";
}

function normalizeSearchText(text) {
  return String(text || "")
    .toLowerCase()
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "");
}

function hasExplicitPrice(rawText) {
  return (
    /(?:\b(usd|usdt|eur|mxn|cop|clp|pen)\b|\$|s\/)\s*[\d.,]+/i.test(rawText) ||
    /[\d.,]+\s*(?:usd|usdt|eur|mxn|cop|clp|pen)\b/i.test(rawText)
  );
}

function isXiaomiFrpOffer(rawText) {
  const text = normalizeSearchText(rawText);
  if (!text) {
    return false;
  }

  const hasXiaomiContext =
    /\b(xiaomi|redmi|poco|hyperos|miui)\b/.test(text) ||
    /\b(sideload|side load|auth|autorizacion|unlocktool|unlock tool|mi flash|xiaomi tool)\b/.test(text);
  const hasServiceIntent =
    /\b(frp|sideload|side load|auth|autorizacion|remove|remover|bypass|servicio|creditos?)\b/.test(text);
  const negativeHints = /\b(no stock|sin stock|agotado|no hay|consulta|pregunta|busco|necesito|quien)\b/.test(text);

  return hasXiaomiContext && hasServiceIntent && hasExplicitPrice(rawText) && !negativeHints;
}

function detectXiaomiFrpTool(rawText) {
  const safeText = String(rawText || "");
  const patterns = [
    /\bunlock\s*tool\b/i,
    /\bunlocktool\b/i,
    /\bumt\b/i,
    /\bchimera\b/i,
    /\bhydra\b/i,
    /\bpandora\b/i,
    /\bdft\b/i,
    /\bmiko\b/i,
    /\bmi\s*flash\b/i,
    /\bxiaomi\s*tool\b/i,
    /\bserver\s+[a-z0-9._-]+\b/i,
    /\btool\s+[a-z0-9._-]+\b/i,
  ];

  for (const pattern of patterns) {
    const match = safeText.match(pattern);
    if (match) {
      return cleanOfferLine(match[0]).replace(/\s+/g, " ");
    }
  }

  return "";
}

function detectXiaomiFrpService(rawText) {
  const text = normalizeSearchText(rawText);
  if (/\bsideload|side load\b/.test(text)) {
    return "Xiaomi FRP Sideload";
  }
  if (/\bauth|autorizacion\b/.test(text)) {
    return "Xiaomi FRP Auth";
  }
  return "Xiaomi FRP";
}

function inferOfferDetails(rawText, fallbackCurrency = "USD") {
  const safeText = String(rawText || "").replace(/\r/g, "").trim();
  if (!safeText) {
    return {
      supplierName: "",
      serviceName: "",
      variant: "",
      cost: null,
      currency: String(fallbackCurrency || "USD").toUpperCase(),
    };
  }

  const lines = safeText
    .split("\n")
    .map((line) => cleanOfferLine(line))
    .filter(Boolean);

  const firstLine = lines[0] || "";
  const secondLine = lines[1] || "";
  const priceInfo = parsePriceText(safeText, fallbackCurrency);
  const xiaomiFrp = isXiaomiFrpOffer(safeText);

  const supplierMatch =
    safeText.match(/(?:proveedor|supplier|vendor)\s*[:\-]\s*([^\n]+)/i) ||
    safeText.match(/(?:by|seller|vendedor)\s*[:\-]\s*([^\n]+)/i) ||
    safeText.match(/@([a-z0-9._]+)/i);

  const supplierName = supplierMatch
    ? cleanOfferLine(supplierMatch[1])
    : firstLine.length <= 40 && lines.length > 1
      ? firstLine
      : "";

  const serviceBaseLine = supplierName && firstLine === supplierName ? secondLine : firstLine;
  const serviceName = cleanOfferLine(
    String(serviceBaseLine || "")
      .replace(/\b(usd|usdt|eur|mxn|cop|clp|pen)\b/gi, "")
      .replace(/[$€]/g, "")
      .replace(/\b\d+([.,]\d+)?\b/g, "")
  );

  return {
    supplierName,
    serviceName: xiaomiFrp ? detectXiaomiFrpService(safeText) : serviceName,
    variant: xiaomiFrp
      ? [detectXiaomiFrpTool(safeText), extractVariant(safeText)]
          .filter(Boolean)
          .filter((value, index, array) => array.indexOf(value) === index)
          .join(" - ")
      : extractVariant(safeText),
    cost: priceInfo.cost,
    currency: priceInfo.currency,
    serviceFamily: xiaomiFrp ? "xiaomi_frp" : "generic",
    confidence: xiaomiFrp ? 0.9 : serviceName ? 0.55 : 0.25,
  };
}

function buildXiaomiFrpPricingBoard(offers = getPriceOffers(300), config = readPricingConfig()) {
  return buildPricingSummary(
    offers.filter((offer) => {
      const text = `${offer.serviceName} ${offer.variant || ""} ${offer.rawText || ""}`;
      return isXiaomiFrpOffer(text) || normalizeSearchText(offer.serviceName).includes("xiaomi frp");
    }),
    config
  ).map((item) => ({
    ...item,
    serviceFamily: "xiaomi_frp",
  }));
}

module.exports = {
  buildXiaomiFrpPricingBoard,
  buildPricingMovements,
  buildPricingSummary,
  calculateSuggestedSale,
  inferOfferDetails,
  isXiaomiFrpOffer,
  parsePriceText,
  readPricingConfig,
  updatePricingConfig,
};
