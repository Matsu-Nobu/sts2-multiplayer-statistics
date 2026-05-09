# ラン全体統計画面 (Run Overview)

戦闘単位の統計とは別に、48 階のランを通した推移を一望するビュー。

## ナビゲーション

WebUI のセッションページ上部に**タブ 2 つ**:

- **戦闘統計** (`combats`) — 既存の `AllCombatsView` / `CombatView`
- **ラン全体** (`run`) — 新規 `RunOverview.svelte`

タブ state は局所（URL 反映なし）。

## 画面構成（ラン全体タブ）

```
┌──────────────────────────────────────────────┐
│ ラン概要 (Header)                             │
│  - キャラ / 上昇 / シード                      │
│  - 結果 (victory / death / abandoned)         │
│  - 到達階 / 入手 relic 数 / 残 HP             │
├──────────────────────────────────────────────┤
│ HP 折れ線グラフ                                │
│  x: 階 (1..48)、y: 階入場時の HP              │
│  ノード: room_type ごとに絵文字 + 色          │
│  ホバー: ツールチップ                          │
│  クリック: 下のインライン詳細を展開            │
├──────────────────────────────────────────────┤
│ サブメトリクス推移 (HP グラフと共通 x 軸)       │
│  - 所持ゴールド推移                           │
│  - 最大 HP 推移                               │
├──────────────────────────────────────────────┤
│ インライン階詳細 (展開時)                       │
│  クリックされた階の room_type に応じた中身     │
└──────────────────────────────────────────────┘
```

## ノード装飾

| room_type | 絵文字 | 色 |
|---|---|---|
| Monster | ⚔️ | slate |
| Elite | 💀 | orange |
| Boss | 👑 | red |
| Event | ❓ | purple |
| Shop | 🏪 | yellow |
| RestSite | 🔥 | green |
| Treasure | 📦 | amber |

## ツールチップ内容

各ノードのホバーで表示:

```
階 N: <encounter_name or room_type>
HP X/Y → A/Y  (Δ -7)
被ダメ: 12   与ダメ: 28
入手: ストライク / 翡翠の置物
ゴールド: 99 → 119 (+20)
```

要素:
1. 階番号 + room_type / encounter 名
2. 入場時 HP/MaxHP → 退出時 HP/MaxHP（差分）
3. その階で被ダメ / 与ダメ合計
4. 入手 card / relic / potion
5. 消費 / 獲得 gold

退出時 HP は次の `room_entered.hp` から取る（最後の階のみ session の最終 HP / run_end）。

## インライン階詳細

クリックされた階に紐づく `events` をフィルタして表示:

| room_type | 表示 |
|---|---|
| Monster / Elite / Boss | 既存 `CombatView` をそのまま埋め込み |
| Event | HP / gold 変化のみ（選択肢ログは現状未対応） |
| Shop | `item_purchased` 群 + その階の `card_removed` |
| RestSite | `rest_action` + 同 floor の `card_upgraded` |
| Treasure | `reward_taken` (RelicReward) |

## データソース (events)

mod が emit する **新規 event_type**:

| event_type | scope | payload |
|---|---|---|
| `room_entered` | global | floor, act_index, room_type, room_class, hp, max_hp, gold |
| `hp_changed` | global | delta, current_hp, max_hp |
| `gold_changed` | global | current_gold |
| `act_entered` | global | act_index |
| `rest_action` | global | option (heal/smith), is_mimicked? |
| `item_purchased` | global | item_kind, card_id?, relic_id?, potion_id?, gold_spent |
| `reward_taken` | global | reward_kind, gold_amount, card_id?, potion_id?, relic_id? |
| `potion_obtained` | global | potion_id |
| `potion_discarded` | global | potion_id |
| `card_upgraded` | global | card_id, card_name |
| `card_removed` | global | card_id, card_name |

既存 (戦闘内) event は変更なし。

## 集計 (web 側)

`web/src/lib/runOverview.ts` で events から **per-floor サマリ**を導出:

```ts
interface FloorSummary {
  floor: number;
  act_index: number;
  room_type: string;       // Monster / Elite / Boss / Event / Shop / RestSite / Treasure
  encounter_name?: string; // 戦闘の場合
  hp_in: number;           // 入場時
  hp_out: number;          // 退出時 (= 次階の hp_in)
  max_hp_in: number;
  max_hp_out: number;
  gold_in: number;
  gold_out: number;
  damage_taken: number;    // この階で被ダメ合計
  damage_dealt: number;    // 与ダメ合計
  cards_obtained: { card_id, card_name }[];
  relics_obtained: { relic_id }[];
  potions_obtained: { potion_id }[];
  cards_removed: { card_id, card_name }[];
  cards_upgraded: { card_id, card_name }[];
}

function buildFloorSummaries(events: EventRecord[]): FloorSummary[]
```

## 既知の制約

- **Event の選択肢ログ**は未対応（hook 未調査）。後追加。
- **CardReward 選択肢のうち skip された card** は記録するが、UI では未表示。
- **古いセッション (room_entered 等が無いもの)** は「未対応」案内のみ。後方互換なし。
- **Smith でアップグレードしたカード**は `card_upgraded` で取れるが、Smith 直後でなく event 時系列順での照合が必要（同じ floor 上で `rest_action: smith` → `card_upgraded` の連続を探す）。
- **マルチプレイ複数人記録**: room_entered は host 視点。各 player の HP は `hp_changed.player_id` から個別追跡可能。グラフは「自分の HP」のみ表示する想定。

## 開発順序

1. ✅ Mod 側 events 実装
2. 🔄 ドキュメント (本書)
3. 🔄 WebUI 骨組み（タブ追加）
4. 🔄 HP グラフ
5. 🔄 ツールチップ
6. 🔄 インライン詳細
7. 🔄 仕上げ
