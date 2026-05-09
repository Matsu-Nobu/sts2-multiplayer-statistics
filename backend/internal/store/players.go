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
// given session, plus the session host. display_name は players テーブルから
// 取れれば取り、無ければ空文字。LEFT JOIN にすることで、UpsertPlayer されて
// いない player_id (= MP の host 以外) も拾える。
func (s *Store) ListPlayersForSession(ctx context.Context, sessionID string) ([]Player, error) {
	rows, err := s.db.QueryContext(ctx, `
		WITH ids AS (
			SELECT DISTINCT player_id AS steam_id FROM events
			WHERE session_id = ? AND player_id IS NOT NULL AND player_id != ''
			UNION
			SELECT host_steam_id AS steam_id FROM sessions
			WHERE id = ? AND host_steam_id IS NOT NULL AND host_steam_id != ''
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
