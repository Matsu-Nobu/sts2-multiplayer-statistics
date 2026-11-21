// Package auth は Steam の公式ログイン (OpenID 2.0) と、ログインの状態を持つ署名付き cookie を扱う
// (docs/spec/user-pages.md「ログイン」)。
package auth

import (
	"context"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"regexp"
	"strings"
	"time"
)

const steamOpenIDEndpoint = "https://steamcommunity.com/openid/login"

// LoginURL は、Steam のログイン画面の URL。承認すると returnTo に戻る。realm は returnTo を含むサイトの URL。
func LoginURL(realm, returnTo string) string {
	q := url.Values{
		"openid.ns":         {"http://specs.openid.net/auth/2.0"},
		"openid.mode":       {"checkid_setup"},
		"openid.return_to":  {returnTo},
		"openid.realm":      {realm},
		"openid.identity":   {"http://specs.openid.net/auth/2.0/identifier_select"},
		"openid.claimed_id": {"http://specs.openid.net/auth/2.0/identifier_select"},
	}
	return steamOpenIDEndpoint + "?" + q.Encode()
}

var claimedIDPattern = regexp.MustCompile(`^https://steamcommunity\.com/openid/id/([0-9]{17})$`)

// Verifier は、Steam から戻ってきたときの OpenID の値を Steam に問い合わせて検証し、Steam ID を返す。
// テストでは差し替える。
type Verifier func(ctx context.Context, params url.Values, expectedReturnTo string) (steamID string, err error)

var ErrInvalidLogin = errors.New("invalid steam login")

// VerifyWithSteam は、OpenID の値をそのまま check_authentication で Steam に送り、is_valid:true を確かめる。
// return_to が自分のコールバックの URL であること、ログインした先が Steam であることも確かめる。
func VerifyWithSteam(client *http.Client) Verifier {
	return func(ctx context.Context, params url.Values, expectedReturnTo string) (string, error) {
		if params.Get("openid.mode") != "id_res" {
			return "", ErrInvalidLogin
		}
		if params.Get("openid.op_endpoint") != steamOpenIDEndpoint {
			return "", ErrInvalidLogin
		}
		if rt := params.Get("openid.return_to"); rt != expectedReturnTo && !strings.HasPrefix(rt, expectedReturnTo+"?") {
			return "", ErrInvalidLogin
		}
		m := claimedIDPattern.FindStringSubmatch(params.Get("openid.claimed_id"))
		if m == nil {
			return "", ErrInvalidLogin
		}
		check := url.Values{}
		for k, v := range params {
			check[k] = v
		}
		check.Set("openid.mode", "check_authentication")
		req, err := http.NewRequestWithContext(ctx, http.MethodPost, steamOpenIDEndpoint, strings.NewReader(check.Encode()))
		if err != nil {
			return "", err
		}
		req.Header.Set("Content-Type", "application/x-www-form-urlencoded")
		res, err := client.Do(req)
		if err != nil {
			return "", fmt.Errorf("steam check_authentication: %w", err)
		}
		defer res.Body.Close()
		body, err := io.ReadAll(io.LimitReader(res.Body, 4096))
		if err != nil {
			return "", err
		}
		if res.StatusCode != http.StatusOK || !strings.Contains(string(body), "is_valid:true") {
			return "", ErrInvalidLogin
		}
		return m[1], nil
	}
}

// DefaultHTTPClient は Steam への問い合わせに使う。応答が無いときに待ち続けないよう時間を区切る。
var DefaultHTTPClient = &http.Client{Timeout: 10 * time.Second}
