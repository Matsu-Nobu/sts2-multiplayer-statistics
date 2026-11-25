using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;

namespace StsStats;

/// <summary>
/// カード報酬の画面を開いた時点の状態 (docs/spec/api.md「card_reward_offered」)。
/// サーバーが AI の評価の入力を作るための値を、ゲームが持っている値のまま送る。
/// </summary>
internal static class CardRewardRecorder
{
    // プレイヤーごとに、最後に提示した報酬画面。Player が捨てられたら一緒に消える
    private static readonly ConditionalWeakTable<Player, RewardsSet> LastOffered = new();

    /// <summary>RewardsSet.Offer() の Prefix。</summary>
    public static void RewardsSetOfferPrefix(RewardsSet __instance)
    {
        try { LastOffered.AddOrUpdate(__instance.Player, __instance); }
        catch (Exception ex) { Log.Error($"[StsStats] RewardsSet.Offer prefix error: {ex.Message}"); }
    }

    /// <summary>CardRewardAlternative.Generate(CardReward) の Postfix。</summary>
    public static void GeneratePostfix(CardReward cardReward, IReadOnlyList<CardRewardAlternative> __result)
    {
        try
        {
            var player = cardReward.Player;
            Lifecycle.Track(player.RunState);
            var set = LastOffered.TryGetValue(player, out var s) ? s : null;
            EventBuffer.EmitGlobalEvent("card_reward_offered", Identity.Of(player), CardRewardPayload.Build(cardReward, __result, set));
        }
        catch (Exception ex) { Log.Error($"[StsStats] CardRewardAlternative.Generate postfix error: {ex.Message}"); }
    }
}
