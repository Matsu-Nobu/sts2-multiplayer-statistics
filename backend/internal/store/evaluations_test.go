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
