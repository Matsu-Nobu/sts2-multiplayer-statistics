package store

import (
	"context"
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"sort"
	"time"
)

// プレイヤーの統計 (docs/spec/player-stats.md、docs/spec/api.md「GET /api/users/{steam_id}/stats」) に使う、ゲームごとの集計。

// summaryVersion は集計の作り方の版。GameSummary の作り方を変えたら上げる。
const summaryVersion = 2

// CardOffer は、1 つの Act で同じカードが提示された回数と取った回数。
type CardOffer struct {
	ID      string `json:"id"`
	Name    string `json:"name"`
	Offered int    `json:"offered"`
	Picked  int    `json:"picked"`
	Act     int    `json:"act_index"` // 0 始まり
}

type Model struct {
	ID   string `json:"id"`
	Name string `json:"name"`
}

type Fight struct {
	ModelID     string `json:"model_id"`
	ModelName   string `json:"model_name"`
	RoomType    string `json:"room_type"`
	DamageTaken int    `json:"damage_taken"`
	Died        bool   `json:"died"`
}

type AncientOption struct {
	Title  string `json:"title"`
	Chosen bool   `json:"chosen"`
}

type AncientVisit struct {
	ModelID   string          `json:"model_id"`
	ModelName string          `json:"model_name"`
	Options   []AncientOption `json:"options"`
}

type EventVisit struct {
	ModelID   string   `json:"model_id"`
	ModelName string   `json:"model_name"`
	Choices   []string `json:"choices"`
}

// GameSummary は 1 つのゲームの、1 人分の集計。
type GameSummary struct {
	SessionID     string   `json:"session_id"`
	CreatedAt     string   `json:"created_at"`
	CharacterID   *string  `json:"character_id"`
	CharacterName *string  `json:"character_name"`
	GameMode      *string  `json:"game_mode"`
	Ascension     *int     `json:"ascension"`
	GameVersion   *string  `json:"game_version"`
	Outcome       string   `json:"outcome"`
	FinalFloor    *int     `json:"final_floor"`
	Partners      []Player `json:"partners"`
	gameDetail
}

// gameDetail は記録から作り、game_summaries に保存する部分。
type gameDetail struct {
	MaxActIndex int            `json:"max_act_index"`
	DamageShare *float64       `json:"damage_share"`
	CardOffers  []CardOffer    `json:"card_offers"`
	Relics      []Model        `json:"relics"`
	Fights      []Fight        `json:"fights"`
	RestChoices []string       `json:"rest_choices"`
	Ancients    []AncientVisit `json:"ancients"`
	Events      []EventVisit   `json:"events"`
}

// ListUserGameSummaries は、steamID が参加したゲームのうち結果が勝利か死亡のものの集計を、新しい順に返す。
// onlyListed の意味は ListUserRuns と同じ。
func (s *Store) ListUserGameSummaries(ctx context.Context, steamID string, onlyListed bool) ([]GameSummary, error) {
	runs, err := s.ListUserRuns(ctx, steamID, onlyListed)
	if err != nil {
		return nil, err
	}
	out := []GameSummary{}
	for _, r := range runs {
		if r.Outcome == nil || (*r.Outcome != "victory" && *r.Outcome != "death") {
			continue
		}
		d, _, err := s.gameDetailFor(ctx, r.SessionID, steamID, *r.Outcome)
		if err != nil {
			return nil, fmt.Errorf("summary %s: %w", r.SessionID, err)
		}
		partners := []Player{}
		for _, p := range r.Participants {
			if p.SteamID != steamID {
				partners = append(partners, p)
			}
		}
		out = append(out, GameSummary{
			SessionID: r.SessionID, CreatedAt: r.CreatedAt, CharacterID: r.CharacterID, CharacterName: r.CharacterName,
			GameMode: r.GameMode, Ascension: r.Ascension, GameVersion: r.GameVersion, Outcome: *r.Outcome, FinalFloor: r.FinalFloor,
			Partners: partners, gameDetail: d,
		})
	}
	return out, nil
}

// gameDetailFor は保存してある集計を返す。版が古いか、集計の後に新しい記録が届いていれば作り直して保存し、built を true にする。
func (s *Store) gameDetailFor(ctx context.Context, sessionID, steamID, outcome string) (d gameDetail, built bool, err error) {
	var lastID int64
	if err := s.db.QueryRowContext(ctx, `SELECT COALESCE(MAX(id), 0) FROM events WHERE session_id = ?`, sessionID).Scan(&lastID); err != nil {
		return gameDetail{}, false, err
	}
	var version, cachedID int64
	var raw string
	err = s.db.QueryRowContext(ctx, `SELECT version, last_event_id, summary_json FROM game_summaries WHERE session_id = ? AND steam_id = ?`,
		sessionID, steamID).Scan(&version, &cachedID, &raw)
	switch {
	case err == nil && version == summaryVersion && cachedID == lastID:
		if err := json.Unmarshal([]byte(raw), &d); err != nil {
			return gameDetail{}, false, err
		}
		return d, false, nil
	case err != nil && !errors.Is(err, sql.ErrNoRows):
		return gameDetail{}, false, err
	}
	start := time.Now()
	d, err = s.buildGameDetail(ctx, sessionID, steamID, outcome)
	if err != nil {
		return gameDetail{}, false, err
	}
	enc, err := json.Marshal(d)
	if err != nil {
		return gameDetail{}, false, err
	}
	_, err = s.db.ExecContext(ctx, `
		INSERT INTO game_summaries (session_id, steam_id, version, last_event_id, summary_json) VALUES (?, ?, ?, ?, ?)
		ON CONFLICT(session_id, steam_id) DO UPDATE SET
			version = excluded.version, last_event_id = excluded.last_event_id, summary_json = excluded.summary_json`,
		sessionID, steamID, summaryVersion, lastID, string(enc))
	if err != nil {
		return gameDetail{}, false, err
	}
	slog.Info("game summary built", "session", sessionID, "steam_id", steamID, "elapsed", time.Since(start))
	return d, true, nil
}

// WarmGameSummaries は、勝利か死亡で終わったゲームについて、参加者全員の集計を作って保存する。
// 統計のタブを開いたときに集計を待たせないため、ゲームが終わった時点で呼ぶ。
// sessionID が空なら、すべての終わったゲームを対象にする (集計の作り方を変えた後の起動時に呼ぶ)。
func (s *Store) WarmGameSummaries(ctx context.Context, sessionID string) (built int, err error) {
	q := `SELECT sp.session_id, sp.steam_id, s.outcome FROM session_players sp JOIN sessions s ON s.id = sp.session_id
		WHERE s.outcome IN ('victory', 'death')`
	args := []any{}
	if sessionID != "" {
		q += ` AND s.id = ?`
		args = append(args, sessionID)
	}
	rows, err := s.db.QueryContext(ctx, q, args...)
	if err != nil {
		return 0, err
	}
	type target struct{ session, steamID, outcome string }
	var targets []target
	for rows.Next() {
		var t target
		if err := rows.Scan(&t.session, &t.steamID, &t.outcome); err != nil {
			rows.Close()
			return 0, err
		}
		targets = append(targets, t)
	}
	rows.Close()
	if err := rows.Err(); err != nil {
		return 0, err
	}
	for _, t := range targets {
		_, b, err := s.gameDetailFor(ctx, t.session, t.steamID, t.outcome)
		if err != nil {
			return built, fmt.Errorf("summary %s %s: %w", t.session, t.steamID, err)
		}
		if b {
			built++
		}
	}
	return built, nil
}

// floorSnapshot は floor_snapshot の payload のうち、集計に使う部分 (docs/spec/api.md「floor_snapshot」)。
type floorSnapshot struct {
	Floor    int `json:"floor"`
	ActIndex int `json:"act_index"`
	Rooms    []struct {
		RoomType  string `json:"room_type"`
		ModelID   string `json:"model_id"`
		ModelName string `json:"model_name"`
	} `json:"rooms"`
	Players []struct {
		PlayerID string `json:"player_id"`
		HP       struct {
			DamageTaken int `json:"damage_taken"`
		} `json:"hp"`
		CardChoices []struct {
			Card      Model `json:"card"`
			WasPicked bool  `json:"was_picked"`
		} `json:"card_choices"`
		Relics          []Model  `json:"relics"`
		RestSiteChoices []string `json:"rest_site_choices"`
		EventChoices    []string `json:"event_choices"`
		AncientChoices  []struct {
			Title     string `json:"title"`
			WasChosen bool   `json:"was_chosen"`
		} `json:"ancient_choices"`
	} `json:"players"`
}

func isCombatRoom(t string) bool { return t == "Monster" || t == "Elite" || t == "Boss" }

// neowModelID はネオーの部屋。ブリッツではネオーの階に選択肢が無く、event_choices に「ブリッツ」だけが入るので、イベントに数えない。
const neowModelID = "NEOW"

func (s *Store) buildGameDetail(ctx context.Context, sessionID, steamID, outcome string) (gameDetail, error) {
	rows, err := s.db.QueryContext(ctx, `SELECT payload_json FROM events WHERE session_id = ? AND event_type = 'floor_snapshot' ORDER BY id`, sessionID)
	if err != nil {
		return gameDetail{}, err
	}
	// 同じ階の snapshot は何度も届くので、階ごとに最後に届いたものだけを使う
	byFloor := map[int]floorSnapshot{}
	for rows.Next() {
		var raw string
		if err := rows.Scan(&raw); err != nil {
			rows.Close()
			return gameDetail{}, err
		}
		var fs floorSnapshot
		if err := json.Unmarshal([]byte(raw), &fs); err != nil {
			rows.Close()
			return gameDetail{}, err
		}
		byFloor[fs.Floor] = fs
	}
	rows.Close()
	if err := rows.Err(); err != nil {
		return gameDetail{}, err
	}
	floors := make([]int, 0, len(byFloor))
	for f := range byFloor {
		floors = append(floors, f)
	}
	sort.Ints(floors)

	d := gameDetail{CardOffers: []CardOffer{}, Relics: []Model{}, Fights: []Fight{}, RestChoices: []string{}, Ancients: []AncientVisit{}, Events: []EventVisit{}}
	type cardKey struct {
		id  string
		act int
	}
	cardIdx := map[cardKey]int{}
	relicSeen := map[string]bool{}
	for i, f := range floors {
		fs := byFloor[f]
		if fs.ActIndex > d.MaxActIndex {
			d.MaxActIndex = fs.ActIndex
		}
		me := -1
		for j := range fs.Players {
			if fs.Players[j].PlayerID == steamID {
				me = j
			}
		}
		if me < 0 {
			continue
		}
		p := fs.Players[me]
		shop, combat, other := false, -1, -1
		for j, r := range fs.Rooms {
			switch {
			case r.RoomType == "Shop":
				shop = true
			case isCombatRoom(r.RoomType):
				combat = j
			case other < 0:
				other = j
			}
		}
		if !shop {
			for _, c := range p.CardChoices {
				key := cardKey{c.Card.ID, fs.ActIndex}
				k, ok := cardIdx[key]
				if !ok {
					k = len(d.CardOffers)
					cardIdx[key] = k
					d.CardOffers = append(d.CardOffers, CardOffer{ID: c.Card.ID, Name: c.Card.Name, Act: fs.ActIndex})
				}
				d.CardOffers[k].Offered++
				if c.WasPicked {
					d.CardOffers[k].Picked++
				}
			}
		}
		for _, r := range p.Relics {
			if !relicSeen[r.ID] {
				relicSeen[r.ID] = true
				d.Relics = append(d.Relics, r)
			}
		}
		if combat >= 0 {
			r := fs.Rooms[combat]
			d.Fights = append(d.Fights, Fight{ModelID: r.ModelID, ModelName: r.ModelName, RoomType: r.RoomType,
				DamageTaken: p.HP.DamageTaken, Died: outcome == "death" && i == len(floors)-1})
		}
		d.RestChoices = append(d.RestChoices, p.RestSiteChoices...)
		room := Model{}
		if other >= 0 {
			room = Model{ID: fs.Rooms[other].ModelID, Name: fs.Rooms[other].ModelName}
		}
		switch {
		case len(p.AncientChoices) > 0:
			v := AncientVisit{ModelID: room.ID, ModelName: room.Name, Options: []AncientOption{}}
			for _, a := range p.AncientChoices {
				v.Options = append(v.Options, AncientOption{Title: a.Title, Chosen: a.WasChosen})
			}
			d.Ancients = append(d.Ancients, v)
		case len(p.EventChoices) > 0 && room.ID != neowModelID:
			d.Events = append(d.Events, EventVisit{ModelID: room.ID, ModelName: room.Name, Choices: p.EventChoices})
		}
	}

	share, err := s.damageShare(ctx, sessionID, steamID)
	if err != nil {
		return gameDetail{}, err
	}
	d.DamageShare = share
	return d, nil
}

// damageShare は、全員が敵に与えたダメージのうち steamID の分の割合。ほかの人が与えていない (ソロ) か、合計が 0 なら nil。
func (s *Store) damageShare(ctx context.Context, sessionID, steamID string) (*float64, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT player_id, SUM(COALESCE(json_extract(payload_json, '$.amount'), 0))
		FROM events WHERE session_id = ? AND event_type = 'damage_dealt' AND player_id IS NOT NULL AND player_id != ''
		GROUP BY player_id`, sessionID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var mine, total float64
	others := false
	for rows.Next() {
		var pid string
		var sum float64
		if err := rows.Scan(&pid, &sum); err != nil {
			return nil, err
		}
		total += sum
		if pid == steamID {
			mine = sum
		} else {
			others = true
		}
	}
	if err := rows.Err(); err != nil {
		return nil, err
	}
	if !others || total == 0 {
		return nil, nil
	}
	v := mine / total
	return &v, nil
}
