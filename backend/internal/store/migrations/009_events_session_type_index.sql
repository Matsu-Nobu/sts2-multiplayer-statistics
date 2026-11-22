-- ゲームごとの集計は、1 つのセッションの決まった種類の記録 (floor_snapshot、damage_dealt) だけを読む。
-- idx_events_type だと、その種類の記録をデータベース全体から読んでからセッションで絞ることになる。
CREATE INDEX IF NOT EXISTS idx_events_session_type ON events(session_id, event_type, id);
