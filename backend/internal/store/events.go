package store

import (
	"context"
	"database/sql"
	"encoding/json"
	"time"
)

type EventInput struct {
	EventUUID  string
	SessionID  string
	PlayerID   *string
	EventType  string
	OccurredAt string
	Floor      *int
	Payload    json.RawMessage
}

// InsertEvent inserts an event. Duplicates by event_uuid are silently ignored.
// Returns whether a new row was actually inserted.
func (s *Store) InsertEvent(ctx context.Context, in EventInput) (inserted bool, err error) {
	now := time.Now().UTC().Format(time.RFC3339)
	res, err := s.db.ExecContext(ctx, `
		INSERT OR IGNORE INTO events
			(event_uuid, session_id, player_id, event_type, occurred_at, received_at, floor, payload_json)
		VALUES (?, ?, ?, ?, ?, ?, ?, ?)
	`, in.EventUUID, in.SessionID, in.PlayerID, in.EventType, in.OccurredAt, now, in.Floor, string(in.Payload))
	if err != nil {
		return false, err
	}
	n, err := res.RowsAffected()
	if err != nil {
		return false, err
	}
	return n > 0, nil
}

// ListEvents returns all events for a session ordered by occurred_at, id.
// Each element is a JSON object containing the request shape plus received_at.
func (s *Store) ListEvents(ctx context.Context, sessionID string) ([]json.RawMessage, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT event_uuid, event_type, occurred_at, received_at, player_id, floor, payload_json
		FROM events
		WHERE session_id = ?
		ORDER BY occurred_at, id
	`, sessionID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()

	var out []json.RawMessage
	for rows.Next() {
		var (
			uuid, etype, occ, recv string
			player                 sql.NullString
			floor                  sql.NullInt64
			payload                string
		)
		if err := rows.Scan(&uuid, &etype, &occ, &recv, &player, &floor, &payload); err != nil {
			return nil, err
		}
		obj := map[string]any{
			"event_uuid":  uuid,
			"event_type":  etype,
			"occurred_at": occ,
			"received_at": recv,
			"payload":     json.RawMessage(payload),
		}
		if player.Valid {
			obj["player_id"] = player.String
		}
		if floor.Valid {
			obj["floor"] = floor.Int64
		}
		raw, err := json.Marshal(obj)
		if err != nil {
			return nil, err
		}
		out = append(out, raw)
	}
	return out, rows.Err()
}
