-- プレイヤーの統計 (docs/spec/player-stats.md) に使う、ゲームと人の組ごとの集計。
-- 集計の作り方を変えたら version を上げ、古い行は次に読まれたときに作り直す。
-- last_event_id は集計を作ったときのそのゲームの events.id の最大で、新しい記録が届いたら作り直すために使う。
CREATE TABLE game_summaries (
  session_id    TEXT NOT NULL,
  steam_id      TEXT NOT NULL,
  version       INTEGER NOT NULL,
  last_event_id INTEGER NOT NULL,
  summary_json  TEXT NOT NULL,
  PRIMARY KEY (session_id, steam_id),
  FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
);
