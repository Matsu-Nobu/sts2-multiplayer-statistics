package api

import (
	"net/http"
	"strings"

	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/store"
	"github.com/go-chi/chi/v5"
	"github.com/go-chi/chi/v5/middleware"
)

type Server struct {
	Store          *store.Store
	BaseURL        string // for share_url construction (no trailing slash)
	AllowedOrigins []string
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

	r.Post("/sessions", s.createSession)

	r.Route("/sessions/{id}", func(r chi.Router) {
		r.With(s.requireWriteToken).Post("/turns", s.postTurn)
		r.With(s.requireWriteToken).Post("/events", s.postEvents)
	})

	r.Get("/api/sessions/{id}", s.getSession)

	// --- 静的 SPA 配信 ---
	// 共有URL（mod がクリップボードにコピーするやつ）はこのルートで開ける。
	r.Get("/", indexHTML())
	r.Get("/s/{id}", indexHTML())
	r.Mount("/assets/", staticAssets())
	// favicon 等のルート直下静的ファイルがあれば追加で配信
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
