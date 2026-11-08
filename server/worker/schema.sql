-- DeepTools telemetry schema for Cloudflare D1 (SQLite).
-- Apply with:
--   npx wrangler d1 execute deeptools-telemetry --remote --file=schema.sql

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
