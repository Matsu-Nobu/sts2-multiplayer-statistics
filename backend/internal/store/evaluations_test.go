package store

import (
	"context"
	"encoding/json"
	"reflect"
	"testing"
	"time"
)

func offerInput(session, uuid string, floor int, payload string) EventInput {
	player := "76561190000000001"
	return EventInput{
		EventUUID: uuid, SessionID: session, PlayerID: &player, EventType: "card_reward_offered",
		OccurredAt: "2026-11-26T00:00:00Z", Floor: &floor, Payload: json.RawMessage(payload),
	}
}

func mustInsert(t *testing.T, st *Store, in EventInput) {
	t.Helper()
	if _, err := st.InsertEvent(context.Background(), in); err != nil {
		t.Fatalf("insert %s: %v", in.EventUUID, err)
	}
}

func TestCardRewardOffers(t *testing.T) {
	ctx := context.Background()

	t.Run("評価していない event があれば、評価済みも含めて保存した順に全件を返す", func(t *testing.T) {
		st := openTestStore(t, "s1", "s2")
		mustInsert(t, st, offerInput("s1", "a", 2, `{"n":1}`))
		mustInsert(t, st, offerInput("s2", "other-session", 2, `{"n":9}`))
		mustInsert(t, st, snapshotInput("s1", "snap", "2026-11-26T00:00:00Z", 2))
		mustInsert(t, st, offerInput("s1", "b", 3, `{"n":2}`))
		_, first, err := st.CardRewardOffers(ctx, "s1")
		if err != nil {
			t.Fatal(err)
		}
		if err := st.InsertCardRewardEvaluations(ctx, []CardRewardEvaluation{{EventID: first[0].ID, Status: "ok"}}, time.Now()); err != nil {
			t.Fatal(err)
		}

		_, offers, err := st.CardRewardOffers(ctx, "s1")
		if err != nil {
			t.Fatal(err)
		}
		if len(offers) != 2 || string(offers[0].Payload) != `{"n":1}` || string(offers[1].Payload) != `{"n":2}` {
			t.Fatalf("offers = %+v", offers)
		}
		if *offers[1].Floor != 3 || *offers[1].PlayerID != "76561190000000001" {
			t.Errorf("floor/player = %v %v", *offers[1].Floor, *offers[1].PlayerID)
		}
	})

	t.Run("すべて評価済みなら何も返さない", func(t *testing.T) {
		st := openTestStore(t, "s1")
		mustInsert(t, st, offerInput("s1", "a", 2, `{}`))
		_, offers, _ := st.CardRewardOffers(ctx, "s1")
		if err := st.InsertCardRewardEvaluations(ctx, []CardRewardEvaluation{{EventID: offers[0].ID, Status: "ok"}}, time.Now()); err != nil {
			t.Fatal(err)
		}
		_, offers, err := st.CardRewardOffers(ctx, "s1")
		if err != nil || offers != nil {
			t.Fatalf("offers = %+v, err = %v", offers, err)
		}
	})

	t.Run("セッションのゲームのバージョンを返す", func(t *testing.T) {
		st := openTestStore(t)
		v := "v0.114.0"
		if err := st.CreateSession(ctx, CreateSessionInput{ID: "s1", WriteToken: "t", GameVersion: &v}); err != nil {
			t.Fatal(err)
		}
		mustInsert(t, st, offerInput("s1", "a", 2, `{}`))
		got, _, err := st.CardRewardOffers(ctx, "s1")
		if err != nil || got == nil || *got != v {
			t.Fatalf("game version = %v, err = %v", got, err)
		}
	})
}

func TestInsertCardRewardEvaluations(t *testing.T) {
	ctx := context.Background()
	st := openTestStore(t, "s1")
	mustInsert(t, st, offerInput("s1", "a", 2, `{}`))
	mustInsert(t, st, offerInput("s1", "b", 2, `{}`))
	_, offers, _ := st.CardRewardOffers(ctx, "s1")
	model, value := "fool_ema_v24181", -0.32
	at := time.Date(2026, 11, 26, 12, 0, 0, 0, time.UTC)
	err := st.InsertCardRewardEvaluations(ctx, []CardRewardEvaluation{
		{EventID: offers[0].ID, Status: "ok", Model: &model, Value: &value,
			CardProbs: json.RawMessage(`[0.1,0.9]`), AlternativeProbs: json.RawMessage(`{"Skip":0}`)},
		{EventID: offers[1].ID, Status: "unknown_card", Model: &model},
	}, at)
	if err != nil {
		t.Fatal(err)
	}
	// 保存済みの結果は上書きしない
	if err := st.InsertCardRewardEvaluations(ctx, []CardRewardEvaluation{{EventID: offers[0].ID, Status: "unsupported_version"}}, at); err != nil {
		t.Fatal(err)
	}

	type row struct {
		Status                  string
		Model                   *string
		Value                   *float64
		CardProbs, AltProbs, At *string
	}
	read := func(id int64) row {
		var r row
		err := st.DB().QueryRow(`SELECT status, model, value, card_probs_json, alternative_probs_json, evaluated_at
			FROM card_reward_evaluations WHERE event_id = ?`, id).Scan(&r.Status, &r.Model, &r.Value, &r.CardProbs, &r.AltProbs, &r.At)
		if err != nil {
			t.Fatal(err)
		}
		return r
	}
	s := func(v string) *string { return &v }
	if got, want := read(offers[0].ID), (row{"ok", &model, &value, s(`[0.1,0.9]`), s(`{"Skip":0}`), s("2026-11-26T12:00:00Z")}); !reflect.DeepEqual(got, want) {
		t.Errorf("ok row = %+v", got)
	}
	if got, want := read(offers[1].ID), (row{"unknown_card", &model, nil, nil, nil, s("2026-11-26T12:00:00Z")}); !reflect.DeepEqual(got, want) {
		t.Errorf("unknown_card row = %+v", got)
	}

	// セッションを消すと、評価も消える
	if _, err := st.DB().Exec(`DELETE FROM sessions WHERE id = 's1'`); err != nil {
		t.Fatal(err)
	}
	var n int
	if err := st.DB().QueryRow(`SELECT COUNT(*) FROM card_reward_evaluations`).Scan(&n); err != nil || n != 0 {
		t.Fatalf("evaluations left = %d, err = %v", n, err)
	}
}

func TestListCardRewardEvaluations(t *testing.T) {
	ctx := context.Background()
	st := openTestStore(t, "s1", "s2")
	mustInsert(t, st, offerInput("s1", "a", 2, `{"offered":[
		{"card_id":"CHO_BARBS","upgrade_level":0,"cost":1,"vars":{}},
		{"card_id":"TWIN_SHOT","upgrade_level":1,"enchantment":{"id":"SHARP","amount":2,"status":"Normal"}}],
		"alternatives":["Skip","REROLL"],"deck":[{"card_id":"STRIKE_FOOL"}]}`))
	mustInsert(t, st, offerInput("s1", "b", 3, `{"offered":[{"card_id":"OPTIMISM","upgrade_level":0}],"alternatives":["Skip"]}`))
	mustInsert(t, st, offerInput("s1", "not-evaluated", 4, `{"offered":[],"alternatives":[]}`))
	mustInsert(t, st, offerInput("s1", "broken", 5, `{"offered":"not an array","alternatives":7}`))
	mustInsert(t, st, offerInput("s2", "other-session", 2, `{"offered":[],"alternatives":[]}`))
	_, s1, _ := st.CardRewardOffers(ctx, "s1")
	_, s2, _ := st.CardRewardOffers(ctx, "s2")
	model, value := "fool_ema_v24181", -0.3
	err := st.InsertCardRewardEvaluations(ctx, []CardRewardEvaluation{
		{EventID: s1[0].ID, Status: "ok", Model: &model, Value: &value,
			CardProbs: json.RawMessage(`[0.25,0.7]`), AlternativeProbs: json.RawMessage(`{"Skip":0.04,"REROLL":0.01}`)},
		{EventID: s1[1].ID, Status: "unknown_card", Model: &model},
		{EventID: s1[3].ID, Status: "invalid_payload"},
		{EventID: s2[0].ID, Status: "ok"},
	}, time.Now())
	if err != nil {
		t.Fatal(err)
	}

	got, err := st.ListCardRewardEvaluations(ctx, "s1")
	if err != nil {
		t.Fatal(err)
	}
	raw, _ := json.Marshal(got)
	var decoded []map[string]any
	_ = json.Unmarshal(raw, &decoded)
	want := []map[string]any{
		{"event_id": float64(s1[0].ID), "player_id": "76561190000000001", "floor": float64(2),
			"offered": []any{
				map[string]any{"card_id": "CHO_BARBS", "upgrade_level": float64(0), "enchanted": false},
				map[string]any{"card_id": "TWIN_SHOT", "upgrade_level": float64(1), "enchanted": true}},
			"alternatives": []any{"Skip", "REROLL"}, "status": "ok",
			"card_probs": []any{0.25, 0.7}, "alternative_probs": map[string]any{"Skip": 0.04, "REROLL": 0.01}},
		{"event_id": float64(s1[1].ID), "player_id": "76561190000000001", "floor": float64(3),
			"offered":      []any{map[string]any{"card_id": "OPTIMISM", "upgrade_level": float64(0), "enchanted": false}},
			"alternatives": []any{"Skip"}, "status": "unknown_card"},
		// mod のバージョン違いで payload の形が読めない event は、提示されたカードと選択肢を空にして status だけを返す
		{"event_id": float64(s1[3].ID), "player_id": "76561190000000001", "floor": float64(5),
			"offered": []any{}, "alternatives": []any{}, "status": "invalid_payload"},
	}
	if !reflect.DeepEqual(decoded, want) {
		t.Fatalf("got  %s\nwant %v", raw, want)
	}

	t.Run("評価が無いセッションは空の配列", func(t *testing.T) {
		st := openTestStore(t, "s3")
		got, err := st.ListCardRewardEvaluations(ctx, "s3")
		if err != nil || got == nil || len(got) != 0 {
			t.Fatalf("got %v, err %v", got, err)
		}
	})
}
