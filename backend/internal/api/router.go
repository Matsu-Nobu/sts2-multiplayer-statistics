package api

import (
	"net/http"
	"strings"
	"sync"
	"time"

	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/auth"
	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/steam"
	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/store"
	"github.com/go-chi/chi/v5"
	"github.com/go-chi/chi/v5/middleware"
	"github.com/go-chi/httprate"
)

type Server struct {
	Store          *store.Store
	BaseURL        string // for share_url construction (no trailing slash)
	AllowedOrigins []string
	Signer         auth.Signer         // ログインの cookie の署名
	Verify         auth.Verifier       // Steam から戻ってきたログインの検証 (テストでは差し替える)
	Friends        steam.FriendChecker // Steam のフレンドかどうか (テストでは差し替える)
	Now            func() time.Time    // nil なら time.Now

	warming sync.WaitGroup // ゲームが終わったときに集計を作るゴルーチン (テストで終わりを待つ)
}

// 防御パラメータ
const (
	// POST / PATCH の body の最大サイズ (バイト)。
	maxBodyBytes int64 = 256 * 1024
	// POST /sessions/{id}/events の body の最大サイズ。4 人のマルチプレイでは、階の記録 (全員のデッキ入り) が
	// mod の 1 回の送信に重なって 256KB に近づく。mod は断られた送信を捨てるので、余裕を大きく取る。
	maxEventsBodyBytes int64 = 2 * 1024 * 1024

	// rate limit: IP あたり N req / windowSec
	// POST /sessions: ボットによる無制限 session 作成を防ぐ。1分10件もあれば実用上十分。
	rateSessionsPerMin = 10
	// POST /sessions/{id}/events: bulk なので mod 側は更に低頻度。1分300件で十分。
	rateEventsPerMin = 300
)

// limitBodyTo は body を n バイトまでにする。超えると読み出しが失敗し、handler は 400 を返す。
func limitBodyTo(n int64) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			r.Body = http.MaxBytesReader(w, r.Body, n)
			next.ServeHTTP(w, r)
		})
	}
}

var limitBody = limitBodyTo(maxBodyBytes)

// rateLimit returns an httprate middleware keyed by IP address.
func rateLimit(reqs int, window time.Duration) func(http.Handler) http.Handler {
	return httprate.LimitByIP(reqs, window)
}

func (s *Server) Routes() http.Handler {
	r := chi.NewRouter()
	r.Use(middleware.RequestID)
	r.Use(middleware.RealIP)
	r.Use(middleware.Recoverer)
	r.Use(middleware.Logger)
	// 5 は gzip の圧縮の強さ (1〜9)。
	// 圧縮するのは、リクエストの Accept-Encoding に gzip があり、応答が JSON・HTML・JS・CSS などの文字のときだけである。
	r.Use(middleware.Compress(5))
	r.Use(s.cors)

	r.Get("/healthz", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/plain")
		_, _ = w.Write([]byte("ok"))
	})

	// POST endpoints は rate limit + body size limit で保護
	r.With(limitBody, rateLimit(rateSessionsPerMin, time.Minute)).Post("/sessions", s.createSession)
	r.With(limitBodyTo(maxEventsBodyBytes), s.requireWriteToken, rateLimit(rateEventsPerMin, time.Minute)).
		Post("/sessions/{id}/events", s.postEvents)

	r.Get("/api/sessions/{id}", s.getSession)

	// ログインとユーザーページ (docs/spec/user-pages.md)
	r.Get("/auth/steam/login", s.steamLogin)
	r.Get("/auth/steam/callback", s.steamCallback)
	r.Post("/auth/logout", s.logout)
	r.Get("/api/me", s.getMe)
	r.Get("/api/users/{steamid}", s.getUser)
	r.Get("/api/users/{steamid}/stats", s.getUserStats)
	r.Get("/api/sessions/{id}/me", s.getRunRelation)
	r.Group(func(r chi.Router) {
		r.Use(limitBody, s.requireLogin)
		r.Patch("/api/me", s.patchMe)
		r.Patch("/api/sessions/{id}", s.patchSession)
		r.Patch("/api/sessions/{id}/listing", s.patchListing)
		r.Delete("/api/sessions/{id}", s.deleteSession)
	})

	// --- 静的配信 ---
	// web の画面 (SPA)。どの URL でも同じ index.html を返し、画面は web 側で決める (spec ui.md「サイトの画面」)
	r.Get("/", indexHTML())
	r.Get("/settings", indexHTML())
	r.Get("/s/{id}", indexHTML())
	r.Get("/s/{id}/*", indexHTML()) // web 側の画面 (spec ui.md「画面と URL」)
	r.Get("/u/{steamid}", indexHTML())
	r.Get("/u/{steamid}/stats", indexHTML())
	r.Get("/u/{steamid}/stats/*", indexHTML()) // 統計の区画 (spec player-stats.md「画面の構成」)
	r.Mount("/assets/", staticAssets())
	r.Get("/favicon.ico", staticAssets().ServeHTTP)
	// /catalog.{lang}.json — STS2 のカード/レリック/ポーション/エンチャント definitions。
	// web/public/ に置いた静的 JSON が embed FS 経由で配信される。
	r.Get("/catalog.{lang}.json", staticAssets().ServeHTTP)

	return r
}

func (s *Server) cors(next http.Handler) http.Handler {
	allowed := strings.Join(s.AllowedOrigins, ", ")
	if allowed == "" {
		allowed = "*"
	}
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Access-Control-Allow-Origin", allowed)
		w.Header().Set("Access-Control-Allow-Headers", "Authorization, Content-Type")
		w.Header().Set("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
		if r.Method == http.MethodOptions {
			w.WriteHeader(http.StatusNoContent)
			return
		}
		next.ServeHTTP(w, r)
	})
}
