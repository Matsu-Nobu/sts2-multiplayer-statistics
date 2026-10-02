using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace StsStats;

/// <summary>
/// 戦闘内 event (docs/spec/data-sources.md §1.2)。
///
/// 与ダメ・被ダメは Hook.AfterDamageGiven の 1 か所から作る。この Hook は終了処理中でも、
/// 攻撃者が空でも、致死の一撃でも必ず呼ばれる (CreatureCmd.Damage、デコンパイル確認済 v0.111.0)。
/// Hook.AfterDamageReceived は致死の一撃で呼ばれないので使わない。
/// </summary>
internal static class CombatRecorder
{
    // === ダメージ ================================================================

    /// <summary>Hook.ModifyDamage: ダメージ補正を補正 1 つずつ記録し (ModifierLog)、直後の AfterDamageGiven で消費する。</summary>
    public static void ModifyDamagePostfix(
        Creature? target,
        Creature? dealer,
        decimal damage,
        ValueProp props,
        CardModel? cardSource,
        CardPlay? cardPlay,
        ModifyDamageHookType modifyDamageHookType,
        CardPreviewMode previewMode,
        ref IEnumerable<AbstractModel> modifiers,
        decimal __result)
    {
        try { ModifierLog.RecordDamage(target, dealer, damage, props, cardSource, cardPlay, modifyDamageHookType, previewMode, modifiers, __result); }
        catch (Exception ex) { Log.Error($"[StsStats] ModifyDamage postfix error: {ex.Message}"); }
    }

    /// <summary>Hook.ModifyHpLost: ブロック後の HP 減少補正 (バッファー・霊体等) を記録する (rMit 用)。</summary>
    public static void ModifyHpLostPostfix(
        Creature? target,
        decimal amount,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource,
        HpLossHookPhase phases,
        ref IEnumerable<AbstractModel> modifiers,
        decimal __result)
    {
        try { ModifierLog.RecordHpLost(target, amount, props, dealer, cardSource, phases, modifiers, __result); }
        catch (Exception ex) { Log.Error($"[StsStats] ModifyHpLost postfix error: {ex.Message}"); }
    }

    /// <summary>Hook.AfterBlockCleared: ブロックが消えたので、誰のブロックかの記録も消す。</summary>
    public static void AfterBlockClearedPostfix(Creature? creature)
    {
        if (creature != null) BlockLedger.Clear(creature);
    }

    public static void AfterDamageGivenPostfix(
        ICombatState? combatState,
        Creature?     dealer,
        DamageResult? results,
        Creature?     target,
        CardModel?    cardSource)
    {
        // 同じ対象・攻撃者の補正記録 (と、受けた側の HP 減少補正) を取り出す。返る前でも必ず取り出して捨てる
        var receiverForLog = results?.Receiver ?? target;
        var (modDamage, modHpLost) = ModifierLog.Take(target, dealer, receiverForLog);
        try
        {
            if (combatState == null || results == null) return;
            if (!Lifecycle.InCombat) return;
            var receiver = results.Receiver ?? target;
            if (receiver == null) return;

            int amount   = results.UnblockedDamage;
            int blocked  = results.BlockedDamage;
            int total    = results.TotalDamage;
            int overkill = results.OverkillDamage;
            if (total <= 0 && overkill <= 0) return;

            CardInfo? source = cardSource != null ? CardInfoOf(cardSource) : SourceContext.CurrentInfo(receiver);
            string sourceKind = cardSource != null ? "card" : SourceContext.CurrentKind();

            if (receiver.Side == CombatSide.Enemy)
            {
                // 相手に付けたデバフ (毒など) が出どころなら、そのダメージは付与者全員のもの (スタック比で按分。
                // spec combat-stats.md §4)。player_id は最大スタックの人、内訳は source_appliers。
                // 毒はターン開始時の発動でも、カード効果 (Outbreak 等) からの発動でも同じ扱い (SourceContext)。
                var appliers = cardSource == null ? SourceContext.DebuffOrigins(receiver) : null;
                // 自分側のパワー (トゲ等) が出どころなら、そのパワーを付けた持ち物 (カード別の表用)
                var sourceOrigin = appliers == null && cardSource == null && SourceContext.ResolveModel(receiver) is PowerModel ownPower
                    ? SourceContext.OriginOfPower(ownPower) : null;
                // それ以外の与え手: プレイヤー (ペットは持ち主)。攻撃者が空なら実行中モデルの持ち主。
                string? dealerId = appliers?.FirstOrDefault().PlayerId
                                ?? Identity.OfCreature(dealer, includePets: true)
                                ?? (dealer == null ? SourceContext.CurrentActorPlayerId(receiver) : null);
                if (dealerId == null) return;   // 敵 → 敵

                EventBuffer.EmitTurnEvent("damage_dealt", dealerId, new
                {
                    amount             = amount,
                    total_damage       = total,
                    blocked_damage     = blocked,
                    overkill_damage    = overkill,
                    was_target_killed  = results.WasTargetKilled,
                    is_doom_kill       = false,
                    source_appliers    = ApplierList(appliers),
                    source_origin      = OriginPayload(sourceOrigin),
                    target_creature_id = CreatureId(receiver),
                    source_card_id     = source?.CardId,
                    source_card_name   = source?.CardName,
                    source_card_type   = source?.CardType,
                    source_kind        = sourceKind,
                    active_on_target   = ActivePowersSnapshot.ForCreature(receiver),
                    active_on_dealer   = ActivePowersSnapshot.ForCreature(dealer),
                    modifications      = ModifierLog.ToPayload(modDamage),
                });
            }
            else
            {
                // 受けた側がプレイヤー本人のときだけ被ダメ (ペットの被ダメは持ち主に数えない)
                if (receiver.Player == null) return;
                string receiverId = Identity.Of(receiver.Player);
                // 防いだブロックを、付けた人ごとに分ける。ペットが狙われてもブロックは持ち主のもの (CreatureCmd.Damage)
                var blockOwner = target?.PetOwner?.Creature ?? target ?? receiver;
                var blockSources = BlockLedger.Consume(blockOwner, blocked, blockOwner.Block, receiverId)
                    .Select(b => (object)new { player_id = b.PlayerId, amount = b.Amount }).ToList();
                EventBuffer.EmitTurnEvent("damage_received", receiverId, new
                {
                    amount             = amount,
                    total_damage       = total,
                    blocked_damage     = blocked,
                    source_creature_id = dealer != null ? CreatureId(dealer) : null,
                    source_card_id     = source?.CardId,
                    active_on_target   = ActivePowersSnapshot.ForCreature(receiver),
                    active_on_dealer   = ActivePowersSnapshot.ForCreature(dealer),
                    modifications      = ModifierLog.ToPayload(modDamage, modHpLost),
                    block_sources      = blockSources,
                });
            }
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterDamageGiven error: {ex.Message}"); }
    }

    /// <summary>
    /// Doom による撃破。DoomPower.DoomKill は CreatureCmd.Kill (= HP を 0 にするだけ) で、damage の Hook を通らない。
    /// Doom 実行中に敵の HP が減ったら、その量を Doom の付与者の与ダメとして送る。
    /// </summary>
    public static void AfterCurrentHpChangedPostfix(Creature? creature, decimal delta)
    {
        try
        {
            if (creature == null || delta >= 0m || !Lifecycle.InCombat) return;
            // Doom の処理中か (DoomPower のインスタンスか、static の DoomKill なら対象に付いた DoomPower)
            if (SourceContext.ResolveModel(creature) is not DoomPower) return;
            if (creature.Side != CombatSide.Enemy) return;
            var appliers = SourceContext.DebuffOrigins(creature);
            string? dealerId = appliers?.FirstOrDefault().PlayerId ?? SourceContext.CurrentActorPlayerId(creature);
            if (dealerId == null) return;
            int lost = (int)(-delta);
            EventBuffer.EmitTurnEvent("damage_dealt", dealerId, new
            {
                amount             = lost,
                total_damage       = lost,
                blocked_damage     = 0,
                overkill_damage    = 0,
                was_target_killed  = true,
                is_doom_kill       = true,
                source_appliers    = ApplierList(appliers),
                target_creature_id = CreatureId(creature),
                source_card_id     = "DOOM_POWER",
                source_card_name   = SourceContext.CurrentInfo(creature)?.CardName ?? "DOOM_POWER",
                source_card_type   = "Power",
                source_kind        = "power",
                active_on_target   = ActivePowersSnapshot.ForCreature(creature),
                active_on_dealer   = new List<object>(),
                modifications      = (object?)null,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterCurrentHpChanged (doom) error: {ex.Message}"); }
    }

    // === ブロック・カード・エナジー・パワー・ポーション =============================

    public static void AfterBlockGainedPostfix(ICombatState? combatState, Creature? creature, decimal amount, CardModel? cardSource)
    {
        try
        {
            if (creature == null || amount <= 0m || !Lifecycle.InCombat) return;
            if (creature.Player == null) return;    // プレイヤー本人のブロックのみ
            string receiverId = Identity.Of(creature.Player);
            CardInfo? source = cardSource != null ? CardInfoOf(cardSource) : SourceContext.CurrentInfo(creature);
            string sourceKind = cardSource != null ? "card" : SourceContext.CurrentKind();
            string? giverId = cardSource != null ? Identity.Of(cardSource.Owner) : SourceContext.CurrentActorPlayerId(creature);

            // パワー (プレート等) が出どころなら、そのパワーを付けた持ち物 (カード別の表用)
            var sourceOrigin = cardSource == null && SourceContext.ResolveModel(creature) is PowerModel ownPower
                ? SourceContext.OriginOfPower(ownPower) : null;

            BlockLedger.Add(creature, giverId ?? receiverId, amount);
            EventBuffer.EmitTurnEvent("block_gained", receiverId, new
            {
                amount           = (int)amount,
                source_card_id   = source?.CardId,
                source_card_name = source?.CardName,
                source_card_type = source?.CardType,
                source_kind      = sourceKind,
                source_origin    = OriginPayload(sourceOrigin),
                from_player      = giverId ?? receiverId,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterBlockGained error: {ex.Message}"); }
    }

    public static void AfterEnergySpentPostfix(ICombatState? combatState, CardModel? card, int amount)
    {
        try
        {
            if (card == null || amount <= 0 || !Lifecycle.InCombat) return;
            EventBuffer.EmitTurnEvent("energy_spent", Identity.Of(card.Owner), new
            {
                amount         = amount,
                source_card_id = card.Id.Entry,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterEnergySpent error: {ex.Message}"); }
    }

    public static void AfterCardPlayedPostfix(ICombatState? combatState, CardPlay? cardPlay)
    {
        try
        {
            if (cardPlay?.Card == null || !Lifecycle.InCombat) return;
            var info = CardInfoOf(cardPlay.Card);
            EventBuffer.EmitTurnEvent("card_played", Identity.Of(cardPlay.Card.Owner), new
            {
                card_id            = info.CardId,
                card_name          = info.CardName,
                card_type          = info.CardType,
                target_creature_id = cardPlay.Target != null ? CreatureId(cardPlay.Target) : null,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterCardPlayed error: {ex.Message}"); }
    }

    public static void AfterCardDrawnPostfix(ICombatState? combatState, CardModel? card, bool fromHandDraw)
    {
        try
        {
            if (card == null || !Lifecycle.InCombat) return;
            EventBuffer.EmitTurnEvent("card_drawn", Identity.Of(card.Owner), new
            {
                card_id        = card.Id.Entry,
                card_name      = ModelInfo.SafeTitle(card),
                from_hand_draw = fromHandDraw,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterCardDrawn error: {ex.Message}"); }
    }

    public static void AfterPowerAmountChangedPostfix(
        ICombatState? combatState,
        PowerModel?   power,
        decimal       amount,
        Creature?     applier,
        CardModel?    cardSource)
    {
        try
        {
            if (power == null || !Lifecycle.InCombat) return;
            int delta = (int)amount;
            if (delta == 0) return;
            string powerId = power.Id.Entry;
            string? applierId = Identity.OfCreature(applier, includePets: true);

            // 付与者ごとの stacks 内訳 (rDPS / rMit の按分用)
            if (power.Owner != null)
            {
                if (applierId != null)
                {
                    // 付けた持ち物: カードならそのカード、無ければ実行中のレリック・ポーション等 (パワーならそれを付けた持ち物)
                    Origin? origin = cardSource != null
                        ? new Origin(cardSource.Id.Entry, ModelInfo.SafeTitle(cardSource), cardSource.Type.ToString(), "card")
                        : SourceContext.CurrentOrigin(power.Owner);
                    PowerOriginRegistry.RecordApply(power.Owner, powerId, applierId, origin, delta);
                }
                else if (delta < 0)    PowerOriginRegistry.RecordDecay(power.Owner, powerId, delta);
            }
            if (applier == null) return;   // 付与者のいない増減 (自然減衰等) は送らない (v1 と同じ)

            EventBuffer.EmitTurnEvent("power_changed", applierId, new
            {
                power_id           = powerId,
                power_name         = PowerNameResolver.Resolve(power),
                delta              = delta,
                target_creature_id = power.Owner != null ? CreatureId(power.Owner) : null,
                target_player_id   = Identity.OfCreature(power.Owner),
                source_card_id     = cardSource?.Id.Entry,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterPowerAmountChanged error: {ex.Message}"); }
    }

    public static void AfterPotionUsedPostfix(ICombatState? combatState, PotionModel? potion, Creature? target)
    {
        try
        {
            if (potion == null || !Lifecycle.InCombat) return;
            EventBuffer.EmitTurnEvent("potion_used", Identity.Of(potion.Owner), new
            {
                potion_id          = potion.Id.Entry,
                target_creature_id = target != null ? CreatureId(target) : null,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterPotionUsed error: {ex.Message}"); }
    }

    // === ヘルパー ================================================================

    /// <summary>source_appliers の形 ([{ player_id, stacks, origin? }])。按分しないダメージは null (送らない)。</summary>
    private static List<object>? ApplierList(List<(string PlayerId, int Stacks, Origin? Origin)>? appliers) =>
        appliers == null || appliers.Count == 0 ? null
            : appliers.Select(a => (object)new { player_id = a.PlayerId, stacks = a.Stacks, origin = OriginPayload(a.Origin) }).ToList();

    private static object? OriginPayload(Origin? o) =>
        o == null ? null : new { id = o.Id, name = o.Name, type = o.Type, kind = o.Kind };

    public static CardInfo CardInfoOf(CardModel card) =>
        new(card.Id.Entry, ModelInfo.SafeTitle(card), card.Type.ToString());

    /// <summary>creature を一意に識別する文字列。同一ラン中はオブジェクトの同一性で安定。</summary>
    public static string CreatureId(Creature c) => "c:" + RuntimeHelpers.GetHashCode(c).ToString();
}
