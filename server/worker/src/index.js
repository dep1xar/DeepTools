// DeepTools anonymous telemetry on Cloudflare Workers + D1.
// Same HTTP API as server/deeptools_telemetry.py:
//   POST /api/v1/event
//   GET  /api/v1/online
//   GET  /api/v1/stats
//   GET  /api/v1/admin/errors
//   GET  /health
// IP addresses are never stored. Country comes from Cloudflare's CF-IPCountry.

const ONLINE_WINDOW = 300;       // 5 minutes without a heartbeat = offline
const RETENTION_DAYS = 90;
const MAX_BODY = 16 * 1024;
const MAX_ID = 64;
const EVENT_NAMES = new Set(["start", "heartbeat", "exit", "error"]);

// Best-effort per-isolate rate limit. It keeps IPs only in memory and never
// writes them to D1. Multiple isolates can each allow the same IP; that is
// fine for an anonymous telemetry endpoint.
const rateBuckets = new Map();
const RATE_LIMIT = 120;
const RATE_WINDOW_MS = 60 * 1000;

export default {
  async fetch(request, env) {
    if (request.method === "OPTIONS") return corsPreflight();

    if (rateLimited(request)) {
      return json({ ok: false, error: "rate_limited" }, 429);
    }

    const url = new URL(request.url);
    const path = url.pathname.replace(/\/+$/, "") || "/";

    try {
      if (request.method === "GET" && path === "/health") {
        return json({ ok: true, service: "deeptools-telemetry" });
      }

      if (request.method === "GET" && path === "/api/v1/online") {
        return json(await buildOnline(env));
      }

      if (request.method === "GET" && path === "/api/v1/stats") {
        if (!viewAuthorized(request, env)) {
          return json({ ok: false, error: "forbidden" }, 403);
        }
        return json(await buildStats(env));
      }

      if (request.method === "GET" && path === "/api/v1/admin/errors") {
        if (!bearerMatches(request, env.DT_ADMIN_TOKEN)) {
          return json({ ok: false, error: "forbidden" }, 403);
        }
        return json({ ok: true, errors: await buildAdminErrors(env, clampLimit(url.searchParams.get("limit"))) });
      }

      if (request.method === "POST" && path === "/api/v1/event") {
        return await handleEvent(request, env);
      }

      return json({ ok: false, error: "not_found" }, 404);
    } catch (err) {
      return json({ ok: false, error: "server_error" }, 500);
    }
  },

  async scheduled(event, env, ctx) {
    ctx.waitUntil(prune(env));
  }
};

function json(payload, status = 200) {
  return new Response(JSON.stringify(payload), {
    status,
    headers: {
      "Content-Type": "application/json; charset=utf-8",
      "Cache-Control": "no-store",
      "X-Content-Type-Options": "nosniff",
      "Access-Control-Allow-Origin": "*"
    }
  });
}

function corsPreflight() {
  return new Response(null, {
    status: 204,
    headers: {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type, Authorization",
      "Access-Control-Max-Age": "86400"
    }
  });
}

function rateLimited(request) {
  const ip = request.headers.get("CF-Connecting-IP") || "unknown";
  const now = Date.now();
  let bucket = rateBuckets.get(ip);
  if (!bucket || now - bucket.start >= RATE_WINDOW_MS) {
    bucket = { start: now, count: 0 };
    rateBuckets.set(ip, bucket);
  }
  bucket.count++;
  if (rateBuckets.size > 20000) {
    for (const [key, value] of rateBuckets) {
      if (now - value.start >= RATE_WINDOW_MS) rateBuckets.delete(key);
    }
  }
  return bucket.count > RATE_LIMIT;
}

function bearerMatches(request, token) {
  if (!token) return false;
  const header = request.headers.get("Authorization") || "";
  if (!header.startsWith("Bearer ")) return false;
  return safeEqual(header.slice(7), token);
}

function safeEqual(a, b) {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

function viewAuthorized(request, env) {
  // Backward compatible: without DT_VIEW_TOKEN the full stats endpoint is
  // public. Production deployments should always set the secret.
  if (!env.DT_VIEW_TOKEN) return true;
  return bearerMatches(request, env.DT_VIEW_TOKEN);
}

function clampLimit(raw) {
  const value = parseInt(raw || "50", 10);
  if (!Number.isFinite(value)) return 50;
  return Math.min(200, Math.max(1, value));
}

function cleanText(value, limit) {
  if (typeof value !== "string") return "";
  return value.replace(/[\r\n]/g, " ").trim().slice(0, limit);
}

function cleanId(value) {
  if (typeof value !== "string") return "";
  const id = value.trim();
  if (id.length < 8 || id.length > MAX_ID) return "";
  return /^[A-Za-z0-9_-]+$/.test(id) ? id : "";
}

function cleanEvent(value) {
  if (typeof value !== "string") return "";
  const event = value.trim().toLowerCase();
  return EVENT_NAMES.has(event) ? event : "";
}

function requestCountry(request) {
  const value = request.headers.get("CF-IPCountry") || request.headers.get("X-Country-Code") || "";
  return /^[A-Za-z]{2}$/.test(value) ? value.toUpperCase() : "";
}

async function handleEvent(request, env) {
  const declared = Number(request.headers.get("content-length") || "0");
  if (declared > MAX_BODY) {
    return json({ ok: false, error: "bad_json" }, 400);
  }

  const text = await request.text();
  if (!text || text.length > MAX_BODY) {
    return json({ ok: false, error: "bad_json" }, 400);
  }

  let data;
  try {
    data = JSON.parse(text);
  } catch {
    return json({ ok: false, error: "bad_json" }, 400);
  }
  if (!data || typeof data !== "object" || Array.isArray(data)) {
    return json({ ok: false, error: "bad_json" }, 400);
  }

  const now = Math.floor(Date.now() / 1000);
  const installId = cleanId(data.install_id);
  const sessionId = cleanId(data.session_id);
  const event = cleanEvent(data.event);
  if (!installId || !event) {
    return json({ ok: false, error: "bad_event" }, 400);
  }

  const version = cleanText(data.version, 32);
  const osName = cleanText(data.os, 64);
  const lang = cleanText(data.lang, 8);
  const errorCode = cleanText(data.error_code, 64);
  const errorTitle = cleanText(data.error_title, 120);
  const errorModule = cleanText(data.error_module, 64);
  const country = requestCountry(request);

  const isStart = event === "start";
  const isError = event === "error";
  const isExit = event === "exit";

  const statements = [
    env.DB.prepare(
      `INSERT INTO installs
         (install_id, first_seen, last_seen, version, os, lang, country, launches, error_count)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
       ON CONFLICT(install_id) DO UPDATE SET
         last_seen = excluded.last_seen,
         version = CASE WHEN excluded.version <> '' THEN excluded.version ELSE installs.version END,
         os = CASE WHEN excluded.os <> '' THEN excluded.os ELSE installs.os END,
         lang = CASE WHEN excluded.lang <> '' THEN excluded.lang ELSE installs.lang END,
         country = CASE WHEN excluded.country <> '' THEN excluded.country ELSE installs.country END,
         launches = installs.launches + CASE WHEN ? = 'start' THEN 1 ELSE 0 END,
         error_count = installs.error_count + CASE WHEN ? = 'error' THEN 1 ELSE 0 END`
    ).bind(
      installId, now, now, version, osName, lang, country,
      isStart ? 1 : 0,
      isError ? 1 : 0,
      event, event
    )
  ];

  if (sessionId) {
    statements.push(
      env.DB.prepare(
        `INSERT INTO sessions
           (session_id, install_id, started_at, last_seen, ended_at, version, os, lang, country)
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
         ON CONFLICT(session_id) DO UPDATE SET
           last_seen = excluded.last_seen,
           ended_at = CASE
             WHEN ? = 'exit' THEN ?
             WHEN ? = 'start' THEN NULL
             ELSE sessions.ended_at
           END,
           version = CASE WHEN excluded.version <> '' THEN excluded.version ELSE sessions.version END,
           os = CASE WHEN excluded.os <> '' THEN excluded.os ELSE sessions.os END,
           lang = CASE WHEN excluded.lang <> '' THEN excluded.lang ELSE sessions.lang END`
      ).bind(
        sessionId, installId, now, now, isExit ? now : null,
        version, osName, lang, country,
        event, now, event
      )
    );
  }

  if (isStart || isExit || isError) {
    statements.push(
      env.DB.prepare(
        `INSERT INTO events
           (install_id, session_id, event, version, os, lang, error_code, error_title, error_module, created_at)
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
      ).bind(installId, sessionId, event, version, osName, lang, errorCode, errorTitle, errorModule, now)
    );
  }

  try {
    await env.DB.batch(statements);
  } catch {
    return json({ ok: false, error: "server_error" }, 500);
  }

  return json({ ok: true });
}

async function buildOnline(env) {
  const now = Math.floor(Date.now() / 1000);
  const row = await env.DB.prepare(
    `SELECT COUNT(*) AS n FROM sessions
     WHERE ended_at IS NULL AND last_seen >= ?`
  ).bind(now - ONLINE_WINDOW).first();
  return { ok: true, online_now: row ? Number(row.n) || 0 : 0 };
}

async function buildStats(env) {
  const now = Math.floor(Date.now() / 1000);
  const sinceOnline = now - ONLINE_WINDOW;
  const sinceDay = Math.floor(now / 86400) * 86400;
  const since24h = now - 86400;

  const onlineNow = await firstNumber(env,
    `SELECT COUNT(*) AS n FROM sessions WHERE ended_at IS NULL AND last_seen >= ?`, sinceOnline);
  const online24h = await firstNumber(env,
    `SELECT COUNT(DISTINCT install_id) AS n FROM sessions WHERE last_seen >= ?`, since24h);
  const usersTotal = await firstNumber(env,
    `SELECT COUNT(*) AS n FROM installs`);
  const launchesToday = await firstNumber(env,
    `SELECT COUNT(*) AS n FROM events WHERE event='start' AND created_at >= ?`, sinceDay);
  const launchesTotal = await firstNumber(env,
    `SELECT COALESCE(SUM(launches), 0) AS n FROM installs`);
  const errors24h = await firstNumber(env,
    `SELECT COUNT(*) AS n FROM events WHERE event='error' AND created_at >= ?`, since24h);
  const errorsTotal = await firstNumber(env,
    `SELECT COALESCE(SUM(error_count), 0) AS n FROM installs`);
  const usersWithErrors24h = await firstNumber(env,
    `SELECT COUNT(DISTINCT install_id) AS n FROM events WHERE event='error' AND created_at >= ?`, since24h);

  let topErrors = await allRows(env,
    `SELECT error_code, error_title, error_module,
            COUNT(*) AS count,
            COUNT(DISTINCT install_id) AS users,
            MAX(created_at) AS last_seen
     FROM events
     WHERE event='error' AND created_at >= ?
     GROUP BY error_code, error_title, error_module
     ORDER BY count DESC, users DESC
     LIMIT 8`, since24h);

  if (!topErrors.length) {
    topErrors = await allRows(env,
      `SELECT error_code, error_title, error_module,
              COUNT(*) AS count,
              COUNT(DISTINCT install_id) AS users,
              MAX(created_at) AS last_seen
       FROM events
       WHERE event='error'
       GROUP BY error_code, error_title, error_module
       ORDER BY count DESC, users DESC
       LIMIT 8`);
  }

  const versions = await allRows(env,
    `SELECT version, COUNT(*) AS users, COALESCE(SUM(launches), 0) AS launches
     FROM installs
     WHERE version <> ''
     GROUP BY version
     ORDER BY users DESC
     LIMIT 10`);

  const countries = await allRows(env,
    `SELECT country, COUNT(*) AS users
     FROM installs
     WHERE country <> ''
     GROUP BY country
     ORDER BY users DESC
     LIMIT 10`);

  return {
    ok: true,
    generated_at: now,
    online_now: onlineNow,
    online_24h: online24h,
    users_total: usersTotal,
    launches_today: launchesToday,
    launches_total: launchesTotal,
    errors_24h: errors24h,
    errors_total: errorsTotal,
    users_with_errors_24h: usersWithErrors24h,
    top_errors: topErrors,
    versions,
    countries
  };
}

async function buildAdminErrors(env, limit) {
  return allRows(env,
    `SELECT error_code, error_title, error_module,
            COUNT(*) AS count,
            COUNT(DISTINCT install_id) AS users,
            MIN(created_at) AS first_seen,
            MAX(created_at) AS last_seen
     FROM events
     WHERE event='error'
     GROUP BY error_code, error_title, error_module
     ORDER BY count DESC, users DESC
     LIMIT ?`, limit);
}

async function firstNumber(env, sql, ...args) {
  const stmt = args.length ? env.DB.prepare(sql).bind(...args) : env.DB.prepare(sql);
  const row = await stmt.first();
  return row ? Number(row.n) || 0 : 0;
}

async function allRows(env, sql, ...args) {
  const stmt = args.length ? env.DB.prepare(sql).bind(...args) : env.DB.prepare(sql);
  const result = await stmt.all();
  return result.results || [];
}

async function prune(env) {
  const cutoff = Math.floor(Date.now() / 1000) - RETENTION_DAYS * 86400;
  await env.DB.batch([
    env.DB.prepare(`DELETE FROM events WHERE created_at < ?`).bind(cutoff),
    env.DB.prepare(`DELETE FROM sessions WHERE ended_at IS NOT NULL AND ended_at < ?`).bind(cutoff)
  ]);
}
