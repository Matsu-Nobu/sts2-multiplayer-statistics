using Xunit;

namespace StsStats.Tests;

public class StatsCollectorTests : IDisposable
{
    public StatsCollectorTests() => StatsCollector.Reset();
    public void Dispose()        => StatsCollector.Reset();

    // --- BeginCombat ---

    [Fact]
    public void BeginCombat_IncrementsCombatIndex()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        var p1 = StatsCollector.FinalizeTurn()!;

        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 20);
        var p2 = StatsCollector.FinalizeTurn()!;

        Assert.Equal(1, p1.CombatIndex);
        Assert.Equal(2, p2.CombatIndex);
    }

    [Fact]
    public void BeginCombat_ResetsTurnNumber()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        StatsCollector.FinalizeTurn();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        StatsCollector.FinalizeTurn();

        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        var p = StatsCollector.FinalizeTurn()!;

        Assert.Equal(1, p.TurnNumber);
    }

    // --- per-turn delta ---

    [Fact]
    public void RecordDamageDealt_AccumulatesWithinTurn()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 15);
        var p = StatsCollector.FinalizeTurn()!;
        Assert.Equal(25, p.Players["p1"].Turn.DamageDealt);
    }

    [Fact]
    public void RecordDamageDealt_TracksMultiplePlayers()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 20);
        StatsCollector.RecordDamageDealt("p2", "PlayerB", 30);
        var p = StatsCollector.FinalizeTurn()!;
        Assert.Equal(20, p.Players["p1"].Turn.DamageDealt);
        Assert.Equal(30, p.Players["p2"].Turn.DamageDealt);
    }

    [Fact]
    public void RecordDamageDealt_IgnoresZeroOrNegative()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 0);
        StatsCollector.RecordDamageDealt("p1", "PlayerA", -5);
        Assert.Null(StatsCollector.FinalizeTurn());
    }

    [Fact]
    public void FinalizeTurn_ReturnsNullWhenNoData()
    {
        StatsCollector.BeginCombat();
        Assert.Null(StatsCollector.FinalizeTurn());
    }

    [Fact]
    public void FinalizeTurn_IncrementsTurnNumber()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        var t1 = StatsCollector.FinalizeTurn()!;
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        var t2 = StatsCollector.FinalizeTurn()!;
        Assert.Equal(1, t1.TurnNumber);
        Assert.Equal(2, t2.TurnNumber);
    }

    [Fact]
    public void FinalizeTurn_IsFinal_True_WhenIsFinalRequested()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        var p = StatsCollector.FinalizeTurn(isFinal: true)!;
        Assert.True(p.IsFinal);
    }

    [Fact]
    public void RecordDamageReceived_AppearsInTurnDelta()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageReceived("p1", "PlayerA", 12);
        var p = StatsCollector.FinalizeTurn()!;
        Assert.Equal(12, p.Players["p1"].Turn.DamageReceived);
    }

    [Fact]
    public void RecordBlockGainedSelf_AppearsInTurnDelta()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordBlockGainedSelf("p1", "PlayerA", 8);
        var p = StatsCollector.FinalizeTurn()!;
        Assert.Equal(8, p.Players["p1"].Turn.BlockGainedSelf);
    }

    [Fact]
    public void RecordEnergyUsed_AccumulatesWithinTurn()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordEnergyUsed("p1", "PlayerA", 2);
        StatsCollector.RecordEnergyUsed("p1", "PlayerA", 1);
        var p = StatsCollector.FinalizeTurn()!;
        Assert.Equal(3, p.Players["p1"].Turn.EnergyUsed);
    }

    [Fact]
    public void RecordCardPlayed_AccumulatesCardsPlayed()
    {
        var card = new CardInfo("Strike_R", "Strike", "Attack");
        StatsCollector.BeginCombat();
        StatsCollector.RecordCardPlayed("p1", "PlayerA", card);
        StatsCollector.RecordCardPlayed("p1", "PlayerA", card);
        var p = StatsCollector.FinalizeTurn()!;
        Assert.Equal(2, p.Players["p1"].Turn.CardsPlayed);
    }

    [Fact]
    public void RecordCardDrawn_AccumulatesCardsDrawn()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordCardDrawn("p1", "PlayerA");
        StatsCollector.RecordCardDrawn("p1", "PlayerA");
        StatsCollector.RecordCardDrawn("p1", "PlayerA");
        var p = StatsCollector.FinalizeTurn()!;
        Assert.Equal(3, p.Players["p1"].Turn.CardsDrawn);
    }

    // --- per-turn cards (★新規) ---

    [Fact]
    public void TurnCards_BuiltFromCardPlayedAndDamageDealt()
    {
        var bash = new CardInfo("BASH", "バッシュ", "Attack");
        StatsCollector.BeginCombat();
        StatsCollector.RecordCardPlayed("p1", "PlayerA", bash);
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 8, bash);
        StatsCollector.RecordDebuffApplied("p1", "PlayerA", "Vulnerable", 2, bash);

        var p = StatsCollector.FinalizeTurn()!;
        var card = p.Players["p1"].Turn.Cards.First(c => c.CardId == "BASH");

        Assert.Equal(1, card.PlayCount);
        Assert.Equal(8, card.DamageDealt);
        Assert.Equal(8, card.MaxSingleHit);
        Assert.Equal(2, card.DebuffsApplied["Vulnerable"]);
    }

    [Fact]
    public void TurnCards_AreClearedBetweenTurns()
    {
        var card = new CardInfo("STRIKE", "ストライク", "Attack");
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 6, card);
        StatsCollector.FinalizeTurn();   // turn 1 確定

        StatsCollector.RecordDamageDealt("p1", "PlayerA", 9, card);
        var t2 = StatsCollector.FinalizeTurn()!;

        var c = t2.Players["p1"].Turn.Cards.First(x => x.CardId == "STRIKE");
        Assert.Equal(9, c.DamageDealt);          // turn1 の 6 が混じっていないこと
    }

    // --- combat 累計 ---

    [Fact]
    public void CombatTotals_AccumulateAcrossTurns()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        StatsCollector.FinalizeTurn();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 20);
        var p = StatsCollector.FinalizeTurn()!;
        Assert.Equal(30, p.Players["p1"].Combat.DamageDealt);
    }

    [Fact]
    public void CombatTotals_PersistAfterFinalizeTurn()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        var p = StatsCollector.FinalizeTurn()!;

        Assert.Equal(10, p.Players["p1"].Combat.DamageDealt);
        Assert.Equal(10, p.Players["p1"].Turn.DamageDealt);
    }

    [Fact]
    public void IsFinalFlush_EmitsCombatTotalsEvenWithoutTurnDelta()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        StatsCollector.FinalizeTurn();   // turn 1: combat=10

        // 何も Record しない状態で is_final 送信
        var p = StatsCollector.FinalizeTurn(isFinal: true)!;

        Assert.True(p.IsFinal);
        Assert.Equal(10, p.Players["p1"].Combat.DamageDealt);
        Assert.Equal(0,  p.Players["p1"].Turn.DamageDealt);
    }

    // --- card_stats (combat 累計のカード別) ---

    [Fact]
    public void CardStats_TrackedInCombatTotals()
    {
        var card = new CardInfo("Strike_R", "Strike", "Attack");
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10, card);
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 15, card);
        var p = StatsCollector.FinalizeTurn()!;
        var cs = p.Players["p1"].Combat.CardStats.First(c => c.CardId == "Strike_R");
        Assert.Equal(25, cs.DamageDealt);
    }

    [Fact]
    public void MaxSingleHit_TrackedPerPlayerAndCard()
    {
        var card = new CardInfo("Bash", "Bash", "Attack");
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 20, card);
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 8, card);
        var p = StatsCollector.FinalizeTurn()!;
        var combat = p.Players["p1"].Combat;
        Assert.Equal(20, combat.MaxSingleHit);
        Assert.Equal(20, combat.CardStats.First(c => c.CardId == "Bash").MaxSingleHit);
    }

    [Fact]
    public void NullCard_TracksAsIndirect()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 6, null);
        var p = StatsCollector.FinalizeTurn()!;
        var cs = p.Players["p1"].Combat.CardStats.First(c => c.CardId == "(indirect)");
        Assert.Equal(6, cs.DamageDealt);
    }

    [Fact]
    public void DebuffApplied_AccumulatesInCombatAndCardStats()
    {
        var card = new CardInfo("Bash", "Bash", "Attack");
        StatsCollector.BeginCombat();
        StatsCollector.RecordDebuffApplied("p1", "PlayerA", "Vulnerable", 2, card);
        StatsCollector.RecordDebuffApplied("p1", "PlayerA", "Vulnerable", 1, card);
        var p = StatsCollector.FinalizeTurn()!;
        var combat = p.Players["p1"].Combat;
        Assert.Equal(3, combat.DebuffsApplied["Vulnerable"]);
        Assert.Equal(3, combat.CardStats.First(c => c.CardId == "Bash").DebuffsApplied["Vulnerable"]);
    }

    [Fact]
    public void BlockGivenToAlly_SeparateFromBlockGainedSelf()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordBlockGainedSelf("p1", "PlayerA", 10);
        StatsCollector.RecordBlockGivenToAlly("p1", "PlayerA", 5);
        var p = StatsCollector.FinalizeTurn()!;
        Assert.Equal(10, p.Players["p1"].Combat.BlockGainedSelf);
        Assert.Equal(5,  p.Players["p1"].Combat.BlockGivenToAllies);
    }
}
