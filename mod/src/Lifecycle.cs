using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Save;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models.Badges;
using MegaCrit.Sts2.Core.Saves;

namespace StsStats;

/// <summary>
/// ラン・戦闘の始まりと終わり (docs/spec/data-sources.md「ラン・階」 / §1.2)。
///
///   ラン開始   … 最初に部屋に入ったとき (AfterRoomEntered)。1 階 = ネオウから記録される
///   戦闘開始   … Hook.BeforeCombatStart の Prefix
///   戦闘勝利   … Hook.AfterCombatEnd (ゲームは勝利時にしか呼ばない)
///   戦闘敗北   … 戦闘中のラン終了 (RunManager.OnEnded)
///   ラン終了   … RunManager.OnEnded(isVictory) の最初の 1 回。放棄は RunManager.IsAbandoned
///   終了直前の HP … RunManager.WinRun / Abandon の直前 (この後ゲームが全員を倒すため)
/// </summary>
internal static class Lifecycle
{
    /// <summary>
    /// 今のラン。RunManager.State (private) を読む。読めなければ Hook が最後に渡してきた IRunState。
    /// (再開直後は「部屋に入った」より先に階の確定処理が走るので、保持した値だけだと前のランを指しうる)
    /// </summary>
    public static IRunState? Run
    {
        get
        {
            try { if (StateGetter != null) return StateGetter(RunManager.Instance) ?? _lastRun; }
            catch { }
            return _lastRun;
        }
        private set => _lastRun = value;
    }
    private static IRunState? _lastRun;

    /// <summary>RunManager.State (private)。起動時に存在を確認する (tools/verify-game-api/members.txt)。</summary>
    internal static readonly Func<RunManager, RunState?>? StateGetter = TryStateGetter();

    private static Func<RunManager, RunState?>? TryStateGetter()
    {
        try
        {
            var getter = HarmonyLib.AccessTools.PropertyGetter(typeof(RunManager), "State");
            if (getter == null) { Log.Error("[StsStats] RunManager.State getter not found"); return null; }
            return (Func<RunManager, RunState?>)Delegate.CreateDelegate(typeof(Func<RunManager, RunState?>), getter);
        }
        catch (Exception ex) { Log.Error($"[StsStats] RunManager.State getter failed: {ex.Message}"); return null; }
    }

    private static object? _runStartFor;   // run_start を送ったランの RunState (同じランで 2 回送らない)
    private static object? _trackedRun;    // ランごとの状態 (下の 3 つ) が対象にしているランの RunState

    public static bool InCombat
    {
        get
        {
            try { return CombatManager.Instance.IsInProgress; }
            catch { return false; }
        }
    }

    private static bool _combatOpen;          // combat_start を送って combat_end をまだ送っていない
    private static bool _runEndEmitted;
    private static readonly Dictionary<string, int> _finalHp = new();

    // === 今のラン ===============================================================

    /// <summary>
    /// event を送る前に呼ぶ。前と違うランなら、ランごとの状態を作り直してセッションを切り替える。
    /// 再開直後は「部屋に入った」より先に階の確定が走るので、部屋に入った時だけでは前のランのセッションに送ってしまう。
    /// </summary>
    public static void Track(IRunState runState)
    {
        Run = runState;
        if (!ReferenceEquals(_trackedRun, runState))
        {
            _trackedRun = runState;
            _runEndEmitted = false;
            _combatOpen = false;
            _finalHp.Clear();
        }
        EventBuffer.UpdateFloor(runState.TotalFloor);
        Session.Ensure(runState);
    }

    // === ラン開始・部屋 =========================================================

    public static void AfterRoomEnteredPostfix(IRunState? runState, AbstractRoom? room)
    {
        try
        {
            if (runState == null) return;
            Track(runState);

            if (!SessionManager.RunStartAlreadyEmitted && !ReferenceEquals(_runStartFor, runState))
            {
                _runStartFor = runState;
                EmitRunStart(runState);
                if (ModEntry.SessionStore != null) SessionManager.MarkRunStartEmitted(ModEntry.SessionStore);
            }

            FloorRecorder.EmitLive(runState);
            CatalogDumper.DumpOnce();
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterRoomEntered error: {ex.Message}"); }
    }

    private static void EmitRunStart(IRunState runState)
    {
        string seed = SafeSeed(runState);
        foreach (var p in runState.Players)
        {
            EventBuffer.EmitGlobalEvent("run_start", Identity.Of(p), new
            {
                character_id = p.Character?.Id.Entry ?? "",
                character_name = p.Character != null ? ModelInfo.Text(p.Character.Title) : "",
                ascension    = runState.AscensionLevel,
                seed         = seed,
                game_mode    = runState.GameMode.ToString(),
                player_name  = Identity.NameOf(p),
                hp           = p.Creature.CurrentHp,
                max_hp       = p.Creature.MaxHp,
                gold         = p.Gold,
            });
        }
    }

    // === 戦闘 ===================================================================

    /// <summary>
    /// Hook.BeforeCombatStart の Prefix。レリック等が戦闘開始時の効果 (棘・プレートの付与等) を実行するより前に戦闘を始める。
    /// Postfix だと、非同期の Hook が最初に待つ所まで効果が進んだ後に呼ばれ、付けたパワーの出どころの記録を消してしまう。
    /// </summary>
    public static void BeforeCombatStartPrefix(IRunState? runState, ICombatState? combatState)
    {
        try
        {
            if (runState != null) Track(runState);
            EventBuffer.BeginCombat();
            PowerOriginRegistry.ClearForCombat();
            _combatOpen = true;

            var enc = combatState?.Encounter;
            EventBuffer.EmitCombatEvent("combat_start", null, new
            {
                combat_index   = EventBuffer.CurrentCombatIndex,
                encounter_id   = enc?.Id.Entry,
                encounter_name = enc != null ? ModelInfo.Text(enc.Title) : null,
                room_type      = runState?.CurrentRoom?.RoomType.ToString(),
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] BeforeCombatStart error: {ex.Message}"); }
    }

    /// <summary>Hook.AfterCombatEnd はゲームが勝利時にしか呼ばない (CombatManager.EndCombatInternal)。</summary>
    public static void AfterCombatEndPostfix(IRunState? runState)
    {
        try
        {
            EmitCombatEnd(victory: true);
            if (runState != null) FloorRecorder.EmitLive(runState);
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterCombatEnd error: {ex.Message}"); }
    }

    private static void EmitCombatEnd(bool victory)
    {
        if (!_combatOpen) return;
        _combatOpen = false;
        EventBuffer.EmitCombatEvent("combat_end", null, new
        {
            combat_index = EventBuffer.CurrentCombatIndex,
            victory      = victory,
        });
    }

    /// <summary>プレイヤー側のターン終了 = ターン区切り。</summary>
    public static void AfterSideTurnEndPostfix(CombatSide side)
    {
        try
        {
            if (side != CombatSide.Player) return;
            EventBuffer.BeginTurn();
            if (Run != null) FloorRecorder.EmitLive(Run);
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterSideTurnEnd error: {ex.Message}"); }
    }

    // === ラン終了 ================================================================

    /// <summary>RunManager.WinRun / Abandon の直前。ゲームはこの後で全員を倒すので、ここで HP を控える。</summary>
    public static void BeforeRunTerminatedPrefix()
    {
        try
        {
            var run = Run;
            if (run == null) return;
            Track(run);
            _finalHp.Clear();
            foreach (var p in run.Players) _finalHp[Identity.Of(p)] = p.Creature.CurrentHp;
        }
        catch (Exception ex) { Log.Error($"[StsStats] WinRun/Abandon prefix error: {ex.Message}"); }
    }

    /// <summary>
    /// RunManager.OnEnded(isVictory)。勝利・全滅・放棄の全てがここを通る。
    /// 勝利後の「全員を倒す」処理で 2 回目が来るので、最初の 1 回だけ使う。
    /// この時点で最終階の floor_snapshot は送信済み (OnEnded 冒頭の UpdatePlayerStatsInMapPointHistory)。
    /// </summary>
    public static void OnEndedPostfix(bool isVictory, PostRunProgressUpdateResult __result)
    {
        try
        {
            var run = Run;
            if (run == null) return;
            Track(run);
            if (_runEndEmitted) return;
            _runEndEmitted = true;
            if (_combatOpen) EmitCombatEnd(victory: false);

            bool abandoned = false;
            try { abandoned = RunManager.Instance.IsAbandoned; } catch { }
            string outcome = isVictory ? "victory" : abandoned ? "abandoned" : "death";

            var finalHp = run.Players.ToDictionary(
                p => Identity.Of(p),
                p => _finalHp.TryGetValue(Identity.Of(p), out var hp) ? hp : p.Creature.CurrentHp);

            EventBuffer.EmitGlobalEvent("run_end", null, new
            {
                outcome     = outcome,
                final_floor = run.TotalFloor,
                final_hp    = finalHp,
                badges      = Badges(__result.run, isVictory),
            });
            EventBuffer.FlushOutgoing();
            Log.Info($"[StsStats] run_end outcome={outcome} floor={run.TotalFloor}");
        }
        catch (Exception ex) { Log.Error($"[StsStats] OnEnded postfix error: {ex.Message}"); }
    }

    /// <summary>
    /// ゲームオーバー画面のバッジ。ゲームと同じ判定 (ScoreUtility.GetBadges) を各プレイヤーに行う。
    /// 名前・説明は翻訳 badges の {ID}.{bronze|silver|gold}Title / Description (無ければ {ID}.title / description)。NBadge.Create と同じ。
    /// </summary>
    private static Dictionary<string, List<object>> Badges(SerializableRun? run, bool isVictory)
    {
        var result = new Dictionary<string, List<object>>();
        if (run == null) return result;
        foreach (var sp in run.Players)
        {
            var list = new List<object>();
            try
            {
                foreach (var b in ScoreUtility.GetBadges(run, sp.NetId, isVictory))
                    list.Add(BadgeInfo(b.Id, b.Rarity));
            }
            catch (Exception ex) { Log.Error($"[StsStats] badges error: {ex.Message}"); }
            result[Identity.OfNetId(sp.NetId)] = list;
        }
        return result;
    }

    /// <summary>run_end.badges の 1 件。名前と説明は翻訳 badges から引く (Badges の説明のとおり)。</summary>
    internal static object BadgeInfo(string id, BadgeRarity rarity)
    {
        string prefix = rarity switch { BadgeRarity.Bronze => "bronze", BadgeRarity.Silver => "silver", BadgeRarity.Gold => "gold", _ => "" };
        bool ranked = prefix != "" && LocString.Exists("badges", $"{id}.{prefix}Title");
        string titleKey = ranked ? $"{id}.{prefix}Title" : $"{id}.title";
        string descKey  = ranked ? $"{id}.{prefix}Description" : $"{id}.description";
        return new
        {
            id          = id,
            name        = ModelInfo.Text(new LocString("badges", titleKey)),
            description = ModelInfo.Text(new LocString("badges", descKey)),
            rarity      = rarity.ToString(),
        };
    }

    internal static string SafeSeed(IRunState runState)
    {
        try { return runState.Rng?.StringSeed ?? ""; }
        catch { return ""; }
    }
}
