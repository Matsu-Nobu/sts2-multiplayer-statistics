package store

import (
	"context"
	"database/sql"
	"encoding/json"
	"time"
)

// EventInput is the payload accepted by InsertEvent. Combat context fields
// (CombatIndex / TurnNumber / Sequence) are nullable: turn-scoped events set
// them, run/floor-scoped events leave them nil.
type EventInput struct {
	EventUUID    string
	SessionID    string
	PlayerID     *string
	EventType    string
	OccurredAt   string
	Floor        *int
	CombatIndex  *int
	TurnNumber   *int
	Sequence     *int
	Payload      json.RawMessage
}

// InsertEvent inserts an event. Duplicates by event_uuid are silently ignored.
// Returns whether a new row was actually inserted.
func (s *Store) InsertEvent(ctx context.Context, in EventInput) (inserted bool, err error) {
	now := time.Now().UTC().Format(time.RFC3339)
	res, err := s.db.ExecContext(ctx, `
		INSERT OR IGNORE INTO events
			(event_uuid, session_id, player_id, event_type, occurred_at, received_at,
			 floor, combat_index, turn_number, sequence, payload_json)
		VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
	`,
		in.EventUUID, in.SessionID, in.PlayerID, in.EventType, in.OccurredAt, now,
		in.Floor, in.CombatIndex, in.TurnNumber, in.Sequence,
		string(in.Payload),
	)
	if err != nil {
		return false, err
	}
	n, err := res.RowsAffected()
	if err != nil {
		return false, err
	}
	return n > 0, nil
}

// DeleteCombatEventsAtFloorExcept deletes all combat-scoped events in a session
// that share the given floor but have a different combat_index. Used to clean up
// abandoned save-mid-combat data when the player saves and resumes (the resumed
// combat starts fresh with a new combat_index, and the previous attempt at the
// same floor should be overwritten).
func (s *Store) DeleteCombatEventsAtFloorExcept(ctx context.Context, sessionID string, floor int, exceptCombatIndex int) (int64, error) {
	res, err := s.db.ExecContext(ctx, `
		DELETE FROM events
		WHERE session_id = ?
		  AND floor = ?
		  AND combat_index IS NOT NULL
		  AND combat_index != ?
	`, sessionID, floor, exceptCombatIndex)
	if err != nil {
		return 0, err
	}
	return res.RowsAffected()
}

// ListEvents returns all events for a session ordered by combat context first
// (NULL combat_index sorts last), then turn_number / sequence / id for total
// ordering. Each element is a JSON object containing the request shape plus
// received_at and the optional combat context fields.
func (s *Store) ListEvents(ctx context.Context, sessionID string) ([]json.RawMessage, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT event_uuid, event_type, occurred_at, received_at, player_id,
		       floor, combat_index, turn_number, sequence, payload_json
		FROM events
		WHERE session_id = ?
		ORDER BY
			(combat_index IS NULL),     -- non-combat events go to the end inside same occurred_at
			combat_index,
			turn_number,
			sequence,
			occurred_at,
			id
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
			floor, combat, turn, seq sql.NullInt64
			payload                string
		)
		if err := rows.Scan(&uuid, &etype, &occ, &recv, &player,
			&floor, &combat, &turn, &seq, &payload); err != nil {
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
		if combat.Valid {
			obj["combat_index"] = combat.Int64
		}
		if turn.Valid {
			obj["turn_number"] = turn.Int64
		}
		if seq.Valid {
			obj["sequence"] = seq.Int64
		}
		raw, err := json.Marshal(obj)
		if err != nil {
			return nil, err
		}
		out = append(out, raw)
	}
	return out, rows.Err()
}
