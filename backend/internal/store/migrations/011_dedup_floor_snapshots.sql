-- floor_snapshot は、同じセッションの同じ階で最後のもの 1 件だけを残す (docs/spec/api.md「floor_snapshot」)。
-- 並べる順は ListEvents が同じ階から 1 件を選ぶ順と同じにする。
DELETE FROM events WHERE id IN (
  SELECT id FROM (
    SELECT id, ROW_NUMBER() OVER (
      PARTITION BY session_id, json_extract(payload_json, '$.floor')
      ORDER BY occurred_at DESC, sequence DESC, id DESC) AS snapshot_rank
    FROM events
    WHERE event_type = 'floor_snapshot'
  ) WHERE snapshot_rank > 1
);
