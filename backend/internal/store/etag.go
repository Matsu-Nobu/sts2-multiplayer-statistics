package store

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"fmt"
)

// ComputeSessionETag returns an opaque ETag for GET /api/sessions/{id}.
// Changes whenever the session row, any turn, or any event has been written.
//
// Strategy: hash (session_id, last received_at across turns/events,
// turns count, events count, session.outcome, session.finished_at).
// All of these change on any data mutation we care about.
func (s *Store) ComputeSessionETag(ctx context.Context, sessionID string) (string, error) {
	var (
		lastRecv             string
		turnCount, eventCnt  int
		outcome, finishedAt  *string
	)
	row := s.db.QueryRowContext(ctx, `
		SELECT
			COALESCE((
				SELECT MAX(received_at) FROM (
					SELECT received_at FROM turns  WHERE session_id = ?1
					UNION ALL
					SELECT received_at FROM events WHERE session_id = ?1
				)
			), '') AS last_recv,
			(SELECT COUNT(*) FROM turns  WHERE session_id = ?1) AS tc,
			(SELECT COUNT(*) FROM events WHERE session_id = ?1) AS ec,
			(SELECT outcome     FROM sessions WHERE id = ?1) AS outcome,
			(SELECT finished_at FROM sessions WHERE id = ?1) AS finished_at
	`, sessionID)
	if err := row.Scan(&lastRecv, &turnCount, &eventCnt, &outcome, &finishedAt); err != nil {
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
	raw := fmt.Sprintf("%s|%s|%d|%d|%s|%s", sessionID, lastRecv, turnCount, eventCnt, out, fa)
	sum := sha256.Sum256([]byte(raw))
	return `"` + hex.EncodeToString(sum[:16]) + `"`, nil
}
