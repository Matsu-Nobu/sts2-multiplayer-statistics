using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;

namespace StsStats;

[ModInitializer("Initialize")]
public static class ModEntry
{
    private static Harmony? _harmony;
    private const string HarmonyId = "com.nobu.sts2.stats";

    /// <summary>HTTP クライアント。SessionConfig.HttpEnabled が false の場合 null。</summary>
    internal static IApiClient? ApiClient { get; private set; }

    /// <summary>HTTP 送信キュー。SessionConfig.HttpEnabled が false の場合 null。</summary>
    internal static HttpSender? HttpSender { get; private set; }

    /// <summary>run_key → session 永続化ストア。</summary>
    internal static RunSessionStore? SessionStore { get; private set; }

    /// <summary>
    /// patch の一覧 (docs/spec/data-sources.md §1)。
    /// 1 行 = 1 patch。tools/verify-game-api がこの表を読み、ゲーム更新後の sts2.dll に対して
    /// 「対象が存在するか」「postfix の引数がすべて結び付くか」を確認する。書式を崩さないこと:
    ///   P(typeof(対象の型), "メソッド名", typeof(patch のクラス), "prefix名 or null", "postfix名 or null"[, 引数の型...]);
    /// </summary>
    private static void PatchTable()
    {
        // ラン・戦闘の始まりと終わり
        P(typeof(Hook),       "AfterRoomEntered",   typeof(Lifecycle), null, "AfterRoomEnteredPostfix");
        P(typeof(Hook),       "BeforeCombatStart",  typeof(Lifecycle), null, "BeforeCombatStartPostfix");
        P(typeof(Hook),       "AfterCombatEnd",     typeof(Lifecycle), null, "AfterCombatEndPostfix");
        P(typeof(Hook),       "AfterSideTurnEnd",   typeof(Lifecycle), null, "AfterSideTurnEndPostfix");
        P(typeof(RunManager), "OnEnded",            typeof(Lifecycle), null, "OnEndedPostfix");
        P(typeof(RunManager), "WinRun",             typeof(Lifecycle), "BeforeRunTerminatedPrefix", null);
        P(typeof(RunManager), "Abandon",            typeof(Lifecycle), "BeforeRunTerminatedPrefix", null);

        // ラン全体 (階ごとの記録)
        P(typeof(RunManager), "UpdatePlayerStatsInMapPointHistory", typeof(FloorRecorder), null, "UpdatePlayerStatsPostfix");
        P(typeof(Hook),       "AfterRewardTaken",   typeof(FloorRecorder), null, "AfterRunActionPostfix");
        P(typeof(Hook),       "AfterRestSiteHeal",  typeof(FloorRecorder), null, "AfterRunActionPostfix");
        P(typeof(Hook),       "AfterRestSiteSmith", typeof(FloorRecorder), null, "AfterRunActionPostfix");
        P(typeof(MerchantCardEntry),        "OnTryPurchase", typeof(FloorRecorder), null, "MerchantCardPurchasePostfix");
        P(typeof(MerchantPotionEntry),      "OnTryPurchase", typeof(FloorRecorder), null, "MerchantPotionPurchasePostfix");
        P(typeof(MerchantRelicEntry),       "OnTryPurchase", typeof(FloorRecorder), null, "MerchantRelicPurchasePostfix");
        P(typeof(MerchantCardRemovalEntry), "OnTryPurchase", typeof(FloorRecorder), null, "MerchantCardRemovalPurchasePostfix", typeof(MerchantInventory), typeof(bool));

        // 戦闘
        P(typeof(CreatureCmd), "Damage",           typeof(ModifierLog), "DamageScopePrefix", "DamageScopePostfix", typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(CardPlay));
        P(typeof(Hook), "ModifyDamage",            typeof(CombatRecorder), null, "ModifyDamagePostfix");
        P(typeof(Hook), "AfterDamageGiven",        typeof(CombatRecorder), null, "AfterDamageGivenPostfix");
        P(typeof(Hook), "AfterCurrentHpChanged",   typeof(CombatRecorder), null, "AfterCurrentHpChangedPostfix");
        P(typeof(Hook), "AfterBlockGained",        typeof(CombatRecorder), null, "AfterBlockGainedPostfix");
        P(typeof(Hook), "AfterBlockCleared",       typeof(CombatRecorder), null, "AfterBlockClearedPostfix");
        P(typeof(Hook), "ModifyHpLost",            typeof(CombatRecorder), null, "ModifyHpLostPostfix");
        P(typeof(Hook), "AfterEnergySpent",        typeof(CombatRecorder), null, "AfterEnergySpentPostfix");
        P(typeof(Hook), "AfterCardPlayed",         typeof(CombatRecorder), null, "AfterCardPlayedPostfix");
        P(typeof(Hook), "AfterCardDrawn",          typeof(CombatRecorder), null, "AfterCardDrawnPostfix");
        P(typeof(Hook), "AfterPowerAmountChanged", typeof(CombatRecorder), null, "AfterPowerAmountChangedPostfix");
        P(typeof(Hook), "AfterPotionUsed",         typeof(CombatRecorder), null, "AfterPotionUsedPostfix");
    }

    private static int _patchOk, _patchFailed;

    public static void Initialize()
    {
        if (_harmony != null) return;

        try
        {
            _harmony = new Harmony(HarmonyId);

            PatchTable();
            if (FloorRecorder.PlayerField == null) { _patchFailed++; }
            if (Lifecycle.StateGetter == null)     { _patchFailed++; }
            Log.Info($"[StsStats] Patch table: {_patchOk} ok, {_patchFailed} failed");

            SourceContext.AutoPatch(_harmony);

            StatsLogger.Initialize();

            string sessionDir = Path.Combine(SafeUserDataDir(), "sts_stats_sessions");
            SessionStore = new RunSessionStore(sessionDir);
            Log.Info($"[StsStats] Session store: {sessionDir}");

            SessionConfig.Load();
            if (SessionConfig.HttpEnabled)
            {
                ApiClient  = new ApiClient(SessionConfig.BackendUrl);
                HttpSender = new HttpSender(ApiClient);
                Log.Info($"[StsStats] HTTP enabled, backend: {SessionConfig.BackendUrl}");
            }
            else
            {
                Log.Info("[StsStats] HTTP disabled (backend URL is empty), JSONL only");
            }

            Log.Info("[StsStats] Initialized successfully");

            // ModelDb が準備済みなら起動直後にカタログを書き出す (未準備なら部屋に入ったときに再試行)
            try { CatalogDumper.DumpOnce(); } catch { }
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] Initialization failed: {ex}");
        }
    }

    /// <summary>patch を 1 件当てる。見つからない・当たらない場合はエラーログを出して数える (黙って続けない)。</summary>
    private static void P(Type target, string method, Type patchClass, string? prefix, string? postfix, params Type[] args)
    {
        string label = $"{target.Name}.{method}";
        try
        {
            MethodBase? original = args.Length > 0
                ? AccessTools.Method(target, method, args)
                : AccessTools.Method(target, method);
            if (original == null) throw new MissingMethodException(target.Name, method);

            HarmonyMethod? pre  = prefix  != null ? new HarmonyMethod(AccessTools.Method(patchClass, prefix)  ?? throw new MissingMethodException(patchClass.Name, prefix))  : null;
            HarmonyMethod? post = postfix != null ? new HarmonyMethod(AccessTools.Method(patchClass, postfix) ?? throw new MissingMethodException(patchClass.Name, postfix)) : null;
            _harmony!.Patch(original, prefix: pre, postfix: post);
            _patchOk++;
            Log.Info($"[StsStats] Patched: {label}");
        }
        catch (Exception ex)
        {
            _patchFailed++;
            Log.Error($"[StsStats] PATCH FAILED: {label}: {ex.Message}");
        }
    }

    private static string SafeUserDataDir()
    {
        try { return OS.GetUserDataDir(); }
        catch { return "/tmp"; }
    }
}
