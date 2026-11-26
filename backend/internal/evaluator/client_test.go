package evaluator

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/store"
)

// evaluatorStub は評価サーバーの代わりに、受け取った要求を記録して、決まった応答を返す。
func evaluatorStub(t *testing.T, status int, body string, got *map[string]any) *Client {
	t.Helper()
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodPost || r.URL.Path != "/evaluate" {
			t.Errorf("request %s %s", r.Method, r.URL.Path)
		}
		if got != nil {
			if err := json.NewDecoder(r.Body).Decode(got); err != nil {
				t.Errorf("decode request: %v", err)
			}
		}
		w.WriteHeader(status)
		_, _ = w.Write([]byte(body))
	}))
	t.Cleanup(srv.Close)
	return &Client{URL: srv.URL + "/", Client: srv.Client()}
}

func offers() []store.CardRewardOffer {
	player, floor := "76561190000000001", 2
	return []store.CardRewardOffer{
		{ID: 10, PlayerID: &player, Floor: &floor, Payload: json.RawMessage(`{"character_id":"FOOL"}`)},
		{ID: 11, PlayerID: &player, Floor: &floor, Payload: json.RawMessage(`{"character_id":"FOOL"}`)},
	}
}

func TestClientEvaluate(t *testing.T) {
	version := "v0.114.0"

	t.Run("要求の形と、結果の読み取り", func(t *testing.T) {
		var got map[string]any
		c := evaluatorStub(t, 200, `{"results":[
			{"id":10,"status":"ok","model":"fool_ema_v24181","value":-0.3,"card_probs":[0.2,0.8],"alternative_probs":{"Skip":0}},
			{"id":11,"status":"unknown_card","model":"fool_ema_v24181"}]}`, &got)
		evals, err := c.Evaluate(context.Background(), &version, offers())
		if err != nil {
			t.Fatal(err)
		}
		if got["game_version"] != version {
			t.Errorf("game_version = %v", got["game_version"])
		}
		events := got["events"].([]any)
		first := events[0].(map[string]any)
		if len(events) != 2 || first["id"] != float64(10) || first["floor"] != float64(2) ||
			first["player_id"] != "76561190000000001" || first["payload"].(map[string]any)["character_id"] != "FOOL" {
			t.Errorf("events = %v", events)
		}
		if evals[0].Status != "ok" || *evals[0].Value != -0.3 || string(evals[0].CardProbs) != "[0.2,0.8]" ||
			string(evals[0].AlternativeProbs) != `{"Skip":0}` || *evals[0].Model != "fool_ema_v24181" {
			t.Errorf("evals[0] = %+v", evals[0])
		}
		if evals[1].EventID != 11 || evals[1].Status != "unknown_card" || evals[1].Value != nil || evals[1].CardProbs != nil || evals[1].AlternativeProbs != nil {
			t.Errorf("evals[1] = %+v", evals[1])
		}
	})

	t.Run("セッションにゲームのバージョンが無いときは null を送る", func(t *testing.T) {
		var got map[string]any
		c := evaluatorStub(t, 200, `{"results":[{"id":10,"status":"unsupported_version"},{"id":11,"status":"unsupported_version"}]}`, &got)
		if _, err := c.Evaluate(context.Background(), nil, offers()); err != nil {
			t.Fatal(err)
		}
		if v, ok := got["game_version"]; !ok || v != nil {
			t.Errorf("game_version = %v (present %v)", v, ok)
		}
	})

	errorCases := []struct{ name, body, want string }{
		{"結果の数が違う", `{"results":[{"id":10,"status":"ok"}]}`, "1 results for 2 events"},
		{"結果の id が違う", `{"results":[{"id":10,"status":"ok"},{"id":12,"status":"ok"}]}`, "want id 11"},
		{"status が無い", `{"results":[{"id":10,"status":"ok"},{"id":11}]}`, `status ""`},
		{"JSON が壊れている", `{"results":`, "decode response"},
	}
	for _, c := range errorCases {
		t.Run(c.name, func(t *testing.T) {
			_, err := evaluatorStub(t, 200, c.body, nil).Evaluate(context.Background(), &version, offers())
			if err == nil || !strings.Contains(err.Error(), c.want) {
				t.Fatalf("err = %v, want %q", err, c.want)
			}
		})
	}

	t.Run("200 以外の応答", func(t *testing.T) {
		_, err := evaluatorStub(t, 422, "KeyError: 'id'", nil).Evaluate(context.Background(), &version, offers())
		if err == nil || !strings.Contains(err.Error(), "422") || !strings.Contains(err.Error(), "KeyError") {
			t.Fatalf("err = %v", err)
		}
	})
}
