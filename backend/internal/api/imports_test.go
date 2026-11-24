package api

import (
	"io"
	"net/http"
	"testing"
	"time"
)

const importPlayer = "76561199000000001"

// liveRun は、mod がゲーム中に記録したセッションを作り、importPlayer の run_start を送る。
func liveRun(t *testing.T, srv *Server, seed string) string {
	t.Helper()
	res := do(t, srv, http.MethodPost, "/sessions", "", map[string]any{"host_steam_id": importPlayer, "seed": seed})
	var out createSessionRes
	decodeJSON(t, res, &out)
	ev := []map[string]any{{
		"event_uuid": "rs-" + out.SessionID, "event_type": "run_start", "occurred_at": time.Now().UTC().Format(time.RFC3339),
		"player_id": importPlayer, "payload": map[string]any{"character_id": "SILENT", "player_name": "nobu"},
	}}
	if r := do(t, srv, http.MethodPost, "/sessions/"+out.SessionID+"/events", out.WriteToken, ev); r.StatusCode != http.StatusNoContent {
		t.Fatalf("post run_start: %d", r.StatusCode)
	}
	return out.SessionID
}

func historyBody(seed string, started time.Time) map[string]any {
	return map[string]any{
		"host_steam_id": importPlayer, "seed": seed, "source": "history",
		"started_at": started.UTC().Format(time.RFC3339), "player_ids": []string{importPlayer},
		"game_version": "v0.105.1",
	}
}

func postHistory(t *testing.T, srv *Server, body map[string]any) (*http.Response, createSessionRes) {
	t.Helper()
	res := do(t, srv, http.MethodPost, "/sessions", "", body)
	var out createSessionRes
	if res.StatusCode == http.StatusCreated {
		decodeJSON(t, res, &out)
	}
	return res, out
}

func TestCreateSession_History(t *testing.T) {
	started := time.Date(2026, 6, 3, 12, 14, 0, 0, time.UTC)

	t.Run("始めた日時と出どころを記録し、プレイヤーを参加者にする", func(t *testing.T) {
		srv, st := newTestServer(t)
		res, out := postHistory(t, srv, historyBody("SEED1", started))
		if res.StatusCode != http.StatusCreated {
			t.Fatalf("status=%d", res.StatusCode)
		}
		sess, err := st.GetSession(t.Context(), out.SessionID)
		if err != nil {
			t.Fatal(err)
		}
		if sess.CreatedAt != "2026-06-03T12:14:00Z" || sess.Source != "history" {
			t.Errorf("created_at=%s source=%s", sess.CreatedAt, sess.Source)
		}
		same, err := st.SameRun(t.Context(), "SEED1", started, []string{importPlayer})
		if err != nil || same != nil {
			t.Errorf("結果の無い取り込みは同じランとみなさない: same=%v err=%v", same, err)
		}
	})

	t.Run("同じランのライブのセッションがあれば 409 でその ID を返す", func(t *testing.T) {
		srv, _ := newTestServer(t)
		live := liveRun(t, srv, "SEED2")
		res := do(t, srv, http.MethodPost, "/sessions", "", historyBody("SEED2", time.Now().Add(-10*time.Minute)))
		if res.StatusCode != http.StatusConflict {
			t.Fatalf("status=%d", res.StatusCode)
		}
		var got map[string]string
		decodeJSON(t, res, &got)
		if got["session_id"] != live {
			t.Errorf("session_id=%s want %s", got["session_id"], live)
		}
	})

	t.Run("始めた日時の 1 時間より前に作られた同じシードのセッションは別のラン", func(t *testing.T) {
		srv, _ := newTestServer(t)
		liveRun(t, srv, "SEED3")
		res, _ := postHistory(t, srv, historyBody("SEED3", time.Now().Add(2*time.Hour)))
		if res.StatusCode != http.StatusCreated {
			t.Fatalf("status=%d", res.StatusCode)
		}
	})

	t.Run("参加者が重ならない同じシードのセッションは別のラン", func(t *testing.T) {
		srv, _ := newTestServer(t)
		liveRun(t, srv, "SEED4")
		body := historyBody("SEED4", time.Now().Add(-10*time.Minute))
		body["player_ids"] = []string{"76561199000000999"}
		res, _ := postHistory(t, srv, body)
		if res.StatusCode != http.StatusCreated {
			t.Fatalf("status=%d", res.StatusCode)
		}
	})

	t.Run("途中で止まった取り込みは消して作り直す", func(t *testing.T) {
		srv, st := newTestServer(t)
		_, first := postHistory(t, srv, historyBody("SEED5", started))
		res, second := postHistory(t, srv, historyBody("SEED5", started))
		if res.StatusCode != http.StatusCreated {
			t.Fatalf("status=%d", res.StatusCode)
		}
		if _, err := st.GetSession(t.Context(), first.SessionID); err == nil {
			t.Errorf("途中で止まったセッション %s が残っている", first.SessionID)
		}
		if _, err := st.GetSession(t.Context(), second.SessionID); err != nil {
			t.Errorf("作り直したセッションが無い: %v", err)
		}
	})

	t.Run("最後まで届いた取り込みは同じランとして 409", func(t *testing.T) {
		srv, _ := newTestServer(t)
		_, first := postHistory(t, srv, historyBody("SEED6", started))
		end := []map[string]any{{
			"event_uuid": "end-1", "event_type": "run_end", "occurred_at": "2026-06-03T13:00:00Z",
			"payload": map[string]any{"outcome": "death", "final_floor": 33},
		}}
		if r := do(t, srv, http.MethodPost, "/sessions/"+first.SessionID+"/events", first.WriteToken, end); r.StatusCode != http.StatusNoContent {
			t.Fatalf("post run_end: %d", r.StatusCode)
		}
		res, _ := postHistory(t, srv, historyBody("SEED6", started))
		if res.StatusCode != http.StatusConflict {
			t.Fatalf("status=%d", res.StatusCode)
		}
	})

	for name, drop := range map[string]string{"シードが無い": "seed", "始めた日時が無い": "started_at", "プレイヤーが無い": "player_ids"} {
		t.Run(name+"と 400", func(t *testing.T) {
			srv, _ := newTestServer(t)
			body := historyBody("SEED7", started)
			delete(body, drop)
			res := do(t, srv, http.MethodPost, "/sessions", "", body)
			if res.StatusCode != http.StatusBadRequest {
				b, _ := io.ReadAll(res.Body)
				t.Fatalf("status=%d body=%s", res.StatusCode, b)
			}
		})
	}
}

func TestCheckImports(t *testing.T) {
	check := func(t *testing.T, srv *Server, runs ...map[string]any) []int {
		t.Helper()
		res := do(t, srv, http.MethodPost, "/imports/check", "", map[string]any{"runs": runs})
		if res.StatusCode != http.StatusOK {
			t.Fatalf("status=%d", res.StatusCode)
		}
		var out struct{ Missing []int }
		decodeJSON(t, res, &out)
		return out.Missing
	}
	run := func(seed, outcome string) map[string]any {
		return map[string]any{
			"seed": seed, "started_at": time.Now().Add(-10 * time.Minute).UTC().Format(time.RFC3339),
			"player_ids": []string{importPlayer}, "outcome": outcome, "final_floor": 7,
		}
	}

	t.Run("サーバに無いランの番号だけを返す", func(t *testing.T) {
		srv, _ := newTestServer(t)
		liveRun(t, srv, "HAVE")
		got := check(t, srv, run("NEW1", "death"), run("HAVE", "death"), run("NEW2", "victory"))
		if len(got) != 2 || got[0] != 0 || got[1] != 2 {
			t.Errorf("missing=%v want [0 2]", got)
		}
	})

	t.Run("結果が空のセッションは送られた結果で埋める", func(t *testing.T) {
		srv, st := newTestServer(t)
		id := liveRun(t, srv, "OPEN")
		check(t, srv, run("OPEN", "abandoned"))
		sess, _ := st.GetSession(t.Context(), id)
		if sess.Outcome == nil || *sess.Outcome != "abandoned" || sess.FinalFloor == nil || *sess.FinalFloor != 7 {
			t.Errorf("outcome=%v final_floor=%v", sess.Outcome, sess.FinalFloor)
		}
	})

	t.Run("結果があるセッションは書き換えない", func(t *testing.T) {
		srv, st := newTestServer(t)
		id := liveRun(t, srv, "DONE")
		if _, err := st.FillRunOutcome(t.Context(), id, "victory", nil, "2026-06-03T13:00:00Z"); err != nil {
			t.Fatal(err)
		}
		check(t, srv, run("DONE", "abandoned"))
		sess, _ := st.GetSession(t.Context(), id)
		if *sess.Outcome != "victory" {
			t.Errorf("outcome=%s want victory", *sess.Outcome)
		}
	})

	t.Run("開始日時の形が違えば 400", func(t *testing.T) {
		srv, _ := newTestServer(t)
		bad := run("X", "death")
		bad["started_at"] = "2026/06/03"
		res := do(t, srv, http.MethodPost, "/imports/check", "", map[string]any{"runs": []any{bad}})
		if res.StatusCode != http.StatusBadRequest {
			t.Fatalf("status=%d", res.StatusCode)
		}
	})
}
