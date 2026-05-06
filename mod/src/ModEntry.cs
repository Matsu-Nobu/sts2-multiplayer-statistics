using System;
using System.IO;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;

namespace StsStats;

[ModInitializer("Initialize")]
public static class ModEntry
{
    private static Harmony? _harmony;
    private const string HarmonyId = "com.nobu.sts2.stats";

    /// <summary>HTTP クライアント。SessionConfig.HttpEnabled が false の場合 null。</summary>
    internal static IApiClient? ApiClient { get; private set; }

    /// <summary>HTTP送信キュー。SessionConfig.HttpEnabled が false の場合 null。</summary>
    internal static HttpSender? HttpSender { get; private set; }

    /// <summary>run_key → session 永続化ストア。常に有効（HTTP無効時は使わないだけ）。</summary>
    internal static RunSessionStore? SessionStore { get; private set; }

    public static void Initialize()
    {
        if (_harmony != null) return;

        try
        {
            _harmony = new Harmony(HarmonyId);

            PatchHook(nameof(Hook.BeforeCombatStart),        nameof(HookPatches.BeforeCombatStartPostfix));
            PatchHook(nameof(Hook.AfterPlayerTurnStart),     nameof(HookPatches.AfterPlayerTurnStartPostfix));
            PatchHook(nameof(Hook.AfterTurnEnd),             nameof(HookPatches.AfterTurnEndPostfix));
            PatchHook(nameof(Hook.AfterCombatEnd),           nameof(HookPatches.AfterCombatEndPostfix));
            PatchHook(nameof(Hook.BeforeDamageReceived),     nameof(HookPatches.BeforeDamageReceivedPostfix));
            PatchHook(nameof(Hook.AfterDamageGiven),         nameof(HookPatches.AfterDamageGivenPostfix));
            PatchHook(nameof(Hook.AfterDamageReceived),      nameof(HookPatches.AfterDamageReceivedPostfix));
            PatchHook(nameof(Hook.AfterBlockGained),         nameof(HookPatches.AfterBlockGainedPostfix));
            PatchHook(nameof(Hook.AfterEnergySpent),         nameof(HookPatches.AfterEnergySpentPostfix));
            PatchHook(nameof(Hook.BeforeCardPlayed),         nameof(HookPatches.BeforeCardPlayedPostfix));
            PatchHook(nameof(Hook.AfterCardPlayed),          nameof(HookPatches.AfterCardPlayedPostfix));
            PatchHook(nameof(Hook.AfterCardDrawn),           nameof(HookPatches.AfterCardDrawnPostfix));
            PatchHook(nameof(Hook.AfterPowerAmountChanged),  nameof(HookPatches.AfterPowerAmountChangedPostfix));
            PatchHook(nameof(Hook.AfterPotionUsed),          nameof(HookPatches.AfterPotionUsedPostfix));
            PatchHook(nameof(Hook.AfterCombatVictory),       nameof(HookPatches.AfterCombatVictoryPostfix));
            PatchHook(nameof(Hook.AfterDeath),               nameof(HookPatches.AfterDeathPostfix));

            // 間接ダメージのソース帰属（Hook では識別できないため、ゲーム本体メソッドを直接 patch）
            PatchPower<PoisonPower>(nameof(PoisonPower.AfterSideTurnStart),
                nameof(IndirectDamagePatches.PoisonPrefix), nameof(IndirectDamagePatches.PoisonPostfix));
            PatchPower<DoomPower>(nameof(DoomPower.BeforeTurnEnd),
                nameof(IndirectDamagePatches.DoomPrefix), nameof(IndirectDamagePatches.DoomPostfix));
            PatchOrb<LightningOrb>(nameof(LightningOrb.Evoke),
                nameof(IndirectDamagePatches.LightningEvokePrefix), nameof(IndirectDamagePatches.LightningEvokePostfix));
            PatchOrb<LightningOrb>(nameof(LightningOrb.Passive),
                nameof(IndirectDamagePatches.LightningPassivePrefix), nameof(IndirectDamagePatches.LightningPassivePostfix));

            // 反射ダメ（Thorns / FlameBarrier）
            PatchPower<ThornsPower>("BeforeDamageReceived",
                nameof(IndirectDamagePatches.ThornsPrefix), nameof(IndirectDamagePatches.ThornsPostfix));
            PatchPower<FlameBarrierPower>("AfterDamageReceived",
                nameof(IndirectDamagePatches.FlameBarrierPrefix), nameof(IndirectDamagePatches.FlameBarrierPostfix));

            // パワー由来ブロック（Rampart / BlockNextTurn / MockGainBlockOnAttack）
            PatchPower<RampartPower>("AfterSideTurnStart",
                nameof(IndirectDamagePatches.RampartPrefix), nameof(IndirectDamagePatches.RampartPostfix));
            PatchPower<BlockNextTurnPower>("AfterBlockCleared",
                nameof(IndirectDamagePatches.BlockNextTurnPrefix), nameof(IndirectDamagePatches.BlockNextTurnPostfix));

            StatsLogger.Initialize();

            // run_key → session 永続化ストア
            string sessionDir = Path.Combine(SafeUserDataDir(), "sts_stats_sessions");
            SessionStore = new RunSessionStore(sessionDir);
            Log.Info($"[StsStats] Session store: {sessionDir}");

            // Backend URL を解決し、有効なら HTTP クライアントとキューを起動
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
        }
        catch (System.Exception ex)
        {
            Log.Error($"[StsStats] Initialization failed: {ex}");
        }
    }

    private static string SafeUserDataDir()
    {
        try { return OS.GetUserDataDir(); }
        catch { return "/tmp"; }
    }

    private static void PatchHook(string hookName, string postfixName)
    {
        MethodInfo original = AccessTools.Method(typeof(Hook), hookName)
            ?? throw new MissingMethodException(nameof(Hook), hookName);
        MethodInfo postfix = AccessTools.Method(typeof(HookPatches), postfixName)
            ?? throw new MissingMethodException(nameof(HookPatches), postfixName);
        _harmony!.Patch(original, postfix: new HarmonyMethod(postfix));
        Log.Info($"[StsStats] Patched: {hookName}");
    }

    /// <summary>PoisonPower / DoomPower 等の Power メソッドを patch。Prefix/Postfix を IndirectDamagePatches から取る。</summary>
    private static void PatchPower<T>(string methodName, string prefixName, string postfixName) =>
        PatchInternal(typeof(T), methodName, prefixName, postfixName);

    /// <summary>LightningOrb 等の Orb メソッドを patch。</summary>
    private static void PatchOrb<T>(string methodName, string prefixName, string postfixName) =>
        PatchInternal(typeof(T), methodName, prefixName, postfixName);

    private static void PatchInternal(Type ownerType, string methodName, string prefixName, string postfixName)
    {
        try
        {
            MethodInfo? original = AccessTools.Method(ownerType, methodName);
            if (original == null)
            {
                Log.Error($"[StsStats] Method not found: {ownerType.Name}.{methodName}");
                return;
            }
            MethodInfo? prefix  = AccessTools.Method(typeof(IndirectDamagePatches), prefixName);
            MethodInfo? postfix = AccessTools.Method(typeof(IndirectDamagePatches), postfixName);
            if (prefix == null || postfix == null)
            {
                Log.Error($"[StsStats] Patch methods not found: {prefixName}/{postfixName}");
                return;
            }
            _harmony!.Patch(original, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
            Log.Info($"[StsStats] Patched: {ownerType.Name}.{methodName}");
        }
        catch (Exception ex)
        {
            // 1個失敗しても他の patch / Hook は続行
            Log.Error($"[StsStats] PatchInternal({ownerType.Name}.{methodName}) failed: {ex.Message}");
        }
    }
}
