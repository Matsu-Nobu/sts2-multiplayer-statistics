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

// Phase 3.5: /turns は廃止、常に 410 を返す
func TestPostTurns_Gone(t *testing.T) {
	srv, _ := newTestServer(t)
	id, _ := createSession(t, srv)

	res := do(t, srv, http.MethodPost, "/sessions/"+id+"/turns", "any", map[string]any{
		"combat_index": 1, "turn_number": 1,
	})
	if res.StatusCode != http.StatusGone {
		t.Fatalf("expected 410 Gone, got %d", res.StatusCode)
	}
}

func TestPostEvents_Auth(t *testing.T) {
	srv, _ := newTestServer(t)
	id, _ := createSession(t, srv)

	body := []map[string]any{eventFixture("e-1", "card_played", 1, 1, 0)}

	res := do(t, srv, http.MethodPost, "/sessions/"+id+"/events", "", body)
	if res.StatusCode != http.StatusUnauthorized {
		t.Fatalf("expected 401, got %d", res.StatusCode)
	}

	res = do(t, srv, http.MethodPost, "/sessions/"+id+"/events", "wrong", body)
	if res.StatusCode != http.StatusUnauthorized {
		t.Fatalf("expected 401, got %d", res.StatusCode)
	}

	res = do(t, srv, http.MethodPost, "/sessions/unknown/events", "any", body)
	if res.StatusCode != http.StatusNotFound {
		t.Fatalf("expected 404, got %d", res.StatusCode)
	}
}

func TestPostEvents_Idempotent(t *testing.T) {
	srv, _ := newTestServer(t)
	id, token := createSession(t, srv)

	body := []map[string]any{eventFixture("e-dup", "card_played", 1, 1, 0)}
	for i := 0; i < 3; i++ {
		res := do(t, srv, http.MethodPost, "/sessions/"+id+"/events", token, body)
		if res.StatusCode != http.StatusNoContent {
			t.Fatalf("status=%d", res.StatusCode)
		}
	}

	res := do(t, srv, http.MethodGet, "/api/sessions/"+id, "", nil)
	var doc sessionDoc
	decodeJSON(t, res, &doc)
	if len(doc.Events) != 1 {
		t.Fatalf("expected 1 event, got %d", len(doc.Events))
	}
}

func TestPostEvents_RunStartEnd_UpdatesSession(t *testing.T) {
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

	res = do(t, srv, http.MethodGet, "/api/sessions/"+id, "", nil)
	var doc sessionDoc
	decodeJSON(t, res, &doc)
	if len(doc.Events) != 2 {
		t.Fatalf("expected 2 events, got %d", len(doc.Events))
	}
	if doc.Session.Outcome == nil || *doc.Session.Outcome != "victory" {
		t.Errorf("session outcome not updated: %+v", doc.Session)
	}
}

func TestPostEvents_TurnContext_Persisted(t *testing.T) {
	srv, _ := newTestServer(t)
	id, token := createSession(t, srv)

	events := []map[string]any{
		{
			"event_uuid":   "tc-1",
			"event_type":   "card_played",
			"occurred_at":  "2026-05-05T00:00:00Z",
			"player_id":    "76561199000000000",
			"floor":        1,
			"combat_index": 1,
			"turn_number":  1,
			"sequence":     0,
			"payload":      map[string]any{"card_id": "BASH", "card_name": "Bash"},
		},
	}
	res := do(t, srv, http.MethodPost, "/sessions/"+id+"/events", token, events)
	if res.StatusCode != http.StatusNoContent {
		t.Fatalf("status=%d", res.StatusCode)
	}

	res = do(t, srv, http.MethodGet, "/api/sessions/"+id, "", nil)
	var doc sessionDoc
	decodeJSON(t, res, &doc)
	if len(doc.Events) != 1 {
		t.Fatalf("expected 1 event, got %d", len(doc.Events))
	}
	var ev map[string]any
	if err := json.Unmarshal(doc.Events[0], &ev); err != nil {
		t.Fatalf("unmarshal: %v", err)
	}
	if v, ok := ev["combat_index"]; !ok || int(v.(float64)) != 1 {
		t.Errorf("combat_index missing/wrong: %+v", ev)
	}
	if v, ok := ev["turn_number"]; !ok || int(v.(float64)) != 1 {
		t.Errorf("turn_number missing/wrong: %+v", ev)
	}
	if v, ok := ev["sequence"]; !ok || int(v.(float64)) != 0 {
		t.Errorf("sequence missing/wrong: %+v", ev)
	}
}

func TestGetSession_PlayersIncludeHostFromCreate(t *testing.T) {
	srv, _ := newTestServer(t)
	id, _ := createSession(t, srv)

	res := do(t, srv, http.MethodGet, "/api/sessions/"+id, "", nil)
	var doc sessionDoc
	decodeJSON(t, res, &doc)
	found := false
	for _, p := range doc.Players {
		if p.SteamID == "76561199000000000" && p.DisplayName == "Nobu" {
			found = true
		}
	}
	if !found {
		t.Errorf("expected host player to be listed, got %+v", doc.Players)
	}
}

func TestGetSession_NotFound(t *testing.T) {
	srv, _ := newTestServer(t)
	res := do(t, srv, http.MethodGet, "/api/sessions/nope", "", nil)
	if res.StatusCode != http.StatusNotFound {
		t.Fatalf("status=%d", res.StatusCode)
	}
}

func TestGetSession_ETag_NotModified(t *testing.T) {
	srv, _ := newTestServer(t)
	id, _ := createSession(t, srv)

	res := do(t, srv, http.MethodGet, "/api/sessions/"+id, "", nil)
	if res.StatusCode != http.StatusOK {
		t.Fatalf("expected 200, got %d", res.StatusCode)
	}
	etag := res.Header.Get("ETag")
	if etag == "" {
		t.Fatal("expected ETag header")
	}
	res.Body.Close()

	req := httptest.NewRequest(http.MethodGet, "/api/sessions/"+id, nil)
	req.Header.Set("If-None-Match", etag)
	rec := httptest.NewRecorder()
	srv.Routes().ServeHTTP(rec, req)
	if rec.Code != http.StatusNotModified {
		t.Fatalf("expected 304, got %d", rec.Code)
	}
	if rec.Body.Len() != 0 {
		t.Errorf("expected empty body on 304, got %d bytes", rec.Body.Len())
	}
}

func TestGetSession_ETag_ChangesAfterEvent(t *testing.T) {
	srv, _ := newTestServer(t)
	id, token := createSession(t, srv)

	res := do(t, srv, http.MethodGet, "/api/sessions/"+id, "", nil)
	first := res.Header.Get("ETag")
	res.Body.Close()

	res = do(t, srv, http.MethodPost, "/sessions/"+id+"/events", token, []map[string]any{
		eventFixture("etag-1", "card_played", 1, 1, 0),
	})
	if res.StatusCode != http.StatusNoContent {
		t.Fatalf("status=%d", res.StatusCode)
	}

	res = do(t, srv, http.MethodGet, "/api/sessions/"+id, "", nil)
	second := res.Header.Get("ETag")
	res.Body.Close()

	if first == "" || second == "" {
		t.Fatal("expected both ETags non-empty")
	}
	if first == second {
		t.Fatalf("expected ETag to change after data update, got %s == %s", first, second)
	}
}

func eventFixture(uuid, eventType string, combat, turn, seq int) map[string]any {
	return map[string]any{
		"event_uuid":   uuid,
		"event_type":   eventType,
		"occurred_at":  "2026-05-05T00:00:00Z",
		"player_id":    "76561199000000000",
		"floor":        1,
		"combat_index": combat,
		"turn_number":  turn,
		"sequence":     seq,
		"payload":      map[string]any{},
	}
}
