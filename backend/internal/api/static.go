package api

import (
	"io/fs"
	"net/http"
	"strings"

	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/static"
)

// staticFS は組み込まれた SPA の dist ルート（embed 経由）。
var staticFS fs.FS = static.FS()

// indexHTML returns a handler that serves the SPA's index.html with the
// proper Content-Type header. Used for SPA fallback routes like
// "/s/{id}" — anything that the Svelte router needs to bootstrap.
func indexHTML() http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		body, err := fs.ReadFile(staticFS, "index.html")
		if err != nil {
			http.Error(w, "index.html missing from build", http.StatusInternalServerError)
			return
		}
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		w.Header().Set("Cache-Control", "no-cache")     // SPA bootstrap は常に最新
		_, _ = w.Write(body)
	}
}

// landingHTML serves the static landing page at "/" — explains what the
// service is and how to use the mod. Distinct from the SPA which lives at
// "/s/{id}".
func landingHTML() http.HandlerFunc {
	body := static.LandingHTML()
	return func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		w.Header().Set("Cache-Control", "public, max-age=300")
		_, _ = w.Write(body)
	}
}

// staticAssets serves files under /assets/* from the embedded FS.
// Asset filenames are content-hashed by Vite so they are immutable; long cache.
func staticAssets() http.Handler {
	fileServer := http.FileServer(http.FS(staticFS))
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if strings.HasPrefix(r.URL.Path, "/assets/") {
			w.Header().Set("Cache-Control", "public, max-age=31536000, immutable")
		}
		fileServer.ServeHTTP(w, r)
	})
}
