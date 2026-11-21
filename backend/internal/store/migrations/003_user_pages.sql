-- ユーザーページ (docs/spec/user-pages.md): ランの公開・非公開、ユーザーページの公開、参加者ごとの「ページに載せるか」。

ALTER TABLE sessions ADD COLUMN visibility TEXT NOT NULL DEFAULT 'public';
ALTER TABLE players  ADD COLUMN profile_public INTEGER NOT NULL DEFAULT 0;

-- ランの参加者。ラン開始の記録 (run_start) の player_id から作る
CREATE TABLE session_players (
  session_id     TEXT NOT NULL,
  steam_id       TEXT NOT NULL,
  character_id   TEXT,
  character_name TEXT,
  hidden         INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (session_id, steam_id),
  FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
);
CREATE INDEX idx_session_players_steam ON session_players(steam_id);

INSERT OR IGNORE INTO session_players (session_id, steam_id, character_id, character_name)
SELECT session_id, player_id,
       json_extract(payload_json, '$.character_id'),
       json_extract(payload_json, '$.character_name')
FROM events
WHERE event_type = 'run_start' AND player_id IS NOT NULL AND player_id != '';
