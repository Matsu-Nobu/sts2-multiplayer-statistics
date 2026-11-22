package api

import (
	"io/fs"
	"net/http"
	"strings"

	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/static"
)

// staticFS は組み込まれた SPA の dist ルート（embed 経由）。
var staticFS fs.FS = static.FS()

const contentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
	"img-src 'self' data:; connect-src 'self'; font-src 'self'; object-src 'none'; base-uri 'none'; " +
	"frame-ancestors 'none'; form-action 'self'"

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
		w.Header().Set("Cache-Control", "no-cache") // SPA bootstrap は常に最新
		// 画面に悪いスクリプトが入っても、このサイト自身のファイル以外は動かさず、通信先もこのサイトだけにする。
		// 他のサイトに画面を埋め込ませない (削除ボタン等を押させる手口を防ぐ)。style 属性は Svelte が使うので許す
		w.Header().Set("Content-Security-Policy", contentSecurityPolicy)
		w.Header().Set("X-Content-Type-Options", "nosniff")
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
