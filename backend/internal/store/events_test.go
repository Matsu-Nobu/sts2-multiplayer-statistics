package store

import (
	"context"
	"encoding/json"
	"fmt"
	"reflect"
	"sort"
	"testing"
	"time"
)

func openTestStore(t *testing.T, sessionIDs ...string) *Store {
	t.Helper()
	st, err := Open(":memory:")
	if err != nil {
		t.Fatalf("open store: %v", err)
	}
	t.Cleanup(func() { _ = st.Close() })
	for _, id := range sessionIDs {
		if err := st.CreateSession(context.Background(), CreateSessionInput{ID: id, WriteToken: "token-" + id}); err != nil {
			t.Fatalf("create session: %v", err)
		}
	}
	return st
}

func snapshotInput(session, uuid, occurredAt string, floor int) EventInput {
	return EventInput{
		EventUUID: uuid, SessionID: session, EventType: "floor_snapshot", OccurredAt: occurredAt, Floor: &floor,
		Payload: json.RawMessage(fmt.Sprintf(`{"floor":%d,"players":[]}`, floor)),
	}
}

func storedUUIDs(t *testing.T, st *Store, session string) []string {
	t.Helper()
	rows, err := st.DB().Query(`SELECT event_uuid FROM events WHERE session_id = ?`, session)
	if err != nil {
		t.Fatalf("query: %v", err)
	}
	defer rows.Close()
	out := []string{}
	for rows.Next() {
		var u string
		if err := rows.Scan(&u); err != nil {
			t.Fatalf("scan: %v", err)
		}
		out = append(out, u)
	}
	sort.Strings(out)
	return out
}

func TestInsertEvent(t *testing.T) {
	ctx := context.Background()

	t.Run("floor_snapshot", func(t *testing.T) {
		cases := []struct {
			name   string
			events []EventInput
			want   map[string][]string
		}{
			{
				name: "同じ階に後から届いた新しい記録が、古い記録を置き換える",
				events: []EventInput{
					snapshotInput("s1", "old", "2026-11-26T00:00:01Z", 1),
					snapshotInput("s1", "new", "2026-11-26T00:00:02Z", 1),
				},
				want: map[string][]string{"s1": {"new"}},
			},
			{
				name: "既にある記録より前の記録が後から届いたら、届いた記録を残さない",
				events: []EventInput{
					snapshotInput("s1", "new", "2026-11-26T00:00:02Z", 1),
					snapshotInput("s1", "old", "2026-11-26T00:00:01Z", 1),
				},
				want: map[string][]string{"s1": {"new"}},
			},
			{
				name: "occurred_at が同じなら、後に届いた記録を残す",
				events: []EventInput{
					snapshotInput("s1", "first", "2026-11-26T00:00:01Z", 1),
					snapshotInput("s1", "second", "2026-11-26T00:00:01Z", 1),
				},
				want: map[string][]string{"s1": {"second"}},
			},
			{
				name: "消した記録がもう一度届いても、残さない",
				events: []EventInput{
					snapshotInput("s1", "old", "2026-11-26T00:00:01Z", 1),
					snapshotInput("s1", "new", "2026-11-26T00:00:02Z", 1),
					snapshotInput("s1", "old", "2026-11-26T00:00:01Z", 1),
				},
				want: map[string][]string{"s1": {"new"}},
			},
			{
				name: "別の階と別のセッションの記録は消さない",
				events: []EventInput{
					snapshotInput("s1", "s1-f1", "2026-11-26T00:00:01Z", 1),
					snapshotInput("s1", "s1-f2", "2026-11-26T00:00:02Z", 2),
					snapshotInput("s2", "s2-f1", "2026-11-26T00:00:03Z", 1),
				},
				want: map[string][]string{"s1": {"s1-f1", "s1-f2"}, "s2": {"s2-f1"}},
			},
		}
		for _, c := range cases {
			t.Run(c.name, func(t *testing.T) {
				st := openTestStore(t, "s1", "s2")
				for _, ev := range c.events {
					if _, err := st.InsertEvent(ctx, ev); err != nil {
						t.Fatalf("insert %s: %v", ev.EventUUID, err)
					}
				}
				for session, want := range c.want {
					if got := storedUUIDs(t, st, session); !reflect.DeepEqual(got, want) {
						t.Errorf("session %s: events = %v, want %v", session, got, want)
					}
				}
			})
		}
	})

	t.Run("floor_snapshot 以外の記録は、同じ階でもすべて残す", func(t *testing.T) {
		st := openTestStore(t, "s1")
		for _, u := range []string{"played-1", "played-2"} {
			floor := 1
			ev := EventInput{EventUUID: u, SessionID: "s1", EventType: "card_played", OccurredAt: "2026-11-26T00:00:01Z",
				Floor: &floor, Payload: json.RawMessage(`{"floor":1}`)}
			if _, err := st.InsertEvent(ctx, ev); err != nil {
				t.Fatalf("insert: %v", err)
			}
		}
		if got, want := storedUUIDs(t, st, "s1"), []string{"played-1", "played-2"}; !reflect.DeepEqual(got, want) {
			t.Errorf("events = %v, want %v", got, want)
		}
	})
}

func TestComputeSessionETag(t *testing.T) {
	ctx := context.Background()
	etag := func(t *testing.T, st *Store) string {
		t.Helper()
		e, err := st.ComputeSessionETag(ctx, "s1")
		if err != nil {
			t.Fatalf("etag: %v", err)
		}
		return e
	}

	t.Run("floor_snapshot を置き換えたら変わる", func(t *testing.T) {
		st := openTestStore(t, "s1")
		// 置き換えでは件数が変わらず、同じ秒に届けば received_at の最大値も変わらない。
		// received_at をそろえて、件数と received_at では区別できない 2 つの状態を作る。
		insertAtSameSecond := func(ev EventInput) {
			t.Helper()
			if _, err := st.InsertEvent(ctx, ev); err != nil {
				t.Fatalf("insert: %v", err)
			}
			if _, err := st.DB().Exec(`UPDATE events SET received_at = '2026-11-26T00:00:00Z'`); err != nil {
				t.Fatalf("update: %v", err)
			}
		}
		insertAtSameSecond(snapshotInput("s1", "old", "2026-11-26T00:00:01Z", 1))
		before := etag(t, st)
		insertAtSameSecond(snapshotInput("s1", "new", "2026-11-26T00:00:02Z", 1))
		if after := etag(t, st); before == after {
			t.Errorf("ETag did not change after the snapshot was replaced: %s", after)
		}
	})

	t.Run("カード報酬の評価を保存したら変わる", func(t *testing.T) {
		st := openTestStore(t, "s1")
		mustInsert(t, st, offerInput("s1", "a", 2, `{}`))
		_, offers, _ := st.CardRewardOffers(ctx, "s1")
		before := etag(t, st)
		if err := st.InsertCardRewardEvaluations(ctx, []CardRewardEvaluation{{EventID: offers[0].ID, Status: "ok"}}, time.Now()); err != nil {
			t.Fatal(err)
		}
		if after := etag(t, st); before == after {
			t.Errorf("ETag did not change after an evaluation was stored: %s", after)
		}
	})
}

func TestMigrationDedupFloorSnapshots(t *testing.T) {
	st := openTestStore(t, "s1", "s2")
	// 置き換えを入れる前に保存された重複を、InsertEvent を通さずに作る
	rows := []struct {
		uuid, session, etype, occurredAt string
		floor                            int
	}{
		{"s1-f1-old", "s1", "floor_snapshot", "2026-11-26T00:00:01Z", 1},
		{"s1-f1-new", "s1", "floor_snapshot", "2026-11-26T00:00:02Z", 1},
		{"s1-f2-first", "s1", "floor_snapshot", "2026-11-26T00:00:03Z", 2},
		{"s1-f2-second", "s1", "floor_snapshot", "2026-11-26T00:00:03Z", 2},
		{"s1-played-1", "s1", "card_played", "2026-11-26T00:00:01Z", 1},
		{"s1-played-2", "s1", "card_played", "2026-11-26T00:00:01Z", 1},
		{"s2-f1", "s2", "floor_snapshot", "2026-11-26T00:00:01Z", 1},
	}
	for _, r := range rows {
		if _, err := st.DB().Exec(`
			INSERT INTO events (event_uuid, session_id, event_type, occurred_at, received_at, floor, payload_json)
			VALUES (?, ?, ?, ?, ?, ?, ?)`,
			r.uuid, r.session, r.etype, r.occurredAt, r.occurredAt, r.floor, fmt.Sprintf(`{"floor":%d}`, r.floor)); err != nil {
			t.Fatalf("insert %s: %v", r.uuid, err)
		}
	}
	body, err := migrationsFS.ReadFile("migrations/011_dedup_floor_snapshots.sql")
	if err != nil {
		t.Fatalf("read migration: %v", err)
	}
	if _, err := st.DB().Exec(string(body)); err != nil {
		t.Fatalf("apply migration: %v", err)
	}
	if got, want := storedUUIDs(t, st, "s1"), []string{"s1-f1-new", "s1-f2-second", "s1-played-1", "s1-played-2"}; !reflect.DeepEqual(got, want) {
		t.Errorf("s1 events = %v, want %v", got, want)
	}
	if got, want := storedUUIDs(t, st, "s2"), []string{"s2-f1"}; !reflect.DeepEqual(got, want) {
		t.Errorf("s2 events = %v, want %v", got, want)
	}
}
