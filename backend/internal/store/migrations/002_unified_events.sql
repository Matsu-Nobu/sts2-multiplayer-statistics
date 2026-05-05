-- Phase 3.5: events テーブルに combat context カラムを追加し、
-- 戦闘内 / 戦闘外イベントを統合する。
-- 既存 events 行は context 列が NULL のまま許容される。

ALTER TABLE events ADD COLUMN combat_index INTEGER;
ALTER TABLE events ADD COLUMN turn_number  INTEGER;
ALTER TABLE events ADD COLUMN sequence     INTEGER;

-- 戦闘内イベント検索（戦闘タブ・タイムラインビュー）用のインデックス
CREATE INDEX idx_events_combat
  ON events(session_id, combat_index, turn_number, sequence);
