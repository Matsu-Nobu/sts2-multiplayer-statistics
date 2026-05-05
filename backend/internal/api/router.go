package api

import (
	"net/http"
	"strings"
	"time"

	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/store"
	"github.com/go-chi/chi/v5"
	"github.com/go-chi/chi/v5/middleware"
	"github.com/go-chi/httprate"
)

type Server struct {
	Store          *store.Store
	BaseURL        string // for share_url construction (no trailing slash)
	AllowedOrigins []string
}

// 防御パラメータ
const (
	// POST body の最大サイズ（バイト）。turns/events は cards / debuffs / event payload で
	// それなりに膨らむ可能性があるが 256KB あれば実用上充分。
	maxBodyBytes int64 = 256 * 1024

	// rate limit: IP あたり N req / windowSec
	// POST /sessions: ボットによる無制限 session 作成を防ぐ。1分10件もあれば実用上十分。
	rateSessionsPerMin = 10
	// POST /sessions/{id}/events: bulk なので mod 側は更に低頻度。1分300件で十分。
	rateEventsPerMin = 300
)

// limitBody wraps the body in MaxBytesReader so reading panics with 413 if exceeded.
func limitBody(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		r.Body = http.MaxBytesReader(w, r.Body, maxBodyBytes)
		next.ServeHTTP(w, r)
	})
}

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
	r.Use(s.cors)

	r.Get("/healthz", func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/plain")
		_, _ = w.Write([]byte("ok"))
	})

	// POST endpoints は rate limit + body size limit で保護
	r.Group(func(r chi.Router) {
		r.Use(limitBody)
		r.With(rateLimit(rateSessionsPerMin, time.Minute)).Post("/sessions", s.createSession)

		r.Route("/sessions/{id}", func(r chi.Router) {
			// /turns は Phase 3.5 で廃止。古い mod クライアントには 410 を返す。
			r.Post("/turns", postTurnGone)
			r.With(s.requireWriteToken, rateLimit(rateEventsPerMin, time.Minute)).
				Post("/events", s.postEvents)
		})
	})

	r.Get("/api/sessions/{id}", s.getSession)

	// --- 静的配信 ---
	// "/" はランディングページ（プロジェクト紹介・使い方）
	// "/s/{id}" は SPA（共有URLで開かれる統計ビュー）
	r.Get("/", landingHTML())
	r.Get("/s/{id}", indexHTML())
	r.Mount("/assets/", staticAssets())
	r.Get("/favicon.ico", staticAssets().ServeHTTP)

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
