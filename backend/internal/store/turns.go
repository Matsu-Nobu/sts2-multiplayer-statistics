package store

import (
	"context"
	"encoding/json"
	"time"
)

type Turn struct {
	SessionID    string          `json:"-"`
	CombatIndex  int             `json:"combat_index"`
	TurnNumber   int             `json:"turn_number"`
	IsFinal      bool            `json:"is_final"`
	ReceivedAt   string          `json:"received_at"`
	PayloadJSON  json.RawMessage `json:"-"`
}

// UpsertTurn inserts or replaces a turn row keyed by (session_id, combat_index, turn_number).
func (s *Store) UpsertTurn(ctx context.Context, sessionID string, combatIndex, turnNumber int, isFinal bool, payload []byte) error {
	now := time.Now().UTC().Format(time.RFC3339)
	_, err := s.db.ExecContext(ctx, `
		INSERT INTO turns (session_id, combat_index, turn_number, received_at, is_final, payload_json)
		VALUES (?, ?, ?, ?, ?, ?)
		ON CONFLICT(session_id, combat_index, turn_number) DO UPDATE SET
			received_at  = excluded.received_at,
			is_final     = excluded.is_final,
			payload_json = excluded.payload_json
	`, sessionID, combatIndex, turnNumber, now, boolToInt(isFinal), string(payload))
	return err
}

// ListTurns returns all turn payloads for a session ordered by (combat_index, turn_number).
// Each element of the returned slice is the raw JSON object as posted by the mod.
func (s *Store) ListTurns(ctx context.Context, sessionID string) ([]json.RawMessage, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT payload_json FROM turns
		WHERE session_id = ?
		ORDER BY combat_index, turn_number
	`, sessionID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var out []json.RawMessage
	for rows.Next() {
		var raw string
		if err := rows.Scan(&raw); err != nil {
			return nil, err
		}
		out = append(out, json.RawMessage(raw))
	}
	return out, rows.Err()
}

func boolToInt(b bool) int {
	if b {
		return 1
	}
	return 0
}
