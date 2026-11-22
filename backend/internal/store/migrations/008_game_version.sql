-- ゲームのバージョン (docs/spec/player-stats.md「絞り込み」)。mod がゲームのフォルダの release_info.json から読んで送る。
-- この列を足す前に記録したゲームは、すべて v0.113.1 で遊んだものとして扱う。
ALTER TABLE sessions ADD COLUMN game_version TEXT;
UPDATE sessions SET game_version = 'v0.113.1';
