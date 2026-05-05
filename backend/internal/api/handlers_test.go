package api

import (
	"bytes"
	"encoding/json"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/store"
)

func newTestServer(t *testing.T) (*Server, *store.Store) {
	t.Helper()
	st, err := store.Open(":memory:")
	if err != nil {
		t.Fatalf("open store: %v", err)
	}
	t.Cleanup(func() { _ = st.Close() })
	return &Server{Store: st, BaseURL: "http://test"}, st
}

func do(t *testing.T, srv *Server, method, path, token string, body any) *http.Response {
	t.Helper()
	var buf io.Reader
	if body != nil {
		raw, err := json.Marshal(body)
		if err != nil {
			t.Fatalf("marshal: %v", err)
		}
		buf = bytes.NewReader(raw)
	}
	req := httptest.NewRequest(method, path, buf)
	if token != "" {
		req.Header.Set("Authorization", "Bearer "+token)
	}
	req.Header.Set("Content-Type", "application/json")
	rec := httptest.NewRecorder()
	srv.Routes().ServeHTTP(rec, req)
	return rec.Result()
}

func decodeJSON(t *testing.T, r *http.Response, into any) {
	t.Helper()
	defer r.Body.Close()
	if err := json.NewDecoder(r.Body).Decode(into); err != nil {
		t.Fatalf("decode response: %v", err)
	}
}

func createSession(t *testing.T, srv *Server) (id, token string) {
	t.Helper()
	res := do(t, srv, http.MethodPost, "/sessions", "", map[string]any{
		"host_name":     "Nobu",
		"host_steam_id": "76561199000000000",
		"character_id":  "IRONCLAD",
		"ascension":     5,
	})
	if res.StatusCode != http.StatusCreated {
		body, _ := io.ReadAll(res.Body)
		t.Fatalf("createSession status=%d body=%s", res.StatusCode, body)
	}
	var out createSessionRes
	decodeJSON(t, res, &out)
	if out.SessionID == "" || out.WriteToken == "" {
		t.Fatalf("missing fields in response: %+v", out)
	}
	if !strings.HasPrefix(out.ShareURL, "http://test/s/") {
		t.Errorf("unexpected share_url: %s", out.ShareURL)
	}
	return out.SessionID, out.WriteToken
}

func TestHealthz(t *testing.T) {
	srv, _ := newTestServer(t)
	res := do(t, srv, http.MethodGet, "/healthz", "", nil)
	if res.StatusCode != 200 {
		t.Fatalf("status=%d", res.StatusCode)
	}
}

func TestCreateSession(t *testing.T) {
	srv, _ := newTestServer(t)
	createSession(t, srv)
}

func TestPostTurn_Auth(t *testing.T) {
	srv, _ := newTestServer(t)
	id, _ := createSession(t, srv)

	body := turnFixture(1, 1, false)

	// without token
	res := do(t, srv, http.MethodPost, "/sessions/"+id+"/turns", "", body)
	if res.StatusCode != http.StatusUnauthorized {
		t.Fatalf("expected 401, got %d", res.StatusCode)
	}

	// wrong token
	res = do(t, srv, http.MethodPost, "/sessions/"+id+"/turns", "wrong", body)
	if res.StatusCode != http.StatusUnauthorized {
		t.Fatalf("expected 401, got %d", res.StatusCode)
	}

	// unknown session
	res = do(t, srv, http.MethodPost, "/sessions/unknown/turns", "any", body)
	if res.StatusCode != http.StatusNotFound {
		t.Fatalf("expected 404, got %d", res.StatusCode)
	}
}

func TestPostTurn_Idempotent(t *testing.T) {
	srv, _ := newTestServer(t)
	id, token := createSession(t, srv)

	for i := 0; i < 3; i++ {
		res := do(t, srv, http.MethodPost, "/sessions/"+id+"/turns", token, turnFixture(1, 1, false))
		if res.StatusCode != http.StatusNoContent {
			t.Fatalf("status=%d", res.StatusCode)
		}
	}
	// only 1 row
	res := do(t, srv, http.MethodGet, "/api/sessions/"+id, "", nil)
	var doc sessionDoc
	decodeJSON(t, res, &doc)
	if len(doc.Turns) != 1 {
		t.Fatalf("expected 1 turn, got %d", len(doc.Turns))
	}
}

func TestPostEvents_BulkAndIdempotent(t *testing.T) {
	srv, _ := newTestServer(t)
	id, token := createSession(t, srv)

	events := []map[string]any{
		{
			"event_uuid":  "uuid-1",
			"event_type":  "run_start",
			"occurred_at": "2026-05-05T00:00:00Z",
			"player_id":   "76561199000000000",
			"floor":       0,
			"payload":     map[string]any{"character_id": "IRONCLAD", "ascension": 5},
		},
		{
			"event_uuid":  "uuid-2",
			"event_type":  "run_end",
			"occurred_at": "2026-05-05T01:00:00Z",
			"player_id":   "76561199000000000",
			"floor":       51,
			"payload":     map[string]any{"outcome": "victory", "final_floor": 51},
		},
	}
	res := do(t, srv, http.MethodPost, "/sessions/"+id+"/events", token, events)
	if res.StatusCode != http.StatusNoContent {
		t.Fatalf("status=%d", res.StatusCode)
	}

	// re-post: should be idempotent
	res = do(t, srv, http.MethodPost, "/sessions/"+id+"/events", token, events)
	if res.StatusCode != http.StatusNoContent {
		t.Fatalf("status=%d", res.StatusCode)
	}

	res = do(t, srv, http.MethodGet, "/api/sessions/"+id, "", nil)
	var doc sessionDoc
	decodeJSON(t, res, &doc)
	if len(doc.Events) != 2 {
		t.Fatalf("expected 2 events, got %d", len(doc.Events))
	}
	if doc.Session.Outcome == nil || *doc.Session.Outcome != "victory" {
		t.Errorf("session outcome not updated: %+v", doc.Session)
	}
	if doc.Session.FinalFloor == nil || *doc.Session.FinalFloor != 51 {
		t.Errorf("session final_floor not updated: %+v", doc.Session)
	}
}

func TestGetSession_PlayersUpsertedFromTurns(t *testing.T) {
	srv, _ := newTestServer(t)
	id, token := createSession(t, srv)

	body := turnFixture(1, 1, true)
	res := do(t, srv, http.MethodPost, "/sessions/"+id+"/turns", token, body)
	if res.StatusCode != http.StatusNoContent {
		t.Fatalf("status=%d", res.StatusCode)
	}

	res = do(t, srv, http.MethodGet, "/api/sessions/"+id, "", nil)
	var doc sessionDoc
	decodeJSON(t, res, &doc)
	if len(doc.Players) == 0 {
		t.Fatalf("expected players to be populated")
	}
	found := false
	for _, p := range doc.Players {
		if p.SteamID == "76561199000000000" && p.DisplayName == "Nobu" {
			found = true
		}
	}
	if !found {
		t.Errorf("expected player from turn payload, got %+v", doc.Players)
	}
}

func TestGetSession_NotFound(t *testing.T) {
	srv, _ := newTestServer(t)
	res := do(t, srv, http.MethodGet, "/api/sessions/nope", "", nil)
	if res.StatusCode != http.StatusNotFound {
		t.Fatalf("status=%d", res.StatusCode)
	}
}

func TestPostTurn_RejectInvalidIndices(t *testing.T) {
	srv, _ := newTestServer(t)
	id, token := createSession(t, srv)

	res := do(t, srv, http.MethodPost, "/sessions/"+id+"/turns", token, map[string]any{
		"combat_index": 0,
		"turn_number":  0,
		"is_final":     false,
		"timestamp":    "2026-05-05T00:00:00Z",
		"players":      map[string]any{},
	})
	if res.StatusCode != http.StatusBadRequest {
		t.Fatalf("expected 400, got %d", res.StatusCode)
	}
}

func turnFixture(combat, turn int, isFinal bool) map[string]any {
	return map[string]any{
		"combat_index": combat,
		"turn_number":  turn,
		"is_final":     isFinal,
		"timestamp":    "2026-05-05T00:00:00Z",
		"players": map[string]any{
			"76561199000000000": map[string]any{
				"player_name": "Nobu",
				"turn":        map[string]any{"damage_dealt": 10},
				"combat":      map[string]any{"damage_dealt": 10},
			},
		},
	}
}
