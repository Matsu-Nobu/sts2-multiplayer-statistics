package api

import (
	"io"
	"net/http"
	"strconv"
	"strings"
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

// importBody は、importPlayer が 1 人で遊んだ、2 階で死んだランの /imports/run の本文を作る。
func importBody(seed string, started time.Time) map[string]any {
	at := started.UTC().Format(time.RFC3339)
	return map[string]any{
		"session": map[string]any{
			"host_steam_id": importPlayer, "seed": seed, "started_at": at,
			"player_ids": []string{importPlayer}, "game_version": "v0.105.1",
		},
		"events": []map[string]any{
			{"event_uuid": seed + "-rs", "event_type": "run_start", "occurred_at": at, "player_id": importPlayer,
				"payload": map[string]any{"character_id": "SILENT", "character_name": "サイレント", "player_name": "nobu", "game_mode": "Standard"}},
			{"event_uuid": seed + "-f1", "event_type": "floor_snapshot", "occurred_at": at, "floor": 1, "payload": map[string]any{"floor": 1}},
			{"event_uuid": seed + "-f2", "event_type": "floor_snapshot", "occurred_at": at, "floor": 2, "payload": map[string]any{"floor": 2}},
			{"event_uuid": seed + "-end", "event_type": "run_end", "occurred_at": at, "floor": 2,
				"payload": map[string]any{"outcome": "death", "final_floor": 2}},
		},
	}
}

func importRun(t *testing.T, srv *Server, body map[string]any) (*http.Response, map[string]string) {
	t.Helper()
	res := do(t, srv, http.MethodPost, "/imports/run", "", body)
	out := map[string]string{}
	if res.StatusCode == http.StatusCreated || res.StatusCode == http.StatusConflict {
		decodeJSON(t, res, &out)
	}
	return res, out
}

func events(body map[string]any) []map[string]any { return body["events"].([]map[string]any) }

func TestImportRun(t *testing.T) {
	started := time.Date(2026, 6, 3, 12, 14, 0, 0, time.UTC)

	t.Run("セッション・参加者・記録・結果を書き込む", func(t *testing.T) {
		srv, st := newTestServer(t)
		res, out := importRun(t, srv, importBody("SEED1", started))
		if res.StatusCode != http.StatusCreated {
			t.Fatalf("status=%d", res.StatusCode)
		}
		sess, err := st.GetSession(t.Context(), out["session_id"])
		if err != nil {
			t.Fatal(err)
		}
		if sess.CreatedAt != "2026-06-03T12:14:00Z" || sess.Source != "history" || *sess.Outcome != "death" || *sess.FinalFloor != 2 {
			t.Errorf("created_at=%s source=%s outcome=%v final_floor=%v", sess.CreatedAt, sess.Source, sess.Outcome, sess.FinalFloor)
		}
		evs, _ := st.ListEvents(t.Context(), sess.ID)
		if len(evs) != 4 {
			t.Errorf("events=%d want 4", len(evs))
		}
		runs, _ := st.ListUserRuns(t.Context(), importPlayer, false)
		if len(runs) != 1 || runs[0].CharacterID == nil || *runs[0].CharacterID != "SILENT" {
			t.Errorf("runs=%+v", runs)
		}
		players, _ := st.ListPlayersForSession(t.Context(), sess.ID)
		if len(players) != 1 || players[0].DisplayName != "nobu" {
			t.Errorf("players=%+v", players)
		}
	})

	t.Run("名前が空ならプレイヤー名を書き換えない", func(t *testing.T) {
		srv, st := newTestServer(t)
		liveRun(t, srv, "OTHER")
		body := importBody("SEED2", started)
		events(body)[0]["payload"].(map[string]any)["player_name"] = ""
		res, out := importRun(t, srv, body)
		if res.StatusCode != http.StatusCreated {
			t.Fatalf("status=%d", res.StatusCode)
		}
		players, _ := st.ListPlayersForSession(t.Context(), out["session_id"])
		if len(players) != 1 || players[0].DisplayName != "nobu" {
			t.Errorf("players=%+v", players)
		}
	})

	t.Run("同じランのライブのセッションがあれば 409 でその ID を返す", func(t *testing.T) {
		srv, _ := newTestServer(t)
		live := liveRun(t, srv, "SEED3")
		res, out := importRun(t, srv, importBody("SEED3", time.Now().Add(-10*time.Minute)))
		if res.StatusCode != http.StatusConflict || out["session_id"] != live {
			t.Fatalf("status=%d session_id=%s want %s", res.StatusCode, out["session_id"], live)
		}
	})

	t.Run("同じランを 2 回送ると 2 回目は 409", func(t *testing.T) {
		srv, _ := newTestServer(t)
		_, first := importRun(t, srv, importBody("SEED4", started))
		body := importBody("SEED4", started)
		for _, ev := range events(body) {
			ev["event_uuid"] = ev["event_uuid"].(string) + "-again"
		}
		res, out := importRun(t, srv, body)
		if res.StatusCode != http.StatusConflict || out["session_id"] != first["session_id"] {
			t.Fatalf("status=%d", res.StatusCode)
		}
	})

	t.Run("始めた日時の 1 時間より前に作られた同じシードのセッションは別のラン", func(t *testing.T) {
		srv, _ := newTestServer(t)
		liveRun(t, srv, "SEED5")
		res, _ := importRun(t, srv, importBody("SEED5", time.Now().Add(2*time.Hour)))
		if res.StatusCode != http.StatusCreated {
			t.Fatalf("status=%d", res.StatusCode)
		}
	})

	t.Run("参加者が重ならない同じシードのセッションは別のラン", func(t *testing.T) {
		srv, _ := newTestServer(t)
		liveRun(t, srv, "SEED6")
		body := importBody("SEED6", time.Now().Add(-10*time.Minute))
		other := "76561199000000999"
		body["session"].(map[string]any)["player_ids"] = []string{other}
		events(body)[0]["player_id"] = other
		res, _ := importRun(t, srv, body)
		if res.StatusCode != http.StatusCreated {
			t.Fatalf("status=%d", res.StatusCode)
		}
	})

	bad := map[string]func(body map[string]any){
		"シードが無い":   func(b map[string]any) { delete(b["session"].(map[string]any), "seed") },
		"始めた日時が無い": func(b map[string]any) { delete(b["session"].(map[string]any), "started_at") },
		"プレイヤーが無い": func(b map[string]any) { delete(b["session"].(map[string]any), "player_ids") },
		"プレイヤーが 5 人": func(b map[string]any) {
			b["session"].(map[string]any)["player_ids"] = []string{"1", "2", "3", "4", "5"}
		},
		"参加者でない人の開始の記録": func(b map[string]any) { events(b)[0]["player_id"] = "76561199000000999" },
		"戦闘の記録":         func(b map[string]any) { events(b)[1]["event_type"] = "damage_dealt" },
		"終わりの記録が無い":     func(b map[string]any) { b["events"] = events(b)[:3] },
		"終わりの記録が最後でない":  func(b map[string]any) { e := events(b); b["events"] = []map[string]any{e[0], e[3], e[1], e[2]} },
		"放棄したラン": func(b map[string]any) {
			events(b)[3]["payload"] = map[string]any{"outcome": "abandoned", "final_floor": 2}
		},
		"階が 101": func(b map[string]any) {
			e := events(b)
			list := []map[string]any{e[0]}
			for i := 1; i <= 101; i++ {
				list = append(list, map[string]any{"event_uuid": "f" + strconv.Itoa(i), "event_type": "floor_snapshot", "occurred_at": e[0]["occurred_at"], "payload": map[string]any{"floor": i}})
			}
			b["events"] = append(list, e[3])
		},
	}
	for name, change := range bad {
		t.Run(name+"と 400 で何も書かない", func(t *testing.T) {
			srv, st := newTestServer(t)
			body := importBody("BAD", started)
			change(body)
			res := do(t, srv, http.MethodPost, "/imports/run", "", body)
			if res.StatusCode != http.StatusBadRequest {
				b, _ := io.ReadAll(res.Body)
				t.Fatalf("status=%d body=%s", res.StatusCode, b)
			}
			if runs, _ := st.ListUserRuns(t.Context(), importPlayer, false); len(runs) != 0 {
				t.Errorf("runs=%d", len(runs))
			}
		})
	}

	t.Run("階が 100 なら受け付ける", func(t *testing.T) {
		srv, _ := newTestServer(t)
		body := importBody("MAX", started)
		e := events(body)
		list := []map[string]any{e[0]}
		for i := 1; i <= 100; i++ {
			list = append(list, map[string]any{"event_uuid": "f" + strconv.Itoa(i), "event_type": "floor_snapshot", "occurred_at": e[0]["occurred_at"], "payload": map[string]any{"floor": i}})
		}
		body["events"] = append(list, e[3])
		res := do(t, srv, http.MethodPost, "/imports/run", "", body)
		if res.StatusCode != http.StatusCreated {
			t.Fatalf("status=%d", res.StatusCode)
		}
	})

	t.Run("本文が 512KB を超えると 400", func(t *testing.T) {
		srv, _ := newTestServer(t)
		body := importBody("BIG", started)
		events(body)[1]["payload"] = map[string]any{"pad": strings.Repeat("x", 600*1024)}
		res := do(t, srv, http.MethodPost, "/imports/run", "", body)
		if res.StatusCode != http.StatusBadRequest {
			t.Fatalf("status=%d", res.StatusCode)
		}
	})
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
