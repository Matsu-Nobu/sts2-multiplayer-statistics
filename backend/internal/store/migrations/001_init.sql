CREATE TABLE sessions (
  id              TEXT PRIMARY KEY,
  created_at      TEXT NOT NULL,
  host_name       TEXT,
  host_steam_id   TEXT,
  character_id    TEXT,
  ascension       INTEGER,
  seed            TEXT,
  outcome         TEXT,
  final_floor     INTEGER,
  finished_at     TEXT,
  write_token     TEXT NOT NULL
);

CREATE TABLE players (
  steam_id      TEXT PRIMARY KEY,
  display_name  TEXT NOT NULL,
  first_seen_at TEXT NOT NULL,
  last_seen_at  TEXT NOT NULL
);

CREATE TABLE turns (
  session_id    TEXT NOT NULL,
  combat_index  INTEGER NOT NULL,
  turn_number   INTEGER NOT NULL,
  received_at   TEXT NOT NULL,
  is_final      INTEGER NOT NULL DEFAULT 0,
  payload_json  TEXT NOT NULL,
  PRIMARY KEY (session_id, combat_index, turn_number),
  FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
);
CREATE INDEX idx_turns_session ON turns(session_id, combat_index, turn_number);

CREATE TABLE events (
  id           INTEGER PRIMARY KEY AUTOINCREMENT,
  event_uuid   TEXT NOT NULL UNIQUE,
  session_id   TEXT NOT NULL,
  player_id    TEXT,
  event_type   TEXT NOT NULL,
  occurred_at  TEXT NOT NULL,
  received_at  TEXT NOT NULL,
  floor        INTEGER,
  payload_json TEXT NOT NULL,
  FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
);
CREATE INDEX idx_events_session ON events(session_id, id);
CREATE INDEX idx_events_player  ON events(player_id, event_type);
CREATE INDEX idx_events_type    ON events(event_type);
