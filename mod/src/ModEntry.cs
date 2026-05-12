using System;
using System.IO;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
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
            PatchHook(nameof(Hook.ModifyDamage),             nameof(HookPatches.ModifyDamagePostfix));
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

            // === ラン全体ビュー用 events =================================
            PatchHookGeneric(nameof(Hook.AfterRoomEntered),       nameof(RunOverviewPatches.AfterRoomEnteredPostfix));
            PatchHookGeneric(nameof(Hook.AfterCurrentHpChanged),  nameof(RunOverviewPatches.AfterCurrentHpChangedPostfix));
            PatchHookGeneric(nameof(Hook.AfterGoldGained),        nameof(RunOverviewPatches.AfterGoldGainedPostfix));
            PatchHookGeneric(nameof(Hook.AfterActEntered),        nameof(RunOverviewPatches.AfterActEnteredPostfix));
            PatchHookGeneric(nameof(Hook.AfterRestSiteHeal),      nameof(RunOverviewPatches.AfterRestSiteHealPostfix));
            PatchHookGeneric(nameof(Hook.AfterRestSiteSmith),     nameof(RunOverviewPatches.AfterRestSiteSmithPostfix));
            // Hook.AfterItemPurchased は ClearAfterPurchase 後に fire するため使えない。
            // 各 MerchantEntry サブクラスの OnTryPurchase に直接 patch する（下記）。
            PatchHookGeneric(nameof(Hook.AfterRewardTaken),       nameof(RunOverviewPatches.AfterRewardTakenPostfix));
            PatchHookGeneric(nameof(Hook.AfterPotionProcured),    nameof(RunOverviewPatches.AfterPotionProcuredPostfix));
            PatchHookGeneric(nameof(Hook.AfterPotionDiscarded),   nameof(RunOverviewPatches.AfterPotionDiscardedPostfix));
            PatchHookGeneric(nameof(Hook.BeforeCardRemoved),      nameof(RunOverviewPatches.BeforeCardRemovedPostfix));

            // === canonical な単一経路で run-overview のデータを取る patches ===
            // CardCmd.Upgrade(IEnumerable<CardModel>, CardPreviewStyle): 全アップグレード
            //   (smith / event / カード効果) の単一経路。pile.Type==Deck のみ実 upgrade。
            //   +1 報酬カードの生成は pile が Deck 以外なので自動除外される。
            // CardPreviewStyle は MegaCrit.Sts2.Core.Nodes.CommonUi namespace (デコンパイル確認済)。
            // MegaCrit.Sts2.Core.Models.CardPreviewStyle と書いてた古い実装は TypeByName が
            // null を返して "Value cannot be null. (Parameter 'types')" で patch fail してた。
            PatchInstanceMethodByName("MegaCrit.Sts2.Core.Commands.CardCmd", "Upgrade",
                nameof(RunOverviewPatches.CardCmdUpgradePostfix),
                new Type[] {
                    typeof(System.Collections.Generic.IEnumerable<>).MakeGenericType(AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardModel")!),
                    AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.CommonUi.CardPreviewStyle")!,
                });
            // CardModel.FloorAddedToDeck setter: deck 追加完了時 (= マスターデッキに入った瞬間) に sync で呼ばれる。
            // CardCmd.Add は async で Postfix の timing が悪いため、setter 経由で確実に拾う。
            PatchPropertySetter(typeof(CardModel), "FloorAddedToDeck", nameof(RunOverviewPatches.FloorAddedToDeckSetterPostfix));
            // Player.Gold setter: gain / loss / 任意の reset を全部 sync で捕捉。
            // Hook.AfterGoldGained は gain にしか発火しないため、event 罰 / shop 購入等の loss が
            // 取れない問題への対応。
            PatchPropertySetterByName("MegaCrit.Sts2.Core.Entities.Players.Player", "Gold",
                nameof(RunOverviewPatches.PlayerGoldSetterPostfix));
            // RelicModel.FloorAddedToDeck setter: RelicCmd.Obtain 内で必ず set されるため、
            // STS2 の全レリック取得 (treasure / reward / event / 戦闘ドロップ) を sync で捕捉。
            // 旧 RelicCmd.Obtain patch は Obtain<T>(Player) との overload 衝突 (Ambiguous match)
            // で起動時 fail してた → setter patch に置換。
            PatchPropertySetterByName("MegaCrit.Sts2.Core.Models.RelicModel", "FloorAddedToDeck",
                nameof(RunOverviewPatches.RelicFloorAddedToDeckSetterPostfix));

            // Merchant***Entry.OnTryPurchase に直接 patch（Hook.AfterItemPurchased では遅すぎるため）
            PatchInstanceMethodByName("MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardEntry",         "OnTryPurchase", nameof(RunOverviewPatches.MerchantCardEntryOnTryPurchasePostfix));
            PatchInstanceMethodByName("MegaCrit.Sts2.Core.Entities.Merchant.MerchantPotionEntry",       "OnTryPurchase", nameof(RunOverviewPatches.MerchantPotionEntryOnTryPurchasePostfix));
            PatchInstanceMethodByName("MegaCrit.Sts2.Core.Entities.Merchant.MerchantRelicEntry",        "OnTryPurchase", nameof(RunOverviewPatches.MerchantRelicEntryOnTryPurchasePostfix));
            // MerchantCardRemovalEntry には 2 つの OnTryPurchase オーバーロードがある（cancelable 引数あり/なし）。
            // 親クラスシグネチャ (MerchantInventory?, bool) を指定して disambiguate
            PatchInstanceMethodByName(
                "MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardRemovalEntry",
                "OnTryPurchase",
                nameof(RunOverviewPatches.MerchantCardRemovalEntryOnTryPurchasePostfix),
                new Type[] {
                    AccessTools.TypeByName("MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory")!,
                    typeof(bool),
                });

            // CardCmd.Enchant が「player action としてのエンチャ実行」の単一経路。
            // EnchantInternal を直接 patch すると deck reload 等でも発火して大量誤検出する。
            // CardCmd.Enchant は overload (Enchant<T>(card, amount) / Enchant(enchantment, card, amount))
            // あるが、generic の方は内部で非 generic を呼ぶので非 generic だけ patch すれば足りる。
            PatchInstanceMethodByName("MegaCrit.Sts2.Core.Commands.CardCmd", "Enchant",
                nameof(RunOverviewPatches.CardCmdEnchantPostfix),
                new Type[] {
                    AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.EnchantmentModel")!,
                    AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.CardModel")!,
                    typeof(decimal),
                });
            // EventOption.Chosen でランダムイベントの選択肢を補足
            PatchInstanceMethodByName("MegaCrit.Sts2.Core.Events.EventOption", "Chosen", nameof(RunOverviewPatches.EventOptionChosenPostfix));

            // CardReward.OnSkipped: skip 時 Hook.AfterRewardTaken は発火しないので
            // OnSkipped を patch して synthetic reward_taken を emit する。
            PatchInstanceMethodByName("MegaCrit.Sts2.Core.Rewards.CardReward", "OnSkipped",
                nameof(RunOverviewPatches.CardRewardOnSkippedPostfix));

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

            // catalog dump を最早 trigger で試す。ModelDb がまだ populated されてなければ
            // CatalogDumper 内で no-op になる (_dumped を立てない) → 後続の AfterRoomEntered /
            // BeforeCombatStart で再試行される。理想的にはここ (mod Initialize) で成功して
            // ユーザは新規ラン開始すら不要、ゲーム起動するだけで dump 完了する。
            try { CatalogDumper.DumpOnce(); } catch { }
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

    /// <summary>名前空間付き型名で型を特定して instance method を patch する。</summary>
    private static void PatchInstanceMethodByName(string fullTypeName, string methodName, string postfixName, Type[]? paramTypes = null)
    {
        try
        {
            var type = AccessTools.TypeByName(fullTypeName);
            if (type == null)
            {
                Log.Error($"[StsStats] Type not found: {fullTypeName}");
                return;
            }
            // overload 解決のためのパラメータ型指定対応
            MethodInfo? method = paramTypes != null
                ? AccessTools.Method(type, methodName, paramTypes)
                : AccessTools.Method(type, methodName);
            if (method == null)
            {
                Log.Error($"[StsStats] Method not found: {type.Name}.{methodName}");
                return;
            }
            MethodInfo postfix = AccessTools.Method(typeof(RunOverviewPatches), postfixName)
                ?? throw new MissingMethodException(nameof(RunOverviewPatches), postfixName);
            _harmony!.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Info($"[StsStats] Patched: {type.Name}.{methodName}");
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] PatchInstanceMethodByName({fullTypeName}.{methodName}) failed: {ex.Message}");
        }
    }

    /// <summary>名前空間付き型名で property setter を patch する。</summary>
    private static void PatchPropertySetterByName(string fullTypeName, string propertyName, string postfixName)
    {
        try
        {
            var type = AccessTools.TypeByName(fullTypeName);
            if (type == null) { Log.Error($"[StsStats] Type not found: {fullTypeName}"); return; }
            PatchPropertySetter(type, propertyName, postfixName);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] PatchPropertySetterByName({fullTypeName}.{propertyName}) failed: {ex.Message}");
        }
    }

    /// <summary>property setter を patch する（postfix で value/__instance を受け取れる）。</summary>
    private static void PatchPropertySetter(Type ownerType, string propertyName, string postfixName)
    {
        try
        {
            var setter = AccessTools.PropertySetter(ownerType, propertyName);
            if (setter == null)
            {
                Log.Error($"[StsStats] Property setter not found: {ownerType.Name}.{propertyName}");
                return;
            }
            MethodInfo postfix = AccessTools.Method(typeof(RunOverviewPatches), postfixName)
                ?? throw new MissingMethodException(nameof(RunOverviewPatches), postfixName);
            _harmony!.Patch(setter, postfix: new HarmonyMethod(postfix));
            Log.Info($"[StsStats] Patched setter: {ownerType.Name}.{propertyName}");
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] PatchPropertySetter({ownerType.Name}.{propertyName}) failed: {ex.Message}");
        }
    }

    /// <summary>任意の type の instance method を patch する（public/non-public 両対応）。</summary>
    private static void PatchInstanceMethod(Type ownerType, string methodName, string postfixName)
    {
        try
        {
            var method = AccessTools.Method(ownerType, methodName);
            if (method == null)
            {
                Log.Error($"[StsStats] Method not found: {ownerType.Name}.{methodName}");
                return;
            }
            MethodInfo postfix = AccessTools.Method(typeof(RunOverviewPatches), postfixName)
                ?? throw new MissingMethodException(nameof(RunOverviewPatches), postfixName);
            _harmony!.Patch(method, postfix: new HarmonyMethod(postfix));
            Log.Info($"[StsStats] Patched: {ownerType.Name}.{methodName}");
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] PatchInstanceMethod({ownerType.Name}.{methodName}) failed: {ex.Message}");
        }
    }

    /// <summary>RunOverviewPatches 等、HookPatches 以外の class からの patch を登録する用。</summary>
    private static void PatchHookGeneric(string hookName, string postfixName)
    {
        try
        {
            MethodInfo original = AccessTools.Method(typeof(Hook), hookName)
                ?? throw new MissingMethodException(nameof(Hook), hookName);
            MethodInfo postfix = AccessTools.Method(typeof(RunOverviewPatches), postfixName)
                ?? throw new MissingMethodException(nameof(RunOverviewPatches), postfixName);
            _harmony!.Patch(original, postfix: new HarmonyMethod(postfix));
            Log.Info($"[StsStats] Patched: {hookName}");
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] PatchHookGeneric({hookName}) failed: {ex.Message}");
        }
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
