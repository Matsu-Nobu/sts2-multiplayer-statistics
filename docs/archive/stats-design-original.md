# 統計情報設計ドキュメント

「何を表示するか」→「何を収集するか」の順に定義する。
収集データの shape と API は `API.md` が正、本ドキュメントは表示要件と指標定義を扱う。

---

## 1. 統計項目 × 時間軸マトリクス

### 凡例
- ◎ … 主要表示（グラフ化）
- ○ … 補助表示（数値）
- △ … 導出値（他の値から計算）
- — … 表示しない（粒度が合わない）

| 統計項目 | ターン毎 | 戦闘毎 | 累計 |
|---------|---------|--------|------|
| **与ダメージ** | ◎ 折れ線 | ◎ 棒グラフ | ◎ 棒グラフ |
| **カードあたり与ダメージ** | △ | ◎ 数値カード | ◎ 数値カード |
| **エナジーあたり与ダメージ** | △ | ◎ 数値カード | ◎ 数値カード |
| **シールド獲得量（自分）** | ○ | ◎ 棒グラフ | ◎ 棒グラフ |
| **被ダメージ** | ○ | ◎ 棒グラフ | ◎ 棒グラフ |
| **エナジー使用量** | ○ | ○ | ○ |
| **カード使用枚数** | ○ | ○ | ○ |
| **カードドロー枚数** | ○ | ○ | ○ |
| **状態異常付与（種別ごと）** | — | ◎ テーブル | ◎ テーブル |
| **味方へのシールド付与** | — | ○ | ○ |
| **貢献スコア（rDPS / rMit）** | — | ◎ 棒グラフ | ◎ 棒グラフ |
| **オーバーキル** | — | ○ | ○ |

### ランキング（累計のみ）

| ランキング項目 | 内容 |
|--------------|------|
| 最大単発ダメージ | その 1 ヒットで出した最大ダメージと使用カード名 |
| カード別総ダメージ | 戦闘全体を通じて最もダメージを稼いだカード上位 N 枚 |
| カード別平均ダメージ | 1 プレイあたり平均ダメージが高いカード上位 N 枚 |
| 最多使用カード | プレイ回数が多いカード上位 N 枚 |
| 最多デバフ付与カード | 付与スタック数が多いカード上位 N 枚 |

---

## 2. 貢献スコア（rDPS / rMit）

「ダメージを直接出さないサポート役」もフェアに評価するため、デバフ・バフの寄与を加味した貢献度スコアを WebUI 側で算定する。

### 設計

- 各 `damage_dealt` / `damage_received` イベントは、発火時の active power と applier を埋め込んで送られる（`active_on_target` / `active_on_dealer`、各 power に `appliers[]`）。
- 複数人が同じデバフ（Vulnerable / Poison 等）を撒いている場合、stacks 比で按分する。
- 計算ロジックは `web/src/lib/rdps.ts` 等にあり、観測ベース（係数を仮定せず、実際の damage event を辿って按分）で算出する。
- whitelist された power: `VULNERABLE_POWER` / `POISON_POWER` / `DOOM_POWER` / `WEAK_POWER` / `STRENGTH_POWER`。

### 参考

- [Skada Damage Meter (STS2 Nexus)](https://www.nexusmods.com/slaythespire2/mods/33) — closed-source、rDPS 二段階アルゴリズム
- [STS2-DamageTracker (GitHub)](https://github.com/BAIGUANGMEI/STS2-DamageTracker) — 貢献度なし、生ダメージのみ
- [FFLogs rDPS Guide](https://www.fflogs.com/help/rdps) — 元ネタの定式化

### 味方エナジー付与
STS2 に専用 hook が確認できないため未追跡。`EnergyNextTurnPower` 等の特定 Power 経由で間接追跡できる可能性は残る（ROADMAP 候補）。

---

## 3. 収集データの形

ターン集計値ではなく**生イベント列**で収集する（Phase 3.5 でこの形に移行）。各 event の shape は `API.md` の event_type カタログを正とし、ここでは「表示要件→必要な event」の対応のみ示す。

### 表示要件 → 必要な event_type

| 表示 | 元になる event_type |
|------|---------------------|
| ターン毎 与ダメージ折れ線 | `damage_dealt` を `(combat_index, turn_number, player_id)` で集計 |
| 戦闘毎 与ダメ／被ダメ／シールド棒 | `damage_dealt` / `damage_received` / `block_gained` を `(combat_index, player_id)` で集計 |
| カード別ダメージテーブル | `damage_dealt` の `source_card_id` 別集計 |
| カード使用枚数・ドロー枚数 | `card_played` / `card_drawn` |
| エナジー使用量 | `energy_spent` |
| 状態異常付与テーブル | `power_changed` を `power_id` × `applier (player_id)` で集計 |
| 味方シールド付与 | `block_gained` の `from_player` |
| ポーション使用 | `potion_used` |
| 貢献スコア (rDPS / rMit) | `damage_dealt` / `damage_received` の `active_on_target` / `active_on_dealer` |
| オーバーキル | `damage_dealt.payload.overkill_damage` |
| 最大単発ダメージ | `damage_dealt.payload.amount` の max（`source_card_id` 付き） |

戦闘・run のメタは `combat_start` / `combat_end` / `run_start` / `run_end` で運ぶ。

---

## 4. 使用するゲーム Hook

mod 側で patch している hook と、生成する event_type の対応:

| Hook | 生成 event |
|------|-----------|
| `BeforeCombatStart` | `combat_start` |
| `AfterCombatEnd` | `combat_end` |
| `AfterTurnEnd(side=Player)` | （ターン確定点。バッファ flush） |
| `AfterDamageGiven` + `ModifyDamage` (post) | `damage_dealt`（overkill / blocked_damage は ModifyDamage post の HP snapshot で確定） |
| `BeforeDamageReceived` / `AfterDamageReceived` | `damage_received` |
| `AfterBlockGained` | `block_gained` |
| `AfterEnergySpent` | `energy_spent` |
| `BeforeCardPlayed` / `AfterCardPlayed` | `card_played`（`CardPlayedScope` の出入り） |
| `AfterCardDrawn` | `card_drawn` |
| `AfterPowerAmountChanged` | `power_changed` |
| `AfterPotionUsed` | `potion_used` |

加えて、間接ダメージ・パワー由来ブロックの帰属のため以下を Harmony patch している（詳細は `DESIGN.md`「間接ダメージ・パワー由来ブロックの帰属」と `API.md`「予約 source_card_id」）:

- `PoisonPower` / `DoomPower`
- `LightningOrb` の Evoke（手動 / 自動）/ Passive
- `ThornsPower` / `FlameBarrierPower`
- `RampartPower` / `BlockNextTurnPower`

---

## 5. API スキーマ

`POST /sessions/{id}/events` の bulk 投稿に一本化済み（旧 `POST /sessions/{id}/turns` は 410 Gone）。
event 別の payload shape は `API.md` を参照。

---

## 6. 実装スコープ

### 実装済み

- damage_dealt（overkill / blocked / total / was_target_killed 含む）
- damage_received
- block_gained（自分 / 味方付与の判別、Power 由来ブロックの帰属）
- energy_spent
- card_played / card_drawn
- power_changed（applier 解決、複数 applier の stacks 内訳）
- potion_used
- combat_start / combat_end / run_start / run_end
- 間接ダメージの source 帰属（Poison / Doom / Lightning 3 種 / Thorns / Flame Barrier / Rampart / BlockNextTurn）
- カード別集計（WebUI 側、`source_card_id` ベース）
- デバフ付与テーブル（WebUI 側）
- ランキング（最大単発ダメ・カード別累計）
- 貢献スコア rDPS / rMit（WebUI 側、観測ベース）

### 未着手 / ROADMAP 側

- 味方へのエナジー付与の追跡（hook 未確認）
- スター消費 / シャッフル回数等の補助統計
- クロスセッション統計（プレイヤー単位・カード単位の集計エンドポイント） — `ROADMAP.md` Phase 4

---

## 関連ドキュメント

- `API.md` — event_type カタログと payload shape の正
- `DESIGN.md` — アーキテクチャ・mod 内部実装
- `ROADMAP.md` — 将来拡張
