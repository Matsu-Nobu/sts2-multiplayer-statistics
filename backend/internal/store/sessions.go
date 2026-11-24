package store

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"time"
)

type Session struct {
	ID          string  `json:"id"`
	CreatedAt   string  `json:"created_at"`
	HostName    *string `json:"host_name"`
	HostSteamID *string `json:"host_steam_id"`
	CharacterID *string `json:"character_id"`
	Ascension   *int    `json:"ascension"`
	Seed        *string `json:"seed"`
	GameVersion *string `json:"game_version"`
	Outcome     *string `json:"outcome"`
	FinalFloor  *int    `json:"final_floor"`
	FinishedAt  *string `json:"finished_at"`
	Visibility  string  `json:"visibility"` // public / private (docs/spec/user-pages.md)
	Source      string  `json:"source"`     // live / history (docs/spec/api.md「POST /sessions」)
	WriteToken  string  `json:"-"`
}

type CreateSessionInput struct {
	ID          string
	WriteToken  string
	HostName    *string
	HostSteamID *string
	CharacterID *string
	Ascension   *int
	Seed        *string
	GameVersion *string
	// CreatedAt は CreateHistorySession だけが使う。ランを始めた日時。
	CreatedAt time.Time
}

func (s *Store) CreateSession(ctx context.Context, in CreateSessionInput) error {
	_, err := s.db.ExecContext(ctx,
		`INSERT INTO sessions (id, created_at, host_name, host_steam_id, character_id, ascension, seed, game_version, write_token)
		 VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
		in.ID, time.Now().UTC().Format(time.RFC3339), in.HostName, in.HostSteamID, in.CharacterID, in.Ascension, in.Seed, in.GameVersion, in.WriteToken,
	)
	return err
}

// SameRun は、seed が同じで、playerIDs の誰かが参加していて、startedAt の 1 時間前より後に作られたセッションのうち、
// 最も早く作られたものを返す (docs/spec/api.md「同じランの見分け方」)。無ければ nil。
// 取り込みの途中で止まったセッション (source が history で結果が空) は数えない。
// 取り込んだランは必ず勝利か死亡の run_end を送るので、結果が空なら最後まで届いていない。
func (s *Store) SameRun(ctx context.Context, seed string, startedAt time.Time, playerIDs []string) (*Session, error) {
	if seed == "" || len(playerIDs) == 0 {
		return nil, nil
	}
	ids, err := json.Marshal(playerIDs)
	if err != nil {
		return nil, err
	}
	var id string
	err = s.db.QueryRowContext(ctx,
		`SELECT s.id FROM sessions s
		 WHERE s.seed = ? AND s.created_at > ?
		   AND NOT (s.source = 'history' AND s.outcome IS NULL)
		   AND EXISTS (SELECT 1 FROM session_players sp
		               WHERE sp.session_id = s.id AND sp.steam_id IN (SELECT value FROM json_each(?)))
		 ORDER BY s.created_at, s.id LIMIT 1`,
		seed, startedAt.Add(-time.Hour).UTC().Format(time.RFC3339), string(ids)).Scan(&id)
	if errors.Is(err, sql.ErrNoRows) {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	return s.GetSession(ctx, id)
}

// FillRunOutcome は、結果がまだ空のセッションにだけ結果と到達階を入れ、入れたら true を返す。
func (s *Store) FillRunOutcome(ctx context.Context, sessionID, outcome string, finalFloor *int, finishedAt string) (bool, error) {
	res, err := s.db.ExecContext(ctx,
		`UPDATE sessions SET outcome = ?, final_floor = ?, finished_at = ? WHERE id = ? AND outcome IS NULL`,
		outcome, finalFloor, finishedAt, sessionID)
	if err != nil {
		return false, err
	}
	n, err := res.RowsAffected()
	return n > 0, err
}

// ErrSessionNotFound is returned when a session does not exist.
var ErrSessionNotFound = errors.New("session not found")

func (s *Store) GetSession(ctx context.Context, id string) (*Session, error) {
	row := s.db.QueryRowContext(ctx,
		`SELECT id, created_at, host_name, host_steam_id, character_id, ascension, seed, game_version,
		        outcome, final_floor, finished_at, visibility, source, write_token
		 FROM sessions WHERE id = ?`, id)
	var sess Session
	err := row.Scan(&sess.ID, &sess.CreatedAt, &sess.HostName, &sess.HostSteamID, &sess.CharacterID,
		&sess.Ascension, &sess.Seed, &sess.GameVersion, &sess.Outcome, &sess.FinalFloor, &sess.FinishedAt, &sess.Visibility, &sess.Source, &sess.WriteToken)
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

// CreateHistorySession は、セーブデータの履歴から取り込むランのセッションを作り、playerIDs を参加者に入れる。
// 同じランのセッションが既にあれば作らずにそれを返す。
// 同じランの取り込みが途中で止まって残ったセッションは消してから作る。
func (s *Store) CreateHistorySession(ctx context.Context, in CreateSessionInput, playerIDs []string) (*Session, error) {
	s.historyMu.Lock()
	defer s.historyMu.Unlock()

	same, err := s.SameRun(ctx, derefString(in.Seed), in.CreatedAt, playerIDs)
	if err != nil || same != nil {
		return same, err
	}
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return nil, err
	}
	defer tx.Rollback()
	created := in.CreatedAt.UTC().Format(time.RFC3339)
	if _, err := tx.ExecContext(ctx,
		`DELETE FROM sessions WHERE source = 'history' AND outcome IS NULL AND seed = ? AND created_at = ?`,
		in.Seed, created); err != nil {
		return nil, err
	}
	if _, err := tx.ExecContext(ctx,
		`INSERT INTO sessions (id, created_at, host_name, host_steam_id, character_id, ascension, seed, game_version, source, write_token)
		 VALUES (?, ?, ?, ?, ?, ?, ?, ?, 'history', ?)`,
		in.ID, created, in.HostName, in.HostSteamID, in.CharacterID, in.Ascension, in.Seed, in.GameVersion, in.WriteToken); err != nil {
		return nil, err
	}
	for _, id := range playerIDs {
		if _, err := tx.ExecContext(ctx,
			`INSERT OR IGNORE INTO session_players (session_id, steam_id) VALUES (?, ?)`, in.ID, id); err != nil {
			return nil, err
		}
	}
	return nil, tx.Commit()
}

func derefString(p *string) string {
	if p == nil {
		return ""
	}
	return *p
}
