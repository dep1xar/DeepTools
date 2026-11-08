#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
DeepTools anonymous telemetry server.

Single file, Python 3.8+, standard library only. Stores aggregate counters in
SQLite and never stores IP addresses. Country codes are taken from a reverse
proxy header (Cloudflare / nginx) only when DT_TRUST_PROXY=1.

Run:
    python deeptools_telemetry.py

Environment:
    DT_PORT             listen port (default 8787)
    DT_DB               SQLite file (default ./telemetry.db)
    DT_TRUST_PROXY      "1" to trust CF-IPCountry / X-Country-Code headers
    DT_RETENTION_DAYS   raw error event retention (default 90)
    DT_ADMIN_TOKEN      optional bearer token for /api/v1/admin/*
    DT_VIEW_TOKEN       optional bearer token for the full /api/v1/stats view
                        (when set, regular clients still get /api/v1/online)

Endpoints:
    POST /api/v1/event          heartbeat / start / exit / error
    GET  /api/v1/online         public online counter for the main window
    GET  /api/v1/stats          full aggregate statistics (view token if set)
    GET  /api/v1/admin/errors   detailed errors (token required)
    GET  /health                liveness probe
"""

import hmac
import json
import os
import re
import sqlite3
import sys
import threading
import time
from collections import defaultdict, deque
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

PORT = int(os.environ.get("DT_PORT", "8787"))
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
DB_PATH = os.environ.get("DT_DB", os.path.join(BASE_DIR, "telemetry.db"))
TRUST_PROXY = os.environ.get("DT_TRUST_PROXY", "0") == "1"
RETENTION_DAYS = int(os.environ.get("DT_RETENTION_DAYS", "90"))
ADMIN_TOKEN = os.environ.get("DT_ADMIN_TOKEN", "")
VIEW_TOKEN = os.environ.get("DT_VIEW_TOKEN", "")

ONLINE_WINDOW = 300          # 5 minutes without a heartbeat = offline
MAX_BODY = 16 * 1024         # 16 KB is enough for one event
MAX_ID = 64

EVENT_NAMES = {"start", "heartbeat", "exit", "error"}

SCHEMA = """
CREATE TABLE IF NOT EXISTS installs (
    install_id  TEXT PRIMARY KEY,
    first_seen  INTEGER NOT NULL,
    last_seen   INTEGER NOT NULL,
    version     TEXT NOT NULL DEFAULT '',
    os          TEXT NOT NULL DEFAULT '',
    lang        TEXT NOT NULL DEFAULT '',
    country     TEXT NOT NULL DEFAULT '',
    launches    INTEGER NOT NULL DEFAULT 0,
    error_count INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS sessions (
    session_id TEXT PRIMARY KEY,
    install_id TEXT NOT NULL,
    started_at INTEGER NOT NULL,
    last_seen  INTEGER NOT NULL,
    ended_at   INTEGER,
    version    TEXT NOT NULL DEFAULT '',
    os         TEXT NOT NULL DEFAULT '',
    lang       TEXT NOT NULL DEFAULT '',
    country    TEXT NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS idx_sessions_last_seen ON sessions(last_seen);
CREATE INDEX IF NOT EXISTS idx_sessions_install ON sessions(install_id);

CREATE TABLE IF NOT EXISTS events (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    install_id   TEXT NOT NULL,
    session_id   TEXT NOT NULL DEFAULT '',
    event        TEXT NOT NULL,
    version      TEXT NOT NULL DEFAULT '',
    os           TEXT NOT NULL DEFAULT '',
    lang         TEXT NOT NULL DEFAULT '',
    error_code   TEXT NOT NULL DEFAULT '',
    error_title  TEXT NOT NULL DEFAULT '',
    error_module TEXT NOT NULL DEFAULT '',
    created_at   INTEGER NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_events_created ON events(created_at);
CREATE INDEX IF NOT EXISTS idx_events_error ON events(event, created_at);
CREATE INDEX IF NOT EXISTS idx_events_error_code ON events(error_code);
"""


def connect():
    conn = sqlite3.connect(DB_PATH, timeout=15)
    conn.row_factory = sqlite3.Row
    conn.execute("PRAGMA journal_mode=WAL")
    conn.execute("PRAGMA synchronous=NORMAL")
    conn.execute("PRAGMA busy_timeout=15000")
    return conn


def init_db():
    conn = connect()
    try:
        conn.executescript(SCHEMA)
        conn.commit()
    finally:
        conn.close()


def utc_day_start():
    now = datetime.now(timezone.utc)
    return int(now.replace(hour=0, minute=0, second=0, microsecond=0).timestamp())


def clean_text(value, limit):
    if not isinstance(value, str):
        return ""
    value = value.replace("\r", " ").replace("\n", " ").strip()
    return value[:limit]


def clean_id(value):
    if not isinstance(value, str):
        return ""
    value = value.strip()
    if len(value) < 8 or len(value) > MAX_ID:
        return ""
    if not re.match(r"^[A-Za-z0-9_-]+$", value):
        return ""
    return value


def clean_event(value):
    if not isinstance(value, str):
        return ""
    value = value.strip().lower()
    return value if value in EVENT_NAMES else ""


def request_country(handler):
    if not TRUST_PROXY:
        return ""
    for header in ("CF-IPCountry", "X-Country-Code"):
        value = handler.headers.get(header, "")
        if value and re.match(r"^[A-Za-z]{2}$", value):
            return value.upper()
    return ""


def store_event(data, country):
    now = int(time.time())
    install_id = clean_id(data.get("install_id"))
    session_id = clean_id(data.get("session_id"))
    event = clean_event(data.get("event"))
    if not install_id or not event:
        return False

    version = clean_text(data.get("version"), 32)
    os_name = clean_text(data.get("os"), 64)
    lang = clean_text(data.get("lang"), 8)
    error_code = clean_text(data.get("error_code"), 64)
    error_title = clean_text(data.get("error_title"), 120)
    error_module = clean_text(data.get("error_module"), 64)

    conn = connect()
    try:
        cur = conn.cursor()
        cur.execute("BEGIN IMMEDIATE")

        row = cur.execute(
            "SELECT install_id FROM installs WHERE install_id=?", (install_id,)
        ).fetchone()
        if row is None:
            cur.execute(
                """INSERT INTO installs
                   (install_id, first_seen, last_seen, version, os, lang, country,
                    launches, error_count)
                   VALUES (?, ?, ?, ?, ?, ?, ?, 0, 0)""",
                (install_id, now, now, version, os_name, lang, country),
            )
        else:
            cur.execute(
                """UPDATE installs SET
                       last_seen = ?,
                       version = CASE WHEN ? <> '' THEN ? ELSE version END,
                       os = CASE WHEN ? <> '' THEN ? ELSE os END,
                       lang = CASE WHEN ? <> '' THEN ? ELSE lang END,
                       country = CASE WHEN ? <> '' THEN ? ELSE country END
                   WHERE install_id = ?""",
                (now,
                 version, version,
                 os_name, os_name,
                 lang, lang,
                 country, country,
                 install_id),
            )

        if event == "start":
            cur.execute(
                "UPDATE installs SET launches = launches + 1 WHERE install_id=?",
                (install_id,),
            )
        elif event == "error":
            cur.execute(
                "UPDATE installs SET error_count = error_count + 1 WHERE install_id=?",
                (install_id,),
            )

        if session_id:
            row = cur.execute(
                "SELECT session_id FROM sessions WHERE session_id=?", (session_id,)
            ).fetchone()
            if row is None:
                cur.execute(
                    """INSERT INTO sessions
                       (session_id, install_id, started_at, last_seen, ended_at,
                        version, os, lang, country)
                       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                    (session_id, install_id, now, now,
                     now if event == "exit" else None,
                     version, os_name, lang, country),
                )
            else:
                cur.execute(
                    """UPDATE sessions SET
                           last_seen = ?,
                           ended_at = CASE
                               WHEN ? = 'exit' THEN ?
                               WHEN ? = 'start' THEN NULL
                               ELSE ended_at
                           END,
                           version = CASE WHEN ? <> '' THEN ? ELSE version END,
                           os = CASE WHEN ? <> '' THEN ? ELSE os END,
                           lang = CASE WHEN ? <> '' THEN ? ELSE lang END
                       WHERE session_id = ?""",
                    (now, event, now, event,
                     version, version,
                     os_name, os_name,
                     lang, lang,
                     session_id),
                )

        # Heartbeats only update presence. Start/exit/error are kept as events
        # so we can compute daily launches, errors and retention.
        if event in ("start", "exit", "error"):
            cur.execute(
                """INSERT INTO events
                   (install_id, session_id, event, version, os, lang,
                    error_code, error_title, error_module, created_at)
                   VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                (install_id, session_id, event, version, os_name, lang,
                 error_code, error_title, error_module, now),
            )

        conn.commit()
        return True
    except Exception:
        conn.rollback()
        raise
    finally:
        conn.close()


_last_prune = 0.0
_prune_lock = threading.Lock()


def maybe_prune():
    global _last_prune
    now = time.time()
    with _prune_lock:
        if now - _last_prune < 3600:
            return
        _last_prune = now
    cutoff = int(now) - RETENTION_DAYS * 86400
    conn = connect()
    try:
        conn.execute("DELETE FROM events WHERE created_at < ?", (cutoff,))
        conn.execute(
            "DELETE FROM sessions WHERE ended_at IS NOT NULL AND ended_at < ?",
            (cutoff,),
        )
        conn.commit()
    finally:
        conn.close()


def build_online():
    """Lightweight public counter used by the main DeepTools window."""
    now = int(time.time())
    conn = connect()
    try:
        online_now = conn.execute(
            """SELECT COUNT(*) FROM sessions
               WHERE ended_at IS NULL AND last_seen >= ?""",
            (now - ONLINE_WINDOW,),
        ).fetchone()[0]
        return {"ok": True, "online_now": online_now}
    finally:
        conn.close()


def build_stats():
    now = int(time.time())
    since_online = now - ONLINE_WINDOW
    since_day = utc_day_start()
    since_24h = now - 86400

    conn = connect()
    try:
        cur = conn.cursor()

        online_now = cur.execute(
            """SELECT COUNT(*) FROM sessions
               WHERE ended_at IS NULL AND last_seen >= ?""",
            (since_online,),
        ).fetchone()[0]

        online_24h = cur.execute(
            "SELECT COUNT(DISTINCT install_id) FROM sessions WHERE last_seen >= ?",
            (since_24h,),
        ).fetchone()[0]

        users_total = cur.execute("SELECT COUNT(*) FROM installs").fetchone()[0]

        launches_today = cur.execute(
            "SELECT COUNT(*) FROM events WHERE event='start' AND created_at >= ?",
            (since_day,),
        ).fetchone()[0]

        launches_total = cur.execute(
            "SELECT COALESCE(SUM(launches), 0) FROM installs"
        ).fetchone()[0]

        errors_24h = cur.execute(
            "SELECT COUNT(*) FROM events WHERE event='error' AND created_at >= ?",
            (since_24h,),
        ).fetchone()[0]

        errors_total = cur.execute(
            "SELECT COALESCE(SUM(error_count), 0) FROM installs"
        ).fetchone()[0]

        users_with_errors_24h = cur.execute(
            """SELECT COUNT(DISTINCT install_id) FROM events
               WHERE event='error' AND created_at >= ?""",
            (since_24h,),
        ).fetchone()[0]

        top_errors = cur.execute(
            """SELECT error_code, error_title, error_module,
                      COUNT(*) AS count,
                      COUNT(DISTINCT install_id) AS users,
                      MAX(created_at) AS last_seen
               FROM events
               WHERE event='error' AND created_at >= ?
               GROUP BY error_code, error_title, error_module
               ORDER BY count DESC, users DESC
               LIMIT 8""",
            (since_24h,),
        ).fetchall()

        if not top_errors:
            top_errors = cur.execute(
                """SELECT error_code, error_title, error_module,
                          COUNT(*) AS count,
                          COUNT(DISTINCT install_id) AS users,
                          MAX(created_at) AS last_seen
                   FROM events
                   WHERE event='error'
                   GROUP BY error_code, error_title, error_module
                   ORDER BY count DESC, users DESC
                   LIMIT 8"""
            ).fetchall()

        versions = cur.execute(
            """SELECT version, COUNT(*) AS users,
                      COALESCE(SUM(launches), 0) AS launches
               FROM installs
               WHERE version <> ''
               GROUP BY version
               ORDER BY users DESC
               LIMIT 10"""
        ).fetchall()

        countries = cur.execute(
            """SELECT country, COUNT(*) AS users
               FROM installs
               WHERE country <> ''
               GROUP BY country
               ORDER BY users DESC
               LIMIT 10"""
        ).fetchall()

        return {
            "ok": True,
            "generated_at": now,
            "online_now": online_now,
            "online_24h": online_24h,
            "users_total": users_total,
            "launches_today": launches_today,
            "launches_total": launches_total,
            "errors_24h": errors_24h,
            "errors_total": errors_total,
            "users_with_errors_24h": users_with_errors_24h,
            "top_errors": [dict(r) for r in top_errors],
            "versions": [dict(r) for r in versions],
            "countries": [dict(r) for r in countries],
        }
    finally:
        conn.close()


def build_admin_errors(limit):
    conn = connect()
    try:
        rows = conn.execute(
            """SELECT error_code, error_title, error_module,
                      COUNT(*) AS count,
                      COUNT(DISTINCT install_id) AS users,
                      MIN(created_at) AS first_seen,
                      MAX(created_at) AS last_seen
               FROM events
               WHERE event='error'
               GROUP BY error_code, error_title, error_module
               ORDER BY count DESC, users DESC
               LIMIT ?""",
            (limit,),
        ).fetchall()
        return [dict(r) for r in rows]
    finally:
        conn.close()


class RateLimiter:
    def __init__(self, limit=120, window=60):
        self.limit = limit
        self.window = window
        self.lock = threading.Lock()
        self.hits = defaultdict(deque)

    def allow(self, ip):
        now = time.time()
        with self.lock:
            q = self.hits[ip]
            while q and q[0] < now - self.window:
                q.popleft()
            if len(q) >= self.limit:
                return False
            q.append(now)
            if len(self.hits) > 20000:
                for key in list(self.hits.keys()):
                    if not self.hits[key]:
                        del self.hits[key]
            return True


_LIMITER = RateLimiter()


class Handler(BaseHTTPRequestHandler):
    server_version = "DeepToolsTelemetry/1.0"
    sys_version = ""
    protocol_version = "HTTP/1.1"

    # Do not write client IPs to logs.
    def log_message(self, fmt, *args):
        pass

    def _client_ip(self):
        if TRUST_PROXY:
            forwarded = self.headers.get("CF-Connecting-IP", "") or \
                self.headers.get("X-Forwarded-For", "")
            if forwarded:
                return forwarded.split(",")[0].strip()
        return self.client_address[0]

    def _send_json(self, status, payload, extra_headers=None):
        body = json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Access-Control-Allow-Origin", "*")
        if extra_headers:
            for key, value in extra_headers.items():
                self.send_header(key, value)
        self.end_headers()
        try:
            self.wfile.write(body)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def _read_json(self):
        try:
            length = int(self.headers.get("Content-Length", "0"))
        except ValueError:
            return None
        if length <= 0 or length > MAX_BODY:
            return None
        raw = self.rfile.read(length)
        try:
            return json.loads(raw.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError):
            return None

    def _bearer_matches(self, token):
        if not token:
            return False
        header = self.headers.get("Authorization", "")
        if not header.startswith("Bearer "):
            return False
        return hmac.compare_digest(header[7:], token)

    def _authorized(self):
        return self._bearer_matches(ADMIN_TOKEN)

    def _view_authorized(self):
        # Backward compatible: if no view token is configured, the full stats
        # endpoint stays public. Set DT_VIEW_TOKEN to restrict it to the
        # DeepToolsStats.exe viewer build.
        if not VIEW_TOKEN:
            return True
        return self._bearer_matches(VIEW_TOKEN)

    def do_OPTIONS(self):
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type, Authorization")
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_GET(self):
        if not _LIMITER.allow(self._client_ip()):
            self._send_json(429, {"ok": False, "error": "rate_limited"})
            return

        parsed = urlparse(self.path)
        path = parsed.path.rstrip("/") or "/"

        if path == "/health":
            self._send_json(200, {"ok": True, "service": "deeptools-telemetry"})
            return

        if path == "/api/v1/online":
            self._send_json(200, build_online())
            return

        if path == "/api/v1/stats":
            if not self._view_authorized():
                self._send_json(403, {"ok": False, "error": "forbidden"})
                return
            self._send_json(200, build_stats())
            return

        if path == "/api/v1/admin/errors":
            if not self._authorized():
                self._send_json(403, {"ok": False, "error": "forbidden"})
                return
            params = parse_qs(parsed.query)
            try:
                limit = min(200, max(1, int(params.get("limit", ["50"])[0])))
            except ValueError:
                limit = 50
            self._send_json(200, {"ok": True, "errors": build_admin_errors(limit)})
            return

        self._send_json(404, {"ok": False, "error": "not_found"})

    def do_POST(self):
        if not _LIMITER.allow(self._client_ip()):
            self._send_json(429, {"ok": False, "error": "rate_limited"})
            return

        parsed = urlparse(self.path)
        path = parsed.path.rstrip("/") or "/"
        if path != "/api/v1/event":
            self._send_json(404, {"ok": False, "error": "not_found"})
            return

        data = self._read_json()
        if not isinstance(data, dict):
            self._send_json(400, {"ok": False, "error": "bad_json"})
            return

        try:
            ok = store_event(data, request_country(self))
        except Exception:
            self._send_json(500, {"ok": False, "error": "server_error"})
            return

        if not ok:
            self._send_json(400, {"ok": False, "error": "bad_event"})
            return

        if time.time() - _last_prune > 3600:
            try:
                maybe_prune()
            except Exception:
                pass

        self._send_json(200, {"ok": True})


def main():
    init_db()
    server = ThreadingHTTPServer(("0.0.0.0", PORT), Handler)
    server.daemon_threads = True
    server.allow_reuse_address = True
    print("DeepTools telemetry listening on 0.0.0.0:%d" % PORT)
    print("Database: %s" % DB_PATH)
    print("Privacy: no IP addresses are stored; country is only read from a trusted proxy header.")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nStopping...")
    finally:
        server.server_close()


if __name__ == "__main__":
    main()
