namespace StsStats;

/// <summary>
/// <see cref="DamageSourceContext"/> に値を出し入れするための Harmony patch 群。
///
/// 対象メソッド（例: <c>PoisonPower.AfterSideTurnStart</c>, <c>LightningOrb.Evoke</c>）
/// を ModEntry で AccessTools.Method 経由で patch し、Prefix で context を Push、
/// Postfix で Restore する。途中で <c>AfterDamageGiven</c> が発火すると
/// <see cref="HookPatches.AfterDamageGivenPostfix"/> が <c>DamageSourceContext.Current</c>
/// を fallback CardInfo として拾う。
///
/// 各 source 用に静的な CardInfo を用意しておき、card_id は予約値（カッコ付き）にして
/// 既存カードと衝突しないようにする。
/// </summary>
internal static class IndirectDamagePatches
{
    // 予約 card_id は既存カードと衝突しないよう、カッコで囲んだ識別子を使う。
    public static readonly CardInfo Poison           = new("(poison)",            "毒",                     "Power");
    public static readonly CardInfo Doom             = new("(doom)",              "破滅 (Doom)",            "Power");
    public static readonly CardInfo LightningEvoke   = new("(lightning_evoke)",   "ライトニング (発動)",    "Orb");
    public static readonly CardInfo LightningPassive = new("(lightning_passive)", "ライトニング (パッシブ)", "Orb");

    // ---- PoisonPower.AfterSideTurnStart(CombatSide, ICombatState) → Task ----

    public static void PoisonPrefix(out CardInfo? __state)
    {
        __state = DamageSourceContext.Push(Poison);
    }
    public static void PoisonPostfix(CardInfo? __state)
    {
        DamageSourceContext.Restore(__state);
    }

    // ---- DoomPower.BeforeTurnEnd(PlayerChoiceContext, CombatSide) → Task ----

    public static void DoomPrefix(out CardInfo? __state)
    {
        __state = DamageSourceContext.Push(Doom);
    }
    public static void DoomPostfix(CardInfo? __state)
    {
        DamageSourceContext.Restore(__state);
    }

    // ---- LightningOrb.Evoke(PlayerChoiceContext) → Task<IEnumerable<...>> ----

    public static void LightningEvokePrefix(out CardInfo? __state)
    {
        __state = DamageSourceContext.Push(LightningEvoke);
    }
    public static void LightningEvokePostfix(CardInfo? __state)
    {
        DamageSourceContext.Restore(__state);
    }

    // ---- LightningOrb.Passive(PlayerChoiceContext, Creature) → Task ----

    public static void LightningPassivePrefix(out CardInfo? __state)
    {
        __state = DamageSourceContext.Push(LightningPassive);
    }
    public static void LightningPassivePostfix(CardInfo? __state)
    {
        DamageSourceContext.Restore(__state);
    }
}
