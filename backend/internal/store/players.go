package store

import (
	"context"
	"time"
)

type Player struct {
	SteamID     string `json:"steam_id"`
	DisplayName string `json:"display_name"`
}

// UpsertPlayer inserts or updates a player record. last_seen_at and display_name
// are updated on conflict; first_seen_at is preserved.
func (s *Store) UpsertPlayer(ctx context.Context, steamID, displayName string) error {
	now := time.Now().UTC().Format(time.RFC3339)
	_, err := s.db.ExecContext(ctx, `
		INSERT INTO players (steam_id, display_name, first_seen_at, last_seen_at)
		VALUES (?, ?, ?, ?)
		ON CONFLICT(steam_id) DO UPDATE SET
			display_name = excluded.display_name,
			last_seen_at = excluded.last_seen_at
	`, steamID, displayName, now, now)
	return err
}

// ListPlayersForSession returns the players who have produced any event for the
// given session.
//
// 優先順:
//  1. events.player_id の DISTINCT (実際にゲーム内行動を記録した player)
//  2. (events に player_id 付き event が一切無い場合のみ) host_steam_id
//
// host_steam_id と events.player_id を UNION すると、SP で両者が異なる format
// (例: host_steam_id="76561...", events.player_id="1") のとき phantom player が
// 2 つ出てしまうため、events 側を信用する。
func (s *Store) ListPlayersForSession(ctx context.Context, sessionID string) ([]Player, error) {
	rows, err := s.db.QueryContext(ctx, `
		WITH ev_ids AS (
			SELECT DISTINCT player_id AS steam_id FROM events
			WHERE session_id = ? AND player_id IS NOT NULL AND player_id != ''
		),
		ids AS (
			SELECT steam_id FROM ev_ids
			UNION
			SELECT host_steam_id FROM sessions
			WHERE id = ? AND host_steam_id IS NOT NULL AND host_steam_id != ''
			  AND NOT EXISTS (SELECT 1 FROM ev_ids)
		)
		SELECT i.steam_id, COALESCE(p.display_name, '') AS display_name
		FROM ids i
		LEFT JOIN players p ON p.steam_id = i.steam_id
		ORDER BY i.steam_id
	`, sessionID, sessionID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []Player
	for rows.Next() {
		var p Player
		if err := rows.Scan(&p.SteamID, &p.DisplayName); err != nil {
			return nil, err
		}
		out = append(out, p)
	}
	return out, rows.Err()
}
