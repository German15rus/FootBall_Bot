-- ============================================================
--  Premier League Bot — Database Schema
--  Compatible with SQLite (dev) and PostgreSQL (prod)
-- ============================================================

-- Teams (seeded from Football API on first run)
CREATE TABLE teams (
    team_id   INTEGER PRIMARY KEY,
    name      TEXT    NOT NULL,
    short_name TEXT   NOT NULL,
    emblem_url TEXT
);

-- Users (auto-registered on first /start)
CREATE TABLE users (
    telegram_id      BIGINT  PRIMARY KEY,
    username         TEXT,
    first_name       TEXT    NOT NULL DEFAULT '',
    favorite_team_id INTEGER REFERENCES teams(team_id) ON DELETE SET NULL,
    registered_at    TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX idx_users_favorite_team ON users(favorite_team_id);

-- Matches (synced from Football API every 10 min)
CREATE TABLE matches (
    match_id                      INTEGER   PRIMARY KEY,
    home_team_id                  INTEGER   NOT NULL REFERENCES teams(team_id),
    away_team_id                  INTEGER   NOT NULL REFERENCES teams(team_id),
    match_date                    TIMESTAMP NOT NULL,
    stadium                       TEXT,
    home_score                    INTEGER,
    away_score                    INTEGER,
    status                        TEXT      NOT NULL DEFAULT 'scheduled',  -- scheduled | live | finished
    pre_match_notification_sent   BOOLEAN   NOT NULL DEFAULT FALSE,
    post_match_notification_sent  BOOLEAN   NOT NULL DEFAULT FALSE
);
CREATE INDEX idx_matches_date   ON matches(match_date);
CREATE INDEX idx_matches_status ON matches(status);

-- Players / Squad (synced once per day)
CREATE TABLE players (
    player_id INTEGER PRIMARY KEY,
    team_id   INTEGER NOT NULL REFERENCES teams(team_id),
    name      TEXT    NOT NULL,
    number    INTEGER NOT NULL DEFAULT 0,
    position  TEXT    NOT NULL  -- goalkeeper | defender | midfielder | forward
);
CREATE INDEX idx_players_team ON players(team_id);

-- Notification log (optional, for debugging & audit)
CREATE TABLE notification_logs (
    id          INTEGER   PRIMARY KEY AUTOINCREMENT,
    telegram_id BIGINT    NOT NULL,
    message     TEXT      NOT NULL,
    sent_at     TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX idx_notif_telegram_id ON notification_logs(telegram_id);
CREATE INDEX idx_notif_sent_at     ON notification_logs(sent_at);
