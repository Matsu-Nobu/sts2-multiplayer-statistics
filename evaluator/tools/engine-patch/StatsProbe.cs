using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2Headless;

// STS2 Stats の試作だけで使う（配布しない）。サーバー側の変換を作るための表と、カード報酬の局面の事実を書き出す。
public sealed partial class Observation
{
    static readonly int[] StatsFormats = { 19, 20, 23, 27, 28 };
    // 何回でも強化できるカード（MaxUpgradeLevel が非常に大きい）があるので、表に書くのは 20 回までにする
    const int StatsMaxUpgrades = 20;

    static void SetFormat(int fmt)
    {
        V20 = fmt >= 20; V21 = fmt >= 21; V22 = fmt is 22 or 28 or 29; V23 = fmt is 23 or 24 or 25 or 26 or 27 or 28 or 29; V24 = fmt is 24 or 25 or 26 or 27 or 28 or 29; V25 = fmt is 25 or 26 or 27 or 28 or 29; V26 = fmt is 26 or 27 or 28 or 29; V27 = fmt is 27 or 28 or 29;
        V29 = fmt >= 29;
        F113 = fmt is 25 or 26 or 27 or 28 or 29 || (fmt != 24 && !NoFix113);
    }

    static void ClearFormat() { V20 = V21 = V22 = V23 = V24 = V25 = V26 = V27 = V29 = F113 = false; }

    /// <summary>戦闘の外の牌組にあるカード 1 枚の記録（附帯の記録を含む）。各記録は [整数 5 個, 特徴 n 個]。</summary>
    static List<object> CardRecords(CardModel c, int fmt)
    {
        var o = new Observation();
        SetFormat(fmt);
        try
        {
            MemoBegin();
            try { o.AddCard(c, Zone.Deck); }
            finally { MemoEnd(); }
        }
        finally { ClearFormat(); }
        int nf = o.TF.Count / o.NumTokens;
        var rows = new List<object>();
        for (int i = 0; i < o.NumTokens; i++)
            rows.Add(new object[] { o.TI.GetRange(i * TokInts, TokInts), o.TF.GetRange(i * nf, nf) });
        return rows;
    }

    public static void DumpStatsTables(string path)
    {
        var s = RunSession.State;
        var me = s.Players[0];
        var cards = new Dictionary<string, object>();
        var varCols = new Dictionary<string, object>();
        foreach (var canon in ModelDb.AllCards)
        {
            var byFmt = new Dictionary<string, object>();
            foreach (int fmt in StatsFormats)
            {
                var ups = new List<object>();
                var card = s.CreateCard(canon, me);
                for (int up = 0; ; up++)
                {
                    ups.Add(CardRecords(card, fmt));
                    if (up >= Math.Min(card.MaxUpgradeLevel, StatsMaxUpgrades)) break;
                    card.UpgradeInternal(); card.FinalizeUpgradeInternal();
                }
                byFmt[fmt.ToString()] = ups;
            }
            cards[Vocab.Model(canon)] = byFmt;
            varCols[Vocab.Model(canon)] = StatsFormats.ToDictionary(f => f.ToString(), f => (object)VarColumns(s.CreateCard(canon, me), f));
        }
        // マッドサイエンスは、イベントで選んだ型と追加効果をカードごとに持つので、組み合わせごとに記録を書く（キーは "型.追加効果"）
        var madScience = new Dictionary<string, object>();
        foreach (int fmt in StatsFormats)
        {
            var combos = new Dictionary<string, object>();
            foreach (var type in new[] { MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack, MegaCrit.Sts2.Core.Entities.Cards.CardType.Skill, MegaCrit.Sts2.Core.Entities.Cards.CardType.Power })
                foreach (var rider in Enum.GetValues<MegaCrit.Sts2.Core.Models.Events.TinkerTime.RiderEffect>())
                {
                    var ups = new List<object>();
                    var canonMs = ModelDb.AllCards.First(c => c is MegaCrit.Sts2.Core.Models.Cards.MadScience);
                    for (int up = 0; up <= Math.Min(canonMs.MaxUpgradeLevel, 1); up++)
                    {
                        var ms = (MegaCrit.Sts2.Core.Models.Cards.MadScience)s.CreateCard(canonMs, me);
                        ms.TinkerTimeType = type; ms.TinkerTimeRider = rider;
                        for (int i = 0; i < up; i++) { ms.UpgradeInternal(); ms.FinalizeUpgradeInternal(); }
                        ups.Add(CardRecords(ms, fmt));
                    }
                    combos[$"{(int)type}.{(int)rider}"] = ups;
                }
            madScience[fmt.ToString()] = combos;
        }
        // エンチャントを付けたカードの記録：量 1、2、3 と、量 1 で無効になった状態（強化は 0 回と 1 回、形式は 19 と 28）
        var enchanted = new Dictionary<string, object>();
        var enchantments = ModelDb.All.OfType<EnchantmentModel>().Where(e => !e.GetType().Name.StartsWith("Mock") && !e.GetType().Name.StartsWith("Deprecated")).ToList();
        foreach (var ench in enchantments)
        {
            var byCard = new Dictionary<string, object>();
            foreach (var canon in ModelDb.AllCards)
            {
                var byFmt = new Dictionary<string, object>();
                foreach (int fmt in new[] { 19, 28 })
                {
                    var ups = new List<object>();
                    for (int up = 0; up <= Math.Min(canon.MaxUpgradeLevel, 1); up++)
                    {
                        var variants = new List<object>();
                        foreach (var (amount, disabled) in new[] { (1, false), (2, false), (3, false), (1, true) })
                        {
                            var card = s.CreateCard(canon, me);
                            for (int i = 0; i < up; i++) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
                            var e = ench.ToMutable();
                            if (!e.CanEnchant(card)) goto next;
                            card.EnchantInternal(e, amount);
                            e.ModifyCard();
                            card.FinalizeUpgradeInternal();
                            if (disabled) e.Status = MegaCrit.Sts2.Core.Entities.Enchantments.EnchantmentStatus.Disabled;
                            variants.Add(CardRecords(card, fmt));
                        }
                        ups.Add(variants);
                    }
                    byFmt[fmt.ToString()] = ups;
                }
                byCard[Vocab.Model(canon)] = byFmt;
            next:;
            }
            enchanted[Vocab.Model(ench)] = byCard;
        }
        File.WriteAllText(Path.ChangeExtension(path, ".enchanted.json"), JsonSerializer.Serialize(enchanted));
        var potions = ModelDb.AllPotions.ToDictionary(p => Vocab.Model(p), p => p.Usage == MegaCrit.Sts2.Core.Entities.Potions.PotionUsage.AnyTime);
        var acts = new Dictionary<string, object>();
        foreach (var a in ModelDb.Acts)
            acts[Vocab.Model(a)] = new
            {
                weak = a.AllWeakEncounters.Select(Vocab.Model).ToList(),
                regular = a.AllRegularEncounters.Select(Vocab.Model).ToList(),
                elite = a.AllEliteEncounters.Select(Vocab.Model).ToList(),
                n_weak = WeakCount?.GetValue(a) is int w ? w : 0,
            };
        var tags = ModelDb.AllEncounters.ToDictionary(e => Vocab.Model(e), e => e.Tags.Select(t => t.ToString()).ToList());
        File.WriteAllText(path, JsonSerializer.Serialize(new { cards, potions, acts, encounter_tags = tags, mad_science = madScience, var_columns = varCols }));
    }

    /// <summary>今のカード報酬の局面で、mod から送ることになる事実（と、確認用にエンジンが計算した値）を 1 行の JSON で追記する。</summary>
    /// <summary>同じ報酬画面でまだ受け取っていない他の報酬（RunSession.CardRewardDecision が d.Shown に入れる順）。カード報酬はそのカードを含める。</summary>
    static List<object> OtherRewards(Decision d)
    {
        var list = new List<object>();
        Dictionary<string, object>? last = null;
        foreach (var (obj, zone, _) in d.Shown)
        {
            switch (obj)
            {
                case MegaCrit.Sts2.Core.Rewards.CardReward:
                    last = new Dictionary<string, object> { ["type"] = "CardReward", ["group"] = 0, ["cards"] = new List<object>() };
                    list.Add(last); continue;
                case CardModel c when last != null:
                    // 何番目のカード報酬かは、カードの区域（RewardGroup1–3）に入っている
                    last["group"] = (int)zone - (int)Zone.RewardGroup1;
                    ((List<object>)last["cards"]).Add(CardFact(c)); continue;
                case MegaCrit.Sts2.Core.Rewards.GoldReward g:
                    list.Add(new Dictionary<string, object> { ["type"] = "GoldReward", ["amount"] = g.Amount }); break;
                case MegaCrit.Sts2.Core.Rewards.PotionReward { Potion: { } pot }:
                    list.Add(new Dictionary<string, object> { ["type"] = "PotionReward", ["potion"] = Vocab.Model(pot) }); break;
                case MegaCrit.Sts2.Core.Rewards.RelicReward { Relic: { } rel }:
                    list.Add(new Dictionary<string, object> { ["type"] = "RelicReward", ["relic"] = RelicFact(rel) }); break;
                default:
                    list.Add(new Dictionary<string, object> { ["type"] = obj.GetType().Name }); break;
            }
            last = null;
        }
        return list;
    }

    static object RelicFact(RelicModel r) => new
    {
        id = Vocab.Model(r), amount = r.DisplayAmount, show_counter = r.ShowCounter, used_up = r.IsUsedUp, wax = r.IsWax, melted = r.IsMelted, status = (int)r.Status,
        wuppy_max_hp = r is MegaCrit.Sts2.Core.Models.Relics.Wuppy w ? w.MaxHp : (int?)null,
    };

    static object CardFact(CardModel c) => new
    {
        id = Vocab.Model(c), upgrade = c.CurrentUpgradeLevel,
        enchantment = c.Enchantment == null ? null : new { id = Vocab.Model(c.Enchantment), amount = c.Enchantment.Amount, status = (int)c.Enchantment.Status },
        affliction = c.Affliction == null ? null : new { id = Vocab.Model(c.Affliction), amount = c.Affliction.Amount },
        tinker = c is MegaCrit.Sts2.Core.Models.Cards.MadScience ms ? new { type = (int)ms.TinkerTimeType, rider = (int)ms.TinkerTimeRider } : null,
        vars = c.DynamicVars.ToDictionary(kv => kv.Key, kv => (float)kv.Value.BaseValue),
        keywords = c.GetKeywordsWithSources(MegaCrit.Sts2.Core.Entities.Cards.KeywordSources.Local).Select(k => k.ToString()).ToList(),
        cost = c.EnergyCost.CostsX ? (int?)null : c.EnergyCost.GetWithModifiers(MegaCrit.Sts2.Core.Entities.Cards.CostModifiers.Local),
        star_cost = c.CurrentStarCost,
    };

    /// <summary>カードの数値の名前が、カードの記録の何列目に入るか（Cards.cs の AddCard の 5–8 列と同じ割り当て）。</summary>
    static List<object[]> VarColumns(CardModel c, int fmt)
    {
        SetFormat(fmt);
        try
        {
            var cols = new List<object[]>();
            int other = 0;
            foreach (var pass in new[] { true, false })
                foreach (var (name, _) in c.DynamicVars)
                {
                    if (name is "Damage" or "CalculatedDamage" || (V22 && name == "OstyDamage")) { if (pass) cols.Add(new object[] { name, 5 }); }
                    else if (name is "Block" or "CalculatedBlock") { if (pass) cols.Add(new object[] { name, 6 }); }
                    else if (name is "CalculationBase" or "CalculationExtra") { }
                    else if (name.StartsWith("Calculated") == pass && other < 2) cols.Add(new object[] { name, 7 + other++ });
                }
            return cols;
        }
        finally { ClearFormat(); }
    }

    public static void AppendStatsFacts(string path, Decision d)
    {
        var s = RunSession.State;
        var me = s.Players[0];
        string? Id(AbstractModel? m) => m == null ? null : Vocab.Model(m);
        object? Rooms(int act) => act < s.MapPointHistory.Count
            ? s.MapPointHistory[act].SelectMany(e => e.Rooms).Select(r => new { room_type = (int)r.RoomType, model_id = r.ModelId?.ToString() }).ToList()
            : null;
        object? visited = null;
        if (RoomsField.GetValue(s.Act) is MegaCrit.Sts2.Core.Rooms.RoomSet rs)
            visited = new
            {
                normal = rs.normalEncounters.Take(Math.Min(rs.normalEncountersVisited, rs.normalEncounters.Count)).Select(Id).ToList(),
                elite = rs.eliteEncounters.Take(Math.Min(rs.eliteEncountersVisited, rs.eliteEncounters.Count)).Select(Id).ToList(),
            };
        var uo = s.Odds.UnknownMapPoint;
        var facts = new
        {
            character = Id(me.Character),
            ascension = s.AscensionLevel,
            act_index = s.CurrentActIndex,
            act = Id(s.Act),
            floor = s.TotalFloor,
            gold = me.Gold,
            hp = me.Creature.CurrentHp,
            max_hp = me.Creature.MaxHp,
            room_type = (int)(s.CurrentRoom?.RoomType ?? 0),
            bosses = new[] { Id(s.Act.BossEncounter), Id(s.Act.SecondBossEncounter) },
            unknown_odds = new[] { uo.MonsterOdds, uo.TreasureOdds, uo.ShopOdds },
            potion_reward_odds = me.PlayerOdds.PotionReward.CurrentValue,
            potion_slots = me.PotionSlots.Count,
            potions = me.PotionSlots.Select((p, i) => (p, i)).Where(t => t.p != null).Select(t => new { id = Id(t.p), slot = t.i }).ToList(),
            deck = me.Deck.Cards.Select(CardFact).ToList(),
            relics = me.Relics.Select(RelicFact).ToList(),
            card_removals_used = me.ExtraFields.CardShopRemovalsUsed,
            rooms_this_act = Rooms(s.CurrentActIndex),
            offered = d.Choices.Where(c => c.Kind == ActionKind.PickCardReward).Select(c => CardFact((CardModel)c.A!)).ToList(),
            alternatives = d.Choices.Where(c => c.Kind == ActionKind.CardRewardAlt).Select(c => c.Key).ToList(),
            other_rewards = OtherRewards(d),
            // mod が送る payload（mod と同じコード）。報酬画面から開いたカード報酬のときだけ作れる
            // mod の PayloadJson と同じく、値の無い項目を省いた JSON にする
            mod = RewardsFlow.ActiveChoice is MegaCrit.Sts2.Core.Rewards.CardReward cr && FakeScreens.CardReward is { } pcr
                ? JsonDocument.Parse(JsonSerializer.Serialize(StsStats.CardRewardPayload.Build(cr, pcr.Alternatives, RewardsFlow.ActiveSet),
                    new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull })).RootElement
                : (JsonElement?)null,
            // ここから下は mod が送らない。サーバー側の変換の結果と比べるために、エンジンが計算した値を書く。
            engine = new { visited, removal_price = RemovalPrice(me), selected = d.Selected, shown = d.Shown.Count, locked = d.Locked.Count },
        };
        File.AppendAllText(path, JsonSerializer.Serialize(facts) + "\n");
    }
}
