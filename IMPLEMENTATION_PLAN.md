# 実装計画

## 前提条件

- macOS (arm64)
- STS2: `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app`
- dotnet SDK: 要インストール (`brew install dotnet`)
- Godot mono: 不要（PoC段階ではUIなし・DLLのみ）

---

## Phase 1: mod PoC（統計ログ出力）

### ゴール
AfterDamageGiven などのhookが正しく発火し、プレイヤーごとのターン統計をJSONLファイルに出力できることを確認する。

### ディレクトリ構成

```
sts2-multiplayer-statistics/
├── DESIGN.md
├── IMPLEMENTATION_PLAN.md
└── mod/
    ├── StsStats.csproj
    ├── StsStats.json
    ├── project.godot
    ├── build.sh
    ├── install.sh
    └── src/
        ├── ModEntry.cs        # Phase 1〜
        ├── HookPatches.cs     # Phase 1〜
        ├── StatsCollector.cs  # Phase 1〜
        └── StatsLogger.cs     # Phase 1のみ（PoC検証用・Phase 2でHttpSenderに置換）
```

> ゲーム内UIオーバーレイは**作らない**。統計閲覧はすべてブラウザに委ねる。
> PCKファイル不要のため `has_pck: false` で確定。Godot monoのインストール不要。

---

### ファイル別実装詳細

#### `StsStats.csproj`

```xml
<Project Sdk="Godot.NET.Sdk/4.5.1">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>true</ImplicitUsings>

    <Sts2AppDir>$(HOME)/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app</Sts2AppDir>
    <Sts2DataDir>$(Sts2AppDir)/Contents/Resources/data_sts2_macos_arm64</Sts2DataDir>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="sts2">
      <HintPath>$(Sts2DataDir)/sts2.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>$(Sts2DataDir)/0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

**ポイント:**
- `Godot.NET.Sdk/4.5.1` はNuGetで自動取得されるためGodotローカルインストール不要
- `Private=false` でDLLをoutputにコピーしない（ゲームのDLLを使う）
- ターゲットフレームワーク: `net9.0`（ゲームの実行環境に合わせる）

---

#### `StsStats.json`（modマニフェスト）

```json
{
  "id": "StsStats",
  "name": "STS Multiplayer Statistics",
  "author": "nobu",
  "description": "Tracks per-player combat statistics and exports them for sharing.",
  "version": "0.1.0",
  "has_pck": false,
  "has_dll": true,
  "dependencies": [],
  "affects_gameplay": false
}
```

**ポイント:**
- `has_pck: false` — ゲーム内UIなし・Godotリソース不使用（全フェーズで変わらない）
- `affects_gameplay: false` — 統計閲覧のみ、ゲームロジック変更なし

---

#### `project.godot`

```ini
; Engine configuration file.
config_version=5

[application]
config/name="StsStats"
config/features=PackedStringArray("4.5", "C#", "Mobile")

[dotnet]
project/assembly_name="StsStats"

[rendering]
renderer/rendering_method="mobile"
```

---

#### `ModEntry.cs`

責務: Harmonyインスタンスの初期化とhook登録。

```csharp
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace StsStats;

[ModInitializer("Initialize")]
public static class ModEntry
{
    private static Harmony? _harmony;
    private const string HarmonyId = "com.nobu.sts2.stats";

    public static void Initialize()
    {
        if (_harmony != null) return;

        _harmony = new Harmony(HarmonyId);

        PatchHook(nameof(Hook.BeforeCombatStart),    nameof(HookPatches.BeforeCombatStartPostfix));
        PatchHook(nameof(Hook.AfterDamageGiven),      nameof(HookPatches.AfterDamageGivenPostfix));
        PatchHook(nameof(Hook.AfterPlayerTurnStart),  nameof(HookPatches.AfterPlayerTurnStartPostfix));
        PatchHook(nameof(Hook.AfterCombatEnd),        nameof(HookPatches.AfterCombatEndPostfix));

        StatsLogger.Initialize();
        Log.Info("[StsStats] Initialized");
    }

    private static void PatchHook(string hookName, string postfixName)
    {
        var original = AccessTools.Method(typeof(Hook), hookName)
            ?? throw new MissingMethodException(nameof(Hook), hookName);
        var postfix = AccessTools.Method(typeof(HookPatches), postfixName)
            ?? throw new MissingMethodException(nameof(HookPatches), postfixName);
        _harmony!.Patch(original, postfix: new HarmonyMethod(postfix));
    }
}
```

---

#### `HookPatches.cs`

責務: hookの postfix 実装。`StatsCollector` と `StatsLogger` を橋渡しする。

```csharp
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace StsStats;

internal static class HookPatches
{
    // BeforeCombatStart(IRunState runState, CombatState? combatState)
    public static void BeforeCombatStartPostfix(IRunState? runState, CombatState? combatState)
    {
        StatsCollector.BeginCombat(runState, combatState);
        Log.Info("[StsStats] Combat started");
    }

    // AfterDamageGiven(PlayerChoiceContext? choiceContext, CombatState? combatState,
    //                  Creature? dealer, DamageResult? results, ValueProp props,
    //                  Creature? target, CardModel? cardSource)
    public static void AfterDamageGivenPostfix(
        CombatState? combatState,
        Creature? dealer,
        DamageResult? results,
        Creature? target,
        CardModel? cardSource)
    {
        if (dealer == null || results == null) return;
        // モンスターのダメージはスキップ（Playerクリーチャーのみ記録）
        if (dealer is not Player) return;

        var amount = results.UnblockedDamage;
        if (amount <= 0) return;

        StatsCollector.RecordDamage(dealer, (int)amount);
    }

    // AfterPlayerTurnStart(CombatState combatState, PlayerChoiceContext? choiceContext, Player player)
    // 「次のターン開始」= 「前ターン終了」として扱う
    public static void AfterPlayerTurnStartPostfix(CombatState combatState, Player player)
    {
        var snapshot = StatsCollector.FinalizeCurrentTurn();
        if (snapshot != null)
            StatsLogger.LogTurnEnd(snapshot);
    }

    // AfterCombatEnd(IRunState runState, CombatState? combatState, CombatRoom room)
    public static void AfterCombatEndPostfix(IRunState? runState, CombatState? combatState)
    {
        var summary = StatsCollector.FinalizeCurrentCombat();
        StatsLogger.LogCombatEnd(summary);
    }
}
```

**注意点:**
- `AfterDamageGiven` のシグネチャは Harmony postfix なので、元メソッドの引数名と型を合わせる必要がある
- `dealer is not Player` でモンスターを除外。型チェックで reflection 不要
- `results.UnblockedDamage` はブロックを貫通した実ダメージ量

---

#### `StatsCollector.cs`

責務: インメモリでターン・戦闘ごとの統計を蓄積する。

```csharp
using System.Collections.Concurrent;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;

namespace StsStats;

internal static class StatsCollector
{
    private static int _combatIndex = 0;
    private static int _turnNumber = 0;
    // playerId -> ダメージ合計（現在ターン）
    private static ConcurrentDictionary<string, int> _currentTurnDamage = new();
    // playerId -> ダメージ合計（現在戦闘全体）
    private static ConcurrentDictionary<string, int> _currentCombatDamage = new();

    public static void BeginCombat(IRunState? runState, CombatState? combatState)
    {
        _combatIndex++;
        _turnNumber = 0;
        _currentTurnDamage.Clear();
        _currentCombatDamage.Clear();
    }

    public static void RecordDamage(Creature dealer, int amount)
    {
        // プレイヤー識別: NetId を文字列キーとして使用
        string key = GetPlayerId(dealer);
        _currentTurnDamage.AddOrUpdate(key, amount, (_, prev) => prev + amount);
        _currentCombatDamage.AddOrUpdate(key, amount, (_, prev) => prev + amount);
    }

    // ターン終了時: スナップショットを返してリセット
    public static TurnSnapshot? FinalizeCurrentTurn()
    {
        if (_currentTurnDamage.IsEmpty) return null;

        _turnNumber++;
        var snapshot = new TurnSnapshot(
            CombatIndex: _combatIndex,
            TurnNumber:  _turnNumber,
            Timestamp:   DateTime.UtcNow,
            DamageByPlayer: new Dictionary<string, int>(_currentTurnDamage)
        );
        _currentTurnDamage.Clear();
        return snapshot;
    }

    // 戦闘終了時: 戦闘全体の集計を返す
    public static CombatSummary FinalizeCurrentCombat()
    {
        return new CombatSummary(
            CombatIndex: _combatIndex,
            TotalTurns:  _turnNumber,
            Timestamp:   DateTime.UtcNow,
            TotalDamageByPlayer: new Dictionary<string, int>(_currentCombatDamage)
        );
    }

    private static string GetPlayerId(Creature dealer)
    {
        // Player型はNetIdを持つ
        if (dealer is MegaCrit.Sts2.Core.Entities.Players.Player p)
            return p.NetId.ToString();
        return dealer.GetType().Name;
    }
}

// データ構造
record TurnSnapshot(
    int CombatIndex,
    int TurnNumber,
    DateTime Timestamp,
    Dictionary<string, int> DamageByPlayer
);

record CombatSummary(
    int CombatIndex,
    int TotalTurns,
    DateTime Timestamp,
    Dictionary<string, int> TotalDamageByPlayer
);
```

---

#### `StatsLogger.cs`

責務: GD.Print と JSONLファイルへの出力。

```csharp
using Godot;
using System.Text.Json;

namespace StsStats;

internal static class StatsLogger
{
    private static string _logPath = "";

    public static void Initialize()
    {
        // 出力先: ~/Library/Application Support/Godot/app_userdata/Slay the Spire 2/
        string userDataDir = OS.GetUserDataDir();
        _logPath = Path.Combine(userDataDir, "sts_stats.jsonl");
        GD.Print($"[StsStats] Log path: {_logPath}");
    }

    public static void LogTurnEnd(TurnSnapshot snapshot)
    {
        var entry = new {
            @event    = "turn_end",
            combat    = snapshot.CombatIndex,
            turn      = snapshot.TurnNumber,
            timestamp = snapshot.Timestamp.ToString("O"),
            damage    = snapshot.DamageByPlayer
        };
        var line = JsonSerializer.Serialize(entry);
        GD.Print($"[StsStats] {line}");
        AppendLine(line);
    }

    public static void LogCombatEnd(CombatSummary summary)
    {
        var entry = new {
            @event    = "combat_end",
            combat    = summary.CombatIndex,
            turns     = summary.TotalTurns,
            timestamp = summary.Timestamp.ToString("O"),
            totals    = summary.TotalDamageByPlayer
        };
        var line = JsonSerializer.Serialize(entry);
        GD.Print($"[StsStats] {line}");
        AppendLine(line);
    }

    private static void AppendLine(string line)
    {
        try
        {
            File.AppendAllText(_logPath, line + "\n");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[StsStats] Failed to write log: {ex.Message}");
        }
    }
}
```

---

#### `build.sh`

```bash
#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

echo "[build] Building StsStats..."
dotnet build -c Debug

echo "[build] Collecting output..."
rm -rf dist
mkdir -p dist

DLL_PATH=".godot/mono/temp/bin/Debug/StsStats.dll"
if [ ! -f "$DLL_PATH" ]; then
    # Godot SDK不使用時のフォールバック
    DLL_PATH="bin/Debug/net9.0/StsStats.dll"
fi

cp "$DLL_PATH" dist/StsStats.dll
cp StsStats.json dist/StsStats.json

echo "[build] Done: $(ls dist/)"
```

---

#### `install.sh`

```bash
#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
STS2_MODS="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods/StsStats"

mkdir -p "$STS2_MODS"
cp -r "$SCRIPT_DIR/dist/"* "$STS2_MODS/"

echo "[install] Installed to: $STS2_MODS"
ls "$STS2_MODS"
```

---

### ビルドフロー

```
brew install dotnet
  ↓
cd mod && bash build.sh
  ↓
bash install.sh
  ↓
STS2 起動 → modを有効化 → 戦闘してターンを終える
  ↓
~/Library/Application Support/Godot/.../sts_stats.jsonl を確認
```

---

### 動作確認チェックリスト

- [ ] ゲーム起動時に `[StsStats] Initialized` がログに出る
- [ ] 戦闘開始時に `[StsStats] Combat started` が出る
- [ ] ターン終了時に `turn_end` JSONが出力される
- [ ] 戦闘終了時に `combat_end` JSONが出力される
- [ ] ダメージ量が正しいこと（目視で確認）
- [ ] マルチプレイ時に全プレイヤーのダメージが記録されること

---

### リスクと対処

| リスク | 対処 |
|--------|------|
| `AfterPlayerTurnStart` がターン終了タイミングとずれる | hookの発火順序をログで確認し、必要なら `AfterPlayerTurnEnd` を探す |
| `dealer is not Player` でプレイヤーが除外される | `dealer.GetType().Name` をログに出して型を確認 |
| `results.UnblockedDamage` が0になる | `results` の全フィールドをリフレクションで列挙して正しいフィールド名を確認 |
| DLLパスが `.godot/` 以下に生成されない | `bin/Debug/net9.0/` フォールバックで対応済み |
| `OS.GetUserDataDir()` がnull/空 | `_logPath` に fallback として `/tmp/sts_stats.jsonl` を使う |

---

## Phase 2: バックエンド + HTTP送信（Phase 1完了後）

### ゴール
ターン統計をサーバーに送信し、URLで仲間と共有できるようにする。

### mod側の変更

`StatsLogger.cs` を削除し、`HttpSender.cs` に置き換える。

```
mod/src/
    ├── ModEntry.cs        （変更なし）
    ├── HookPatches.cs     （StatsLogger → HttpSender に呼び出し先変更）
    ├── StatsCollector.cs  （変更なし）
    └── HttpSender.cs      （新規: HttpClient でバックエンドに送信）
```

#### `HttpSender.cs` の責務
- `Initialize()`: セッションID（UUID v4）を生成し `POST /sessions` でサーバーに登録、URLを `OS.Clipboard` にセット
- `SendTurn(TurnSnapshot)`: `POST /sessions/{id}/turns` をバックグラウンドスレッドで非同期送信
- ホスト判定: `RunManager.Instance.NetService.Type != NetGameType.Host` なら何もしない

#### クリップボードへのURL通知（唯一のゲーム内UI）
```csharp
// ゲーム内UIオーバーレイは作らない。これだけ。
OS.Clipboard = $"https://<app>.fly.dev/s/{sessionId}";
GD.Print($"[StsStats] Stats URL copied: {OS.Clipboard}");
```

### バックエンド技術スタック（決定済み）

| レイヤー | 技術 |
|----------|------|
| APIサーバー | Go (`net/http`) |
| DB | SQLite (`mattn/go-sqlite3`) |
| WebUI | HTML + vanilla JS（同一サーバーから配信） |
| デプロイ | Fly.io（無料tier） |

### バックエンドのディレクトリ構成

```
server/
├── main.go
├── db/
│   ├── schema.sql
│   └── db.go
├── handler/
│   ├── sessions.go   # POST /sessions, GET /sessions/{id}
│   └── turns.go      # POST /sessions/{id}/turns, GET /sessions/{id}/turns
├── static/
│   └── index.html    # 閲覧WebUI
├── Dockerfile
└── fly.toml
```

詳細なAPI仕様は `DESIGN.md` を参照。

---

## Phase 3: 閲覧WebUI（Phase 2完了後）

バックエンドの `static/index.html` として配信。mod側の追加実装は不要。

### 画面構成
- URLパラメータからセッションIDを取得（`/s/{id}`）
- 10秒ポーリングで `GET /sessions/{id}/turns` を叩く
- プレイヤー別ダメージ棒グラフ（戦闘累計）
- ターン推移折れ線グラフ
- 戦闘セレクタ（何戦目かを切り替え）
