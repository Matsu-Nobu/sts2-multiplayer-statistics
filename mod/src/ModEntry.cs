using System.IO;
using System.Reflection;
using Godot;
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
            PatchHook(nameof(Hook.AfterCombatEnd),           nameof(HookPatches.AfterCombatEndPostfix));
            PatchHook(nameof(Hook.AfterDamageGiven),         nameof(HookPatches.AfterDamageGivenPostfix));
            PatchHook(nameof(Hook.AfterDamageReceived),      nameof(HookPatches.AfterDamageReceivedPostfix));
            PatchHook(nameof(Hook.AfterBlockGained),         nameof(HookPatches.AfterBlockGainedPostfix));
            PatchHook(nameof(Hook.AfterEnergySpent),         nameof(HookPatches.AfterEnergySpentPostfix));
            PatchHook(nameof(Hook.AfterCardPlayed),          nameof(HookPatches.AfterCardPlayedPostfix));
            PatchHook(nameof(Hook.AfterCardDrawn),           nameof(HookPatches.AfterCardDrawnPostfix));
            PatchHook(nameof(Hook.AfterPowerAmountChanged),  nameof(HookPatches.AfterPowerAmountChangedPostfix));
            PatchHook(nameof(Hook.AfterPotionUsed),          nameof(HookPatches.AfterPotionUsedPostfix));
            PatchHook(nameof(Hook.AfterCombatVictory),       nameof(HookPatches.AfterCombatVictoryPostfix));
            PatchHook(nameof(Hook.AfterDeath),               nameof(HookPatches.AfterDeathPostfix));

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
}
