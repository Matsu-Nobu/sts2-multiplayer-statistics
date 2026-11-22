-- 古い mod はラン開始の記録にキャラクターの表示名を入れていなかった。
-- 同じキャラクターの、表示名が分かっている参加者の値で埋める (docs/spec/user-pages.md「画面」)。
UPDATE session_players
SET character_name = (
  SELECT p2.character_name FROM session_players p2
  WHERE p2.character_id = session_players.character_id AND p2.character_name IS NOT NULL AND p2.character_name != ''
  LIMIT 1
)
WHERE (character_name IS NULL OR character_name = '') AND character_id IS NOT NULL;
