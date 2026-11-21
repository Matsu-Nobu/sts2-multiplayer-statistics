package auth

import (
	"crypto/hmac"
	"crypto/sha256"
	"encoding/base64"
	"net/http"
	"strconv"
	"strings"
	"time"
)

// CookieName はログインの状態を持つ cookie の名前。
const CookieName = "sts2stats_auth"

// CookieMaxAge はログインの有効期間。
const CookieMaxAge = 30 * 24 * time.Hour

// Signer は「Steam ID と有効期限」を HMAC-SHA256 で署名した cookie を作り、検証する。
type Signer struct{ Key []byte }

func (s Signer) sign(payload string) string {
	m := hmac.New(sha256.New, s.Key)
	m.Write([]byte(payload))
	return base64.RawURLEncoding.EncodeToString(m.Sum(nil))
}

// Issue は steamID のログインの cookie を作る。secure は https で配信しているとき true。
func (s Signer) Issue(steamID string, now time.Time, secure bool) *http.Cookie {
	exp := now.Add(CookieMaxAge).Unix()
	payload := steamID + "." + strconv.FormatInt(exp, 10)
	return &http.Cookie{
		Name: CookieName, Value: payload + "." + s.sign(payload), Path: "/",
		MaxAge: int(CookieMaxAge.Seconds()), HttpOnly: true, Secure: secure, SameSite: http.SameSiteLaxMode,
	}
}

// Clear はログアウト用の、cookie を消す指示。
func Clear(secure bool) *http.Cookie {
	return &http.Cookie{Name: CookieName, Value: "", Path: "/", MaxAge: -1, HttpOnly: true, Secure: secure, SameSite: http.SameSiteLaxMode}
}

// SteamIDFrom は、リクエストの cookie の署名と有効期限を確かめて Steam ID を返す。ログインしていなければ "".
func (s Signer) SteamIDFrom(r *http.Request, now time.Time) string {
	c, err := r.Cookie(CookieName)
	if err != nil {
		return ""
	}
	parts := strings.Split(c.Value, ".")
	if len(parts) != 3 {
		return ""
	}
	payload := parts[0] + "." + parts[1]
	if !hmac.Equal([]byte(parts[2]), []byte(s.sign(payload))) {
		return ""
	}
	exp, err := strconv.ParseInt(parts[1], 10, 64)
	if err != nil || now.Unix() > exp {
		return ""
	}
	return parts[0]
}
