// Package steam は Steam Web API への問い合わせ (フレンドかどうか。docs/spec/user-pages.md「Steam のフレンドかどうか」)。
package steam

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"net/http"
	"net/url"
	"sync"
	"time"
)

// FriendChecker は、2 人が Steam のフレンドかどうかを答える。
type FriendChecker interface {
	AreFriends(ctx context.Context, owner, viewer string) (bool, error)
}

// errPrivateList は、フレンド一覧が非公開で取れないとき (Steam は 401 を返す)。
var errPrivateList = errors.New("friend list is private")

const cacheTTL = 10 * time.Minute

// WebAPI は Steam Web API の ISteamUser/GetFriendList で調べる。結果は 2 人の組ごとに cacheTTL の間覚える。
type WebAPI struct {
	Key     string
	Client  *http.Client
	BaseURL string // テストで差し替える。空なら https://api.steampowered.com

	mu    sync.Mutex
	cache map[[2]string]cached
}

type cached struct {
	friends bool
	at      time.Time
}

// AreFriends は、持ち主のフレンド一覧に見に来た人がいるかを調べる。持ち主の一覧が非公開なら、見に来た人の一覧に持ち主がいるかを調べる。
// どちらも非公開なら false (フレンドと確かめられないので見せない)。
func (w *WebAPI) AreFriends(ctx context.Context, owner, viewer string) (bool, error) {
	k := [2]string{owner, viewer}
	if owner > viewer {
		k = [2]string{viewer, owner}
	}
	w.mu.Lock()
	if c, ok := w.cache[k]; ok && time.Since(c.at) < cacheTTL {
		w.mu.Unlock()
		return c.friends, nil
	}
	w.mu.Unlock()

	friends, err := w.listContains(ctx, owner, viewer)
	if errors.Is(err, errPrivateList) {
		friends, err = w.listContains(ctx, viewer, owner)
		if errors.Is(err, errPrivateList) {
			friends, err = false, nil
		}
	}
	if err != nil {
		return false, err
	}
	w.mu.Lock()
	if w.cache == nil {
		w.cache = map[[2]string]cached{}
	}
	w.cache[k] = cached{friends: friends, at: time.Now()}
	w.mu.Unlock()
	return friends, nil
}

func (w *WebAPI) listContains(ctx context.Context, steamID, target string) (bool, error) {
	base := w.BaseURL
	if base == "" {
		base = "https://api.steampowered.com"
	}
	q := url.Values{"key": {w.Key}, "steamid": {steamID}, "relationship": {"friend"}}
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, base+"/ISteamUser/GetFriendList/v1/?"+q.Encode(), nil)
	if err != nil {
		return false, err
	}
	res, err := w.Client.Do(req)
	if err != nil {
		return false, fmt.Errorf("steam GetFriendList: %w", err)
	}
	defer res.Body.Close()
	if res.StatusCode == http.StatusUnauthorized {
		return false, errPrivateList
	}
	if res.StatusCode != http.StatusOK {
		return false, fmt.Errorf("steam GetFriendList: status %d", res.StatusCode)
	}
	var body struct {
		FriendsList struct {
			Friends []struct {
				SteamID string `json:"steamid"`
			} `json:"friends"`
		} `json:"friendslist"`
	}
	if err := json.NewDecoder(res.Body).Decode(&body); err != nil {
		return false, fmt.Errorf("steam GetFriendList: %w", err)
	}
	for _, f := range body.FriendsList.Friends {
		if f.SteamID == target {
			return true, nil
		}
	}
	return false, nil
}
