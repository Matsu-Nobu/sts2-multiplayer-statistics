package api

import (
	"context"
	"errors"
	"net/http"
	"sync"
	"testing"

	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/store"
)

// fakeEvaluator は評価サーバーの代わり。受け取った要求を記録し、fail が true なら失敗を返す。
type fakeEvaluator struct {
	mu    sync.Mutex
	calls [][]int64
	fail  bool
}

func (f *fakeEvaluator) Evaluate(_ context.Context, gameVersion *string, offers []store.CardRewardOffer) ([]store.CardRewardEvaluation, error) {
	f.mu.Lock()
	defer f.mu.Unlock()
	ids := []int64{}
	evals := []store.CardRewardEvaluation{}
	for _, o := range offers {
		ids = append(ids, o.ID)
		evals = append(evals, store.CardRewardEvaluation{EventID: o.ID, Status: "ok:" + *gameVersion})
	}
	f.calls = append(f.calls, ids)
	if f.fail {
		return nil, errors.New("evaluator unavailable")
	}
	return evals, nil
}

func offeredEvent(uuid string) map[string]any {
	return map[string]any{
		"event_uuid": uuid, "event_type": "card_reward_offered", "occurred_at": "2026-11-26T00:00:00Z",
		"player_id": "76561199000000000", "floor": 2, "payload": map[string]any{"character_id": "FOOL"},
	}
}

func otherEvent(uuid string) map[string]any {
	return map[string]any{"event_uuid": uuid, "event_type": "floor_snapshot", "occurred_at": "2026-11-26T00:00:00Z",
		"floor": 2, "payload": map[string]any{"floor": 2, "players": []any{}}}
}

func postEventsAndWait(t *testing.T, srv *Server, id, token string, events ...map[string]any) {
	t.Helper()
	res := do(t, srv, http.MethodPost, "/sessions/"+id+"/events", token, events)
	if res.StatusCode != http.StatusNoContent {
		t.Fatalf("post events status=%d", res.StatusCode)
	}
	srv.warming.Wait()
}

func evaluationStatuses(t *testing.T, st *store.Store) map[int64]string {
	t.Helper()
	rows, err := st.DB().Query(`SELECT event_id, status FROM card_reward_evaluations`)
	if err != nil {
		t.Fatal(err)
	}
	defer rows.Close()
	out := map[int64]string{}
	for rows.Next() {
		var id int64
		var status string
		if err := rows.Scan(&id, &status); err != nil {
			t.Fatal(err)
		}
		out[id] = status
	}
	return out
}

func TestPostEvents_EvaluatesCardRewards(t *testing.T) {
	t.Run("card_reward_offered を保存したら、セッションの全件を評価して保存する", func(t *testing.T) {
		srv, st := newTestServer(t)
		ev := &fakeEvaluator{}
		srv.Evaluator = ev
		id, token := createSession(t, srv)
		postEventsAndWait(t, srv, id, token, offeredEvent("a"), otherEvent("snap"))
		postEventsAndWait(t, srv, id, token, offeredEvent("b"))

		if len(ev.calls) != 2 || len(ev.calls[0]) != 1 || len(ev.calls[1]) != 2 {
			t.Fatalf("calls = %v", ev.calls)
		}
		got := evaluationStatuses(t, st)
		if len(got) != 2 || got[ev.calls[1][0]] != "ok:"+testGameVersion || got[ev.calls[1][1]] != "ok:"+testGameVersion {
			t.Fatalf("evaluations = %v", got)
		}
	})

	t.Run("card_reward_offered が無いバッチと、重複して届いた card_reward_offered では評価しない", func(t *testing.T) {
		srv, _ := newTestServer(t)
		ev := &fakeEvaluator{}
		srv.Evaluator = ev
		id, token := createSession(t, srv)
		postEventsAndWait(t, srv, id, token, otherEvent("snap"))
		postEventsAndWait(t, srv, id, token, offeredEvent("a"))
		postEventsAndWait(t, srv, id, token, offeredEvent("a"))
		if len(ev.calls) != 1 {
			t.Fatalf("calls = %v", ev.calls)
		}
	})

	t.Run("評価に失敗したら、次の card_reward_offered のときに評価していない event をまとめて送り直す", func(t *testing.T) {
		srv, st := newTestServer(t)
		ev := &fakeEvaluator{fail: true}
		srv.Evaluator = ev
		id, token := createSession(t, srv)
		postEventsAndWait(t, srv, id, token, offeredEvent("a"))
		if got := evaluationStatuses(t, st); len(got) != 0 {
			t.Fatalf("evaluations after failure = %v", got)
		}
		ev.fail = false
		postEventsAndWait(t, srv, id, token, offeredEvent("b"))
		if len(ev.calls) != 2 || len(ev.calls[1]) != 2 {
			t.Fatalf("calls = %v", ev.calls)
		}
		if got := evaluationStatuses(t, st); len(got) != 2 {
			t.Fatalf("evaluations = %v", got)
		}
	})

	t.Run("評価サーバーが設定されていなければ評価しない", func(t *testing.T) {
		srv, st := newTestServer(t)
		id, token := createSession(t, srv)
		postEventsAndWait(t, srv, id, token, offeredEvent("a"))
		if got := evaluationStatuses(t, st); len(got) != 0 {
			t.Fatalf("evaluations = %v", got)
		}
	})
}
