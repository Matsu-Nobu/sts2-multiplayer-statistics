using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;

namespace StsStats;

/// <summary>
/// card_reward_offered の payload (docs/spec/api.md「card_reward_offered」) を、ゲームの値から組み立てる。
/// mod のほかの部分に依存しないので、サーバー側の変換を確かめるヘッドレスエンジンにも同じファイルを組み込める。
/// </summary>
internal static class CardRewardPayload
{
    /// <summary>set はカード報酬を提示した報酬画面。分からなければ null。</summary>
    public static object Build(CardReward reward, IReadOnlyList<CardRewardAlternative> alternatives, RewardsSet? set)
    {
        var player = reward.Player;
        var run = player.RunState;
        var odds = run.Odds.UnknownMapPoint;
        int act = run.CurrentActIndex;
        return new
        {
            character_id = player.Character.Id.Entry,
            ascension = run.AscensionLevel,
            act_index = act,
            act_id = run.Act.Id.Entry,
            boss_ids = new[] { run.Act.BossEncounter?.Id.Entry, run.Act.SecondBossEncounter?.Id.Entry },
            room_type = run.CurrentRoom?.RoomType.ToString(),
            hp = player.Creature.CurrentHp,
            max_hp = player.Creature.MaxHp,
            gold = player.Gold,
            unknown_room_odds = new { monster = odds.MonsterOdds, treasure = odds.TreasureOdds, shop = odds.ShopOdds },
            potion_reward_odds = player.PlayerOdds.PotionReward.CurrentValue,
            potion_slots = player.PotionSlots.Count,
            potions = player.PotionSlots.Select((p, i) => (p, i)).Where(t => t.p != null)
                .Select(t => new { potion_id = t.p!.Id.Entry, slot = t.i }).ToList(),
            card_shop_removals_used = player.ExtraFields.CardShopRemovalsUsed,
            deck = player.Deck.Cards.Select(Card).ToList(),
            relics = player.Relics.Select(Relic).ToList(),
            rooms_this_act = act < run.MapPointHistory.Count
                ? run.MapPointHistory[act].SelectMany(e => e.Rooms)
                    .Select(r => new { room_type = r.RoomType.ToString(), model_id = r.ModelId?.Entry }).ToList()
                : null,
            offered = reward.Cards.Select(Card).ToList(),
            alternatives = alternatives.Select(a => a.OptionId).ToList(),
            rewards = Rewards(reward, set),
        };
    }

    /// <summary>同じ報酬画面の報酬すべて。カード報酬がその画面に無ければ (イベントが直接開いたとき)、そのカード報酬だけ。</summary>
    private static List<object> Rewards(CardReward reward, RewardsSet? set)
    {
        var list = set != null && set.Rewards.Contains(reward)
            ? set.Rewards.ToList()
            : new List<Reward> { reward };
        return list.Select(r => (object)(r switch
        {
            CardReward cr => new { type = "CardReward", taken = cr.SuccessfullySelected, linked = cr.ParentRewardSet != null, is_this = cr == reward, cards = cr.Cards.Select(Card).ToList() },
            GoldReward g => new { type = "GoldReward", taken = g.SuccessfullySelected, linked = g.ParentRewardSet != null, is_this = false, amount = g.Amount },
            PotionReward { Potion: { } p } pr => new { type = "PotionReward", taken = pr.SuccessfullySelected, linked = pr.ParentRewardSet != null, is_this = false, potion_id = p.Id.Entry },
            RelicReward { Relic: { } rel } rr => new { type = "RelicReward", taken = rr.SuccessfullySelected, linked = rr.ParentRewardSet != null, is_this = false, relic = Relic(rel) },
            _ => (object)new { type = r.GetType().Name, taken = r.SuccessfullySelected, linked = r.ParentRewardSet != null, is_this = false },
        })).ToList();
    }

    private static object Card(CardModel c) => new
    {
        card_id = c.Id.Entry,
        upgrade_level = c.CurrentUpgradeLevel,
        cost = c.EnergyCost.CostsX ? (int?)null : c.EnergyCost.GetWithModifiers(CostModifiers.Local),
        star_cost = c.CurrentStarCost,
        keywords = c.GetKeywordsWithSources(KeywordSources.Local).Select(k => k.ToString()).ToList(),
        vars = c.DynamicVars.ToDictionary(kv => kv.Key, kv => kv.Value.BaseValue),
        enchantment = c.Enchantment == null ? null : new { id = c.Enchantment.Id.Entry, amount = c.Enchantment.Amount, status = c.Enchantment.Status.ToString() },
        affliction = c.Affliction == null ? null : new { id = c.Affliction.Id.Entry, amount = c.Affliction.Amount },
        tinker = c is MegaCrit.Sts2.Core.Models.Cards.MadScience ms
            ? new { type = ms.TinkerTimeType.ToString(), rider = ms.TinkerTimeRider.ToString() }
            : null,
    };

    private static object Relic(RelicModel r) => new
    {
        relic_id = r.Id.Entry,
        counter = r.DisplayAmount,
        show_counter = r.ShowCounter,
        used_up = r.IsUsedUp,
        wax = r.IsWax,
        melted = r.IsMelted,
        status = r.Status.ToString(),
        max_hp = r is MegaCrit.Sts2.Core.Models.Relics.Wuppy w ? w.MaxHp : (int?)null,
    };
}
