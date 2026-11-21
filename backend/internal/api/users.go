package api

import (
	"encoding/json"
	"errors"
	"net/http"
	"net/url"
	"strings"
	"time"

	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/auth"
	"github.com/Matsu-Nobu/sts2-multiplayer-statistics/backend/internal/store"
	"github.com/go-chi/chi/v5"
)

// ログインとユーザーページ (docs/spec/user-pages.md、docs/spec/api.md「ログイン」「ユーザー」)。

func (s *Server) now() time.Time {
	if s.Now != nil {
		return s.Now()
	}
	return time.Now()
}

func (s *Server) secureCookie() bool { return strings.HasPrefix(s.BaseURL, "https://") }

// viewer はログインしている人の Steam ID。ログインしていなければ ""。
func (s *Server) viewer(r *http.Request) string { return s.Signer.SteamIDFrom(r, s.now()) }

// safeReturn は、ログイン後に戻る先として受け付けるパス。自分のサイトの中のパスだけにする (別のサイトへ飛ばされないように)。
func safeReturn(p string) string {
	if !strings.HasPrefix(p, "/") || strings.HasPrefix(p, "//") || strings.Contains(p, "\\") {
		return "/"
	}
	return p
}

func (s *Server) callbackURL() string { return s.BaseURL + "/auth/steam/callback" }

func (s *Server) steamLogin(w http.ResponseWriter, r *http.Request) {
	ret := safeReturn(r.URL.Query().Get("return"))
	returnTo := s.callbackURL() + "?return=" + url.QueryEscape(ret)
	http.Redirect(w, r, auth.LoginURL(s.BaseURL, returnTo), http.StatusFound)
}

func (s *Server) steamCallback(w http.ResponseWriter, r *http.Request) {
	params := r.URL.Query()
	steamID, err := s.Verify(r.Context(), params, s.callbackURL())
	if err != nil {
		writeError(w, http.StatusUnauthorized, "steam login failed")
		return
	}
	http.SetCookie(w, s.Signer.Issue(steamID, s.now(), s.secureCookie()))
	http.Redirect(w, r, safeReturn(params.Get("return")), http.StatusFound)
}

func (s *Server) logout(w http.ResponseWriter, r *http.Request) {
	http.SetCookie(w, auth.Clear(s.secureCookie()))
	w.WriteHeader(http.StatusNoContent)
}

type meRes struct {
	SteamID       string `json:"steam_id"`
	DisplayName   string `json:"display_name"`
	ProfilePublic bool   `json:"profile_public"`
}

func (s *Server) getMe(w http.ResponseWriter, r *http.Request) {
	me := s.viewer(r)
	if me == "" {
		writeError(w, http.StatusUnauthorized, "not logged in")
		return
	}
	p, err := s.Store.GetUserProfile(r.Context(), me)
	if err != nil {
		writeError(w, http.StatusInternalServerError, "failed to load user")
		return
	}
	writeJSON(w, http.StatusOK, meRes{SteamID: me, DisplayName: displayName(p), ProfilePublic: p.ProfilePublic})
}

func displayName(p store.UserProfile) string {
	if p.DisplayName != "" {
		return p.DisplayName
	}
	return p.SteamID
}

type userPageRes struct {
	User meRes           `json:"user"`
	IsMe bool            `json:"is_me"`
	Runs []store.UserRun `json:"runs"`
}

func (s *Server) getUser(w http.ResponseWriter, r *http.Request) {
	id := chi.URLParam(r, "steamid")
	me := s.viewer(r)
	p, err := s.Store.GetUserProfile(r.Context(), id)
	if err != nil {
		writeError(w, http.StatusInternalServerError, "failed to load user")
		return
	}
	isMe := me != "" && me == id
	if !isMe && !p.ProfilePublic {
		writeError(w, http.StatusForbidden, "profile is private")
		return
	}
	runs, err := s.Store.ListUserRuns(r.Context(), id, !isMe)
	if err != nil {
		writeError(w, http.StatusInternalServerError, "failed to load runs")
		return
	}
	if !p.Known && len(runs) == 0 && !isMe {
		writeError(w, http.StatusNotFound, "user not found")
		return
	}
	writeJSON(w, http.StatusOK, userPageRes{
		User: meRes{SteamID: id, DisplayName: displayName(p), ProfilePublic: p.ProfilePublic},
		IsMe: isMe,
		Runs: runs,
	})
}

// requireLogin は、ログインしている人だけを通し、自分のサイトの画面からの操作であることを Origin で確かめる。
// cookie は SameSite=Lax なので別のサイトからの書き換えには付かないが、Origin でも重ねて断る。
func (s *Server) requireLogin(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if o := r.Header.Get("Origin"); o != "" && o != s.BaseURL {
			writeError(w, http.StatusForbidden, "cross-origin request")
			return
		}
		if s.viewer(r) == "" {
			writeError(w, http.StatusUnauthorized, "not logged in")
			return
		}
		next.ServeHTTP(w, r)
	})
}

func decodeBody(w http.ResponseWriter, r *http.Request, into any) bool {
	if err := json.NewDecoder(r.Body).Decode(into); err != nil {
		writeError(w, http.StatusBadRequest, "invalid json body")
		return false
	}
	return true
}

func (s *Server) patchMe(w http.ResponseWriter, r *http.Request) {
	var in struct {
		ProfilePublic *bool `json:"profile_public"`
	}
	if !decodeBody(w, r, &in) {
		return
	}
	if in.ProfilePublic == nil {
		writeError(w, http.StatusBadRequest, "profile_public is required")
		return
	}
	me := s.viewer(r)
	p, err := s.Store.GetUserProfile(r.Context(), me)
	if err == nil {
		err = s.Store.SetProfilePublic(r.Context(), me, displayName(p), *in.ProfilePublic)
	}
	if err != nil {
		writeError(w, http.StatusInternalServerError, "failed to update user")
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

func (s *Server) patchSession(w http.ResponseWriter, r *http.Request) {
	var in struct {
		Visibility string `json:"visibility"`
	}
	if !decodeBody(w, r, &in) {
		return
	}
	if in.Visibility != "public" && in.Visibility != "private" {
		writeError(w, http.StatusBadRequest, "visibility must be public or private")
		return
	}
	s.writeStoreResult(w, s.Store.SetSessionVisibility(r.Context(), chi.URLParam(r, "id"), s.viewer(r), in.Visibility))
}

func (s *Server) patchListing(w http.ResponseWriter, r *http.Request) {
	var in struct {
		Hidden *bool `json:"hidden"`
	}
	if !decodeBody(w, r, &in) {
		return
	}
	if in.Hidden == nil {
		writeError(w, http.StatusBadRequest, "hidden is required")
		return
	}
	s.writeStoreResult(w, s.Store.SetListingHidden(r.Context(), chi.URLParam(r, "id"), s.viewer(r), *in.Hidden))
}

func (s *Server) deleteSession(w http.ResponseWriter, r *http.Request) {
	s.writeStoreResult(w, s.Store.DeleteSession(r.Context(), chi.URLParam(r, "id"), s.viewer(r)))
}

func (s *Server) writeStoreResult(w http.ResponseWriter, err error) {
	switch {
	case err == nil:
		w.WriteHeader(http.StatusNoContent)
	case errors.Is(err, store.ErrSessionNotFound):
		writeError(w, http.StatusNotFound, "session not found")
	case errors.Is(err, store.ErrNotAllowed):
		writeError(w, http.StatusForbidden, "not allowed")
	default:
		writeError(w, http.StatusInternalServerError, "failed to update session")
	}
}
