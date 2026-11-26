package store

import (
	"context"
	"database/sql"
	"encoding/json"
	"time"
)

// CardRewardOffer は、評価サーバーに送る card_reward_offered の event 1 件 (docs/spec/card-evaluation.md「評価サーバーの API」)。
type CardRewardOffer struct {
	ID       int64           `json:"id"`
	PlayerID *string         `json:"player_id"`
	Floor    *int            `json:"floor"`
	Payload  json.RawMessage `json:"payload"`
}

// CardRewardEvaluation は、card_reward_offered の event 1 件の評価の結果 (docs/spec/card-evaluation.md「保存する表」)。
type CardRewardEvaluation struct {
	EventID          int64
	Status           string
	Model            *string
	Value            *float64
	CardProbs        json.RawMessage // status が ok でなければ nil
	AlternativeProbs json.RawMessage // status が ok でなければ nil
}

// CardRewardOffers は、セッションの card_reward_offered を保存した順に返す。
// 評価していない event が 1 件も無ければ、offers は nil を返す。評価サーバーには同じセッションの全件をまとめて送る必要があるので、
// 評価していない event があるときは、評価済みのものも含めて全件を返す。
func (s *Store) CardRewardOffers(ctx context.Context, sessionID string) (gameVersion *string, offers []CardRewardOffer, err error) {
	var pending int
	err = s.db.QueryRowContext(ctx, `
		SELECT s.game_version, (
			SELECT COUNT(*) FROM events e
			LEFT JOIN card_reward_evaluations c ON c.event_id = e.id
			WHERE e.session_id = s.id AND e.event_type = 'card_reward_offered' AND c.event_id IS NULL)
		FROM sessions s WHERE s.id = ?`, sessionID).Scan(&gameVersion, &pending)
	if err != nil || pending == 0 {
		return gameVersion, nil, err
	}
	rows, err := s.db.QueryContext(ctx, `
		SELECT id, player_id, floor, payload_json FROM events
		WHERE session_id = ? AND event_type = 'card_reward_offered'
		ORDER BY id`, sessionID)
	if err != nil {
		return nil, nil, err
	}
	defer rows.Close()
	for rows.Next() {
		var o CardRewardOffer
		var payload string
		var player sql.NullString
		var floor sql.NullInt64
		if err := rows.Scan(&o.ID, &player, &floor, &payload); err != nil {
			return nil, nil, err
		}
		if player.Valid {
			o.PlayerID = &player.String
		}
		if floor.Valid {
			f := int(floor.Int64)
			o.Floor = &f
		}
		o.Payload = json.RawMessage(payload)
		offers = append(offers, o)
	}
	return gameVersion, offers, rows.Err()
}

// InsertCardRewardEvaluations は評価の結果を保存する。保存済みの event の結果は上書きしない。
func (s *Store) InsertCardRewardEvaluations(ctx context.Context, evals []CardRewardEvaluation, now time.Time) error {
	tx, err := s.db.BeginTx(ctx, nil)
	if err != nil {
		return err
	}
	defer tx.Rollback()
	at := now.UTC().Format(time.RFC3339)
	for _, e := range evals {
		_, err := tx.ExecContext(ctx, `
			INSERT OR IGNORE INTO card_reward_evaluations
			  (event_id, status, model, value, card_probs_json, alternative_probs_json, evaluated_at)
			VALUES (?, ?, ?, ?, ?, ?, ?)`,
			e.EventID, e.Status, e.Model, e.Value, nullableJSON(e.CardProbs), nullableJSON(e.AlternativeProbs), at)
		if err != nil {
			return err
		}
	}
	return tx.Commit()
}

func nullableJSON(v json.RawMessage) any {
	if v == nil {
		return nil
	}
	return string(v)
}

// OfferedCard は、評価の表示に使うカードの最小限の値 (docs/spec/api.md「GET /api/sessions/{id}」)。
type OfferedCard struct {
	CardID       string `json:"card_id"`
	UpgradeLevel int    `json:"upgrade_level"`
	Enchanted    bool   `json:"enchanted"`
}

// CardRewardEvaluationView は、GET /api/sessions/{id} の card_reward_evaluations の 1 要素。
type CardRewardEvaluationView struct {
	EventID          int64           `json:"event_id"`
	PlayerID         *string         `json:"player_id"`
	Floor            *int            `json:"floor"`
	Offered          []OfferedCard   `json:"offered"`
	Alternatives     []string        `json:"alternatives"`
	Status           string          `json:"status"`
	CardProbs        json.RawMessage `json:"card_probs,omitempty"`
	AlternativeProbs json.RawMessage `json:"alternative_probs,omitempty"`
}

// ListCardRewardEvaluations は、セッションのカード報酬の評価を event の id の順に返す。
// 提示されたカードと選択肢は card_reward_offered の payload から取る。payload の deck などは返さない (1 件が数 KB あるため)。
func (s *Store) ListCardRewardEvaluations(ctx context.Context, sessionID string) ([]CardRewardEvaluationView, error) {
	rows, err := s.db.QueryContext(ctx, `
		SELECT e.id, e.player_id, e.floor,
		       COALESCE(json_extract(e.payload_json, '$.offered'), '[]'),
		       COALESCE(json_extract(e.payload_json, '$.alternatives'), '[]'),
		       v.status, v.card_probs_json, v.alternative_probs_json
		FROM card_reward_evaluations v JOIN events e ON e.id = v.event_id
		WHERE e.session_id = ?
		ORDER BY e.id`, sessionID)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	out := []CardRewardEvaluationView{}
	for rows.Next() {
		var v CardRewardEvaluationView
		var player sql.NullString
		var floor sql.NullInt64
		var offered, alternatives string
		var cardProbs, altProbs sql.NullString
		if err := rows.Scan(&v.EventID, &player, &floor, &offered, &alternatives, &v.Status, &cardProbs, &altProbs); err != nil {
			return nil, err
		}
		if player.Valid {
			v.PlayerID = &player.String
		}
		if floor.Valid {
			f := int(floor.Int64)
			v.Floor = &f
		}
		var cards []struct {
			CardID       string          `json:"card_id"`
			UpgradeLevel int             `json:"upgrade_level"`
			Enchantment  json.RawMessage `json:"enchantment"`
		}
		// payload は mod が送った値で、mod のバージョンによって形が違うことがある。読めないときは空にして、評価の status だけを返す
		if json.Unmarshal([]byte(offered), &cards) != nil {
			cards = nil
		}
		v.Offered = make([]OfferedCard, len(cards))
		for i, c := range cards {
			v.Offered[i] = OfferedCard{CardID: c.CardID, UpgradeLevel: c.UpgradeLevel, Enchanted: len(c.Enchantment) > 0 && string(c.Enchantment) != "null"}
		}
		if json.Unmarshal([]byte(alternatives), &v.Alternatives) != nil || v.Alternatives == nil {
			v.Alternatives = []string{}
		}
		if cardProbs.Valid {
			v.CardProbs = json.RawMessage(cardProbs.String)
		}
		if altProbs.Valid {
			v.AlternativeProbs = json.RawMessage(altProbs.String)
		}
		out = append(out, v)
	}
	return out, rows.Err()
}
