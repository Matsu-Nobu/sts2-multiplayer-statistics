# Phase 3.5 実装計画 — ターン送信のイベント列化と rDPS 対応

## 1. ゴール

ターンごとに送るデータを **集計値 → 生イベント列** に変えることで、各 damage 発生時のコンテキスト（dealer / target / 適用中の active powers）を保持する。これにより以下が可能になる:

- **rDPS の正確な算出**（Skada 風）: ある player の damage がチームメイトのバフ・デバフによってどれだけ底上げされたか、を後から計算可能
- 将来的な詳細分析の余地（カード単位のヒット履歴・ダメージ回避量・power 効率等）

旧形式（集計済 turns ペイロード）との **互換は持たない**。新形式に完全切替。

---

## 2. データモデル

### 廃止
- `turns` テーブル — Phase 3.5 リリース時点で書き込み停止、既存セッションは閲覧不可

### 新設

```sql
CREATE TABLE turn_events (
  id           INTEGER PRIMARY KEY AUTOINCREMENT,
  session_id   TEXT NOT NULL,
  combat_index INTEGER NOT NULL,
  turn_number  INTEGER NOT NULL,    -- ターン中で起きた event を 1 グループに
  sequence     INTEGER NOT NULL,    -- 同ターン内の発生順序
  event_uuid   TEXT NOT NULL UNIQUE,-- mod 生成、冪等性キー
  event_type   TEXT NOT NULL,
  player_id    TEXT,                -- イベント主体（無いものは NULL）
  occurred_at  TEXT NOT NULL,
  received_at  TEXT NOT NULL,
  payload_json TEXT NOT NULL,
  FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
);
CREATE INDEX idx_turn_events_session ON turn_events(session_id, combat_index, turn_number, sequence);
CREATE INDEX idx_turn_events_type    ON turn_events(event_type);
```

### 既存テーブル

- `sessions`（変更なし）
- `players`（変更なし）
- `events`（discrete event = run_start / combat_start 等、変更なし）
- `turns` — 削除しないが書き込み停止、UI 側で「旧形式」扱い

---

## 3. API 契約

### 廃止: `POST /sessions/{id}/turns`
- 410 Gone を返す（mod 側は新形式のみ送る）

### 新設: `POST /sessions/{id}/turn-events` `[Auth]`

ターン終了時に、そのターン中に発生したイベントを bulk で送る。

**Body**
```json
{
  "combat_index": 2,
  "turn_number":  5,
  "is_final":     false,
  "events": [
    {
      "event_uuid":  "uuid-v4",
      "sequence":    0,
      "event_type":  "card_played",
      "occurred_at": "2026-...",
      "player_id":   "76561...A",
      "payload": { "card_id": "BASH", "card_name": "...", "card_type": "Attack",
                   "target_creature_id": "monster:1" }
    },
    {
      "event_uuid":  "uuid-v4",
      "sequence":    1,
      "event_type":  "power_changed",
      "occurred_at": "2026-...",
      "player_id":   "76561...A",   // applier
      "payload": { "power_id": "VULNERABLE", "delta": 2,
                   "target_creature_id": "monster:1",
                   "source_card_id": "BASH" }
    },
    {
      "event_uuid":  "uuid-v4",
      "sequence":    2,
      "event_type":  "damage_dealt",
      "occurred_at": "2026-...",
      "player_id":   "76561...A",   // dealer
      "payload": {
        "amount":             18,
        "target_creature_id": "monster:1",
        "source_card_id":     "BASH",
        "hit_index":          0,
        "active_on_target":   [{"power_id": "VULNERABLE", "stacks": 2, "applier": "76561...B"}],
        "active_on_dealer":   [{"power_id": "STRENGTH",   "stacks": 3, "applier": "76561...A"}]
      }
    }
  ]
}
```

冪等性: `event_uuid` UNIQUE で重複弾く（既存 `events` テーブルと同じ方針）。

### 新設 (内部用): `GET /api/sessions/{id}`

レスポンス構造を変更:

```json
{
  "session": { ... },
  "players": [...],
  "events":  [ /* discrete events: run_start, combat_start ... */ ],
  "turn_events": [
    { "session_id": "...", "combat_index": 2, "turn_number": 5,
      "sequence": 0, "event_uuid": "...", "event_type": "card_played",
      "player_id": "...", "occurred_at": "...", "payload": { ... } },
    ...
  ]
}
```

クライアント（WebUI）は turn_events から戦闘単位サマリ・カード別集計・rDPS を導出する。

### 集計用の便利エンドポイント（オプション、後回し）

`GET /api/sessions/{id}/aggregate` で SQL 集計済みの turn / combat / card 単位サマリを返す。クライアント集計が重くなれば追加検討。

---

## 4. Event Schema（最終版）

すべて `payload` フィールドの中身。

### `card_played`
```json
{ "card_id": "BASH", "card_name": "バッシュ", "card_type": "Attack",
  "target_creature_id": "monster:1" /* optional */,
  "energy_cost": 2 }
```

### `card_drawn`
```json
{ "card_id": "STRIKE", "from_hand_draw": false }
```

### `damage_dealt`
```json
{
  "amount":              18,
  "target_creature_id":  "monster:1",
  "target_player_id":    null,        // プレイヤー間ダメージなら set
  "source_card_id":      "BASH",      // 間接ダメージ (Poison等) なら null
  "hit_index":           0,
  "active_on_target":    [{"power_id":"VULNERABLE","stacks":2,"applier":"76561...B"}],
  "active_on_dealer":    [{"power_id":"STRENGTH","stacks":3,"applier":"76561...A"}]
}
```

`player_id` (top level) = dealer の player_id。

### `damage_received`
```json
{
  "amount":            8,
  "source_creature_id":"monster:1",
  "source_card_id":    null /* enemy intent */,
  "active_on_target":  [...]
}
```

`player_id` (top level) = target player_id（受けた人）。

### `block_gained`
```json
{ "amount": 8, "source_card_id": "DEFEND",
  "from_player": "76561...A" /* 自己付与なら同じID */ }
```

### `power_changed`
```json
{ "power_id":         "VULNERABLE",
  "delta":            2,
  "target_creature_id": "monster:1",
  "target_player_id":   null /* プレイヤー対象なら set */,
  "source_card_id":   "BASH" }
```

`player_id` (top level) = applier。

### `energy_spent`
```json
{ "amount": 2, "source_card_id": "BASH" }
```

### `potion_used`
```json
{ "potion_id": "BLOOD_POTION", "target_creature_id": "..." }
```

---

## 5. mod 側の変更

### `StatsCollector` を廃止し `TurnEventBuffer` に置換

```csharp
internal static class TurnEventBuffer
{
    private static int _combatIndex = 0;
    private static int _turnNumber  = 0;
    private static int _sequence    = 0;
    private static readonly List<TurnEvent> _events = new();

    public static void BeginCombat()      { _combatIndex++; _turnNumber = 0; _sequence = 0; _events.Clear(); }
    public static void BeginTurn()        { _turnNumber++; _sequence = 0; }   // AfterPlayerTurnStart で呼ぶ

    public static void Emit(string type, string? playerId, object payload)
    {
        _events.Add(new TurnEvent(
            EventUuid:  Guid.NewGuid(),
            Sequence:   _sequence++,
            EventType:  type,
            PlayerId:   playerId,
            OccurredAt: DateTime.UtcNow,
            Payload:    payload));
    }

    public static TurnEventsPayload? FinalizeTurn(bool isFinal = false)
    {
        if (_events.Count == 0 && !isFinal) return null;
        var snapshot = _events.ToArray();
        _events.Clear();
        return new TurnEventsPayload(_combatIndex, _turnNumber, isFinal, DateTime.UtcNow, snapshot);
    }
}
```

### `ActivePowersSnapshot` ヘルパ（新規）

`damage_dealt` 発火時に、dealer と target の active powers を whitelist で抽出する。

```csharp
internal static class ActivePowersSnapshot
{
    // rDPS で意味のある power のみ snapshot する。
    private static readonly HashSet<string> Whitelist = new()
    {
        "VULNERABLE_POWER", "POISON_POWER", "STRENGTH_POWER", "WEAK_POWER",
        // 他に有意なものがあれば追加（毒キャラの新パワー等）
    };

    public static List<PowerSnapshot> ForCreature(Creature? c, CombatState? cs)
    {
        if (c == null) return new();
        var result = new List<PowerSnapshot>();
        // c.Powers をリフレクションで列挙、whitelist 該当のみ
        // 各 power の applier を runtime context から取り出す（後述）
        return result;
    }
}

internal record PowerSnapshot(string PowerId, int Stacks, string? ApplierPlayerId);
```

**applier の追跡**: `AfterPowerAmountChanged` で「この power をこのプレイヤーが付与した」を記録する **PowerOriginRegistry** を別途持つ:

```csharp
internal static class PowerOriginRegistry
{
    // (target_creature_id, power_id) → 最新の applier_player_id
    private static readonly Dictionary<(string, string), string> _origin = new();

    public static void Record(string targetCreatureId, string powerId, string applierPlayerId)
        => _origin[(targetCreatureId, powerId)] = applierPlayerId;

    public static string? Lookup(string targetCreatureId, string powerId)
        => _origin.TryGetValue((targetCreatureId, powerId), out var p) ? p : null;

    public static void ClearForCombat() => _origin.Clear();
}
```

### Hook 改修一覧

| Hook | 旧 (集計値更新) | 新 (event emit) |
|------|----------------|---------------|
| `BeforeCombatStart` | `StatsCollector.BeginCombat()` | `TurnEventBuffer.BeginCombat()` + `PowerOriginRegistry.ClearForCombat()` |
| `AfterPlayerTurnStart` | turn 確定 finalize | `TurnEventBuffer.BeginTurn()` |
| `AfterTurnEnd` | nothing | `TurnEventBuffer.FinalizeTurn(false)` → `DispatchTurnEvents()` |
| `AfterCombatEnd` | finalize last turn | `FinalizeTurn(true)` → dispatch |
| `AfterCardPlayed` | RecordCardPlayed | `Emit("card_played", player_id, {...})` |
| `AfterCardDrawn` | RecordCardDrawn | `Emit("card_drawn", ...)` |
| `AfterDamageGiven` | RecordDamageDealt | `Emit("damage_dealt", dealer_id, { amount, active_on_target: snapshot, active_on_dealer: snapshot, ... })` |
| `AfterDamageReceived` | RecordDamageReceived | `Emit("damage_received", target_id, { ... })` |
| `AfterBlockGained` | RecordBlockGainedSelf/ToAlly | `Emit("block_gained", receiver_id, { amount, from_player: giver_id })` |
| `AfterEnergySpent` | RecordEnergyUsed | `Emit("energy_spent", ...)` |
| `AfterPowerAmountChanged` | RecordDebuffApplied | `PowerOriginRegistry.Record(target, power, applier)` + `Emit("power_changed", applier_id, { ... })` |
| `AfterPotionUsed` | RecordPotionUsed | `Emit("potion_used", ...)` |

### `PayloadJson` の変更

`BuildTurnBody` を廃止、`BuildTurnEventsBody` を新規作成。

### `HttpSender` の変更

- `PostTurn` → `PostTurnEvents` にリネーム
- 投稿先 URL を `/turns` → `/turn-events` に

### テスト

- `TurnEventBufferTests`（StatsCollectorTests を置換）
- `PowerOriginRegistryTests`
- イベント順序保証、sequence 番号、Reset 動作

---

## 6. backend 側の変更

### マイグレーション
- `migrations/002_turn_events.sql` で新テーブル作成（既存 `turns` は残すが書き込み停止）

### ハンドラ
- `POST /sessions/{id}/turns` → `410 Gone`（旧クライアント拒否）
- `POST /sessions/{id}/turn-events` → 新ハンドラ、bulk insert
- `GET /api/sessions/{id}` → レスポンスに `turn_events` を追加（`turns` は空配列で返す or 廃止）

### ストア
- `turns.go` → 廃止 or 読み取り専用に
- `turn_events.go` 新規（`InsertTurnEvents` / `ListTurnEventsForSession`）

### ETag
- `turn_events` の最新 received_at と件数を ETag 計算に含める（既存 `turns` の代わり）

### テスト
- ハンドラテスト、冪等性、bulk insert
- 既存 `turns` 用テストは削除

---

## 7. WebUI 側の変更

### データソース切替
- `lib/api.ts` のレスポンス型を変更（`turn_events` を持つ）
- `lib/aggregate.ts` を全面書き換え: turn_events から戦闘単位サマリ・カード別集計・per-turn delta を導出する純関数群

### 戦闘単位サマリの導出
`turn_events` を `(combat_index, turn_number, sequence)` 順に処理し、各 player の damage_dealt/received/block 等を集計。これは Phase 2 の集計済 payload と同じ shape を生成する純関数。WebUI の既存表示コンポーネント（StatCard / DamageChart / CardTable 等）は変更なしで動かせる。

### rDPS パネル新規追加
新コンポーネント `RdpsPanel.svelte`:
- run 全体（または戦闘単位）でプレイヤー別 rDPS をバーチャート表示
- 「自分が直接出した damage」「味方バフ・デバフから貰った damage」を内訳で表示
- 各 damage source（VULNERABLE 等）ごとの内訳もホバーで見られる

### 既存セッション（旧形式 turns）の扱い
- WebUI は新形式のみ表示。旧 turns データの GET 結果は無視 or 「旧形式（閲覧不可）」エラー表示
- 既存の sample/seed/mock データは破棄、新形式の seed.ts に書き換え

---

## 8. rDPS アルゴリズム（Skada 風 v1）

### 対象 power（whitelist）
- `VULNERABLE_POWER`: 受けるダメージ +50%
- `POISON_POWER`: 毎ターン残スタック分のダメージを受ける（poison 自身が damage 源）

他は v2 以降。

### 計算式

各 `damage_dealt` イベントについて:

```
amount       = 観測値（例: 18）
dealer       = top-level player_id
contributors = []  // {applier, contribution} のリスト

# Vulnerable on target
if active_on_target.contains(VULNERABLE_POWER) and applier != dealer:
    # Vulnerable は damage を 1.5 倍にする → 全ダメージのうち 1/3 が Vulnerable 由来
    contribution = amount / 3
    contributors.push({applier, contribution, source: "vulnerable"})

# Poison tick (source_card_id == null and 内部判定で poison damage の場合)
# → これは Poison の applier に 100% 帰属
if source_card_id == null and 'poison_tick' :
    # ... 別途 power_id をマーカーとして扱う or damage_dealt に flag を持たせる
    contributors = [{applier: poison_applier, contribution: amount, source: "poison"}]
    dealer = poison_applier  # dealer 帰属を再割り当て
```

dealer 自身の "self" 寄与:
```
self_contribution = amount - sum(contributors.contribution)
```

各 player の rDPS = 自分が dealer のときの self_contribution + 他人が dealer のときに自分が contributor で受け取った contribution の合計。

### v1 の制限
- Strength は対象外（base damage がイベントに無いため正確に算出不可。v2 で `card_base_damage` をイベントに含めれば対応可）
- Weak は対象外（damage を減らす side だが、attacker 側に attribution する仕様はないので v1 ではスキップ）
- 上記ホワイトリスト外の power は無視

### v2 候補
- Strength の貢献度（各カードの base damage を mod が card model から取得して付与する必要あり）
- Weak / Frail を damage prevention として可視化
- Buff のスタック効果を時系列で重み付け

---

## 9. マイグレーション運用

- 旧 turns テーブルは DB 上に残すが、Phase 3.5 リリース後に書き込まれることはない
- 既存の Phase 2 形式のセッションは、WebUI で開いてもデータが見えない（turn_events が空）
- これは「PoC 段階のため過去データは捨てる」という前提（ユーザ承認済）

公開リリース時の挙動:
1. 新 mod を入れた人が新 run を始める → 新形式で送信、正常表示
2. 古い mod のままの人がリクエスト送ると `410 Gone` → mod 内のリトライキューに溜まり続ける（ユーザに mod 更新を促すログを出す）
3. 過去のセッション URL を開く → 何も表示されない（or "outdated session" メッセージ）

---

## 10. ステップ別実装計画

| ステップ | 内容 | 推定 |
|---------|------|------|
| **1. 設計確定（このドキュメント）** | 設計レビュー・確定 | 完了後着手 |
| **2. backend マイグレーション + 新エンドポイント** | turn_events テーブル、POST /turn-events、GET 拡張、テスト、旧 endpoint 410 化 | 半日 |
| **3. mod 側 TurnEventBuffer 実装** | 新型、各 hook 改修、PowerOriginRegistry、ActivePowersSnapshot、HttpSender 更新、テスト | 1〜2 日 |
| **4. WebUI データソース切替** | 新型、aggregate 関数 rewrite、既存表示は変更なしで動くこと確認 | 半日 |
| **5. rDPS パネル実装** | RdpsPanel.svelte、Skada 風計算 in TS、配置 | 半日〜1日 |
| **6. seed/mock データを新形式に** | mock.ts 書き換え、seed.ts 動作確認 | 半日 |
| **7. 動作確認 + デプロイ** | local → fly deploy、本番動作確認 | 半日 |
| **8. ドキュメント更新** | API.md / ROADMAP / README 整合 | 半日 |

合計 **4〜5 日**。

実装は 2→3→4→5→6→7→8 の順がスムーズ（backend を先に変えるとローカル mod 開発で叩く先が決まる）。

---

## 11. リスク・注意点

- **mod の active power snapshot の精度**: STS2 の Power 内部表現を reflection で正しく読めるか要検証。サンプル run でログ出して確認するフェーズが必要
- **applier の追跡**: `AfterPowerAmountChanged` 発火時に必ず applier_player_id を記録できるか。passive power（カードでなく相棒・遺物由来）の applier 不明時の扱いを決める（"system" 扱い or null）
- **イベント数の増大**: 1 ターンあたり 30〜100 events 程度になる可能性。1 戦闘 200 turns 上限で 20,000 events/戦闘、50 戦闘 run で 100 万 events。SQLite では問題ないが将来 PostgreSQL 移行時に検討
- **マルチプレイで他クライアントの認識ずれ**: ロックステップなので基本同じ events が見えるはずだが、レアな同期ずれは `event_uuid` UNIQUE で吸収

---

## 12. 確認したい論点

- ✓ 旧形式との互換は持たない（確定）
- ✓ rDPS は Skada 風 v1 から（確定）
- 上記 5 の **applier 不明時の扱い**: "system" 扱いに統一する？ NULL のまま rDPS 計算から除外する？
- 上記 8 の **whitelist**: VULNERABLE と POISON の 2 種で開始でいいか？
- ETag 生成式: 新 turn_events の最新 received_at を組み込めば OK か？
- WebUI 「旧形式セッション」の見せ方: エラーページ？ 何も表示しない？

これらが固まれば実装着手できます。
