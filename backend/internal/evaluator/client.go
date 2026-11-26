// Package evaluator は、カード報酬を AI で評価する評価サーバー (evaluator/) を呼ぶ (docs/spec/card-evaluation.md)。
package evaluator

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"strings"

	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/store"
)

// Evaluator は評価サーバーを呼ぶ。テストでは差し替える。
type Evaluator interface {
	Evaluate(ctx context.Context, gameVersion *string, offers []store.CardRewardOffer) ([]store.CardRewardEvaluation, error)
}

// Client は評価サーバーの POST /evaluate を呼ぶ。
type Client struct {
	URL    string // 例 http://sts2stats-eval.flycast (末尾の / は無し)
	Client *http.Client
}

type request struct {
	GameVersion *string                 `json:"game_version"`
	Events      []store.CardRewardOffer `json:"events"`
}

type result struct {
	ID               int64           `json:"id"`
	Status           string          `json:"status"`
	Model            *string         `json:"model"`
	Value            *float64        `json:"value"`
	CardProbs        json.RawMessage `json:"card_probs"`
	AlternativeProbs json.RawMessage `json:"alternative_probs"`
}

func (c *Client) Evaluate(ctx context.Context, gameVersion *string, offers []store.CardRewardOffer) ([]store.CardRewardEvaluation, error) {
	body, err := json.Marshal(request{GameVersion: gameVersion, Events: offers})
	if err != nil {
		return nil, err
	}
	req, err := http.NewRequestWithContext(ctx, http.MethodPost, strings.TrimRight(c.URL, "/")+"/evaluate", bytes.NewReader(body))
	if err != nil {
		return nil, err
	}
	req.Header.Set("Content-Type", "application/json")
	resp, err := c.Client.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		msg, _ := io.ReadAll(io.LimitReader(resp.Body, 1024))
		return nil, fmt.Errorf("evaluator: %s: %s", resp.Status, msg)
	}
	var out struct {
		Results []result `json:"results"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&out); err != nil {
		return nil, fmt.Errorf("evaluator: decode response: %w", err)
	}
	// 評価サーバーは events と同じ順に 1 件ずつ返す。数か id が合わない応答は保存しない
	if len(out.Results) != len(offers) {
		return nil, fmt.Errorf("evaluator: %d results for %d events", len(out.Results), len(offers))
	}
	evals := make([]store.CardRewardEvaluation, len(out.Results))
	for i, r := range out.Results {
		if r.ID != offers[i].ID || r.Status == "" {
			return nil, fmt.Errorf("evaluator: result %d has id %d status %q, want id %d", i, r.ID, r.Status, offers[i].ID)
		}
		evals[i] = store.CardRewardEvaluation{
			EventID: r.ID, Status: r.Status, Model: r.Model, Value: r.Value,
			CardProbs: nonNull(r.CardProbs), AlternativeProbs: nonNull(r.AlternativeProbs),
		}
	}
	return evals, nil
}

func nonNull(v json.RawMessage) json.RawMessage {
	if len(v) == 0 || string(v) == "null" {
		return nil
	}
	return v
}
