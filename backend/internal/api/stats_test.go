package api

import (
	"net/http"
	"testing"
)

// プレイヤーの統計の API (docs/spec/api.md「GET /api/users/{steam_id}/stats」) のテスト。

type statsGame struct {
	SessionID   string   `json:"session_id"`
	Outcome     string   `json:"outcome"`
	MaxActIndex int      `json:"max_act_index"`
	DamageShare *float64 `json:"damage_share"`
	Partners    []struct {
		SteamID string `json:"steam_id"`
	} `json:"partners"`
	CardOffers []struct {
		ID      string `json:"id"`
		Offered int    `json:"offered"`
		Picked  int    `json:"picked"`
	} `json:"card_offers"`
	Relics []struct {
		ID string `json:"id"`
	} `json:"relics"`
	Fights []struct {
		ModelID     string `json:"model_id"`
		RoomType    string `json:"room_type"`
		DamageTaken int    `json:"damage_taken"`
		Died        bool   `json:"died"`
	} `json:"fights"`
	RestChoices []string `json:"rest_choices"`
	Ancients    []struct {
		ModelID string `json:"model_id"`
		Options []struct {
			Title  string `json:"title"`
			Chosen bool   `json:"chosen"`
		} `json:"options"`
	} `json:"ancients"`
	Events []struct {
		ModelID string   `json:"model_id"`
		Choices []string `json:"choices"`
	} `json:"events"`
}

type statsPage struct {
	IsMe  bool        `json:"is_me"`
	Games []statsGame `json:"games"`
}

func getStats(t *testing.T, srv *Server, userID, viewer string) (int, statsPage) {
	t.Helper()
	res := doAs(t, srv, http.MethodGet, "/api/users/"+userID+"/stats", viewer, nil, "")
	var page statsPage
	if res.StatusCode == http.StatusOK {
		decodeJSON(t, res, &page)
	}
	return res.StatusCode, page
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
//	4 階 (Act 2): イベント。続けてエリート戦があり、ここで死ぬ
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
			map[string]any{"hp": map[string]any{"damage_taken": 40}, "event_choices": []any{"FIGHT"}}),
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

func TestGetUserStats_Summary(t *testing.T) {
	srv, _ := newTestServer(t)
	id, _ := seedFinishedRun(t, srv, "death")
	code, page := getStats(t, srv, hostID, hostID)
	if code != http.StatusOK || !page.IsMe || len(page.Games) != 1 {
		t.Fatalf("status=%d page=%+v", code, page)
	}
	g := page.Games[0]

	t.Run("ゲームの結果と一緒に遊んだ人", func(t *testing.T) {
		if g.SessionID != id || g.Outcome != "death" || g.MaxActIndex != 1 || len(g.Partners) != 1 || g.Partners[0].SteamID != friendID {
			t.Fatalf("game=%+v", g)
		}
	})
	t.Run("カードの提示は階ごとに最後の snapshot から取り、ショップの階を除く", func(t *testing.T) {
		if len(g.CardOffers) != 2 || g.CardOffers[0].ID != "A" || g.CardOffers[0].Picked != 1 || g.CardOffers[1].ID != "B" || g.CardOffers[1].Picked != 0 {
			t.Fatalf("card_offers=%+v", g.CardOffers)
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
	if len(page.Games) != 1 || page.Games[0].Outcome != "victory" {
		t.Fatalf("games=%+v", page.Games)
	}
}

func TestGetUserStats_Visibility(t *testing.T) {
	srv, _ := newTestServer(t)
	listed, _ := seedFinishedRun(t, srv, "victory")
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
		if _, page := getStats(t, srv, hostID, otherID); len(page.Games) != 1 || page.Games[0].SessionID != listed {
			t.Fatalf("games=%+v", page.Games)
		}
		if _, page := getStats(t, srv, hostID, hostID); len(page.Games) != 2 {
			t.Fatalf("own games=%d", len(page.Games))
		}
	})
}

func TestGetUserStats_RebuildsAfterNewEvents(t *testing.T) {
	srv, _ := newTestServer(t)
	id, token := seedFinishedRun(t, srv, "victory")
	if _, page := getStats(t, srv, hostID, hostID); *page.Games[0].DamageShare != 0.75 {
		t.Fatalf("share=%v", *page.Games[0].DamageShare)
	}
	// 集計を保存した後に、同じゲームへ記録が届いたら作り直す
	if res := do(t, srv, http.MethodPost, "/sessions/"+id+"/events", token, []map[string]any{damage(id+"d3", friendID, 60)}); res.StatusCode != http.StatusNoContent {
		t.Fatalf("post status=%d", res.StatusCode)
	}
	if _, page := getStats(t, srv, hostID, hostID); *page.Games[0].DamageShare != 0.3 {
		t.Fatalf("share after new event=%v", *page.Games[0].DamageShare)
	}
}
