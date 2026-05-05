package api

import (
	"errors"
	"net/http"
	"strings"

	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/store"
	"github.com/go-chi/chi/v5"
)

// requireWriteToken validates that the Authorization header carries a Bearer
// token matching the session's write_token. The session must exist.
func (s *Server) requireWriteToken(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		id := chi.URLParam(r, "id")
		sess, err := s.Store.GetSession(r.Context(), id)
		if errors.Is(err, store.ErrSessionNotFound) {
			writeError(w, http.StatusNotFound, "session not found")
			return
		}
		if err != nil {
			writeError(w, http.StatusInternalServerError, "failed to load session")
			return
		}

		auth := r.Header.Get("Authorization")
		const prefix = "Bearer "
		if !strings.HasPrefix(auth, prefix) {
			writeError(w, http.StatusUnauthorized, "missing bearer token")
			return
		}
		token := strings.TrimPrefix(auth, prefix)
		if token == "" || token != sess.WriteToken {
			writeError(w, http.StatusUnauthorized, "invalid write token")
			return
		}
		next.ServeHTTP(w, r)
	})
}
