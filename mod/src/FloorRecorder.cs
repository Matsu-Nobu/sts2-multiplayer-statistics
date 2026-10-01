using System;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;

namespace StsStats;

/// <summary>
/// ラン全体の画面のデータ (docs/spec/data-sources.md §1.1)。
///
/// ゲーム自身の階ごとの記録 (MapPointHistoryEntry) をそのまま写して floor_snapshot として送る。
/// 記録は全プレイヤー分あり、マルチプレイの相手の分もホストの手元で埋まる (ロックステップ + RewardSynchronizer)。
///
///   確定 … RunManager.UpdatePlayerStatsInMapPointHistory() の後 (次の階に入る直前 / ラン終了時)
///   途中 … ライブ表示用に、部屋に入ったとき・戦闘終了・ターン終了・報酬・休憩所で送る
/// 同じ階は何度送ってもよい (web は階ごとに最後の 1 件を使う)。
/// </summary>
internal static class FloorRecorder
{
    // === 確定・途中経過 ==========================================================

    /// <summary>RunManager.UpdatePlayerStatsInMapPointHistory (private, 引数なし) の Postfix。</summary>
    public static void UpdatePlayerStatsPostfix()
    {
        try { if (Lifecycle.Run != null) Emit(Lifecycle.Run, isFinal: true); }
        catch (Exception ex) { Log.Error($"[StsStats] UpdatePlayerStats postfix error: {ex.Message}"); }
    }

    public static void EmitLive(IRunState runState)
    {
        try { Emit(runState, isFinal: false); }
        catch (Exception ex) { Log.Error($"[StsStats] floor_snapshot (live) error: {ex.Message}"); }
    }

    /// <summary>Hook.AfterRewardTaken / AfterRestSiteHeal / AfterRestSiteSmith の Postfix (途中経過の送信)。</summary>
    public static void AfterRunActionPostfix(IRunState? runState)
    {
        if (runState != null) EmitLive(runState);
    }

    private static void Emit(IRunState runState, bool isFinal)
    {
        var history = runState.MapPointHistory;
        var entry = runState.CurrentMapPointHistoryEntry;
        if (entry == null || history.Count == 0) return;

        EventBuffer.EmitGlobalEvent("floor_snapshot", null, new
        {
            floor          = runState.TotalFloor,
            act_index      = history.Count - 1,
            is_final       = isFinal,
            map_point_type = entry.MapPointType.ToString(),
            rooms          = entry.Rooms.Select(r => new
            {
                room_type   = r.RoomType.ToString(),
                model_id    = r.ModelId?.Entry,
                model_name  = ModelInfo.RoomModelName(r.ModelId),
                monster_ids = r.MonsterIds.Select(m => m.Entry).ToList(),
                turns_taken = r.TurnsTaken,
            }).ToList(),
            players = entry.PlayerStats.Select(BuildPlayer).ToList(),
        });
    }

    private static object BuildPlayer(PlayerMapPointHistoryEntry e) => new
    {
        player_id = Identity.OfNetId(e.PlayerId),
        hp = new
        {
            current      = e.CurrentHp,
            max          = e.MaxHp,
            damage_taken = e.DamageTaken,
            healed       = e.HpHealed,
            max_gained   = e.MaxHpGained,
            max_lost     = e.MaxHpLost,
        },
        gold = new
        {
            current = e.CurrentGold,
            gained  = e.GoldGained,
            spent   = e.GoldSpent,
            lost    = e.GoldLost,
            stolen  = e.GoldStolen,
        },
        cards_gained      = e.CardsGained.Select(ModelInfo.Card).ToList(),
        cards_removed     = e.CardsRemoved.Select(ModelInfo.Card).ToList(),
        cards_transformed = e.CardsTransformed.Select(t => new { from = ModelInfo.Card(t.OriginalCard), to = ModelInfo.Card(t.FinalCard) }).ToList(),
        cards_upgraded    = e.UpgradedCards.Select(ModelInfo.CardRef).ToList(),
        cards_downgraded  = e.DowngradedCards.Select(ModelInfo.CardRef).ToList(),
        cards_enchanted   = e.CardsEnchanted.Select(c => new
        {
            card             = ModelInfo.Card(c.Card),
            enchantment_id   = c.Enchantment?.Entry,
            enchantment_name = ModelInfo.EnchantmentName(c.Enchantment),
        }).ToList(),
        card_choices      = e.CardChoices.Select(c => new { card = ModelInfo.Card(c.Card), was_picked = c.wasPicked }).ToList(),
        relic_choices     = e.RelicChoices.Select(c => ModelInfo.Relic(c.choice, c.wasPicked)).ToList(),
        potion_choices    = e.PotionChoices.Select(c => ModelInfo.Potion(c.choice, c.wasPicked)).ToList(),
        potions_used      = e.PotionUsed.Select(id => ModelInfo.Potion(id)).ToList(),
        potions_discarded = e.PotionDiscarded.Select(id => ModelInfo.Potion(id)).ToList(),
        relics_removed    = e.RelicsRemoved.Select(id => ModelInfo.Relic(id)).ToList(),
        event_choices     = e.EventChoices.Select(c => ModelInfo.Text(c.Title)).ToList(),
        ancient_choices   = e.AncientChoices.Select(c => new { title = ModelInfo.Text(c.Title), was_chosen = c.WasChosen }).ToList(),
        rest_site_choices = e.RestSiteChoices.ToList(),
        bought = new
        {
            relics    = e.BoughtRelics.Select(id => ModelInfo.Relic(id)).ToList(),
            potions   = e.BoughtPotions.Select(id => ModelInfo.Potion(id)).ToList(),
            colorless = e.BoughtColorless.Select(ModelInfo.CardRef).ToList(),
        },
        completed_quests = e.CompletedQuests.Select(ModelInfo.Generic).ToList(),
    };

    // === ショップの値段 ===========================================================
    // 購入処理 (OnTryPurchase) は買った人の手元でしか動かないので、取れるのはホスト自身の購入だけ。
    // 品物そのものは階の記録 (cards_gained 等) に全員分入る。

    /// <summary>MerchantEntry._player (protected readonly)。起動時に存在を確認する (ModEntry.RequiredMembers)。</summary>
    internal static readonly AccessTools.FieldRef<MerchantEntry, Player>? PlayerField =
        TryFieldRef();

    private static AccessTools.FieldRef<MerchantEntry, Player>? TryFieldRef()
    {
        try { return AccessTools.FieldRefAccess<MerchantEntry, Player>("_player"); }
        catch (Exception ex) { Log.Error($"[StsStats] MerchantEntry._player not found: {ex.Message}"); return null; }
    }

    public static void MerchantCardPurchasePostfix(MerchantCardEntry __instance)
    {
        var card = __instance.CreationResult?.Card;
        var item = new System.Collections.Generic.Dictionary<string, object?>();
        if (card != null)
        {
            item["card_id"]     = card.Id.Entry;
            item["card_name"]   = ModelInfo.SafeTitle(card);
            item["card_rarity"] = card.Rarity.ToString();
            item["is_upgraded"] = card.IsUpgraded;
        }
        EmitPurchase(__instance, "MerchantCardEntry", item);
    }

    public static void MerchantPotionPurchasePostfix(MerchantPotionEntry __instance)
    {
        var m = __instance.Model;
        var item = new System.Collections.Generic.Dictionary<string, object?>();
        if (m != null) { item["potion_id"] = m.Id.Entry; item["potion_name"] = ModelInfo.Text(m.Title); }
        EmitPurchase(__instance, "MerchantPotionEntry", item);
    }

    public static void MerchantRelicPurchasePostfix(MerchantRelicEntry __instance)
    {
        var m = __instance.Model;
        var item = new System.Collections.Generic.Dictionary<string, object?>();
        if (m != null) { item["relic_id"] = m.Id.Entry; item["relic_name"] = ModelInfo.Text(m.Title); }
        EmitPurchase(__instance, "MerchantRelicEntry", item);
    }

    public static void MerchantCardRemovalPurchasePostfix(MerchantCardRemovalEntry __instance)
    {
        EmitPurchase(__instance, "MerchantCardRemovalEntry", new System.Collections.Generic.Dictionary<string, object?>());
    }

    /// <summary>payload は api.md どおり平らな形: item_kind, (card_* | potion_* | relic_*), gold_spent。</summary>
    private static void EmitPurchase(MerchantEntry entry, string kind, System.Collections.Generic.Dictionary<string, object?> payload)
    {
        try
        {
            Player? player = PlayerField != null ? PlayerField(entry) : null;
            payload["item_kind"]  = kind;
            payload["gold_spent"] = entry.Cost;
            EventBuffer.EmitGlobalEvent("item_purchased", player != null ? Identity.Of(player) : null, payload);
            if (Lifecycle.Run != null) EmitLive(Lifecycle.Run);
        }
        catch (Exception ex) { Log.Error($"[StsStats] {kind}.OnTryPurchase postfix error: {ex.Message}"); }
    }
}
