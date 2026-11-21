package api

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"net/http/httptest"
	"net/url"
	"strings"
	"testing"
	"time"

	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/auth"
)

// stubVerify は Steam に問い合わせず、openid.mode=id_res のときに claimed_id の Steam ID を本人とする。
func stubVerify(_ context.Context, params url.Values, _ string) (string, error) {
	if params.Get("openid.mode") != "id_res" {
		return "", auth.ErrInvalidLogin
	}
	return strings.TrimPrefix(params.Get("openid.claimed_id"), "https://steamcommunity.com/openid/id/"), nil
}

const (
	hostID   = "76561199000000000" // createSession の host_steam_id (記録した人)
	friendID = "76561199000000001" // 同じランの参加者
	otherID  = "76561199000000002" // ランに関係ない人
)

// doAs は steamID でログインした状態のリクエストを送る。steamID が "" ならログインしていない。
func doAs(t *testing.T, srv *Server, method, path, steamID string, body any, origin string) *http.Response {
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
	req.Header.Set("Content-Type", "application/json")
	if origin != "" {
		req.Header.Set("Origin", origin)
	}
	if steamID != "" {
		req.AddCookie(srv.Signer.Issue(steamID, time.Now(), false))
	}
	rec := httptest.NewRecorder()
	srv.Routes().ServeHTTP(rec, req)
	return rec.Result()
}

// seedRun は、記録した人 (hostID) と参加者 (friendID) のランを作り、セッション ID を返す。
func seedRun(t *testing.T, srv *Server) string {
	t.Helper()
	id, token := createSession(t, srv)
	runStart := func(uuid, pid, name, char string) map[string]any {
		return map[string]any{
			"event_uuid": uuid, "event_type": "run_start", "occurred_at": "2026-11-22T00:00:00Z", "player_id": pid, "floor": 0,
			"payload": map[string]any{"character_id": char, "character_name": char, "player_name": name, "ascension": 3, "seed": "S", "game_mode": "Blitz"},
		}
	}
	res := do(t, srv, http.MethodPost, "/sessions/"+id+"/events", token, []map[string]any{
		runStart("rs-host", hostID, "Nobu", "FOOL"),
		runStart("rs-friend", friendID, "Friend", "REGENT"),
	})
	if res.StatusCode != http.StatusNoContent {
		t.Fatalf("post run_start status=%d", res.StatusCode)
	}
	return id
}

type userPage struct {
	User struct {
		DisplayName   string `json:"display_name"`
		ProfilePublic bool   `json:"profile_public"`
	} `json:"user"`
	IsMe bool `json:"is_me"`
	Runs []struct {
		SessionID     string `json:"session_id"`
		CharacterName string `json:"character_name"`
		GameMode      string `json:"game_mode"`
		Visibility    string `json:"visibility"`
		Hidden        bool   `json:"hidden"`
		IsRecorder    bool   `json:"is_recorder"`
		Participants  []struct {
			SteamID string `json:"steam_id"`
		} `json:"participants"`
	} `json:"runs"`
}

func getUserPage(t *testing.T, srv *Server, userID, viewer string) (int, userPage) {
	t.Helper()
	res := doAs(t, srv, http.MethodGet, "/api/users/"+userID, viewer, nil, "")
	var page userPage
	if res.StatusCode == http.StatusOK {
		decodeJSON(t, res, &page)
	}
	return res.StatusCode, page
}

func TestGetUser_PrivateProfile(t *testing.T) {
	srv, _ := newTestServer(t)
	id := seedRun(t, srv)

	t.Run("本人以外には 403", func(t *testing.T) {
		if code, _ := getUserPage(t, srv, friendID, otherID); code != http.StatusForbidden {
			t.Fatalf("status=%d", code)
		}
		if code, _ := getUserPage(t, srv, friendID, ""); code != http.StatusForbidden {
			t.Fatalf("anonymous status=%d", code)
		}
	})
	t.Run("本人には参加したランとキャラクターと参加者", func(t *testing.T) {
		code, page := getUserPage(t, srv, friendID, friendID)
		if code != http.StatusOK || !page.IsMe || len(page.Runs) != 1 {
			t.Fatalf("status=%d page=%+v", code, page)
		}
		r := page.Runs[0]
		if r.SessionID != id || r.CharacterName != "REGENT" || r.GameMode != "Blitz" || r.IsRecorder || len(r.Participants) != 2 {
			t.Fatalf("run=%+v", r)
		}
	})
}

func TestGetUser_PublicProfileListsOnlyPublicListedRuns(t *testing.T) {
	srv, _ := newTestServer(t)
	id := seedRun(t, srv)
	if res := doAs(t, srv, http.MethodPatch, "/api/me", friendID, map[string]any{"profile_public": true}, ""); res.StatusCode != http.StatusNoContent {
		t.Fatalf("patch me status=%d", res.StatusCode)
	}

	t.Run("公開で載せているランは本人以外にも見える", func(t *testing.T) {
		if _, page := getUserPage(t, srv, friendID, otherID); len(page.Runs) != 1 {
			t.Fatalf("runs=%d", len(page.Runs))
		}
	})
	t.Run("記録した人が非公開にしたランは本人以外に見えない", func(t *testing.T) {
		doAs(t, srv, http.MethodPatch, "/api/sessions/"+id, hostID, map[string]any{"visibility": "private"}, "")
		if _, page := getUserPage(t, srv, friendID, otherID); len(page.Runs) != 0 {
			t.Fatalf("runs=%d", len(page.Runs))
		}
		if _, page := getUserPage(t, srv, friendID, friendID); len(page.Runs) != 1 || page.Runs[0].Visibility != "private" {
			t.Fatalf("self page=%+v", page)
		}
		doAs(t, srv, http.MethodPatch, "/api/sessions/"+id, hostID, map[string]any{"visibility": "public"}, "")
	})
	t.Run("参加者が載せないことにしたランは本人以外に見えない", func(t *testing.T) {
		if res := doAs(t, srv, http.MethodPatch, "/api/sessions/"+id+"/listing", friendID, map[string]any{"hidden": true}, ""); res.StatusCode != http.StatusNoContent {
			t.Fatalf("patch listing status=%d", res.StatusCode)
		}
		if _, page := getUserPage(t, srv, friendID, otherID); len(page.Runs) != 0 {
			t.Fatalf("runs=%d", len(page.Runs))
		}
		if _, page := getUserPage(t, srv, friendID, friendID); len(page.Runs) != 1 || !page.Runs[0].Hidden {
			t.Fatalf("self page=%+v", page)
		}
	})
}

func TestGetSession_Private(t *testing.T) {
	srv, _ := newTestServer(t)
	id := seedRun(t, srv)
	doAs(t, srv, http.MethodPatch, "/api/sessions/"+id, hostID, map[string]any{"visibility": "private"}, "")

	for _, viewer := range []string{"", friendID, otherID} {
		if res := doAs(t, srv, http.MethodGet, "/api/sessions/"+id, viewer, nil, ""); res.StatusCode != http.StatusNotFound {
			t.Errorf("viewer=%q status=%d", viewer, res.StatusCode)
		}
	}
	if res := doAs(t, srv, http.MethodGet, "/api/sessions/"+id, hostID, nil, ""); res.StatusCode != http.StatusOK {
		t.Errorf("recorder status=%d", res.StatusCode)
	}
}

func TestPatchSession_OnlyRecorder(t *testing.T) {
	srv, _ := newTestServer(t)
	id := seedRun(t, srv)
	if res := doAs(t, srv, http.MethodPatch, "/api/sessions/"+id, friendID, map[string]any{"visibility": "private"}, ""); res.StatusCode != http.StatusForbidden {
		t.Fatalf("participant status=%d", res.StatusCode)
	}
	if res := doAs(t, srv, http.MethodPatch, "/api/sessions/"+id, hostID, map[string]any{"visibility": "secret"}, ""); res.StatusCode != http.StatusBadRequest {
		t.Fatalf("bad value status=%d", res.StatusCode)
	}
}

func TestPatchListing_OnlyParticipant(t *testing.T) {
	srv, _ := newTestServer(t)
	id := seedRun(t, srv)
	if res := doAs(t, srv, http.MethodPatch, "/api/sessions/"+id+"/listing", otherID, map[string]any{"hidden": true}, ""); res.StatusCode != http.StatusForbidden {
		t.Fatalf("status=%d", res.StatusCode)
	}
}

func TestDeleteSession(t *testing.T) {
	srv, st := newTestServer(t)
	id := seedRun(t, srv)

	if res := doAs(t, srv, http.MethodDelete, "/api/sessions/"+id, friendID, nil, ""); res.StatusCode != http.StatusForbidden {
		t.Fatalf("participant status=%d", res.StatusCode)
	}
	if res := doAs(t, srv, http.MethodDelete, "/api/sessions/"+id, hostID, nil, ""); res.StatusCode != http.StatusNoContent {
		t.Fatalf("recorder status=%d", res.StatusCode)
	}
	if res := doAs(t, srv, http.MethodGet, "/api/sessions/"+id, hostID, nil, ""); res.StatusCode != http.StatusNotFound {
		t.Fatalf("after delete status=%d", res.StatusCode)
	}
	var events, players int
	_ = st.DB().QueryRow(`SELECT COUNT(*) FROM events WHERE session_id = ?`, id).Scan(&events)
	_ = st.DB().QueryRow(`SELECT COUNT(*) FROM session_players WHERE session_id = ?`, id).Scan(&players)
	if events != 0 || players != 0 {
		t.Fatalf("left events=%d session_players=%d", events, players)
	}
}

func TestMutations_RequireLoginAndSameOrigin(t *testing.T) {
	srv, _ := newTestServer(t)
	id := seedRun(t, srv)
	if res := doAs(t, srv, http.MethodDelete, "/api/sessions/"+id, "", nil, ""); res.StatusCode != http.StatusUnauthorized {
		t.Fatalf("anonymous status=%d", res.StatusCode)
	}
	if res := doAs(t, srv, http.MethodDelete, "/api/sessions/"+id, hostID, nil, "https://evil.example"); res.StatusCode != http.StatusForbidden {
		t.Fatalf("cross-origin status=%d", res.StatusCode)
	}
	if res := doAs(t, srv, http.MethodDelete, "/api/sessions/"+id, hostID, nil, "http://test"); res.StatusCode != http.StatusNoContent {
		t.Fatalf("same-origin status=%d", res.StatusCode)
	}
}

func TestSteamLoginAndCallback(t *testing.T) {
	srv, _ := newTestServer(t)

	t.Run("ログインは Steam へ、戻り先を付けて送る", func(t *testing.T) {
		res := doAs(t, srv, http.MethodGet, "/auth/steam/login?return=/u/"+friendID, "", nil, "")
		loc := res.Header.Get("Location")
		if res.StatusCode != http.StatusFound || !strings.HasPrefix(loc, "https://steamcommunity.com/openid/login?") {
			t.Fatalf("status=%d location=%s", res.StatusCode, loc)
		}
		u, _ := url.Parse(loc)
		if rt := u.Query().Get("openid.return_to"); rt != "http://test/auth/steam/callback?return=%2Fu%2F"+friendID {
			t.Fatalf("return_to=%s", rt)
		}
	})
	t.Run("検証できたら cookie を付けて戻り先へ", func(t *testing.T) {
		q := url.Values{"openid.mode": {"id_res"}, "openid.claimed_id": {"https://steamcommunity.com/openid/id/" + friendID}, "return": {"/u/" + friendID}}
		res := doAs(t, srv, http.MethodGet, "/auth/steam/callback?"+q.Encode(), "", nil, "")
		if res.StatusCode != http.StatusFound || res.Header.Get("Location") != "/u/"+friendID {
			t.Fatalf("status=%d location=%s", res.StatusCode, res.Header.Get("Location"))
		}
		req := httptest.NewRequest(http.MethodGet, "/", nil)
		for _, c := range res.Cookies() {
			req.AddCookie(c)
		}
		if got := srv.Signer.SteamIDFrom(req, time.Now()); got != friendID {
			t.Fatalf("cookie steam id=%q", got)
		}
	})
	t.Run("別のサイトへの戻り先は / にする", func(t *testing.T) {
		q := url.Values{"openid.mode": {"id_res"}, "openid.claimed_id": {"https://steamcommunity.com/openid/id/" + friendID}, "return": {"//evil.example"}}
		res := doAs(t, srv, http.MethodGet, "/auth/steam/callback?"+q.Encode(), "", nil, "")
		if res.Header.Get("Location") != "/" {
			t.Fatalf("location=%s", res.Header.Get("Location"))
		}
	})
	t.Run("検証できなければ 401", func(t *testing.T) {
		res := doAs(t, srv, http.MethodGet, "/auth/steam/callback?openid.mode=cancel", "", nil, "")
		if res.StatusCode != http.StatusUnauthorized || len(res.Cookies()) != 0 {
			t.Fatalf("status=%d cookies=%d", res.StatusCode, len(res.Cookies()))
		}
	})
}

func TestVerifyWithSteam_RejectsBeforeAskingSteam(t *testing.T) {
	// Steam に問い合わせる前の確認で断る場合だけを調べる (http.Client はどこにも繋がらないものにする)
	v := auth.VerifyWithSteam(&http.Client{Transport: roundTripFunc(func(*http.Request) (*http.Response, error) {
		return nil, errors.New("must not call steam")
	})})
	base := url.Values{
		"openid.mode": {"id_res"}, "openid.op_endpoint": {"https://steamcommunity.com/openid/login"},
		"openid.return_to": {"http://test/auth/steam/callback?return=%2F"}, "openid.claimed_id": {"https://steamcommunity.com/openid/id/" + friendID},
	}
	cases := map[string]func(url.Values){
		"ログイン先が Steam でない": func(p url.Values) { p.Set("openid.op_endpoint", "https://evil.example/openid") },
		"戻り先が自分のコールバックでない": func(p url.Values) { p.Set("openid.return_to", "https://evil.example/cb") },
		"Steam ID の形でない":   func(p url.Values) { p.Set("openid.claimed_id", "https://evil.example/id/1") },
	}
	for name, mutate := range cases {
		t.Run(name, func(t *testing.T) {
			p := url.Values{}
			for k, vs := range base {
				p[k] = append([]string(nil), vs...)
			}
			mutate(p)
			if _, err := v(context.Background(), p, "http://test/auth/steam/callback"); !errors.Is(err, auth.ErrInvalidLogin) {
				t.Fatalf("err=%v", err)
			}
		})
	}
}

type roundTripFunc func(*http.Request) (*http.Response, error)

func (f roundTripFunc) RoundTrip(r *http.Request) (*http.Response, error) { return f(r) }

func TestGetRunRelation(t *testing.T) {
	srv, _ := newTestServer(t)
	id := seedRun(t, srv)
	rel := func(viewer string) (int, map[string]any) {
		res := doAs(t, srv, http.MethodGet, "/api/sessions/"+id+"/me", viewer, nil, "")
		var out map[string]any
		if res.StatusCode == http.StatusOK {
			decodeJSON(t, res, &out)
		}
		return res.StatusCode, out
	}
	if code, out := rel(hostID); code != http.StatusOK || out["is_recorder"] != true || out["is_participant"] != true {
		t.Fatalf("recorder code=%d out=%v", code, out)
	}
	if code, out := rel(friendID); code != http.StatusOK || out["is_recorder"] != false || out["is_participant"] != true {
		t.Fatalf("participant code=%d out=%v", code, out)
	}
	if code, out := rel(otherID); code != http.StatusOK || out["is_participant"] != false {
		t.Fatalf("other code=%d out=%v", code, out)
	}
	if code, _ := rel(""); code != http.StatusUnauthorized {
		t.Fatalf("anonymous code=%d", code)
	}
	doAs(t, srv, http.MethodPatch, "/api/sessions/"+id, hostID, map[string]any{"visibility": "private"}, "")
	if code, _ := rel(friendID); code != http.StatusNotFound {
		t.Fatalf("private participant code=%d", code)
	}
	if code, _ := rel(hostID); code != http.StatusOK {
		t.Fatalf("private recorder code=%d", code)
	}
}
