using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using System.Reflection;

namespace StsStats;

[ModInitializer("Initialize")]
public static class ModEntry
{
    private static Harmony? _harmony;
    private const string HarmonyId = "com.nobu.sts2.stats";

    public static void Initialize()
    {
        if (_harmony != null) return;

        try
        {
            _harmony = new Harmony(HarmonyId);

            PatchHook(nameof(Hook.BeforeCombatStart),   nameof(HookPatches.BeforeCombatStartPostfix));
            PatchHook(nameof(Hook.AfterDamageGiven),     nameof(HookPatches.AfterDamageGivenPostfix));
            PatchHook(nameof(Hook.AfterPlayerTurnStart), nameof(HookPatches.AfterPlayerTurnStartPostfix));
            PatchHook(nameof(Hook.AfterCombatEnd),       nameof(HookPatches.AfterCombatEndPostfix));

            StatsLogger.Initialize();
            Log.Info("[StsStats] Initialized successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] Initialization failed: {ex}");
        }
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
