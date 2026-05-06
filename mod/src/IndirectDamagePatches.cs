namespace StsStats;

/// <summary>
/// <see cref="DamageSourceContext"/> / <see cref="BlockSourceContext"/> に値を
/// 出し入れするための Harmony patch 群。
///
/// 対象 power / orb メソッドを ModEntry で AccessTools.Method 経由で patch し、
/// Prefix で context を Push、Postfix で Restore する。途中で AfterDamageGiven /
/// AfterBlockGained が発火すると、それぞれの Hook ハンドラが
/// <c>DamageSourceContext.Current</c> / <c>BlockSourceContext.Current</c> を
/// fallback として拾って source を埋める。
///
/// 各 source 用に静的な CardInfo を用意しておき、card_id は予約値（カッコ付き）にして
/// 既存カードと衝突しないようにする。CardType に "Power" / "Orb" を入れて区別。
/// </summary>
internal static class IndirectDamagePatches
{
    // 表示名はゲーム内の正式日本語名に依存しない英語ラベルにしておき、
    // 必要なら web 側で powerNames lookup（mod から拾った PowerModel.Title）に
    // 寄せて表示する。勝手な邦訳は付けない。
    //
    // ---- DoT / 間接ダメ ----
    public static readonly CardInfo Poison             = new("(poison)",                "Poison",            "Power");
    public static readonly CardInfo Doom               = new("(doom)",                  "Doom",              "Power");
    public static readonly CardInfo LightningEvoke     = new("(lightning_evoke)",       "Lightning (manual)", "Orb");
    public static readonly CardInfo LightningEvokeAuto = new("(lightning_evoke_auto)",  "Lightning (auto)",   "Orb");
    public static readonly CardInfo LightningPassive   = new("(lightning_passive)",     "Lightning (passive)","Orb");

    // ---- 反射ダメ ----
    public static readonly CardInfo Thorns             = new("(thorns)",                "Thorns",            "Power");
    public static readonly CardInfo FlameBarrier       = new("(flame_barrier)",         "Flame Barrier",     "Power");

    // ---- パワー由来ブロック ----
    public static readonly CardInfo Rampart            = new("(rampart)",               "Rampart",           "Power");
    public static readonly CardInfo BlockNextTurn      = new("(block_next_turn)",       "Block Next Turn",   "Power");
    public static readonly CardInfo MockBlockOnAttack  = new("(mock_block_on_attack)",  "Mock Block On Attack","Power");

    // === DoT ====================================================================

    public static void PoisonPrefix(out CardInfo? __state) { __state = DamageSourceContext.Push(Poison); }
    public static void PoisonPostfix(CardInfo? __state)    { DamageSourceContext.Restore(__state); }

    public static void DoomPrefix(out CardInfo? __state) { __state = DamageSourceContext.Push(Doom); }
    public static void DoomPostfix(CardInfo? __state)    { DamageSourceContext.Restore(__state); }

    public static void LightningEvokePrefix(out CardInfo? __state)
    {
        // カードプレイ中なら手動 (Zap 等)、外なら自動 (channel overflow / EOT 等)
        var info = CardPlayedScope.Active ? LightningEvoke : LightningEvokeAuto;
        __state = DamageSourceContext.Push(info);
    }
    public static void LightningEvokePostfix(CardInfo? __state)    { DamageSourceContext.Restore(__state); }

    public static void LightningPassivePrefix(out CardInfo? __state) { __state = DamageSourceContext.Push(LightningPassive); }
    public static void LightningPassivePostfix(CardInfo? __state)    { DamageSourceContext.Restore(__state); }

    // === 反射 ===================================================================

    public static void ThornsPrefix(out CardInfo? __state) { __state = DamageSourceContext.Push(Thorns); }
    public static void ThornsPostfix(CardInfo? __state)    { DamageSourceContext.Restore(__state); }

    public static void FlameBarrierPrefix(out CardInfo? __state) { __state = DamageSourceContext.Push(FlameBarrier); }
    public static void FlameBarrierPostfix(CardInfo? __state)    { DamageSourceContext.Restore(__state); }

    // === パワー由来ブロック =====================================================

    public static void RampartPrefix(out CardInfo? __state) { __state = BlockSourceContext.Push(Rampart); }
    public static void RampartPostfix(CardInfo? __state)    { BlockSourceContext.Restore(__state); }

    public static void BlockNextTurnPrefix(out CardInfo? __state) { __state = BlockSourceContext.Push(BlockNextTurn); }
    public static void BlockNextTurnPostfix(CardInfo? __state)    { BlockSourceContext.Restore(__state); }

    public static void MockBlockOnAttackPrefix(out CardInfo? __state) { __state = BlockSourceContext.Push(MockBlockOnAttack); }
    public static void MockBlockOnAttackPostfix(CardInfo? __state)    { BlockSourceContext.Restore(__state); }
}
