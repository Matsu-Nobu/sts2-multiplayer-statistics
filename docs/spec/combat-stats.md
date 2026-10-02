# 戦闘統計画面 仕様

`SessionView` 上部タブ「戦闘統計」の挙動を定義する。実装は `web/src/components/SessionView.svelte` (タブ + 戦闘セレクタ)、`web/src/components/AllCombatsView.svelte` (全体集計)、`web/src/components/CombatView.svelte` (個別戦闘)。

集計ロジックは `web/src/lib/aggregate.ts` (events → CombatInfo[]、turn / cum 累計)、`web/src/lib/rdps.ts` (rDPS / rMit 観測ベース算出)。

---

> 画面の構成・ナビゲーション・配置は [`ui.md`](./ui.md) (2026-10-03) が正。本書の §1・§2 の配置の記述は古い。表示する値・集計方法は本書のまま。

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

**行はカード・レリック・ポーション (など、プレイヤーの持ち物) だけにする** (2026-10-03 確定)。パワーによるダメージ・ブロックは、そのパワーを付けた持ち物の行に足す:
- 相手に付けたデバフ (毒など) のダメージ: `source_appliers[].origin` ごとにスタック比で分けて、それぞれのカード等の行に足す
  (例: 毒 10 のうち 6 を感染爆発、4 をバウンドフラスコで付けていれば、毒ダメージの 6/10 を感染爆発、4/10 をバウンドフラスコに)
- 自分側のパワー (トゲ・プレート等) のダメージ・ブロック: `source_origin` のカード等の行に足す
- 付けたものが分からない (古いセッション・記録できなかった) 分だけ、パワー名の行で残す
- **カードの効果が直接発動させた、カード以外のもののダメージ・ブロック** は、そのカードが起こしたものとして扱う (2026-10-03 確定。`triggered_by`)。
  カードごとの個別対応はせず、次の共通の決まりで判定する:
  - 「カード以外のもの」のうち、**ゲームのイベントへの反応** (`AbstractModel` で宣言された Hook の上書き。例: カードを使うたびにダメージを与えるパワーの `AfterCardPlayed`、ターン開始時の毒の `AfterSideTurnStart`) は、そのもの自身の行為。カードのプレイ中に起きても、プレイ中のカードのものにしない
  - それ以外の処理 (毒の `Trigger`、オーブの `Evoke` / `Passive`、破滅の `DoomKill` など) が、カードのプレイ中に、反応の中からではなく呼ばれたら、そのカードの効果
  - 例: 感染爆発の毒の発動、デュアルキャスト等のオーブの解放、暗黒・テスラコイルのオーブの自動効果の発動、終末の日の破滅
  - 相手に付けたデバフ (毒など) のダメージのとき:
    - カードを使ったプレイヤーのカード別の表では、**発動したダメージの全量をそのカードの行に足す** (付けた人の内訳に関係なく)
    - ほかのプレイヤーのカード別の表では、その人の取り分 (スタック比) を、その人が毒を付けたカード等の行に足す
    - 与ダメージ・与ダメ貢献 (rDPS) は、これまでどおり付けた人ごとにスタック比で分ける。
      そのため、使ったプレイヤーのカードの行の合計は、その人の与ダメージより大きくなることがある
  - それ以外 (オーブ等) のダメージ・ブロックは、そのカードの行に足す
  - ターン開始時の毒など、カードのプレイ中でないダメージは、付けたカード等の行にスタック比で分ける (上の通り)

### 3.4 デバフ付与 (DebuffTable)

power × applier の行列:
- `power_changed` event を `power_id × applier` で集計

### 3.5 貢献スコア (2026-10-02 改訂)

観測ベース。係数を仮定せず、ゲームが実際に計算したダメージ補正を 1 つずつ記録して按分する。
対象はダメージ補正・HP 減少補正・ブロック。**カードの発動回数を増やす効果 (TagTeam 等) と、味方へのエナジー譲渡は扱わない** (2026-10-02 確定)。

#### 補正の記録 (mod、`modifications`)

1 ヒットごとに、ゲームの補正計算 (`Hook.ModifyDamage` / `Hook.ModifyHpLost`) で **値を変えたモデル** を、ゲームの計算順に 1 つずつ記録する
(ゲームが返す「値を変えたモデルの一覧」の各モデルに、同じ引数で補正計算をもう一度させて値を得る)。

| phase | 記録する値 (`value`) | 寄与の計算 (web) |
|---|---|---|
| `enchant` | カードのエンチャントによる増減量 | 増減量そのまま |
| `additive` (足し算。筋力・活力など) | 増減量 | 増減量そのまま |
| `multiplicative` (掛け算。弱体・挟撃・脱力・かばう等) | 倍率 | **対数で按分** (下記) |
| `cap` (上限。霊体等) | 上限で下がった量 (負) | そのまま |
| `hp_lost` (ブロック後の HP 減少補正。バッファー・霊体等) | 減少量の変化 (負なら軽減) | そのまま |

掛け算の寄与: 足し算の後の値を A、掛け算の後を M、各倍率を f_i として、寄与_i = L(M, A) × ln f_i
(L(M, A) = (M − A) / ln(M / A)、M = A なら A)。寄与の合計は必ず M − A になり、**掛ける順番に依らない**
(例: 10 に弱体 ×1.5 と挟撃 ×1.5 → 増えた 12.5 を 6.25 ずつ)。

各補正には、それが **誰の行為か** を付ける:
- パワー: 付与者ごとのスタック数 (`appliers`。全パワーが対象。v1 の 5 種類の制限は廃止)
- レリック・エンチャント: 持ち主 (`owner`)

#### rDPS (与ダメージへの貢献)

1 ヒットの有効ダメージ E (= HP に通った分 + ブロックを削った分) を配る。
1. 相手に付けたデバフ (毒・Doom 等) によるダメージ (`source_appliers` あり) → 付与者にスタック比で 100% (§4)
2. それ以外: 補正前の基礎ダメージ → 攻撃した本人。正の寄与 → その補正の行為者 (パワーは付与者にスタック比、本人が付けたものは本人)。
   負の寄与 (自分に付いた脱力・敵の霊体等) → 本人の取り分から引く (0 未満にはしない)。
3. 最後に、補正計算の結果 (ブロック前・HP 上限前) と E の比で全体を縮め、**合計を E に一致させる** (オーバーキルは含まない)。

#### rMit (被ダメ軽減への貢献)

プレイヤーが受けた 1 ヒットについて、**防いだ量** を行為者に配る。
1. 補正の負の寄与 (敵に付いた脱力・筋力ダウン、自分や味方に付いたかばう・霊体・バッファー等) → その補正の行為者
2. ブロックで防いだ量 → **そのブロックを付けた人**。誰のブロックがどれだけ残っているかを mod が記録し、
   防いだ量を残量の比で配る (`block_sources`)。自分で張ったブロックは自分。
3. 正の寄与 (敵の筋力・自分に付いた弱体等) は誰の貢献にもしない。

#### 表示

`RdpsPanel` / `RmitPanel` は従来どおり 合計 / 自力 / 他人へ / 他人から を出す。内訳の出どころ名は補正のモデル名 (パワー名・レリック名)。
`modifications` が v1 形式 (`pre` / `post` / `modifier_ids`) の旧データは、従来の計算で表示する。

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
| rDPS | `damage_dealt` の `modifications` (補正 1 つずつ) と `source_appliers` (§3.5) |
| rMit | `damage_received` の `modifications` と `block_sources` (§3.5) |

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
