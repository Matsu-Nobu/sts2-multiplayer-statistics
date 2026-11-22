package store

import (
	"reflect"
	"testing"
	"time"
)

var statsNow = time.Date(2026, 11, 23, 0, 0, 0, 0, time.UTC)

func ptr[T any](v T) *T { return &v }

// game は、FOOL でソロ、Standard、アセンション 0、v0.113.1 の勝ったゲームを作り、mod で変える。
func game(id string, mod func(g *GameSummary)) GameSummary {
	g := GameSummary{
		SessionID: id, CreatedAt: "2026-11-20T00:00:00Z", CharacterID: ptr("FOOL"), CharacterName: ptr("フール"),
		GameMode: ptr("Standard"), Ascension: ptr(0), GameVersion: ptr("v0.113.1"), Outcome: "victory", FinalFloor: ptr(48),
		Partners: []Player{},
		gameDetail: gameDetail{MaxActIndex: 2, CardOffers: []CardOffer{}, Relics: []Model{}, Fights: []Fight{},
			RestChoices: []string{}, Ancients: []AncientVisit{}, Events: []EventVisit{}},
	}
	if mod != nil {
		mod(&g)
	}
	return g
}

func statsGames(t *testing.T, games []GameSummary, f StatsFilter) int {
	t.Helper()
	st := AggregatePlayerStats(games, f, statsNow).Stats
	if st == nil {
		return 0
	}
	return st.Games
}

func TestAggregatePlayerStats_Filters(t *testing.T) {
	games := []GameSummary{
		game("base", nil),
		game("version", func(g *GameSummary) { g.GameVersion = ptr("v0.114.0") }),
		game("no-version", func(g *GameSummary) { g.GameVersion = nil }),
		game("mode", func(g *GameSummary) { g.GameMode = ptr("Blitz") }),
		game("asc", func(g *GameSummary) { g.Ascension = ptr(5) }),
		game("multi", func(g *GameSummary) { g.Partners = []Player{{SteamID: "p1"}} }),
		game("old", func(g *GameSummary) { g.CreatedAt = "2026-10-01T00:00:00Z" }),
	}
	cases := []struct {
		name string
		f    StatsFilter
		want int
	}{
		{"条件が無ければすべて", StatsFilter{}, 7},
		{"バージョン", StatsFilter{Version: "v0.114.0"}, 1},
		{"バージョンの記録が無いゲーム", StatsFilter{Version: UnknownVersion}, 1},
		{"ゲームモード", StatsFilter{Mode: "Blitz"}, 1},
		{"アセンション", StatsFilter{Ascension: "5"}, 1},
		{"ソロ", StatsFilter{Players: "solo"}, 6},
		{"マルチプレイ", StatsFilter{Players: "multi"}, 1},
		{"直近 30 日", StatsFilter{Period: "30d"}, 6},
	}
	for _, c := range cases {
		t.Run(c.name, func(t *testing.T) {
			if got := statsGames(t, games, c.f); got != c.want {
				t.Fatalf("games=%d want %d", got, c.want)
			}
		})
	}
}

func TestAggregatePlayerStats_Options(t *testing.T) {
	games := []GameSummary{
		game("a", func(g *GameSummary) { g.GameVersion = ptr("v0.9.1"); g.Ascension = ptr(5); g.GameMode = ptr("Blitz") }),
		game("b", func(g *GameSummary) { g.GameVersion = nil }),
		game("c", func(g *GameSummary) {
			g.GameVersion = ptr("v0.10.0")
			g.CharacterID = ptr("REGENT")
			g.CharacterName = ptr("リージェント")
		}),
		game("d", func(g *GameSummary) {
			g.GameVersion = ptr("v0.10.0")
			g.CharacterID = ptr("REGENT")
			g.CharacterName = ptr("リージェント")
		}),
	}
	res := AggregatePlayerStats(games, StatsFilter{Mode: "Standard"}, statsNow)

	t.Run("絞り込む前のゲーム数", func(t *testing.T) {
		if res.GameCount != 4 {
			t.Fatalf("game_count=%d", res.GameCount)
		}
	})
	t.Run("バージョンは新しい順で、記録の無いものは最後", func(t *testing.T) {
		want := []VersionOption{{"v0.10.0", 2}, {"v0.9.1", 1}, {UnknownVersion, 1}}
		if !reflect.DeepEqual(res.Options.Versions, want) {
			t.Fatalf("versions=%+v", res.Options.Versions)
		}
	})
	t.Run("ゲームモードは出てきた順、アセンションは小さい順", func(t *testing.T) {
		if !reflect.DeepEqual(res.Options.Modes, []string{"Blitz", "Standard"}) || !reflect.DeepEqual(res.Options.Ascensions, []int{0, 5}) {
			t.Fatalf("modes=%v ascensions=%v", res.Options.Modes, res.Options.Ascensions)
		}
	})
	t.Run("キャラクターは絞り込んだ後のゲーム数の多い順で、先頭を集計する", func(t *testing.T) {
		want := []CharacterOption{{"REGENT", "リージェント", 2}, {"FOOL", "フール", 1}}
		if !reflect.DeepEqual(res.Options.Characters, want) || *res.Character != "REGENT" || res.Stats.Games != 2 {
			t.Fatalf("characters=%+v character=%v", res.Options.Characters, *res.Character)
		}
	})
	t.Run("選んだキャラクターを集計する", func(t *testing.T) {
		r := AggregatePlayerStats(games, StatsFilter{Character: "FOOL", Mode: "Standard"}, statsNow)
		if *r.Character != "FOOL" || r.Stats.Games != 1 {
			t.Fatalf("character=%v games=%d", *r.Character, r.Stats.Games)
		}
	})
	t.Run("条件に合うゲームが無ければ集計しない", func(t *testing.T) {
		r := AggregatePlayerStats(games, StatsFilter{Mode: "Daily"}, statsNow)
		if r.Character != nil || r.Stats != nil || len(r.Options.Characters) != 0 {
			t.Fatalf("res=%+v", r)
		}
	})
}

func TestAggregatePlayerStats_Overview(t *testing.T) {
	games := []GameSummary{
		game("w1", func(g *GameSummary) { g.CreatedAt = "2026-11-01T00:00:00Z"; g.Ascension = ptr(3) }),
		game("l1", func(g *GameSummary) {
			g.CreatedAt = "2026-11-04T00:00:00Z"
			g.Outcome = "death"
			g.FinalFloor = ptr(20)
			g.MaxActIndex = 1
			g.Ascension = ptr(9)
		}),
		game("w2", func(g *GameSummary) { g.CreatedAt = "2026-11-02T00:00:00Z" }),
		game("w3", func(g *GameSummary) { g.CreatedAt = "2026-11-03T00:00:00Z"; g.FinalFloor = nil }),
	}
	st := AggregatePlayerStats(games, StatsFilter{}, statsNow).Stats

	t.Run("勝った数、平均到達階、勝利時の最高アセンション", func(t *testing.T) {
		if st.Wins != 3 || *st.AvgFloor != (48+48+20)/3.0 || *st.BestAscension != 3 {
			t.Fatalf("wins=%d avg=%v best=%v", st.Wins, *st.AvgFloor, *st.BestAscension)
		}
	})
	t.Run("最長連勝はゲームを始めた順に数える", func(t *testing.T) {
		if st.LongestStreak != 3 {
			t.Fatalf("streak=%d", st.LongestStreak)
		}
	})
	t.Run("Act の突破率", func(t *testing.T) {
		want := []ActRate{{1, 1}, {2, 0.75}, {3, 0.75}}
		if !reflect.DeepEqual(st.Acts, want) {
			t.Fatalf("acts=%+v", st.Acts)
		}
	})
	t.Run("死亡時の階", func(t *testing.T) {
		if !reflect.DeepEqual(st.DeathFloors, []FloorCount{{20, 1}}) {
			t.Fatalf("death_floors=%+v", st.DeathFloors)
		}
	})
}

func TestAggregatePlayerStats_Sections(t *testing.T) {
	games := []GameSummary{
		game("win", func(g *GameSummary) {
			g.Partners = []Player{{SteamID: "p1", DisplayName: "agapants"}}
			g.DamageShare = ptr(0.6)
			g.Relics = []Model{{ID: "R1", Name: "R1_NAME"}}
			g.Fights = []Fight{{ModelID: "SLUGS", ModelName: "", RoomType: "Monster", DamageTaken: 10}}
			g.RestChoices = []string{"HEAL", "SMITH", "SMITH"}
			g.Ancients = []AncientVisit{{ModelID: "NEOW", ModelName: "ネオー", Options: []AncientOption{{"A", true}, {"B", false}}}}
			g.Events = []EventVisit{{ModelID: "SHRINE", ModelName: "祠", Choices: []string{"PRAY"}}}
		}),
		game("loss", func(g *GameSummary) {
			g.Outcome = "death"
			g.Partners = []Player{{SteamID: "p1", DisplayName: "agapants"}}
			g.Relics = []Model{{ID: "R1", Name: "R1_NAME"}, {ID: "R2", Name: "R2_NAME"}}
			g.Fights = []Fight{{ModelID: "SLUGS", RoomType: "Monster", DamageTaken: 30, Died: true}}
			g.Ancients = []AncientVisit{{ModelID: "NEOW", ModelName: "ネオー", Options: []AncientOption{{"B", true}, {"C", false}}}}
		}),
		game("solo", nil),
	}
	st := AggregatePlayerStats(games, StatsFilter{}, statsNow).Stats

	t.Run("レリックは持っていたゲームの数と勝った数", func(t *testing.T) {
		want := []*RelicRow{{"R1", "R1_NAME", 2, 1}, {"R2", "R2_NAME", 1, 0}}
		if !reflect.DeepEqual(st.Relics, want) {
			t.Fatalf("relics=%+v", st.Relics)
		}
	})
	t.Run("敵は名前が無ければ ID を名前にする", func(t *testing.T) {
		want := []*EnemyRow{{"SLUGS", "SLUGS", "Monster", 2, 40, 1}}
		if !reflect.DeepEqual(st.Enemies, want) {
			t.Fatalf("enemies=%+v", st.Enemies)
		}
	})
	t.Run("休憩所の選択は多い順", func(t *testing.T) {
		if !reflect.DeepEqual(st.Rest, []RestCount{{"SMITH", 2}, {"HEAL", 1}}) {
			t.Fatalf("rest=%+v", st.Rest)
		}
	})
	t.Run("エンシェントは祝福ごとに提示回数・選択回数・勝った数", func(t *testing.T) {
		want := []*VisitGroup{{ID: "NEOW", Name: "ネオー", Visits: 2, Rows: []*ChoiceRow{{"A", 1, 1, 1}, {"B", 2, 1, 0}, {"C", 1, 0, 0}}}}
		if !reflect.DeepEqual(st.Ancients, want) {
			t.Fatalf("ancients=%+v", st.Ancients[0].Rows)
		}
	})
	t.Run("イベントは選んだ選択肢ごと", func(t *testing.T) {
		want := []*VisitGroup{{ID: "SHRINE", Name: "祠", Visits: 1, Rows: []*ChoiceRow{{"PRAY", 0, 1, 1}}}}
		if !reflect.DeepEqual(st.Events, want) {
			t.Fatalf("events=%+v", st.Events)
		}
	})
	t.Run("ソロとマルチプレイの勝率、一緒に遊んだ人", func(t *testing.T) {
		want := []*PartnerRow{{"p1", "agapants", 2, 1, 0.6, 1}}
		if st.Solo != (WinCount{1, 1}) || st.Multi != (WinCount{2, 1}) || !reflect.DeepEqual(st.Partners, want) {
			t.Fatalf("solo=%+v multi=%+v partners=%+v", st.Solo, st.Multi, st.Partners)
		}
	})
}

func TestAggregatePlayerStats_Cards(t *testing.T) {
	games := []GameSummary{
		// Act 1 で A を 2 回提示されて 1 回取り、Act 2 でも A を取って、Act 2 で死ぬ
		game("loss", func(g *GameSummary) {
			g.Outcome = "death"
			g.MaxActIndex = 1
			g.CardOffers = []CardOffer{{ID: "A", Name: "A_NAME", Offered: 2, Picked: 1, Act: 0}, {ID: "B", Name: "B_NAME", Offered: 1, Act: 0},
				{ID: "A", Name: "A_NAME", Offered: 1, Picked: 1, Act: 1}}
		}),
		// Act 1 で B を取って勝つ。Act 4 の提示は、表に出す Act より先なので数えない
		game("win", func(g *GameSummary) {
			g.MaxActIndex = 2
			g.CardOffers = []CardOffer{{ID: "B", Name: "B_NAME", Offered: 1, Picked: 1, Act: 0}, {ID: "C", Name: "C_NAME", Offered: 1, Picked: 1, Act: 3}}
		}),
	}
	st := AggregatePlayerStats(games, StatsFilter{}, statsNow).Stats

	t.Run("カードごとに Act ごとの提示・取得・取ったゲーム・突破したゲーム", func(t *testing.T) {
		want := []*CardRow{
			{ID: "A", Name: "A_NAME", Games: 1, Wins: 0, Offered: []int{2, 1, 0}, Picks: []int{1, 1, 0}, PickedGames: []int{1, 1, 0}, Cleared: []int{1, 0, 0}},
			{ID: "B", Name: "B_NAME", Games: 1, Wins: 1, Offered: []int{2, 0, 0}, Picks: []int{1, 0, 0}, PickedGames: []int{1, 0, 0}, Cleared: []int{1, 0, 0}},
		}
		if !reflect.DeepEqual(st.Cards, want) {
			for _, c := range st.Cards {
				t.Logf("%+v", *c)
			}
			t.Fatal("cards differ")
		}
	})
	t.Run("1 行目は Act に進んだゲームと、そのうち突破したゲーム", func(t *testing.T) {
		want := CardTotal{Games: 2, Wins: 1, PickedGames: []int{2, 2, 1}, Cleared: []int{2, 1, 1}}
		if !reflect.DeepEqual(st.CardTotal, want) {
			t.Fatalf("card_total=%+v", st.CardTotal)
		}
	})
}

func TestAggregatePlayerStats_NameOrder(t *testing.T) {
	games := []GameSummary{game("g", func(g *GameSummary) {
		g.Ancients = []AncientVisit{
			{ModelID: "PAEL", ModelName: "パエル", Options: []AncientOption{}},
			{ModelID: "VAKUU", ModelName: "ヴァクー", Options: []AncientOption{}},
		}
	})}
	st := AggregatePlayerStats(games, StatsFilter{}, statsNow).Stats

	t.Run("出会った回数が同じなら、名前の日本語の辞書順", func(t *testing.T) {
		if st.Ancients[0].ID != "VAKUU" || st.Ancients[1].ID != "PAEL" {
			t.Fatalf("ancients=%s,%s", st.Ancients[0].ID, st.Ancients[1].ID)
		}
	})
}
