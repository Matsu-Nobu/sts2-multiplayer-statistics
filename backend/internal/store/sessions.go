package store

import (
	"context"
	"database/sql"
	"errors"
	"time"
)

type Session struct {
	ID           string  `json:"id"`
	CreatedAt    string  `json:"created_at"`
	HostName     *string `json:"host_name"`
	HostSteamID  *string `json:"host_steam_id"`
	CharacterID  *string `json:"character_id"`
	Ascension    *int    `json:"ascension"`
	Seed         *string `json:"seed"`
	Outcome      *string `json:"outcome"`
	FinalFloor   *int    `json:"final_floor"`
	FinishedAt   *string `json:"finished_at"`
	WriteToken   string  `json:"-"`
}

type CreateSessionInput struct {
	ID          string
	WriteToken  string
	HostName    *string
	HostSteamID *string
	CharacterID *string
	Ascension   *int
	Seed        *string
}

func (s *Store) CreateSession(ctx context.Context, in CreateSessionInput) error {
	now := time.Now().UTC().Format(time.RFC3339)
	_, err := s.db.ExecContext(ctx,
		`INSERT INTO sessions (id, created_at, host_name, host_steam_id, character_id, ascension, seed, write_token)
		 VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
		in.ID, now, in.HostName, in.HostSteamID, in.CharacterID, in.Ascension, in.Seed, in.WriteToken,
	)
	return err
}

// ErrSessionNotFound is returned when a session does not exist.
var ErrSessionNotFound = errors.New("session not found")

func (s *Store) GetSession(ctx context.Context, id string) (*Session, error) {
	row := s.db.QueryRowContext(ctx,
		`SELECT id, created_at, host_name, host_steam_id, character_id, ascension, seed,
		        outcome, final_floor, finished_at, write_token
		 FROM sessions WHERE id = ?`, id)
	var sess Session
	err := row.Scan(&sess.ID, &sess.CreatedAt, &sess.HostName, &sess.HostSteamID, &sess.CharacterID,
		&sess.Ascension, &sess.Seed, &sess.Outcome, &sess.FinalFloor, &sess.FinishedAt, &sess.WriteToken)
	if errors.Is(err, sql.ErrNoRows) {
		return nil, ErrSessionNotFound
	}
	if err != nil {
		return nil, err
	}
	return &sess, nil
}

// UpdateRunOutcome sets outcome / final_floor / finished_at on the session.
// Used when a run_end event is received.
func (s *Store) UpdateRunOutcome(ctx context.Context, sessionID, outcome string, finalFloor *int, finishedAt string) error {
	_, err := s.db.ExecContext(ctx,
		`UPDATE sessions SET outcome = ?, final_floor = ?, finished_at = ? WHERE id = ?`,
		outcome, finalFloor, finishedAt, sessionID)
	return err
}
