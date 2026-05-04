using System.Collections;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace StsStats;

internal static class HookPatches
{
    // BeforeCombatStart(IRunState runState, CombatState? combatState)
    public static void BeforeCombatStartPostfix(IRunState? runState, CombatState? combatState)
    {
        try
        {
            StatsCollector.BeginCombat();
            Log.Info("[StsStats] Combat started");
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] BeforeCombatStart error: {ex.Message}");
        }
    }

    // AfterDamageGiven(PlayerChoiceContext? choiceContext, CombatState? combatState,
    //                  Creature? dealer, DamageResult? results, ValueProp props,
    //                  Creature? target, CardModel? cardSource)
    public static void AfterDamageGivenPostfix(
        CombatState? combatState,
        Creature? dealer,
        DamageResult? results,
        Creature? target,
        CardModel? cardSource)
    {
        try
        {
            if (dealer == null || results == null) return;

            int amount = (int)results.UnblockedDamage;

            // dealer がどのプレイヤーの creature か RunState から検索
            var playerInfo = TryFindPlayerForCreature(combatState, dealer);
            if (playerInfo == null)
            {
                Log.Info($"[StsStats] DamageGiven skipped (non-player) dealer={dealer.GetType().Name} amount={amount}");
                return;
            }

            Log.Info($"[StsStats] DamageGiven player={playerInfo.Value.name} amount={amount}");
            StatsCollector.RecordDamage(playerInfo.Value.id, playerInfo.Value.name, amount);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterDamageGiven error: {ex.Message}");
        }
    }

    // AfterPlayerTurnStart(CombatState combatState, PlayerChoiceContext? choiceContext, Player player)
    // 「次のターン開始」＝「前ターン終了」として前ターンデータを確定する
    public static void AfterPlayerTurnStartPostfix(CombatState? combatState)
    {
        try
        {
            var snapshot = StatsCollector.FinalizeCurrentTurn();
            if (snapshot != null)
                StatsLogger.LogTurnEnd(snapshot);
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

    // RunState.Players から dealer に対応するプレイヤーをリフレクションで検索
    private static (string id, string name)? TryFindPlayerForCreature(CombatState? combatState, Creature dealer)
    {
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

                string id = player.GetType().GetProperty("NetId")?.GetValue(player)?.ToString() ?? "unknown";
                string name = TryGetCharacterTitle(player) ?? id;
                return (id, name);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] TryFindPlayerForCreature error: {ex.Message}");
        }
        return null;
    }

    private static string? TryGetCharacterTitle(object player)
    {
        try
        {
            var character = player.GetType().GetProperty("Character")?.GetValue(player);
            if (character == null) return null;

            var title = character.GetType().GetProperty("Title")?.GetValue(character);
            if (title == null) return null;

            // LocString型: Valueプロパティかstring変換演算子で実テキストを取得
            var valueStr = title.GetType().GetProperty("Value")?.GetValue(title)?.ToString();
            if (!string.IsNullOrEmpty(valueStr)) return valueStr;

            // 暗黙のstring変換を試みる
            var op = title.GetType().GetMethod("op_Implicit", [title.GetType()]);
            if (op != null) return op.Invoke(null, [title])?.ToString();

            return null;
        }
        catch { return null; }
    }
}
