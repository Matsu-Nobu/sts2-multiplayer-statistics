-- 階ごとの記録 (floor_snapshot) が 1 件も無いゲームを消す (2026-10 より前の mod の記録と、始めた直後に途切れた記録)。
-- 今の画面では表示できないため。記録 (events) と参加者 (session_players) は ON DELETE CASCADE で一緒に消える。
-- 作ってから 1 日以内のゲームは、遊び始めたばかりで階の記録がまだ届いていないことがあるので残す。
DELETE FROM sessions
WHERE created_at < strftime('%Y-%m-%dT%H:%M:%SZ', 'now', '-1 day')
  AND NOT EXISTS (
  SELECT 1 FROM events e WHERE e.session_id = sessions.id AND e.event_type = 'floor_snapshot'
);
