-- カード報酬の AI の評価 (docs/spec/card-evaluation.md「保存する表」)。card_reward_offered の event 1 件につき 1 行
CREATE TABLE card_reward_evaluations (
  event_id INTEGER PRIMARY KEY,
  status TEXT NOT NULL,
  model TEXT,
  value REAL,
  card_probs_json TEXT,
  alternative_probs_json TEXT,
  evaluated_at TEXT NOT NULL,
  FOREIGN KEY (event_id) REFERENCES events(id) ON DELETE CASCADE
);
