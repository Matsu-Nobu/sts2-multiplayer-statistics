using System.Collections.Concurrent;

namespace StsStats;

// ゲーム型に依存しないため単体テスト可能
internal static class StatsCollector
{
    private static int _combatIndex = 0;
    private static int _turnNumber = 0;
    private static readonly ConcurrentDictionary<string, PlayerTurnStats> _currentTurn = new();
    private static readonly ConcurrentDictionary<string, PlayerTurnStats> _currentCombat = new();

    public static void BeginCombat()
    {
        _combatIndex++;
        _turnNumber = 0;
        _currentTurn.Clear();
        _currentCombat.Clear();
    }

    // ゲーム型はHookPatchesで解決済み。ここでは文字列のみ受け取る。
    public static void RecordDamage(string playerId, string playerName, int amount)
    {
        if (amount <= 0) return;

        // ターン内のみに記録。戦闘合計はFinalizeCurrentTurnで集約する。
        _currentTurn.AddOrUpdate(
            playerId,
            new PlayerTurnStats(playerId, playerName, amount),
            (_, existing) => existing with { DamageDealt = existing.DamageDealt + amount });
    }

    public static TurnSnapshot? FinalizeCurrentTurn()
    {
        if (_currentTurn.IsEmpty) return null;

        _turnNumber++;
        var snapshot = new TurnSnapshot(
            CombatIndex:   _combatIndex,
            TurnNumber:    _turnNumber,
            Timestamp:     DateTime.UtcNow,
            StatsByPlayer: new Dictionary<string, PlayerTurnStats>(_currentTurn)
        );

        // ターン確定時に戦闘合計へ集約
        foreach (var (id, stats) in _currentTurn)
            _currentCombat.AddOrUpdate(
                id,
                stats,
                (_, existing) => existing with { DamageDealt = existing.DamageDealt + stats.DamageDealt });

        _currentTurn.Clear();
        return snapshot;
    }

    public static CombatSummary FinalizeCurrentCombat()
    {
        // 最終ターンがFinalizeされていない場合（戦闘終了hookが先に来た場合）も集約する
        if (!_currentTurn.IsEmpty)
        {
            _turnNumber++;
            foreach (var (id, stats) in _currentTurn)
                _currentCombat.AddOrUpdate(
                    id,
                    stats,
                    (_, existing) => existing with { DamageDealt = existing.DamageDealt + stats.DamageDealt });
            _currentTurn.Clear();
        }

        return new CombatSummary(
            CombatIndex:    _combatIndex,
            TotalTurns:     _turnNumber,
            Timestamp:      DateTime.UtcNow,
            TotalsByPlayer: new Dictionary<string, PlayerTurnStats>(_currentCombat)
        );
    }

    // テスト用: 内部状態をリセット
    internal static void Reset()
    {
        _combatIndex = 0;
        _turnNumber = 0;
        _currentTurn.Clear();
        _currentCombat.Clear();
    }
}

internal record PlayerTurnStats(
    string PlayerId,
    string PlayerName,
    int DamageDealt
);

internal record TurnSnapshot(
    int CombatIndex,
    int TurnNumber,
    DateTime Timestamp,
    Dictionary<string, PlayerTurnStats> StatsByPlayer
);

internal record CombatSummary(
    int CombatIndex,
    int TotalTurns,
    DateTime Timestamp,
    Dictionary<string, PlayerTurnStats> TotalsByPlayer
);
