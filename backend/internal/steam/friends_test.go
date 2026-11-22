package steam

import (
	"context"
	"net/http"
	"net/http/httptest"
	"testing"
)

// fakeSteam は、Steam ID ごとのフレンド一覧を返す。一覧が無い Steam ID は非公開として 401 を返す。
func fakeSteam(t *testing.T, lists map[string]string) (*WebAPI, *int) {
	t.Helper()
	calls := 0
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		calls++
		body, ok := lists[r.URL.Query().Get("steamid")]
		if !ok {
			w.WriteHeader(http.StatusUnauthorized)
			return
		}
		_, _ = w.Write([]byte(body))
	}))
	t.Cleanup(srv.Close)
	return &WebAPI{Key: "k", Client: srv.Client(), BaseURL: srv.URL}, &calls
}

func TestAreFriends(t *testing.T) {
	const a, b, c = "1", "2", "3"
	list := func(ids ...string) string {
		s := `{"friendslist":{"friends":[`
		for i, id := range ids {
			if i > 0 {
				s += ","
			}
			s += `{"steamid":"` + id + `","relationship":"friend"}`
		}
		return s + `]}}`
	}
	cases := []struct {
		name  string
		lists map[string]string
		want  bool
	}{
		{"持ち主の一覧にいる", map[string]string{a: list(b)}, true},
		{"持ち主の一覧にいない", map[string]string{a: list(c)}, false},
		{"持ち主の一覧が非公開で、見に来た人の一覧に持ち主がいる", map[string]string{b: list(a)}, true},
		{"どちらの一覧も非公開", map[string]string{}, false},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			api, _ := fakeSteam(t, tc.lists)
			got, err := api.AreFriends(context.Background(), a, b)
			if err != nil || got != tc.want {
				t.Fatalf("got=%v err=%v", got, err)
			}
		})
	}
	t.Run("同じ 2 人は覚えた結果を使う", func(t *testing.T) {
		api, calls := fakeSteam(t, map[string]string{a: list(b)})
		_, _ = api.AreFriends(context.Background(), a, b)
		_, _ = api.AreFriends(context.Background(), b, a)
		if *calls != 1 {
			t.Fatalf("calls=%d", *calls)
		}
	})
}
