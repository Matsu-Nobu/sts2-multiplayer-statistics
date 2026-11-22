// Package static embeds the built Svelte SPA from web/dist and exposes it as a
// fs.FS. The build pipeline (`make app-build`, Dockerfile) is responsible for populating
// dist/ from the repo's top-level web/dist/ before `go build` runs.
//
// Only dist/.gitignore is committed, so `go build` succeeds without a frontend
// build. In that case "/s/{id}" answers 500 "index.html missing from build".
package static

import (
	"embed"
	"io/fs"
)

//go:embed all:dist
var raw embed.FS

// FS returns the embedded SPA filesystem rooted at the dist/ directory
// (so callers see "index.html", "assets/..." rather than "dist/index.html").
func FS() fs.FS {
	sub, err := fs.Sub(raw, "dist")
	if err != nil {
		// Should be impossible because dist/ is always present at build time.
		panic(err)
	}
	return sub
}
