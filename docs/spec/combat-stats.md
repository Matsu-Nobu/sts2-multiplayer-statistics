# 戦闘統計画面 仕様

`SessionView` 上部タブ「戦闘統計」の挙動を定義する。実装は `web/src/components/SessionView.svelte` (タブ + 戦闘セレクタ)、`web/src/components/AllCombatsView.svelte` (全体集計)、`web/src/components/CombatView.svelte` (個別戦闘)。

集計ロジックは `web/src/lib/aggregate.ts` (events → CombatInfo[]、turn / cum 累計)、`web/src/lib/rdps.ts` (rDPS / rMit 観測ベース算出)。

---

## 1. ナビゲーション

`SessionView` 上部タブ:
- `戦闘統計` (`combats`) — このドキュメントの対象
- `ラン全体` (`run`) — `spec/run-overview.md` 参照

戦闘統計タブ内のサブセレクタ (プルダウン):
- `全体（N戦闘）` — `AllCombatsView`
- 各戦闘 — `CombatView` (combat_index 指定)

ラベル形式: `${ordinal}. ${encounter_name} [Elite|Boss]?`
ordinal は出現順 1 始まり。

---

## 2. AllCombatsView (累計表示)

全戦闘合計のサマリ + 全戦闘横断のテーブル。

### 2.1 StatCard 群 (累計)

プレイヤーごとに以下の数値カード:
- 与ダメージ
- 被ダメージ
- 有効ブロック (ダメージ吸収量)
- カード使用枚数 / カードドロー枚数
- エナジー使用量
- ポーション使用回数
- カード単価ダメージ (与ダメ / カード使用枚数)
- エナジー単価ダメージ (与ダメ / エナジー使用量)
- 最大単発ダメージ + 使用カード名

各 StatCard には `STAT_HELP` の説明文が tooltip で出る。

### 2.2 累計テーブル

- カード別累計テーブル: `card_id` ごとの play_count / total_damage / max_single_hit / debuffs_applied
- デバフ付与テーブル: `power_id` ごとの累計付与量 (player x power 行列)
- 貢献スコア (rDPS / rMit): `RdpsPanel` / `RmitPanel`

詳細は `web/src/components/CardTable.svelte`, `DebuffTable.svelte`, `RdpsPanel.svelte`, `RmitPanel.svelte`。

### 2.3 戦闘横断ビュー

- 戦闘ごとの簡易統計を表で見せる
- 戦闘間の比較・推移把握用

---

## 3. CombatView (個別戦闘表示)

選ばれた `combat_index` の 1 戦闘。

### 3.1 メタ情報

- encounter_name / room_type / 結果 (`勝利` / `敗北`、`combat_end.victory`。戦闘中のまま終わっていない戦闘は `進行中`) / ターン数

### 3.2 ターン推移 (PerTurnTable)

各ターン:
- 与ダメ / 被ダメ / 有効ブロック / エナジー使用 / カード使用 / カードドロー
- カード使用内訳 (turn 内の card_played 群)

最終ターンは累計値も併記。

### 3.3 カード別 (CardTable)

その戦闘の `card_played` を `source_card_id` で集計:
- play_count, damage_dealt, block_provided, max_single_hit, debuffs_applied

### 3.4 デバフ付与 (DebuffTable)

power × applier の行列:
- `power_changed` event を `power_id × applier` で集計

### 3.5 貢献スコア

- **rDPS** (Recount-style Damage Per Second 相当): デバフ・バフを撒いた人の按分 +、自分の素ダメと合算
  - 詳細: `web/src/lib/rdps.ts`
  - whitelist: VULNERABLE_POWER / POISON_POWER / DOOM_POWER / WEAK_POWER / STRENGTH_POWER
- **rMit**: 被ダメ削減への貢献 (味方付与ブロック等)

観測ベース (係数を仮定せず、実 damage event を辿って按分)。

---

## 4. データソース

ベースは `web/src/lib/aggregate.ts` の `buildCombatInfos(doc)` → `CombatInfo[]`。

各 `combat_index` について **最後の `combat_start` より後の event だけ** を使う (中断→再開で戦闘をやり直した場合、
中断前の分はゲーム上無かったことになっているため)。`combat_start` の無い `combat_index` は戦闘として扱わない。
さらに `buildRunTotals(combats)` で累計。

| 集計 | 元 event_type |
|---|---|
| ターン毎 与ダメ | `damage_dealt`, group by (combat_index, turn_number, player_id) |
| 戦闘毎 与ダメ | 同上 + combat_index 集計 |
| 被ダメ | `damage_received` |
| 有効ブロック | `block_gained` の差分 |
| カード使用 | `card_played` |
| カードドロー | `card_drawn` |
| エナジー使用 | `energy_spent` |
| デバフ付与 | `power_changed` の applier ベース集計 |
| ポーション使用 | `potion_used` |
| 最大単発 | `damage_dealt.payload.amount` の max + `source_card_id` |
| オーバーキル | `damage_dealt.payload.overkill_damage` |
| rDPS | `damage_dealt` の `active_on_dealer` / `active_on_target`。`source_appliers` があるもの (毒・Doom など相手に付けたデバフによるダメージ) は 100% を付与者にスタック比で配分 |

**相手に付けたデバフによるダメージ (`source_appliers` あり) は、与ダメージ・カード別の表・最大単発も、付与者ごとにスタック比で按分して数える** (rDPS と同じ数字になる)。毒はターン開始時の発動も、カード効果 (`Outbreak` 等) からの発動も同じ扱い。

mod 側で patch している hook と event_type の対応は [`spec/data-sources.md`](./data-sources.md) 参照。

---

## 5. 単一プレイ vs MP

- player_id ごとに集計。MP は per-player tab で切り替え。
- プレイヤー ID は mod が正規化して送る (シングルプレイでも Steam ID)。web 側の読み替えは無い。

---

## 6. 既知の制約

- 味方へのエナジー付与: 未追跡 (`PlayerCmd.GainEnergy` で取れることは確認済み。`redesign-v2.md` §2.4-4、v2 の後続)
- スター消費 / シャッフル回数: 未追跡
- クロスセッション統計（プレイヤー単位・カード単位の集計エンドポイント）: `roadmap.md` Phase 4
