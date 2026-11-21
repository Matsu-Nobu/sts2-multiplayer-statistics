package store

import (
	"context"
	"database/sql"
	"errors"
)

// ErrNotAllowed は、操作する人にその権限が無いとき (記録した人でない・参加者でない) に返す。
var ErrNotAllowed = errors.New("not allowed")

// UpsertSessionPlayer はランの参加者を記録する (run_start を受けたとき)。キャラクターは後から来た値で上書きする。
func (s *Store) UpsertSessionPlayer(ctx context.Context, sessionID, steamID string, characterID, characterName *string) error {
	_, err := s.db.ExecContext(ctx, `
		INSERT INTO session_players (session_id, steam_id, character_id, character_name)
		VALUES (?, ?, ?, ?)
		ON CONFLICT(session_id, steam_id) DO UPDATE SET
			character_id = COALESCE(excluded.character_id, character_id),
			character_name = COALESCE(excluded.character_name, character_name)
	`, sessionID, steamID, characterID, characterName)
	return err
}

// UserProfile はユーザーページの持ち主の情報。players に無い Steam ID は Known=false。
type UserProfile struct {
	SteamID       string `json:"steam_id"`
	DisplayName   string `json:"display_name"`
	ProfilePublic bool   `json:"profile_public"`
	Known         bool   `json:"-"`
}

func (s *Store) GetUserProfile(ctx context.Context, steamID string) (UserProfile, error) {
	p := UserProfile{SteamID: steamID}
	var public int
	err := s.db.QueryRowContext(ctx, `SELECT display_name, profile_public FROM players WHERE steam_id = ?`, steamID).
		Scan(&p.DisplayName, &public)
	if errors.Is(err, sql.ErrNoRows) {
		return p, nil
	}
	if err != nil {
		return p, err
	}
	p.ProfilePublic = public != 0
	p.Known = true
	return p, nil
}

// SetProfilePublic は本人がユーザーページの公開・非公開を切り替える。players に行が無ければ作る。
func (s *Store) SetProfilePublic(ctx context.Context, steamID, displayName string, public bool) error {
	_, err := s.db.ExecContext(ctx, `
		INSERT INTO players (steam_id, display_name, first_seen_at, last_seen_at, profile_public)
		VALUES (?, ?, datetime('now'), datetime('now'), ?)
		ON CONFLICT(steam_id) DO UPDATE SET profile_public = excluded.profile_public
	`, steamID, displayName, boolInt(public))
	return err
}

// UserRun はユーザーページの一覧の 1 行。
type UserRun struct {
	SessionID     string   `json:"session_id"`
	CreatedAt     string   `json:"created_at"`
	CharacterID   *string  `json:"character_id"`
	CharacterName *string  `json:"character_name"`
	Ascension     *int     `json:"ascension"`
	Outcome       *string  `json:"outcome"`
	FinalFloor    *int     `json:"final_floor"`
	Visibility    string   `json:"visibility"`
	Hidden        bool     `json:"hidden"`
	IsRecorder    bool     `json:"is_recorder"`
	Participants  []Player `json:"participants"`
}

// ListUserRuns は steamID が参加したランを新しい順に返す。
// onlyListed なら、公開で、かつ本人がページに載せているランだけを返す (本人以外が見るとき)。
func (s *Store) ListUserRuns(ctx context.Context, steamID string, onlyListed bool) ([]UserRun, error) {
	q := `
		SELECT s.id, s.created_at, sp.character_id, sp.character_name, s.ascension, s.outcome, s.final_floor,
		       s.visibility, sp.hidden, COALESCE(s.host_steam_id, '') = sp.steam_id
		FROM session_players sp
		JOIN sessions s ON s.id = sp.session_id
		WHERE sp.steam_id = ?`
	if onlyListed {
		q += ` AND s.visibility = 'public' AND sp.hidden = 0`
	}
	q += ` ORDER BY s.created_at DESC`
	rows, err := s.db.QueryContext(ctx, q, steamID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	out := []UserRun{}
	for rows.Next() {
		var r UserRun
		var hidden int
		var recorder bool
		if err := rows.Scan(&r.SessionID, &r.CreatedAt, &r.CharacterID, &r.CharacterName, &r.Ascension,
			&r.Outcome, &r.FinalFloor, &r.Visibility, &hidden, &recorder); err != nil {
			return nil, err
		}
		r.Hidden = hidden != 0
		r.IsRecorder = recorder
		out = append(out, r)
	}
	if err := rows.Err(); err != nil {
		return nil, err
	}
	for i := range out {
		ps, err := s.listSessionParticipants(ctx, out[i].SessionID)
		if err != nil {
			return nil, err
		}
		out[i].Participants = ps
	}
	return out, nil
}

func (s *Store) listSessionParticipants(ctx context.Context, sessionID string) ([]Player, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT sp.steam_id, COALESCE(p.display_name, '')
		FROM session_players sp LEFT JOIN players p ON p.steam_id = sp.steam_id
		WHERE sp.session_id = ? ORDER BY sp.steam_id`, sessionID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	out := []Player{}
	for rows.Next() {
		var p Player
		if err := rows.Scan(&p.SteamID, &p.DisplayName); err != nil {
			return nil, err
		}
		out = append(out, p)
	}
	return out, rows.Err()
}

// SetSessionVisibility は記録した人がランの公開・非公開を切り替える。記録した人でなければ ErrNotAllowed。
func (s *Store) SetSessionVisibility(ctx context.Context, sessionID, steamID, visibility string) error {
	return s.execAsRecorder(ctx, sessionID, steamID, `UPDATE sessions SET visibility = ? WHERE id = ?`, visibility, sessionID)
}

// DeleteSession は記録した人がランを消す。記録 (events) と参加者は外部キーの ON DELETE CASCADE で消える。
func (s *Store) DeleteSession(ctx context.Context, sessionID, steamID string) error {
	return s.execAsRecorder(ctx, sessionID, steamID, `DELETE FROM sessions WHERE id = ?`, sessionID)
}

func (s *Store) execAsRecorder(ctx context.Context, sessionID, steamID, query string, args ...any) error {
	sess, err := s.GetSession(ctx, sessionID)
	if err != nil {
		return err
	}
	if sess.HostSteamID == nil || *sess.HostSteamID != steamID {
		return ErrNotAllowed
	}
	_, err = s.db.ExecContext(ctx, query, args...)
	return err
}

// SetListingHidden は参加者が、そのランを自分のユーザーページに載せるかどうかを切り替える。参加者でなければ ErrNotAllowed。
func (s *Store) SetListingHidden(ctx context.Context, sessionID, steamID string, hidden bool) error {
	res, err := s.db.ExecContext(ctx, `UPDATE session_players SET hidden = ? WHERE session_id = ? AND steam_id = ?`,
		boolInt(hidden), sessionID, steamID)
	if err != nil {
		return err
	}
	n, err := res.RowsAffected()
	if err != nil {
		return err
	}
	if n == 0 {
		return ErrNotAllowed
	}
	return nil
}

// RunRelation は、ある人とランの関係 (ランの設定を出すかどうか。docs/spec/ui.md「ランの設定」)。
type RunRelation struct {
	IsParticipant bool   `json:"is_participant"`
	IsRecorder    bool   `json:"is_recorder"`
	Visibility    string `json:"visibility"`
	Hidden        bool   `json:"hidden"`
}

func (s *Store) GetRunRelation(ctx context.Context, sessionID, steamID string) (RunRelation, error) {
	sess, err := s.GetSession(ctx, sessionID)
	if err != nil {
		return RunRelation{}, err
	}
	rel := RunRelation{Visibility: sess.Visibility, IsRecorder: sess.HostSteamID != nil && *sess.HostSteamID == steamID}
	var hidden int
	err = s.db.QueryRowContext(ctx, `SELECT hidden FROM session_players WHERE session_id = ? AND steam_id = ?`, sessionID, steamID).Scan(&hidden)
	switch {
	case err == nil:
		rel.IsParticipant = true
		rel.Hidden = hidden != 0
	case errors.Is(err, sql.ErrNoRows):
	default:
		return RunRelation{}, err
	}
	return rel, nil
}

func boolInt(b bool) int {
	if b {
		return 1
	}
	return 0
}
