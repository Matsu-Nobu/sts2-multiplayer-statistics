package store

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"fmt"
)

// ComputeSessionETag returns an opaque ETag for GET /api/sessions/{id}.
// Changes whenever the session row or any event has been written.
//
// Strategy: hash (session_id, last received_at across events,
// events count, max event id, evaluations count, session.outcome, session.finished_at, session.visibility).
// カード報酬の評価は event を受け取ったあとで保存され、一度保存した行は変わらないので、件数で変化を検出できる。
// floor_snapshot は同じ階の古い記録を置き換えるので件数が変わらない。
// received_at は秒の単位なので、同じ秒に置き換わると変わらない。
// id は AUTOINCREMENT で、新しい行ごとに必ず増えるので、置き換えも検出できる。
func (s *Store) ComputeSessionETag(ctx context.Context, sessionID string) (string, error) {
	var (
		lastRecv            string
		eventCnt            int
		maxID               int64
		evalCnt             int
		outcome, finishedAt *string
		visibility          string
	)
	row := s.db.QueryRowContext(ctx, `
		SELECT
			COALESCE((SELECT MAX(received_at) FROM events WHERE session_id = ?1), '') AS last_recv,
			(SELECT COUNT(*) FROM events WHERE session_id = ?1)                       AS ec,
			(SELECT COALESCE(MAX(id), 0) FROM events WHERE session_id = ?1)          AS max_id,
			(SELECT COUNT(*) FROM card_reward_evaluations v JOIN events e ON e.id = v.event_id
			 WHERE e.session_id = ?1)                                                 AS eval_cnt,
			(SELECT outcome     FROM sessions WHERE id = ?1)                          AS outcome,
			(SELECT finished_at FROM sessions WHERE id = ?1)                          AS finished_at,
			(SELECT visibility  FROM sessions WHERE id = ?1)                          AS visibility
	`, sessionID)
	if err := row.Scan(&lastRecv, &eventCnt, &maxID, &evalCnt, &outcome, &finishedAt, &visibility); err != nil {
		return "", err
	}
	out := ""
	if outcome != nil {
		out = *outcome
	}
	fa := ""
	if finishedAt != nil {
		fa = *finishedAt
	}
	raw := fmt.Sprintf("%s|%s|%d|%d|%d|%s|%s|%s", sessionID, lastRecv, eventCnt, maxID, evalCnt, out, fa, visibility)
	sum := sha256.Sum256([]byte(raw))
	return `"` + hex.EncodeToString(sum[:16]) + `"`, nil
}
