-- セッションの出どころ (docs/spec/api.md「POST /sessions」)。live はゲーム中に mod が記録したもの、history はセーブデータの履歴から取り込んだもの。
ALTER TABLE sessions ADD COLUMN source TEXT NOT NULL DEFAULT 'live';
CREATE INDEX IF NOT EXISTS idx_sessions_seed ON sessions(seed);
