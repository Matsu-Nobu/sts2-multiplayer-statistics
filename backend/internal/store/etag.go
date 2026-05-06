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
// events count, session.outcome, session.finished_at).
// All of these change on any data mutation we care about.
func (s *Store) ComputeSessionETag(ctx context.Context, sessionID string) (string, error) {
	var (
		lastRecv            string
		eventCnt            int
		outcome, finishedAt *string
	)
	row := s.db.QueryRowContext(ctx, `
		SELECT
			COALESCE((SELECT MAX(received_at) FROM events WHERE session_id = ?1), '') AS last_recv,
			(SELECT COUNT(*) FROM events WHERE session_id = ?1)                       AS ec,
			(SELECT outcome     FROM sessions WHERE id = ?1)                          AS outcome,
			(SELECT finished_at FROM sessions WHERE id = ?1)                          AS finished_at
	`, sessionID)
	if err := row.Scan(&lastRecv, &eventCnt, &outcome, &finishedAt); err != nil {
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
	raw := fmt.Sprintf("%s|%s|%d|%s|%s", sessionID, lastRecv, eventCnt, out, fa)
	sum := sha256.Sum256([]byte(raw))
	return `"` + hex.EncodeToString(sum[:16]) + `"`, nil
}
