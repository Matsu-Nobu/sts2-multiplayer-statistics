using System.Collections;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace StsStats;

internal static class HookPatches
{
    // Tracks the player whose turn is currently active (set in AfterPlayerTurnStart)
    private static (string id, string name)? _currentTurnPlayer = null;

    // 直近で finalize したラウンド番号。CombatState.RoundNumber が増えたときだけターン確定する。
    // マルチプレイで AfterPlayerTurnStart がプレイヤー数ぶん発火する問題への対策。
    private static int _lastFinalizedRound = 0;

    // BeforeCombatStart(IRunState runState, CombatState? combatState)
    public static void BeforeCombatStartPostfix(IRunState? runState, CombatState? combatState)
    {
        try
        {
            StatsCollector.BeginCombat();
            _currentTurnPlayer = null;
            _lastFinalizedRound = 0;
            Log.Info("[StsStats] Combat started");
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] BeforeCombatStart error: {ex.Message}");
        }
    }

    // AfterPlayerTurnStart(ICombatState combatState, PlayerChoiceContext choiceContext, Player player)
    // 「次のターン開始」＝「前ターン終了」として前ターンデータを確定する
    public static void AfterPlayerTurnStartPostfix(CombatState? combatState, object? player)
    {
        try
        {
            // ラウンド番号が変化したときだけ前ターンを確定する。
            // 同一ラウンド内で複数プレイヤーぶん発火しても2重 finalize しない。
            int round = combatState?.RoundNumber ?? 0;
            if (round > _lastFinalizedRound)
            {
                var snapshot = StatsCollector.FinalizeCurrentTurn();
                if (snapshot != null)
                    StatsLogger.LogTurnEnd(snapshot);
                _lastFinalizedRound = round;
            }

            _currentTurnPlayer = TryGetPlayerInfo(player);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterPlayerTurnStart error: {ex.Message}");
        }
    }

    // AfterCombatEnd(IRunState runState, CombatState? combatState, CombatRoom room)
    public static void AfterCombatEndPostfix(IRunState? runState, CombatState? combatState)
    {
        try
        {
            var summary = StatsCollector.FinalizeCurrentCombat();
            StatsLogger.LogCombatEnd(summary);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterCombatEnd error: {ex.Message}");
        }
    }

    // AfterDamageGiven(PlayerChoiceContext? choiceContext, CombatState? combatState,
    //                  Creature? dealer, DamageResult? results, ValueProp props,
    //                  Creature? target, CardModel? cardSource)
    public static void AfterDamageGivenPostfix(
        CombatState? combatState,
        Creature?    dealer,
        DamageResult? results,
        CardModel?   cardSource)
    {
        try
        {
            if (dealer == null || results == null) return;

            int amount = (int)results.UnblockedDamage;
            if (amount <= 0) return;

            var playerInfo = TryFindPlayerForCreature(combatState, dealer);
            if (playerInfo == null) return;

            var card = TryGetCardInfo(cardSource);
            StatsCollector.RecordDamageDealt(playerInfo.Value.id, playerInfo.Value.name, amount, card);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterDamageGiven error: {ex.Message}");
        }
    }

    // AfterDamageReceived(PlayerChoiceContext choiceContext, IRunState runState, ICombatState combatState,
    //                     Creature target, DamageResult result, ValueProp props, Creature dealer, CardModel cardSource)
    public static void AfterDamageReceivedPostfix(
        CombatState?  combatState,
        Creature?     target,
        DamageResult? result)
    {
        try
        {
            if (target == null || result == null) return;

            int amount = (int)result.UnblockedDamage;
            if (amount <= 0) return;

            var playerInfo = TryFindPlayerForCreature(combatState, target);
            if (playerInfo == null) return;

            StatsCollector.RecordDamageReceived(playerInfo.Value.id, playerInfo.Value.name, amount);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterDamageReceived error: {ex.Message}");
        }
    }

    // AfterBlockGained(ICombatState combatState, Creature creature, Decimal amount, ValueProp props, CardModel cardSource)
    public static void AfterBlockGainedPostfix(
        CombatState? combatState,
        Creature?    creature,
        decimal      amount,
        CardModel?   cardSource)
    {
        try
        {
            if (creature == null || amount <= 0) return;

            int blockAmt = (int)amount;

            // Who received the block?
            var receiver = TryFindPlayerForCreature(combatState, creature);
            if (receiver == null) return;

            // Who gave the block? (the player who played the card)
            var giver = TryFindPlayerForCardOwner(cardSource);

            if (giver != null && giver.Value.id != receiver.Value.id)
            {
                // A player gave block to a different player
                StatsCollector.RecordBlockGainedSelf(receiver.Value.id, receiver.Value.name, blockAmt,
                    TryGetCardInfo(cardSource));
                StatsCollector.RecordBlockGivenToAlly(giver.Value.id, giver.Value.name, blockAmt,
                    TryGetCardInfo(cardSource));
            }
            else
            {
                // Self-block (or giver unknown)
                StatsCollector.RecordBlockGainedSelf(receiver.Value.id, receiver.Value.name, blockAmt,
                    TryGetCardInfo(cardSource));
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterBlockGained error: {ex.Message}");
        }
    }

    // AfterEnergySpent(ICombatState combatState, CardModel card, Int32 amount)
    public static void AfterEnergySpentPostfix(CombatState? combatState, CardModel? card, int amount)
    {
        try
        {
            if (amount <= 0) return;

            var playerInfo = TryFindPlayerForCardOwner(card) ?? _currentTurnPlayer;
            if (playerInfo == null) return;

            StatsCollector.RecordEnergyUsed(playerInfo.Value.id, playerInfo.Value.name, amount);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterEnergySpent error: {ex.Message}");
        }
    }

    // AfterCardPlayed(ICombatState combatState, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    public static void AfterCardPlayedPostfix(CombatState? combatState, object? cardPlay)
    {
        try
        {
            if (cardPlay == null) return;

            var cardModel = cardPlay.GetType().GetProperty("Card")?.GetValue(cardPlay) as CardModel;
            if (cardModel == null) return;

            var playerInfo = TryFindPlayerForCardOwner(cardModel);
            if (playerInfo == null) return;

            var cardInfo = TryGetCardInfo(cardModel);
            if (cardInfo == null) return;

            StatsCollector.RecordCardPlayed(playerInfo.Value.id, playerInfo.Value.name, cardInfo);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterCardPlayed error: {ex.Message}");
        }
    }

    // AfterCardDrawn(ICombatState combatState, PlayerChoiceContext choiceContext, CardModel card, Boolean fromHandDraw)
    public static void AfterCardDrawnPostfix(CombatState? combatState, CardModel? card)
    {
        try
        {
            if (card == null) return;

            var playerInfo = TryFindPlayerForCardOwner(card) ?? _currentTurnPlayer;
            if (playerInfo == null) return;

            StatsCollector.RecordCardDrawn(playerInfo.Value.id, playerInfo.Value.name);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterCardDrawn error: {ex.Message}");
        }
    }

    // AfterPowerAmountChanged(ICombatState combatState, PlayerChoiceContext choiceContext,
    //                         PowerModel power, Decimal amount, Creature applier, CardModel cardSource)
    public static void AfterPowerAmountChangedPostfix(
        CombatState? combatState,
        PowerModel?  power,
        decimal      amount,
        Creature?    applier,
        CardModel?   cardSource)
    {
        try
        {
            if (power == null || applier == null || amount <= 0) return;

            // applier must be a player
            var playerInfo = TryFindPlayerForCreature(combatState, applier);
            if (playerInfo == null) return;

            // power.Owner must NOT be a player (i.e., it's an enemy getting debuffed)
            var ownerIsPlayer = TryFindPlayerForCreature(combatState, power.Owner);
            if (ownerIsPlayer != null) return;

            string powerId = power.Id.Entry;
            int stacks = (int)amount;

            StatsCollector.RecordDebuffApplied(playerInfo.Value.id, playerInfo.Value.name, powerId, stacks,
                TryGetCardInfo(cardSource));
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterPowerAmountChanged error: {ex.Message}");
        }
    }

    // AfterPotionUsed(IRunState runState, ICombatState combatState, PotionModel potion, Creature target)
    public static void AfterPotionUsedPostfix(CombatState? combatState, object? target)
    {
        try
        {
            // target is the Creature the potion was used on; find the acting player via current turn
            var playerInfo = _currentTurnPlayer;
            if (playerInfo == null) return;

            StatsCollector.RecordPotionUsed(playerInfo.Value.id, playerInfo.Value.name);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterPotionUsed error: {ex.Message}");
        }
    }

    // --- helpers ---

    private static (string id, string name)? TryFindPlayerForCreature(CombatState? combatState, Creature? dealer)
    {
        if (dealer == null) return null;
        try
        {
            var runState = combatState?.RunState;
            if (runState == null) return null;

            var players = runState.GetType().GetProperty("Players")?.GetValue(runState) as IEnumerable;
            if (players == null) return null;

            foreach (var player in players)
            {
                var creature = player.GetType().GetProperty("Creature")?.GetValue(player) as Creature;
                if (creature != dealer) continue;

                return BuildPlayerInfo(player);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] TryFindPlayerForCreature error: {ex.Message}");
        }
        return null;
    }

    // CardModel.Owner is of type Player — extract player info from it directly
    private static (string id, string name)? TryFindPlayerForCardOwner(CardModel? cardModel)
    {
        if (cardModel == null) return null;
        try
        {
            var owner = cardModel.GetType().GetProperty("Owner")?.GetValue(cardModel);
            return TryGetPlayerInfo(owner);
        }
        catch { return null; }
    }

    private static (string id, string name)? TryGetPlayerInfo(object? player)
    {
        if (player == null) return null;
        try { return BuildPlayerInfo(player); }
        catch { return null; }
    }

    private static (string id, string name) BuildPlayerInfo(object player)
    {
        ulong netId = (player.GetType().GetProperty("NetId")?.GetValue(player) as ulong?) ?? 0UL;

        // 1. NetId が Steam ID ならそれで名前解決（マルチプレイ）
        string? name = TryGetSteamName(netId);

        // 2. シングルプレイでは NetId が合成ID（1等）になる。ローカルSteam IDで再試行
        ulong resolvedId = netId;
        if (string.IsNullOrEmpty(name))
        {
            ulong localId = TryGetLocalPlayerId();
            if (localId != 0UL)
            {
                name = TryGetSteamName(localId);
                if (!string.IsNullOrEmpty(name)) resolvedId = localId;
            }
        }

        // 3. それでも取れなければキャラクターIDをフォールバック名に使う
        name ??= TryGetCharacterId(player);

        string id = resolvedId != 0UL ? resolvedId.ToString() : (name ?? "unknown");
        return (id, name ?? id);
    }

    private static string? TryGetCharacterId(object player)
    {
        try
        {
            var character = player.GetType().GetProperty("Character")?.GetValue(player);
            var modelId   = character?.GetType().GetProperty("Id")?.GetValue(character);
            return modelId?.GetType().GetProperty("Entry")?.GetValue(modelId)?.ToString();
        }
        catch { return null; }
    }

    private static ulong TryGetLocalPlayerId()
    {
        try { return PlatformUtil.GetLocalPlayerId(PlatformUtil.PrimaryPlatform); }
        catch { return 0UL; }
    }

    private static string? TryGetSteamName(ulong steamId)
    {
        if (steamId == 0UL) return null;
        try
        {
            string name = PlatformUtil.GetPlayerName(PlatformUtil.PrimaryPlatform, steamId);
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] TryGetSteamName error: {ex.Message}");
            return null;
        }
    }

    private static CardInfo? TryGetCardInfo(CardModel? cardModel)
    {
        if (cardModel == null) return null;
        try
        {
            var idObj  = cardModel.GetType().GetProperty("Id")?.GetValue(cardModel);
            string cardId = idObj?.GetType().GetProperty("Entry")?.GetValue(idObj)?.ToString() ?? "";
            if (string.IsNullOrEmpty(cardId)) return null;

            // CardModel.Title is String (not LocString)
            string cardName = cardModel.GetType().GetProperty("Title")?.GetValue(cardModel)?.ToString() ?? cardId;
            string cardType = cardModel.GetType().GetProperty("Type")?.GetValue(cardModel)?.ToString() ?? "";

            return new CardInfo(cardId, cardName, cardType);
        }
        catch { return null; }
    }
}
