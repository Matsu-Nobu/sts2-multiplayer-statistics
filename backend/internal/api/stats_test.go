package api

import (
	"context"
	"net/http"
	"testing"

	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/store"
)

// プレイヤーの統計の API (docs/spec/api.md「GET /api/users/{steam_id}/stats」) のテスト。

type statsPage struct {
	IsMe      bool    `json:"is_me"`
	GameCount int     `json:"game_count"`
	Character *string `json:"character"`
	Options   struct {
		Characters []struct {
			Key string `json:"key"`
		} `json:"characters"`
	} `json:"options"`
	Stats *struct {
		Games int `json:"games"`
		Wins  int `json:"wins"`
	} `json:"stats"`
}

func getStats(t *testing.T, srv *Server, userID, viewer string) (int, statsPage) {
	t.Helper()
	return getStatsQuery(t, srv, userID, viewer, "")
}

func getStatsQuery(t *testing.T, srv *Server, userID, viewer, query string) (int, statsPage) {
	t.Helper()
	res := doAs(t, srv, http.MethodGet, "/api/users/"+userID+"/stats"+query, viewer, nil, "")
	var page statsPage
	if res.StatusCode == http.StatusOK {
		decodeJSON(t, res, &page)
	}
	return res.StatusCode, page
}

// ownSummaries は hostID のゲームごとの集計を、本人が見るときの範囲で返す。
func ownSummaries(t *testing.T, srv *Server) []store.GameSummary {
	t.Helper()
	games, err := srv.Store.ListUserGameSummaries(context.Background(), hostID, false)
	if err != nil {
		t.Fatalf("summaries: %v", err)
	}
	return games
}

func snapshot(uuid string, floor, act int, rooms []map[string]any, host map[string]any) map[string]any {
	host["player_id"] = hostID
	return map[string]any{
		"event_uuid": uuid, "event_type": "floor_snapshot", "occurred_at": "2026-11-22T00:00:00Z", "floor": floor,
		"payload": map[string]any{"floor": floor, "act_index": act, "is_final": true, "rooms": rooms,
			"players": []map[string]any{host, {"player_id": friendID, "hp": map[string]any{"damage_taken": 99}}}},
	}
}

func room(roomType, id string) map[string]any {
	return map[string]any{"room_type": roomType, "model_id": id, "model_name": id + "_NAME"}
}

func card(id string, picked bool) map[string]any {
	return map[string]any{"card": map[string]any{"id": id, "name": id + "_NAME"}, "was_picked": picked}
}

func damage(uuid, pid string, amount int) map[string]any {
	return map[string]any{"event_uuid": uuid, "event_type": "damage_dealt", "occurred_at": "2026-11-22T00:00:00Z", "player_id": pid,
		"floor": 1, "combat_index": 1, "turn_number": 1, "sequence": 0, "payload": map[string]any{"amount": amount}}
}

func runEnd(uuid, outcome string, floor int) map[string]any {
	return map[string]any{"event_uuid": uuid, "event_type": "run_end", "occurred_at": "2026-11-22T01:00:00Z", "floor": floor,
		"payload": map[string]any{"outcome": outcome, "final_floor": floor}}
}

// seedFinishedRun は、hostID と friendID の 2 人のゲームを作り、hostID の記録として次の階を送ってから outcome で終える。
//
//	1 階: 通常の戦闘。カード A を取り B を見送る。最初に届いた snapshot は 2 回目で置き換わる
//	2 階: ショップ。商品のカード C は提示に数えない
//	3 階: エンシェント。休憩所の選択もここに置く
//	4 階 (Act 2): イベント。続けてエリート戦があり、ここで死ぬ。カード A を Act 2 でも取る
//	0 階: ブリッツのネオー。選択肢が無く event_choices だけがある (イベントに数えない)
func seedFinishedRun(t *testing.T, srv *Server, outcome string) (id, token string) {
	t.Helper()
	id, token = createSession(t, srv)
	runStart := func(uuid, pid, char string) map[string]any {
		return map[string]any{"event_uuid": id + uuid, "event_type": "run_start", "occurred_at": "2026-11-22T00:00:00Z", "player_id": pid, "floor": 0,
			"payload": map[string]any{"character_id": char, "character_name": char, "ascension": 3, "game_mode": "Standard"}}
	}
	events := []map[string]any{
		runStart("rs-host", hostID, "FOOL"),
		runStart("rs-friend", friendID, "REGENT"),
		snapshot(id+"f1-old", 1, 0, []map[string]any{room("Monster", "SLUGS")},
			map[string]any{"hp": map[string]any{"damage_taken": 1}, "card_choices": []any{card("OLD", true)}}),
		snapshot(id+"f1", 1, 0, []map[string]any{room("Monster", "SLUGS")},
			map[string]any{"hp": map[string]any{"damage_taken": 12}, "card_choices": []any{card("A", true), card("B", false)},
				"relics": []any{map[string]any{"id": "R1", "name": "R1_NAME"}}}),
		snapshot(id+"f2", 2, 0, []map[string]any{room("Shop", "SHOP")},
			map[string]any{"card_choices": []any{card("C", false)}, "relics": []any{map[string]any{"id": "R1"}, map[string]any{"id": "R2"}}}),
		snapshot(id+"f3", 3, 0, []map[string]any{room("Event", "NEOW")},
			map[string]any{"rest_site_choices": []any{"SMITH"}, "event_choices": []any{"BLESS_A"},
				"ancient_choices": []any{map[string]any{"title": "BLESS_A", "was_chosen": true}, map[string]any{"title": "BLESS_B", "was_chosen": false}}}),
		snapshot(id+"f0", 0, 0, []map[string]any{room("Event", "NEOW")},
			map[string]any{"event_choices": []any{"BLITZ"}}),
		snapshot(id+"f4", 4, 1, []map[string]any{room("Event", "SHRINE"), room("Elite", "LOUSE")},
			map[string]any{"hp": map[string]any{"damage_taken": 40}, "event_choices": []any{"FIGHT"}, "card_choices": []any{card("A", true)}}),
		damage(id+"d1", hostID, 30),
		damage(id+"d2", friendID, 10),
	}
	if outcome != "" {
		events = append(events, runEnd(id+"end", outcome, 4))
	}
	if res := do(t, srv, http.MethodPost, "/sessions/"+id+"/events", token, events); res.StatusCode != http.StatusNoContent {
		t.Fatalf("post events status=%d", res.StatusCode)
	}
	return id, token
}

func TestListUserGameSummaries(t *testing.T) {
	srv, _ := newTestServer(t)
	id, _ := seedFinishedRun(t, srv, "death")
	games := ownSummaries(t, srv)
	if len(games) != 1 {
		t.Fatalf("games=%+v", games)
	}
	g := games[0]

	t.Run("ゲームの結果と一緒に遊んだ人", func(t *testing.T) {
		if g.SessionID != id || g.Outcome != "death" || g.MaxActIndex != 1 || len(g.Partners) != 1 || g.Partners[0].SteamID != friendID {
			t.Fatalf("game=%+v", g)
		}
	})
	t.Run("ゲームのバージョン", func(t *testing.T) {
		if g.GameVersion == nil || *g.GameVersion != testGameVersion {
			t.Fatalf("game_version=%v", g.GameVersion)
		}
	})
	t.Run("カードの提示は階ごとに最後の snapshot から取り、ショップの階を除く", func(t *testing.T) {
		o := g.CardOffers
		if len(o) != 3 || o[0].ID != "A" || o[0].Picked != 1 || o[1].ID != "B" || o[1].Picked != 0 || o[0].Act != 0 || o[1].Act != 0 {
			t.Fatalf("card_offers=%+v", o)
		}
	})
	t.Run("同じカードでも Act が違えば分けて数える", func(t *testing.T) {
		o := g.CardOffers
		if len(o) != 3 || o[2].ID != "A" || o[2].Act != 1 || o[2].Offered != 1 || o[2].Picked != 1 {
			t.Fatalf("card_offers=%+v", o)
		}
	})
	t.Run("レリックは途中で持っていたものを 1 回ずつ", func(t *testing.T) {
		if len(g.Relics) != 2 || g.Relics[0].ID != "R1" || g.Relics[1].ID != "R2" {
			t.Fatalf("relics=%+v", g.Relics)
		}
	})
	t.Run("戦闘は自分の被ダメージを持ち、最後の階の戦闘で死んだことを示す", func(t *testing.T) {
		if len(g.Fights) != 2 || g.Fights[0].DamageTaken != 12 || g.Fights[0].Died ||
			g.Fights[1].ModelID != "LOUSE" || g.Fights[1].RoomType != "Elite" || !g.Fights[1].Died {
			t.Fatalf("fights=%+v", g.Fights)
		}
	})
	t.Run("エンシェントとネオーの階はイベントに入れない", func(t *testing.T) {
		if len(g.Ancients) != 1 || g.Ancients[0].ModelID != "NEOW" || len(g.Ancients[0].Options) != 2 || !g.Ancients[0].Options[0].Chosen {
			t.Fatalf("ancients=%+v", g.Ancients)
		}
		if len(g.Events) != 1 || g.Events[0].ModelID != "SHRINE" || g.Events[0].Choices[0] != "FIGHT" {
			t.Fatalf("events=%+v", g.Events)
		}
	})
	t.Run("休憩所の選択と与ダメージの割合", func(t *testing.T) {
		if len(g.RestChoices) != 1 || g.RestChoices[0] != "SMITH" || g.DamageShare == nil || *g.DamageShare != 0.75 {
			t.Fatalf("rest=%v share=%v", g.RestChoices, g.DamageShare)
		}
	})
}

func TestGetUserStats_OnlyVictoryAndDeath(t *testing.T) {
	srv, _ := newTestServer(t)
	seedFinishedRun(t, srv, "victory")
	seedFinishedRun(t, srv, "abandoned")
	seedFinishedRun(t, srv, "")
	_, page := getStats(t, srv, hostID, hostID)
	if page.GameCount != 1 || page.Stats == nil || page.Stats.Wins != 1 {
		t.Fatalf("page=%+v", page)
	}
}

func TestGetUserStats_Filters(t *testing.T) {
	srv, _ := newTestServer(t)
	seedFinishedRun(t, srv, "victory")
	seedFinishedRun(t, srv, "death")

	t.Run("条件が無ければ、ゲーム数の多いキャラクターのゲームを集計する", func(t *testing.T) {
		_, page := getStats(t, srv, hostID, hostID)
		if page.GameCount != 2 || page.Character == nil || *page.Character != "FOOL" || page.Stats.Games != 2 || page.Stats.Wins != 1 {
			t.Fatalf("page=%+v", page)
		}
	})
	t.Run("絞り込みの条件をクエリで受け取る", func(t *testing.T) {
		_, page := getStatsQuery(t, srv, hostID, hostID, "?players=multi&ascension=5&mode=Standard&version="+testGameVersion)
		if page.Stats == nil || page.Stats.Games != 2 {
			t.Fatalf("page=%+v", page)
		}
	})
	t.Run("条件に合うゲームが無ければ、キャラクターも集計も返さない", func(t *testing.T) {
		_, page := getStatsQuery(t, srv, hostID, hostID, "?players=solo")
		if page.GameCount != 2 || page.Character != nil || page.Stats != nil || len(page.Options.Characters) != 0 {
			t.Fatalf("page=%+v", page)
		}
	})
	t.Run("知らないキャラクターなら、先頭のキャラクターを集計する", func(t *testing.T) {
		_, page := getStatsQuery(t, srv, hostID, hostID, "?character=NOPE")
		if page.Character == nil || *page.Character != "FOOL" {
			t.Fatalf("page=%+v", page)
		}
	})
}

func TestGetUserStats_Visibility(t *testing.T) {
	srv, _ := newTestServer(t)
	seedFinishedRun(t, srv, "victory")
	hidden, _ := seedFinishedRun(t, srv, "victory")

	t.Run("非公開のユーザーページなら本人以外には 403", func(t *testing.T) {
		if code, _ := getStats(t, srv, hostID, otherID); code != http.StatusForbidden {
			t.Fatalf("status=%d", code)
		}
	})
	t.Run("公開なら、本人がページに載せていないゲームを本人以外には除く", func(t *testing.T) {
		if res := doAs(t, srv, http.MethodPatch, "/api/me", hostID, map[string]any{"profile_visibility": "public"}, ""); res.StatusCode != http.StatusNoContent {
			t.Fatalf("patch me status=%d", res.StatusCode)
		}
		if res := doAs(t, srv, http.MethodPatch, "/api/sessions/"+hidden+"/listing", hostID, map[string]any{"hidden": true}, ""); res.StatusCode != http.StatusNoContent {
			t.Fatalf("patch listing status=%d", res.StatusCode)
		}
		if _, page := getStats(t, srv, hostID, otherID); page.GameCount != 1 {
			t.Fatalf("games=%d", page.GameCount)
		}
		if _, page := getStats(t, srv, hostID, hostID); page.GameCount != 2 {
			t.Fatalf("own games=%d", page.GameCount)
		}
	})
}

func TestGetUserStats_RebuildsAfterNewEvents(t *testing.T) {
	srv, _ := newTestServer(t)
	id, token := seedFinishedRun(t, srv, "victory")
	if g := ownSummaries(t, srv); *g[0].DamageShare != 0.75 {
		t.Fatalf("share=%v", *g[0].DamageShare)
	}
	// 集計を保存した後に、同じゲームへ記録が届いたら作り直す
	if res := do(t, srv, http.MethodPost, "/sessions/"+id+"/events", token, []map[string]any{damage(id+"d3", friendID, 60)}); res.StatusCode != http.StatusNoContent {
		t.Fatalf("post status=%d", res.StatusCode)
	}
	if g := ownSummaries(t, srv); *g[0].DamageShare != 0.3 {
		t.Fatalf("share after new event=%v", *g[0].DamageShare)
	}
}

func summaryCount(t *testing.T, st *store.Store) int {
	t.Helper()
	var n int
	if err := st.DB().QueryRow(`SELECT COUNT(*) FROM game_summaries`).Scan(&n); err != nil {
		t.Fatalf("count summaries: %v", err)
	}
	return n
}

func TestPostEvents_WarmsSummariesAtRunEnd(t *testing.T) {
	cases := []struct {
		name    string
		outcome string
		want    int
	}{
		{"勝利で終わると参加者全員の集計を保存する", "victory", 2},
		{"死亡で終わると参加者全員の集計を保存する", "death", 2},
		{"放棄したゲームは集計しない", "abandoned", 0},
		{"終わっていないゲームは集計しない", "", 0},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			srv, st := newTestServer(t)
			seedFinishedRun(t, srv, c.outcome)
			srv.warming.Wait()
			if got := summaryCount(t, st); got != c.want {
				t.Fatalf("summaries=%d want %d", got, c.want)
			}
		})
	}
}

func TestWarmGameSummaries_AllGames(t *testing.T) {
	srv, st := newTestServer(t)
	seedFinishedRun(t, srv, "victory")
	seedFinishedRun(t, srv, "death")
	srv.warming.Wait()
	if _, err := st.DB().Exec(`DELETE FROM game_summaries`); err != nil {
		t.Fatal(err)
	}

	t.Run("すべての終わったゲームの参加者の集計を作る", func(t *testing.T) {
		if n, err := st.WarmGameSummaries(context.Background(), ""); err != nil || n != 4 {
			t.Fatalf("built=%d err=%v", n, err)
		}
	})
	t.Run("保存してある集計が新しければ作り直さない", func(t *testing.T) {
		if n, err := st.WarmGameSummaries(context.Background(), ""); err != nil || n != 0 {
			t.Fatalf("built=%d err=%v", n, err)
		}
	})
}
