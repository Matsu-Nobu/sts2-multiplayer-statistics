package store

import (
	"sort"
	"strconv"
	"strings"
	"time"

	"golang.org/x/text/collate"
	"golang.org/x/text/language"
)

// プレイヤーの統計の、1 キャラクター分の集計 (docs/spec/player-stats.md、docs/spec/api.md「GET /api/users/{steam_id}/stats」)。
// ゲームごとの集計 (GameSummary) を絞り込み、選んだキャラクターのゲームを足し合わせる。

// UnknownVersion は、バージョンの記録が無いゲームを選ぶときの version の値。
const UnknownVersion = "unknown"

// StatsFilter は統計の絞り込み。空の値は絞り込まない。
type StatsFilter struct {
	Character string
	Version   string
	Mode      string
	Ascension string
	Players   string // "solo" か "multi"
	Period    string // "30d"
}

type VersionOption struct {
	Key   string `json:"key"`
	Games int    `json:"games"`
}

type CharacterOption struct {
	Key   string `json:"key"`
	Name  string `json:"name"`
	Games int    `json:"games"`
}

type StatsOptions struct {
	Versions   []VersionOption   `json:"versions"`
	Modes      []string          `json:"modes"`
	Ascensions []int             `json:"ascensions"`
	Characters []CharacterOption `json:"characters"`
}

type ActRate struct {
	Act  int     `json:"act"` // 1 始まり
	Rate float64 `json:"rate"`
}

type FloorCount struct {
	Floor int `json:"floor"`
	Games int `json:"games"`
}

type RestCount struct {
	ID    string `json:"id"`
	Count int    `json:"count"`
}

// CardRow の配列はどれも Act ごと (0 始まり)。
type CardRow struct {
	ID          string `json:"id"`
	Name        string `json:"name"`
	Games       int    `json:"games"`
	Wins        int    `json:"wins"`
	Offered     []int  `json:"offered"`
	Picks       []int  `json:"picks"`
	PickedGames []int  `json:"picked_games"`
	Cleared     []int  `json:"cleared"`
}

// CardTotal はカードの表の 1 行目。PickedGames は Act に進んだゲームの数、Cleared はそのうちその Act を突破したゲームの数。
type CardTotal struct {
	Games       int   `json:"games"`
	Wins        int   `json:"wins"`
	PickedGames []int `json:"picked_games"`
	Cleared     []int `json:"cleared"`
}

type RelicRow struct {
	ID    string `json:"id"`
	Name  string `json:"name"`
	Games int    `json:"games"`
	Wins  int    `json:"wins"`
}

type EnemyRow struct {
	ID       string `json:"id"`
	Name     string `json:"name"`
	RoomType string `json:"room_type"`
	Fights   int    `json:"fights"`
	Damage   int    `json:"damage"`
	Deaths   int    `json:"deaths"`
}

type ChoiceRow struct {
	Title   string `json:"title"`
	Offered int    `json:"offered"`
	Chosen  int    `json:"chosen"`
	Wins    int    `json:"wins"`
}

type VisitGroup struct {
	ID     string       `json:"id"`
	Name   string       `json:"name"`
	Visits int          `json:"visits"`
	Rows   []*ChoiceRow `json:"rows"`
}

type WinCount struct {
	Games int `json:"games"`
	Wins  int `json:"wins"`
}

type PartnerRow struct {
	SteamID    string  `json:"steam_id"`
	Name       string  `json:"name"`
	Games      int     `json:"games"`
	Wins       int     `json:"wins"`
	ShareSum   float64 `json:"share_sum"`
	ShareGames int     `json:"share_games"`
}

type CharacterStats struct {
	Games         int           `json:"games"`
	Wins          int           `json:"wins"`
	AvgFloor      *float64      `json:"avg_floor"`
	BestAscension *int          `json:"best_ascension"`
	LongestStreak int           `json:"longest_streak"`
	Acts          []ActRate     `json:"acts"`
	DeathFloors   []FloorCount  `json:"death_floors"`
	Rest          []RestCount   `json:"rest"`
	Cards         []*CardRow    `json:"cards"`
	CardTotal     CardTotal     `json:"card_total"`
	Relics        []*RelicRow   `json:"relics"`
	Enemies       []*EnemyRow   `json:"enemies"`
	Ancients      []*VisitGroup `json:"ancients"`
	Events        []*VisitGroup `json:"events"`
	Solo          WinCount      `json:"solo"`
	Multi         WinCount      `json:"multi"`
	Partners      []*PartnerRow `json:"partners"`
}

type PlayerStats struct {
	GameCount int             `json:"game_count"`
	Options   StatsOptions    `json:"options"`
	Character *string         `json:"character"`
	Stats     *CharacterStats `json:"stats"`
}

func characterKey(g GameSummary) string {
	if g.CharacterID == nil {
		return ""
	}
	return *g.CharacterID
}

func characterName(g GameSummary) string {
	switch {
	case g.CharacterName != nil:
		return *g.CharacterName
	case g.CharacterID != nil:
		return *g.CharacterID
	}
	return "—"
}

func versionKey(g GameSummary) string {
	if g.GameVersion == nil {
		return UnknownVersion
	}
	return *g.GameVersion
}

func isWin(g GameSummary) bool { return g.Outcome == "victory" }

// clearedAct は、Act act (0 始まり) より先の Act に進んだか、勝ったか。
func clearedAct(g GameSummary, act int) bool { return isWin(g) || g.MaxActIndex > act }

func orEmpty(s *string) string {
	if s == nil {
		return ""
	}
	return *s
}

// nameLess は、名前を辞書順で比べる。同じ数の行を名前の順に並べるときに使う。
// 仮名は五十音順、漢字は文字コードの順になる。
// language.Japanese の照合順序は、x/text では仮名を五十音順に並べないので使わない。
// collate.Collator は同時に複数のゴルーチンから使えないので、集計のたびに作る。
type nameLess func(a, b string) bool

func newNameLess() nameLess {
	c := collate.New(language.Und)
	return func(a, b string) bool { return c.CompareString(a, b) < 0 }
}

// AggregatePlayerStats は、games (新しい順) を f で絞り込み、1 キャラクター分の集計を返す。now は「直近 30 日」の基準。
func AggregatePlayerStats(games []GameSummary, f StatsFilter, now time.Time) PlayerStats {
	less := newNameLess()
	out := PlayerStats{GameCount: len(games), Options: statsOptions(games)}
	since := now.Add(-30 * 24 * time.Hour)
	filtered := []GameSummary{}
	for _, g := range games {
		if f.Version != "" && versionKey(g) != f.Version ||
			f.Mode != "" && orEmpty(g.GameMode) != f.Mode ||
			f.Ascension != "" && (g.Ascension == nil || strconv.Itoa(*g.Ascension) != f.Ascension) ||
			f.Players != "" && (f.Players == "multi") != (len(g.Partners) > 0) {
			continue
		}
		if f.Period == "30d" {
			t, err := time.Parse(time.RFC3339, g.CreatedAt)
			if err != nil || t.Before(since) {
				continue
			}
		}
		filtered = append(filtered, g)
	}
	out.Options.Characters = characterOptions(filtered, less)
	if len(out.Options.Characters) == 0 {
		return out
	}
	key := out.Options.Characters[0].Key
	for _, c := range out.Options.Characters {
		if c.Key == f.Character {
			key = c.Key
		}
	}
	out.Character = &key
	mine := []GameSummary{}
	for _, g := range filtered {
		if characterKey(g) == key {
			mine = append(mine, g)
		}
	}
	st := characterStats(mine, less)
	out.Stats = &st
	return out
}

func statsOptions(games []GameSummary) StatsOptions {
	o := StatsOptions{Versions: []VersionOption{}, Modes: []string{}, Ascensions: []int{}, Characters: []CharacterOption{}}
	vIdx := map[string]int{}
	seenMode := map[string]bool{}
	seenAsc := map[int]bool{}
	for _, g := range games {
		k := versionKey(g)
		if i, ok := vIdx[k]; ok {
			o.Versions[i].Games++
		} else {
			vIdx[k] = len(o.Versions)
			o.Versions = append(o.Versions, VersionOption{Key: k, Games: 1})
		}
		if g.GameMode != nil && !seenMode[*g.GameMode] {
			seenMode[*g.GameMode] = true
			o.Modes = append(o.Modes, *g.GameMode)
		}
		if g.Ascension != nil && !seenAsc[*g.Ascension] {
			seenAsc[*g.Ascension] = true
			o.Ascensions = append(o.Ascensions, *g.Ascension)
		}
	}
	sort.SliceStable(o.Versions, func(i, j int) bool { return newerVersion(o.Versions[i].Key, o.Versions[j].Key) })
	sort.Ints(o.Ascensions)
	return o
}

// newerVersion は、a を b より前に並べるか (新しいバージョンが先で、UnknownVersion は最後)。
// 番号は "v" を除いて "." で区切った数で比べ、数でない部分は比べない。番号で決まらなければ、文字列の大きい方を先にする。
func newerVersion(a, b string) bool {
	if a == UnknownVersion || b == UnknownVersion {
		return b == UnknownVersion && a != UnknownVersion
	}
	pa, pb := versionParts(a), versionParts(b)
	for i := 0; i < len(pa) || i < len(pb); i++ {
		x, xok := partAt(pa, i)
		y, yok := partAt(pb, i)
		if xok && yok && x != y {
			return x > y
		}
	}
	return a > b
}

// versionParts の要素は、数に読めない部分なら nil。
func versionParts(v string) []*int {
	var out []*int
	for _, p := range strings.Split(strings.TrimPrefix(v, "v"), ".") {
		n, err := strconv.Atoi(p)
		if p == "" {
			n, err = 0, nil
		}
		if err != nil {
			out = append(out, nil)
			continue
		}
		out = append(out, &n)
	}
	return out
}

// partAt は i 番目の番号。i 番目が無ければ 0 とする。数に読めなければ ok は false。
func partAt(ps []*int, i int) (n int, ok bool) {
	if i >= len(ps) {
		return 0, true
	}
	if ps[i] == nil {
		return 0, false
	}
	return *ps[i], true
}

func characterOptions(games []GameSummary, less nameLess) []CharacterOption {
	out := []CharacterOption{}
	idx := map[string]int{}
	for _, g := range games {
		k := characterKey(g)
		if i, ok := idx[k]; ok {
			out[i].Games++
			continue
		}
		idx[k] = len(out)
		out = append(out, CharacterOption{Key: k, Name: characterName(g), Games: 1})
	}
	sort.SliceStable(out, func(i, j int) bool {
		if out[i].Games != out[j].Games {
			return out[i].Games > out[j].Games
		}
		return less(out[i].Name, out[j].Name)
	})
	return out
}

// ordered は、出てきた順を保ったまま、キーごとに 1 つの値を作る。
// 表の並べ替えで同じ値の行は、ここで出てきた順に並ぶ。
type ordered[T any] struct {
	idx  map[string]int
	list []*T
}

func newOrdered[T any]() *ordered[T] { return &ordered[T]{idx: map[string]int{}, list: []*T{}} }

func (o *ordered[T]) get(k string, make func() *T) *T {
	if i, ok := o.idx[k]; ok {
		return o.list[i]
	}
	o.idx[k] = len(o.list)
	v := make()
	o.list = append(o.list, v)
	return v
}

func characterStats(games []GameSummary, less nameLess) CharacterStats {
	st := CharacterStats{Games: len(games), Acts: []ActRate{}, DeathFloors: []FloorCount{}, Rest: []RestCount{}}
	floorSum, floorN := 0, 0
	for _, g := range games {
		if isWin(g) {
			st.Wins++
			if g.Ascension != nil && (st.BestAscension == nil || *g.Ascension > *st.BestAscension) {
				a := *g.Ascension
				st.BestAscension = &a
			}
		}
		if g.FinalFloor != nil {
			floorSum += *g.FinalFloor
			floorN++
		}
	}
	if floorN > 0 {
		v := float64(floorSum) / float64(floorN)
		st.AvgFloor = &v
	}

	byStart := append([]GameSummary(nil), games...)
	sort.SliceStable(byStart, func(i, j int) bool { return byStart[i].CreatedAt < byStart[j].CreatedAt })
	streak := 0
	for _, g := range byStart {
		if isWin(g) {
			streak++
		} else {
			streak = 0
		}
		st.LongestStreak = max(st.LongestStreak, streak)
	}

	relics := newOrdered[RelicRow]()
	enemies := newOrdered[EnemyRow]()
	ancients := newOrdered[VisitGroup]()
	events := newOrdered[VisitGroup]()
	partners := newOrdered[PartnerRow]()
	rest := newOrdered[RestCount]()
	deaths := map[int]int{}
	maxAct := 0
	for _, g := range games {
		w := 0
		if isWin(g) {
			w = 1
		}
		for _, x := range g.Relics {
			r := relics.get(x.ID, func() *RelicRow { return &RelicRow{ID: x.ID, Name: x.Name} })
			r.Games++
			r.Wins += w
		}
		for _, f := range g.Fights {
			r := enemies.get(f.ModelID, func() *EnemyRow {
				return &EnemyRow{ID: f.ModelID, Name: nameOr(f.ModelName, f.ModelID), RoomType: f.RoomType}
			})
			r.Fights++
			r.Damage += f.DamageTaken
			if f.Died {
				r.Deaths++
			}
		}
		if !isWin(g) && g.FinalFloor != nil {
			deaths[*g.FinalFloor]++
		}
		for _, c := range g.RestChoices {
			rest.get(c, func() *RestCount { return &RestCount{ID: c} }).Count++
		}
		for _, a := range g.Ancients {
			grp := ancients.get(a.ModelID, func() *VisitGroup {
				return &VisitGroup{ID: a.ModelID, Name: nameOr(a.ModelName, a.ModelID), Rows: []*ChoiceRow{}}
			})
			grp.Visits++
			for _, o := range a.Options {
				r := choiceRow(grp, o.Title)
				r.Offered++
				if o.Chosen {
					r.Chosen++
					r.Wins += w
				}
			}
		}
		for _, e := range g.Events {
			grp := events.get(e.ModelID, func() *VisitGroup {
				return &VisitGroup{ID: e.ModelID, Name: nameOr(e.ModelName, e.ModelID), Rows: []*ChoiceRow{}}
			})
			grp.Visits++
			for _, c := range e.Choices {
				r := choiceRow(grp, c)
				r.Chosen++
				r.Wins += w
			}
		}
		side := &st.Solo
		if len(g.Partners) > 0 {
			side = &st.Multi
		}
		side.Games++
		side.Wins += w
		for _, p := range g.Partners {
			r := partners.get(p.SteamID, func() *PartnerRow {
				return &PartnerRow{SteamID: p.SteamID, Name: nameOr(p.DisplayName, p.SteamID)}
			})
			r.Games++
			r.Wins += w
			if g.DamageShare != nil {
				r.ShareSum += *g.DamageShare
				r.ShareGames++
			}
		}
		maxAct = max(maxAct, g.MaxActIndex)
	}

	acts := 0
	if len(games) > 0 {
		acts = maxAct + 1
	}
	for i := 0; i < acts; i++ {
		n := 0
		for _, g := range games {
			if clearedAct(g, i) {
				n++
			}
		}
		st.Acts = append(st.Acts, ActRate{Act: i + 1, Rate: float64(n) / float64(len(games))})
	}
	for f, n := range deaths {
		st.DeathFloors = append(st.DeathFloors, FloorCount{Floor: f, Games: n})
	}
	sort.Slice(st.DeathFloors, func(i, j int) bool { return st.DeathFloors[i].Floor < st.DeathFloors[j].Floor })
	for _, r := range rest.list {
		st.Rest = append(st.Rest, *r)
	}
	sort.SliceStable(st.Rest, func(i, j int) bool { return st.Rest[i].Count > st.Rest[j].Count })
	byVisits := func(gs []*VisitGroup) []*VisitGroup {
		sort.SliceStable(gs, func(i, j int) bool {
			if gs[i].Visits != gs[j].Visits {
				return gs[i].Visits > gs[j].Visits
			}
			return less(gs[i].Name, gs[j].Name)
		})
		return gs
	}
	st.Relics, st.Enemies, st.Partners = relics.list, enemies.list, partners.list
	st.Ancients, st.Events = byVisits(ancients.list), byVisits(events.list)
	st.Cards = cardRows(games, acts)
	st.CardTotal = cardTotal(games, acts)
	return st
}

func nameOr(name, id string) string {
	if name != "" {
		return name
	}
	return id
}

func choiceRow(grp *VisitGroup, title string) *ChoiceRow {
	for _, r := range grp.Rows {
		if r.Title == title {
			return r
		}
	}
	r := &ChoiceRow{Title: title}
	grp.Rows = append(grp.Rows, r)
	return r
}

// cardRows はカードの表の行。acts は表に出す Act の数で、それより先の Act の提示は数えない。
func cardRows(games []GameSummary, acts int) []*CardRow {
	cards := newOrdered[CardRow]()
	for _, g := range games {
		pickedAny := map[string]bool{}
		pickedAct := make([]map[string]bool, acts)
		for i := range pickedAct {
			pickedAct[i] = map[string]bool{}
		}
		for _, c := range g.CardOffers {
			if c.Act >= acts {
				continue
			}
			r := cards.get(c.ID, func() *CardRow {
				return &CardRow{ID: c.ID, Name: c.Name,
					Offered: make([]int, acts), Picks: make([]int, acts), PickedGames: make([]int, acts), Cleared: make([]int, acts)}
			})
			r.Offered[c.Act] += c.Offered
			r.Picks[c.Act] += c.Picked
			if c.Picked > 0 {
				pickedAny[c.ID] = true
				pickedAct[c.Act][c.ID] = true
			}
		}
		for _, r := range cards.list {
			if pickedAny[r.ID] {
				r.Games++
				if isWin(g) {
					r.Wins++
				}
			}
			for act := 0; act < acts; act++ {
				if pickedAct[act][r.ID] {
					r.PickedGames[act]++
					if clearedAct(g, act) {
						r.Cleared[act]++
					}
				}
			}
		}
	}
	return cards.list
}

func cardTotal(games []GameSummary, acts int) CardTotal {
	t := CardTotal{Games: len(games), PickedGames: make([]int, acts), Cleared: make([]int, acts)}
	for _, g := range games {
		if isWin(g) {
			t.Wins++
		}
		for act := 0; act < acts; act++ {
			if g.MaxActIndex >= act {
				t.PickedGames[act]++
				if clearedAct(g, act) {
					t.Cleared[act]++
				}
			}
		}
	}
	return t
}
