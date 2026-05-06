# Phase 3.5 実装計画 — イベント列化と rDPS 対応（統合版）

## 1. ゴール

ターンごとに送るデータを **集計値 → 生イベント列** に変える。各 damage 発生時のコンテキスト（dealer / target / 適用中 power）を保持し、後段で rDPS（Skada 風）を算出可能にする。

加えて、戦闘内 / 戦闘外イベントを **1 つの events テーブルに統合** することで、データ構造を最小化する。

旧形式（集計済 turns ペイロード）との **互換は持たない**。新形式に完全切替。

---

## 2. データモデル

### 既存 `events` テーブルに combat context カラムを追加

```sql
-- migrations/002_unified_events.sql
ALTER TABLE events ADD COLUMN combat_index INTEGER;
ALTER TABLE events ADD COLUMN turn_number  INTEGER;
ALTER TABLE events ADD COLUMN sequence     INTEGER;

CREATE INDEX idx_events_combat
  ON events(session_id, combat_index, turn_number, sequence);
```

### 結果スキーマ

```sql
CREATE TABLE events (
  id            INTEGER PRIMARY KEY AUTOINCREMENT,
  event_uuid    TEXT NOT NULL UNIQUE,
  session_id    TEXT NOT NULL,
  event_type    TEXT NOT NULL,
  player_id     TEXT,
  occurred_at   TEXT NOT NULL,
  received_at   TEXT NOT NULL,

  -- コンテキスト（NULL 可）
  floor         INTEGER,    -- 階層（戦闘外イベントで主に使う）
  combat_index  INTEGER,    -- 戦闘番号（戦闘外は NULL）
  turn_number   INTEGER,    -- ターン番号（戦闘内のみ）
  sequence      INTEGER,    -- 同一ターン内の発生順序（戦闘内のみ）

  payload_json  TEXT NOT NULL,
  FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
);
```

### 旧 `turns` テーブル
- 廃止。書き込み停止。実装＆動作確認後に `DROP TABLE turns` する migration を追加（別 commit で）。

### イベントごとの context フィールド設定

| event_type | floor | combat_index | turn_number | sequence | player_id |
|-----------|-------|--------------|-------------|----------|-----------|
| `run_start` | ✓ | — | — | — | host |
| `run_end` | ✓ | — | — | — | host |
| `combat_start` | ✓ | ✓ | — | — | — |
| `combat_end` | ✓ | ✓ | — | — | — |
| `card_played` | ✓ | ✓ | ✓ | ✓ | dealer |
| `card_drawn` | ✓ | ✓ | ✓ | ✓ | drawer |
| `damage_dealt` | ✓ | ✓ | ✓ | ✓ | dealer |
| `damage_received` | ✓ | ✓ | ✓ | ✓ | target |
| `block_gained` | ✓ | ✓ | ✓ | ✓ | receiver |
| `power_changed` | ✓ | ✓ | ✓ | ✓ | applier |
| `energy_spent` | ✓ | ✓ | ✓ | ✓ | player |
| `potion_used` | ✓ | ✓? | ✓? | ✓? | user |

---

## 3. API 契約

### 廃止
- `POST /sessions/{id}/turns` → **410 Gone**

### 既存（変更）: `POST /sessions/{id}/events` `[Auth]`

bulk 投稿。すべての event をこのエンドポイントに送る（戦闘内・外問わず）。

**Body**
```json
[
  {
    "event_uuid":   "uuid-v4",
    "event_type":   "run_start",
    "occurred_at":  "2026-...",
    "player_id":    "76561...",
    "floor":        0,
    "payload":      { "character_id": "IRONCLAD", "ascension": 5, "seed": "..." }
  },
  {
    "event_uuid":   "uuid-v4",
    "event_type":   "combat_start",
    "occurred_at":  "...",
    "floor":        1,
    "combat_index": 1,
    "payload":      { "encounter_id": "CULTIST", "encounter_name": "...", "room_type": "Monster" }
  },
  {
    "event_uuid":   "uuid-v4",
    "event_type":   "card_played",
    "occurred_at":  "...",
    "player_id":    "76561...A",
    "floor":        1,
    "combat_index": 1,
    "turn_number":  1,
    "sequence":     0,
    "payload":      { "card_id": "BASH", "card_name": "...", "card_type": "Attack" }
  },
  {
    "event_uuid":   "uuid-v4",
    "event_type":   "damage_dealt",
    "occurred_at":  "...",
    "player_id":    "76561...A",
    "floor":        1,
    "combat_index": 1,
    "turn_number":  1,
    "sequence":     1,
    "payload": {
      "amount":             18,
      "target_creature_id": "monster:1",
      "source_card_id":     "BASH",
      "hit_index":          0,
      "active_on_target":   [{"power_id":"VULNERABLE_POWER","stacks":2,"applier":"76561...B"}],
      "active_on_dealer":   []
    }
  }
]
```

冪等性: `event_uuid` UNIQUE、重複は無視。

mod 側送信単位:
- ターン終了時: そのターン中に蓄積した戦闘内 event を bulk POST
- 戦闘外 event（run_start, card_picked 等）も同じバッファ・同じ endpoint
- バッファに溜まれば flush（粒度自由、現行は ターン終わりに drain）

### `GET /api/sessions/{id}`

レスポンス構造:
```json
{
  "session": { ... },
  "players": [...],
  "events": [ /* 全イベント、(combat_index NULLS FIRST, turn_number, sequence, occurred_at) 順 */ ]
}
```

`turns` フィールドは **削除**（旧 API の名残無し）。

---

## 4. Event Schema

各 event の `payload` フィールドの中身。トップレベルの context 列（floor, combat_index, turn_number, sequence, player_id）と payload は重複しない。

### `card_played` (戦闘内)
```json
{ "card_id": "BASH", "card_name": "バッシュ", "card_type": "Attack",
  "target_creature_id": "monster:1", "energy_cost": 2 }
```

### `card_drawn` (戦闘内)
```json
{ "card_id": "STRIKE", "from_hand_draw": false }
```

### `damage_dealt` (戦闘内)
```json
{
  "amount":              18,
  "target_creature_id":  "monster:1",
  "target_player_id":    null,        // プレイヤー間ダメージなら set
  "source_card_id":      "BASH",      // 間接ダメージなら "(poison)" "(doom)" 等
  "hit_index":           0,
  "active_on_target":    [{"power_id":"VULNERABLE_POWER","stacks":2,"applier":"76561...B"}],
  "active_on_dealer":    [{"power_id":"STRENGTH_POWER","stacks":3,"applier":"76561...A"}]
}
```

### `damage_received` (戦闘内)
```json
{ "amount": 8, "source_creature_id": "monster:1", "source_card_id": null,
  "active_on_target": [...] }
```

### `block_gained` (戦闘内)
```json
{ "amount": 8, "source_card_id": "DEFEND", "from_player": "76561...A" }
```

`player_id` (top level) = block を受けた player。`from_player` = block を生成した player（自己 / 味方）。

### `power_changed` (戦闘内)
```json
{ "power_id": "VULNERABLE_POWER", "delta": 2,
  "target_creature_id": "monster:1", "target_player_id": null,
  "source_card_id": "BASH" }
```

`player_id` (top level) = applier。

### `energy_spent` (戦闘内)
```json
{ "amount": 2, "source_card_id": "BASH" }
```

### `potion_used`
```json
{ "potion_id": "BLOOD_POTION", "target_creature_id": "..." }
```

### `run_start` / `run_end` / `combat_start` / `combat_end`

既存と同じ payload。

---

## 5. mod 側の変更

### `StatsCollector` → 廃止

集計はサーバ・WebUI 側で行うので mod 側に保持する必要がない。

### 単一 `EventBuffer` に統合

```csharp
internal static class EventBuffer
{
    private static int _combatIndex = 0;
    private static int _turnNumber  = 0;
    private static int _sequence    = 0;
    private static int _floor       = 0;
    private static readonly List<EventRecord> _pending = new();
    private static readonly object _lock = new();

    public static void BeginCombat(int floor)        { _combatIndex++; _turnNumber = 0; _sequence = 0; _floor = floor; }
    public static void BeginTurn()                   { _turnNumber++; _sequence = 0; }
    public static void UpdateFloor(int floor)        { _floor = floor; }

    /// <summary>戦闘内 event（combat_index/turn_number/sequence を自動付与）</summary>
    public static void EmitTurnEvent(string type, string? playerId, object payload)
    {
        Emit(type, playerId, payload, withCombatContext: true, withTurnContext: true);
    }

    /// <summary>戦闘単位 event（combat_index は付くが turn は付かない）</summary>
    public static void EmitCombatEvent(string type, string? playerId, object payload)
    {
        Emit(type, playerId, payload, withCombatContext: true, withTurnContext: false);
    }

    /// <summary>戦闘外 event（floor のみ）</summary>
    public static void EmitGlobalEvent(string type, string? playerId, object payload)
    {
        Emit(type, playerId, payload, withCombatContext: false, withTurnContext: false);
    }

    private static void Emit(string type, string? playerId, object payload,
                             bool withCombatContext, bool withTurnContext)
    {
        var ev = new EventRecord(
            EventUuid:    Guid.NewGuid(),
            EventType:    type,
            OccurredAt:   DateTime.UtcNow,
            PlayerId:     playerId,
            Floor:        _floor,
            CombatIndex:  withCombatContext ? _combatIndex : (int?)null,
            TurnNumber:   withTurnContext   ? _turnNumber  : (int?)null,
            Sequence:     withTurnContext   ? _sequence++  : (int?)null,
            Payload:      payload
        );
        StatsLogger.LogEvent(ev);
        lock (_lock) _pending.Add(ev);
        TryFlush();
    }

    private static void TryFlush()
    {
        if (!SessionManager.IsReady) return;
        var sender = ModEntry.HttpSender;
        if (sender == null) return;
        EventRecord[] snapshot;
        lock (_lock)
        {
            if (_pending.Count == 0) return;
            snapshot = _pending.ToArray();
            _pending.Clear();
        }
        sender.EnqueueEvents(SessionManager.SessionId!, SessionManager.WriteToken!, snapshot);
    }

    public static void FlushPending() => TryFlush();   // session 確立時に呼ばれる

    internal static void Reset() { /* ... */ }
}
```

### `PowerOriginRegistry`（新規）

`AfterPowerAmountChanged` で applier_player_id を記録。`damage_dealt` 発火時に target/dealer の active power の applier を引く。

```csharp
internal static class PowerOriginRegistry
{
    // (creature_identity, power_id) → applier_player_id
    private static readonly Dictionary<(int creatureId, string powerId), string> _origin = new();

    public static void Record(Creature creature, string powerId, string applierPlayerId)
    {
        int id = (int)(creature.GetType().GetProperty("Id")?.GetValue(creature) ?? 0);  // Creature の一意 ID
        _origin[(id, powerId)] = applierPlayerId;
    }

    public static string? Lookup(Creature creature, string powerId)
    {
        int id = (int)(creature.GetType().GetProperty("Id")?.GetValue(creature) ?? 0);
        return _origin.TryGetValue((id, powerId), out var p) ? p : null;
    }

    public static void ClearForCombat() => _origin.Clear();
}
```

### `ActivePowersSnapshot`（新規）

```csharp
internal static class ActivePowersSnapshot
{
    private static readonly HashSet<string> Whitelist = new()
    {
        "VULNERABLE_POWER", "POISON_POWER", "DOOM_POWER", "STRENGTH_POWER",
    };

    public static List<PowerSnapshot> ForCreature(Creature? c)
    {
        var result = new List<PowerSnapshot>();
        if (c == null) return result;
        try
        {
            var powers = c.GetType().GetProperty("Powers")?.GetValue(c) as IEnumerable;
            if (powers == null) return result;
            foreach (var p in powers)
            {
                var idObj = p.GetType().GetProperty("Id")?.GetValue(p);
                string? powerId = idObj?.GetType().GetProperty("Entry")?.GetValue(idObj)?.ToString();
                if (powerId == null || !Whitelist.Contains(powerId)) continue;
                int stacks = (int?)p.GetType().GetProperty("Amount")?.GetValue(p) ?? 0;
                if (stacks == 0) continue;
                string? applier = PowerOriginRegistry.Lookup(c, powerId);
                result.Add(new PowerSnapshot(powerId, stacks, applier));
            }
        }
        catch { /* best-effort */ }
        return result;
    }
}

internal record PowerSnapshot(string PowerId, int Stacks, string? Applier);
```

### Hook 改修

| Hook | 変更内容 |
|------|---------|
| `BeforeCombatStart` | `EventBuffer.BeginCombat(floor)` + `PowerOriginRegistry.ClearForCombat()` + `EmitCombatEvent("combat_start", ...)` |
| `AfterPlayerTurnStart` | `_currentTurnPlayer` 更新（既存通り） |
| `AfterTurnEnd` | `EventBuffer.BeginTurn()`（次ターンの sequence 起点） |
| `AfterCombatEnd` | `EmitCombatEvent("combat_end", { victory })` |
| `AfterCombatVictory` | victory フラグだけ立てる（combat_end で参照） |
| `AfterDeath` | プレイヤー死亡なら `EmitGlobalEvent("run_end", { outcome: "death" })` |
| `AfterCardPlayed` | `EmitTurnEvent("card_played", ...)` |
| `AfterCardDrawn` | `EmitTurnEvent("card_drawn", ...)` |
| `AfterDamageGiven` | active power snapshot を取り、`EmitTurnEvent("damage_dealt", ...)` |
| `AfterDamageReceived` | `EmitTurnEvent("damage_received", ...)` |
| `AfterBlockGained` | `EmitTurnEvent("block_gained", ...)` |
| `AfterEnergySpent` | `EmitTurnEvent("energy_spent", ...)` |
| `AfterPowerAmountChanged` | `PowerOriginRegistry.Record(...)` + `EmitTurnEvent("power_changed", ...)` |
| `AfterPotionUsed` | `EmitTurnEvent("potion_used", ...)` |

### `StatsLogger` の変更
- `LogTurn` 削除、`LogEvent` のみ残す
- 既存の JSONL は event 列形式に統一

### `HttpSender` の変更
- `PostTurnAsync` 削除、`PostEventsAsync` のみ残す（既に存在）
- mod 側のバッファ flush 機構と統合

### `PayloadJson`
- `BuildTurnBody` 削除、`BuildEventBody` のみ残す
- context 列（floor, combat_index, turn_number, sequence）も含めて serialize

### テスト
- `StatsCollectorTests` 削除（`StatsCollector` 廃止のため）
- `EventBufferTests`（新規）: BeginCombat/Turn の sequence 番号、Emit のコンテキスト分離、FlushPending 動作
- 既存 `HttpSenderTests` / `RunSessionStoreTests` / `SessionConfigTests` は維持

---

## 6. backend 側の変更

### マイグレーション
- `migrations/002_unified_events.sql` で events テーブルにカラム追加
- 新カラムは NULLABLE、既存データに対して NULL のまま許容

### handler
- `POST /sessions/{id}/turns` → `410 Gone`（互換維持期間なし、即廃止）
- `POST /sessions/{id}/events` → 新コンテキスト列も受け取る形に拡張
- `GET /api/sessions/{id}` → レスポンスから `turns` フィールド削除、`events` のみ

### store
- `events.go` 更新（コンテキスト列の挿入・取り出し）
- `turns.go` 削除（または読み取り専用に縮小、後で migration で table drop）

### ETag
- `events` テーブルの最新 received_at と件数で計算（既存ロジックから turns を外す）

### テスト
- 既存ハンドラテストを新形式に更新（`turn` payload を投げてた箇所を event 列に書き換え）
- 旧 `POST /turns` への 410 テスト追加

---

## 7. WebUI 側の変更

### データソース切替
- `lib/api.ts` のレスポンス型から `turns` 削除
- `lib/aggregate.ts` を **events 列から戦闘単位サマリ・カード別集計を導出する純関数群** に置き換え
  - 各 event を時系列で処理し、player ごとに totals を蓄積
  - これは Phase 2 の集計済 payload と同じ shape を生成するので、既存表示コンポーネントは変更不要

### rDPS パネル新規
- `lib/rdps.ts`: events から rDPS を計算（whitelist のロジック）
- `components/RdpsPanel.svelte`: バーチャートで player 別 rDPS 表示

### タイムラインビュー新規
- `components/TimelineView.svelte`: events を sequence 順に並べて表示
- 戦闘タブの「Summary / Timeline」サブタブ切替

### mock データ更新
- `lib/mock.ts` を新形式に書き換え
- `scripts/seed.ts` 動作確認

### 旧形式セッションの扱い
- `turn_events` 列でなく events 列で見るので、API レスポンスに events が空ならば「データなし」表示
- 旧 `turns` テーブルにのみあるセッションは API 側で events 空配列が返り、自然に「空」表示（個別ハンドリング不要）

---

## 8. rDPS アルゴリズム（Skada 風 v1）

### Whitelist
- `VULNERABLE_POWER` — 受けるダメ +50%
- `POISON_POWER` — tick damage 100% を applier に
- `DOOM_POWER` — 即死系 damage 100% を applier に

### 計算式

各 `damage_dealt` event を処理:

```
amount       = event.payload.amount
dealer       = event.player_id
contributors = []   // {applier, contribution, source}

# 1. Vulnerable on target by other player
for power in event.payload.active_on_target:
    if power.power_id == "VULNERABLE_POWER" and power.applier and power.applier != dealer:
        # damage の 1/3 が Vulnerable 由来
        contributors.push({applier: power.applier, contribution: amount / 3, source: "vulnerable"})

# 2. Poison / Doom: source_card_id が予約値ならその applier に 100%
if source_card_id == "(poison)":
    poison_applier = active_on_target にある POISON_POWER の applier
    if poison_applier:
        contributors = [{applier: poison_applier, contribution: amount, source: "poison"}]
        dealer = poison_applier   # 帰属再割り当て

if source_card_id == "(doom)":
    same as poison
```

dealer 自身の self_contribution = `amount - sum(contributors.contribution)`。

各 player の rDPS:
- self_contribution の総和（自分が dealer のとき）
- + contributors[applier == self].contribution の総和（他人の damage に貢献したとき）

### v2 候補
- STRENGTH（base damage を event に含める必要あり）
- WEAK（damage 抑止の可視化）
- 他の有意な power（カード固有）

---

## 9. マイグレーション運用

- 旧 `turns` テーブルは Phase 3.5 リリース後に書き込み停止
- 既存 Phase 2 形式のセッションは、events が空なので WebUI で「データなし」表示
- 動作確認完了後 → migration 003 で `DROP TABLE turns`

公開リリース時:
- 新 mod を入れた人 → 新形式で送信、正常表示
- 古い mod のままの人 → POST /turns が 410 → リトライキューに溜まる、ユーザに mod 更新を促すログ
- 過去のセッション URL → 「データなし」表示（自然）

---

## 10. ステップ別実装計画

| ステップ | 内容 | 推定 |
|---------|------|------|
| **1. 設計確定（このドキュメント）** | 統合方針確定済 | 完了 |
| **2. backend 実装** | migration 002、events ハンドラ拡張、turns 廃止、tests 更新 | 半日 |
| **3. mod 実装** | StatsCollector 削除、EventBuffer 統合、Hook 改修、PowerOriginRegistry、ActivePowersSnapshot、tests 更新 | 1〜2 日 |
| **4. WebUI 実装** | aggregate.ts 書換、rdps.ts、RdpsPanel、TimelineView、mock 更新 | 半日〜1日 |
| **5. 動作確認** | local + fly deploy + smoke test、seed で multi-player 検証 | 半日 |
| **6. 旧 turns テーブル DROP** | 動作確認後の migration 003 | 数十分 |
| **7. ドキュメント更新** | API.md / ROADMAP / README 整合 | 半日 |

合計 **3〜4 日**（統合により Phase 3.5 当初見積より 1 日短縮）。

実装は 2→3→4→5→6→7 の順がスムーズ（backend を先に変えるとローカル mod 開発で叩く先が決まる）。

---

## 11. リスク・注意点

- **mod の active power snapshot の精度**: Creature.Powers をリフレクションで列挙できるか要検証。サンプル run でログ出して確認するフェーズが必要
- **applier の追跡**: passive power（レリック由来等）は applier 不明 → NULL で記録、rDPS 除外（仕様通り）
- **イベント数の増大**: 1 ターンあたり 30〜100、1 戦闘 200 turns 上限で 20,000、50 戦闘 run で 100 万。SQLite 可
- **rate limit との整合**: events bulk POST は 1 ターンに 1 回（`rateEventsPerMin = 300`）で十分余裕

---

## 12. 確定した方針

- 旧形式との互換は持たない、完全切替
- rDPS は Skada 風 v1 から（VULNERABLE / POISON / DOOM）
- applier 不明時は NULL、rDPS 計算から除外
- 旧形式セッションは events 空 → WebUI 自然に「データなし」表示
- 動作確認後に旧 `turns` テーブル DROP
- タイムラインビュー追加
- **events テーブルに統合**（戦闘内/外を分離せず、context 列で表現）
