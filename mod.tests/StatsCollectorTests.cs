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
        var s1 = StatsCollector.FinalizeCurrentTurn()!;

        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 20);
        var s2 = StatsCollector.FinalizeCurrentTurn()!;

        Assert.Equal(1, s1.CombatIndex);
        Assert.Equal(2, s2.CombatIndex);
    }

    [Fact]
    public void BeginCombat_ResetsTurnNumber()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        StatsCollector.FinalizeCurrentTurn();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        StatsCollector.FinalizeCurrentTurn(); // turn 2

        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        var snapshot = StatsCollector.FinalizeCurrentTurn()!;

        Assert.Equal(1, snapshot.TurnNumber);
    }

    // --- RecordDamageDealt / FinalizeCurrentTurn ---

    [Fact]
    public void RecordDamageDealt_AccumulatesWithinTurn()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 15);

        var snapshot = StatsCollector.FinalizeCurrentTurn()!;

        Assert.Equal(25, snapshot.StatsByPlayer["p1"].DamageDealt);
    }

    [Fact]
    public void RecordDamageDealt_TracksMultiplePlayers()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 20);
        StatsCollector.RecordDamageDealt("p2", "PlayerB", 30);

        var snapshot = StatsCollector.FinalizeCurrentTurn()!;

        Assert.Equal(20, snapshot.StatsByPlayer["p1"].DamageDealt);
        Assert.Equal(30, snapshot.StatsByPlayer["p2"].DamageDealt);
    }

    [Fact]
    public void RecordDamageDealt_IgnoresZeroOrNegative()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 0);
        StatsCollector.RecordDamageDealt("p1", "PlayerA", -5);

        var snapshot = StatsCollector.FinalizeCurrentTurn();

        Assert.Null(snapshot);
    }

    [Fact]
    public void FinalizeCurrentTurn_ReturnsNullWhenNoDataThisTurn()
    {
        StatsCollector.BeginCombat();

        var snapshot = StatsCollector.FinalizeCurrentTurn();

        Assert.Null(snapshot);
    }

    [Fact]
    public void FinalizeCurrentTurn_ClearsAfterSnapshot()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        StatsCollector.FinalizeCurrentTurn();

        var second = StatsCollector.FinalizeCurrentTurn();

        Assert.Null(second);
    }

    [Fact]
    public void FinalizeCurrentTurn_IncrementsTurnNumber()
    {
        StatsCollector.BeginCombat();

        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        var t1 = StatsCollector.FinalizeCurrentTurn()!;

        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        var t2 = StatsCollector.FinalizeCurrentTurn()!;

        Assert.Equal(1, t1.TurnNumber);
        Assert.Equal(2, t2.TurnNumber);
    }

    // --- New per-turn stats ---

    [Fact]
    public void RecordDamageReceived_AppearsInTurnSnapshot()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageReceived("p1", "PlayerA", 12);

        var snapshot = StatsCollector.FinalizeCurrentTurn()!;

        Assert.Equal(12, snapshot.StatsByPlayer["p1"].DamageReceived);
    }

    [Fact]
    public void RecordBlockGainedSelf_AppearsInTurnSnapshot()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordBlockGainedSelf("p1", "PlayerA", 8);

        var snapshot = StatsCollector.FinalizeCurrentTurn()!;

        Assert.Equal(8, snapshot.StatsByPlayer["p1"].BlockGainedSelf);
    }

    [Fact]
    public void RecordEnergyUsed_AccumulatesWithinTurn()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordEnergyUsed("p1", "PlayerA", 2);
        StatsCollector.RecordEnergyUsed("p1", "PlayerA", 1);

        var snapshot = StatsCollector.FinalizeCurrentTurn()!;

        Assert.Equal(3, snapshot.StatsByPlayer["p1"].EnergyUsed);
    }

    [Fact]
    public void RecordCardPlayed_AccumulatesCardsPlayed()
    {
        var card = new CardInfo("Strike_R", "Strike", "Attack");
        StatsCollector.BeginCombat();
        StatsCollector.RecordCardPlayed("p1", "PlayerA", card);
        StatsCollector.RecordCardPlayed("p1", "PlayerA", card);

        var snapshot = StatsCollector.FinalizeCurrentTurn()!;

        Assert.Equal(2, snapshot.StatsByPlayer["p1"].CardsPlayed);
    }

    [Fact]
    public void RecordCardDrawn_AccumulatesCardsDrawn()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordCardDrawn("p1", "PlayerA");
        StatsCollector.RecordCardDrawn("p1", "PlayerA");
        StatsCollector.RecordCardDrawn("p1", "PlayerA");

        var snapshot = StatsCollector.FinalizeCurrentTurn()!;

        Assert.Equal(3, snapshot.StatsByPlayer["p1"].CardsDrawn);
    }

    // --- FinalizeCurrentCombat ---

    [Fact]
    public void FinalizeCurrentCombat_AggregatesTotalAcrossTurns()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        StatsCollector.FinalizeCurrentTurn();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 20);
        StatsCollector.FinalizeCurrentTurn();

        var summary = StatsCollector.FinalizeCurrentCombat();

        Assert.Equal(30, summary.TotalsByPlayer["p1"].DamageDealt);
    }

    [Fact]
    public void FinalizeCurrentCombat_IncludesUnfinalizedCurrentTurn()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        StatsCollector.FinalizeCurrentTurn();
        // ターン確定せずに戦闘終了
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 5);

        var summary = StatsCollector.FinalizeCurrentCombat();

        Assert.Equal(15, summary.TotalsByPlayer["p1"].DamageDealt);
        Assert.Equal(2, summary.TotalTurns);
    }

    [Fact]
    public void FinalizeCurrentCombat_ReportsCorrectTotalTurns()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        StatsCollector.FinalizeCurrentTurn();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        StatsCollector.FinalizeCurrentTurn();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        StatsCollector.FinalizeCurrentTurn();

        var summary = StatsCollector.FinalizeCurrentCombat();

        Assert.Equal(3, summary.TotalTurns);
    }

    // --- Card stats ---

    [Fact]
    public void RecordDamageDealt_WithCard_TrackedInCardStats()
    {
        var card = new CardInfo("Strike_R", "Strike", "Attack");
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10, card);
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 15, card);

        var summary = StatsCollector.FinalizeCurrentCombat();
        var cs = summary.TotalsByPlayer["p1"].CardStats.First(c => c.CardId == "Strike_R");

        Assert.Equal(25, cs.DamageDealt);
    }

    [Fact]
    public void RecordDamageDealt_MaxSingleHit_TrackedPerPlayerAndCard()
    {
        var card = new CardInfo("Bash", "Bash", "Attack");
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 20, card);
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 8, card);

        var summary = StatsCollector.FinalizeCurrentCombat();
        var player  = summary.TotalsByPlayer["p1"];
        var cs      = player.CardStats.First(c => c.CardId == "Bash");

        Assert.Equal(20, player.MaxSingleHit);
        Assert.Equal(20, cs.MaxSingleHit);
    }

    [Fact]
    public void RecordDamageDealt_NullCard_TracksAsIndirect()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 6, null);

        var summary = StatsCollector.FinalizeCurrentCombat();
        var cs = summary.TotalsByPlayer["p1"].CardStats.First(c => c.CardId == "(indirect)");

        Assert.Equal(6, cs.DamageDealt);
    }

    [Fact]
    public void RecordCardPlayed_PlayCount_AppearsInCardStats()
    {
        var card = new CardInfo("Defend_R", "Defend", "Skill");
        StatsCollector.BeginCombat();
        StatsCollector.RecordCardPlayed("p1", "PlayerA", card);
        StatsCollector.RecordCardPlayed("p1", "PlayerA", card);
        StatsCollector.RecordCardPlayed("p1", "PlayerA", card);

        var summary = StatsCollector.FinalizeCurrentCombat();
        var cs = summary.TotalsByPlayer["p1"].CardStats.First(c => c.CardId == "Defend_R");

        Assert.Equal(3, cs.PlayCount);
    }

    [Fact]
    public void RecordDebuffApplied_AccumulatesInCombatAndCardStats()
    {
        var card = new CardInfo("Bash", "Bash", "Attack");
        StatsCollector.BeginCombat();
        StatsCollector.RecordDebuffApplied("p1", "PlayerA", "Vulnerable", 2, card);
        StatsCollector.RecordDebuffApplied("p1", "PlayerA", "Vulnerable", 1, card);

        var summary = StatsCollector.FinalizeCurrentCombat();
        var player  = summary.TotalsByPlayer["p1"];
        var cs      = player.CardStats.First(c => c.CardId == "Bash");

        Assert.Equal(3, player.DebuffsApplied["Vulnerable"]);
        Assert.Equal(3, cs.DebuffsApplied["Vulnerable"]);
    }

    [Fact]
    public void RecordBlockGivenToAlly_SeparateFromBlockGainedSelf()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordBlockGainedSelf("p1", "PlayerA", 10);
        StatsCollector.RecordBlockGivenToAlly("p1", "PlayerA", 5);

        var summary = StatsCollector.FinalizeCurrentCombat();
        var player  = summary.TotalsByPlayer["p1"];

        Assert.Equal(10, player.BlockGainedSelf);
        Assert.Equal(5,  player.BlockGivenToAllies);
    }
}
