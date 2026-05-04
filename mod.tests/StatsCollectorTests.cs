using Xunit;

namespace StsStats.Tests;

public class StatsCollectorTests : IDisposable
{
    public StatsCollectorTests() => StatsCollector.Reset();
    public void Dispose()    => StatsCollector.Reset();

    // --- BeginCombat ---

    [Fact]
    public void BeginCombat_IncrementsCombatIndex()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamage("p1", "PlayerA", 10);
        var s1 = StatsCollector.FinalizeCurrentTurn()!;

        StatsCollector.BeginCombat();
        StatsCollector.RecordDamage("p1", "PlayerA", 20);
        var s2 = StatsCollector.FinalizeCurrentTurn()!;

        Assert.Equal(1, s1.CombatIndex);
        Assert.Equal(2, s2.CombatIndex);
    }

    [Fact]
    public void BeginCombat_ResetsTurnNumber()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamage("p1", "PlayerA", 10);
        StatsCollector.FinalizeCurrentTurn();
        StatsCollector.RecordDamage("p1", "PlayerA", 10);
        StatsCollector.FinalizeCurrentTurn(); // turn 2

        StatsCollector.BeginCombat();
        StatsCollector.RecordDamage("p1", "PlayerA", 10);
        var snapshot = StatsCollector.FinalizeCurrentTurn()!;

        Assert.Equal(1, snapshot.TurnNumber);
    }

    // --- RecordDamage / FinalizeCurrentTurn ---

    [Fact]
    public void RecordDamage_AccumulatesWithinTurn()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamage("p1", "PlayerA", 10);
        StatsCollector.RecordDamage("p1", "PlayerA", 15);

        var snapshot = StatsCollector.FinalizeCurrentTurn()!;

        Assert.Equal(25, snapshot.StatsByPlayer["p1"].DamageDealt);
    }

    [Fact]
    public void RecordDamage_TracksMultiplePlayers()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamage("p1", "PlayerA", 20);
        StatsCollector.RecordDamage("p2", "PlayerB", 30);

        var snapshot = StatsCollector.FinalizeCurrentTurn()!;

        Assert.Equal(20, snapshot.StatsByPlayer["p1"].DamageDealt);
        Assert.Equal(30, snapshot.StatsByPlayer["p2"].DamageDealt);
    }

    [Fact]
    public void RecordDamage_IgnoresZeroOrNegative()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamage("p1", "PlayerA", 0);
        StatsCollector.RecordDamage("p1", "PlayerA", -5);

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
        StatsCollector.RecordDamage("p1", "PlayerA", 10);
        StatsCollector.FinalizeCurrentTurn();

        var second = StatsCollector.FinalizeCurrentTurn();

        Assert.Null(second);
    }

    [Fact]
    public void FinalizeCurrentTurn_IncrementsTurnNumber()
    {
        StatsCollector.BeginCombat();

        StatsCollector.RecordDamage("p1", "PlayerA", 10);
        var t1 = StatsCollector.FinalizeCurrentTurn()!;

        StatsCollector.RecordDamage("p1", "PlayerA", 10);
        var t2 = StatsCollector.FinalizeCurrentTurn()!;

        Assert.Equal(1, t1.TurnNumber);
        Assert.Equal(2, t2.TurnNumber);
    }

    // --- FinalizeCurrentCombat ---

    [Fact]
    public void FinalizeCurrentCombat_AggregatesTotalAcrossTurns()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamage("p1", "PlayerA", 10);
        StatsCollector.FinalizeCurrentTurn();
        StatsCollector.RecordDamage("p1", "PlayerA", 20);
        StatsCollector.FinalizeCurrentTurn();

        var summary = StatsCollector.FinalizeCurrentCombat();

        Assert.Equal(30, summary.TotalsByPlayer["p1"].DamageDealt);
    }

    [Fact]
    public void FinalizeCurrentCombat_IncludesUnfinalizedCurrentTurn()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamage("p1", "PlayerA", 10);
        StatsCollector.FinalizeCurrentTurn();
        // ターン確定せずに戦闘終了
        StatsCollector.RecordDamage("p1", "PlayerA", 5);

        var summary = StatsCollector.FinalizeCurrentCombat();

        Assert.Equal(15, summary.TotalsByPlayer["p1"].DamageDealt);
        Assert.Equal(2, summary.TotalTurns);
    }

    [Fact]
    public void FinalizeCurrentCombat_ReportsCorrectTotalTurns()
    {
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamage("p1", "PlayerA", 10);
        StatsCollector.FinalizeCurrentTurn();
        StatsCollector.RecordDamage("p1", "PlayerA", 10);
        StatsCollector.FinalizeCurrentTurn();
        StatsCollector.RecordDamage("p1", "PlayerA", 10);
        StatsCollector.FinalizeCurrentTurn();

        var summary = StatsCollector.FinalizeCurrentCombat();

        Assert.Equal(3, summary.TotalTurns);
    }
}
