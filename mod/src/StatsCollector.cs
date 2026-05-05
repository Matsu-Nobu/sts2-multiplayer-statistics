using System.Collections.Generic;
using System.Linq;

namespace StsStats;

internal record CardInfo(string CardId, string CardName, string CardType);

internal static class StatsCollector
{
    private static int _combatIndex = 0;
    private static int _turnNumber = 0;
    private static readonly Dictionary<string, MutableTurnData> _currentTurn = new();
    private static readonly Dictionary<string, MutableCombatData> _currentCombat = new();

    public static void BeginCombat()
    {
        _combatIndex++;
        _turnNumber = 0;
        _currentTurn.Clear();
        _currentCombat.Clear();
    }

    public static void RecordDamageDealt(string playerId, string playerName, int amount, CardInfo? card = null)
    {
        if (amount <= 0) return;
        GetOrCreateTurn(playerId, playerName).DamageDealt += amount;
        var combat = GetOrCreateCombat(playerId, playerName);
        combat.DamageDealt += amount;
        if (amount > combat.MaxSingleHit) combat.MaxSingleHit = amount;
        var cs = GetOrCreateCardStats(combat, card);
        cs.DamageDealt += amount;
        if (amount > cs.MaxSingleHit) cs.MaxSingleHit = amount;
    }

    public static void RecordDamageReceived(string playerId, string playerName, int amount)
    {
        if (amount <= 0) return;
        GetOrCreateTurn(playerId, playerName).DamageReceived += amount;
        GetOrCreateCombat(playerId, playerName).DamageReceived += amount;
    }

    public static void RecordBlockGainedSelf(string playerId, string playerName, int amount, CardInfo? card = null)
    {
        if (amount <= 0) return;
        GetOrCreateTurn(playerId, playerName).BlockGainedSelf += amount;
        var combat = GetOrCreateCombat(playerId, playerName);
        combat.BlockGainedSelf += amount;
        GetOrCreateCardStats(combat, card).BlockProvided += amount;
    }

    public static void RecordBlockGivenToAlly(string playerId, string playerName, int amount, CardInfo? card = null)
    {
        if (amount <= 0) return;
        GetOrCreateTurn(playerId, playerName).BlockGivenToAllies += amount;
        var combat = GetOrCreateCombat(playerId, playerName);
        combat.BlockGivenToAllies += amount;
        GetOrCreateCardStats(combat, card).BlockProvided += amount;
    }

    public static void RecordEnergyUsed(string playerId, string playerName, int amount)
    {
        if (amount <= 0) return;
        GetOrCreateTurn(playerId, playerName).EnergyUsed += amount;
        GetOrCreateCombat(playerId, playerName).EnergyUsed += amount;
    }

    public static void RecordCardPlayed(string playerId, string playerName, CardInfo card)
    {
        GetOrCreateTurn(playerId, playerName).CardsPlayed++;
        var combat = GetOrCreateCombat(playerId, playerName);
        combat.CardsPlayed++;
        GetOrCreateCardStats(combat, card).PlayCount++;
    }

    public static void RecordCardDrawn(string playerId, string playerName)
    {
        GetOrCreateTurn(playerId, playerName).CardsDrawn++;
        GetOrCreateCombat(playerId, playerName).CardsDrawn++;
    }

    public static void RecordDebuffApplied(string playerId, string playerName, string powerId, int stacks, CardInfo? card = null)
    {
        if (stacks <= 0) return;
        var combat = GetOrCreateCombat(playerId, playerName);
        combat.DebuffsApplied.TryGetValue(powerId, out int prev);
        combat.DebuffsApplied[powerId] = prev + stacks;
        var cs = GetOrCreateCardStats(combat, card);
        cs.DebuffsApplied.TryGetValue(powerId, out int csPrev);
        cs.DebuffsApplied[powerId] = csPrev + stacks;
    }

    public static void RecordPotionUsed(string playerId, string playerName)
    {
        GetOrCreateCombat(playerId, playerName).PotionsUsed++;
    }

    public static TurnSnapshot? FinalizeCurrentTurn()
    {
        if (_currentTurn.Count == 0) return null;

        _turnNumber++;
        var snapshot = new TurnSnapshot(
            CombatIndex: _combatIndex,
            TurnNumber:  _turnNumber,
            Timestamp:   DateTime.UtcNow,
            StatsByPlayer: _currentTurn.ToDictionary(
                kv => kv.Key,
                kv => new PlayerTurnStats(
                    kv.Key,
                    kv.Value.PlayerName,
                    kv.Value.DamageDealt,
                    kv.Value.DamageReceived,
                    kv.Value.BlockGainedSelf,
                    kv.Value.BlockGivenToAllies,
                    kv.Value.EnergyUsed,
                    kv.Value.CardsPlayed,
                    kv.Value.CardsDrawn))
        );
        _currentTurn.Clear();
        return snapshot;
    }

    public static CombatSummary FinalizeCurrentCombat()
    {
        bool hasUnfinalized = _currentTurn.Count > 0;
        int totalTurns = _turnNumber + (hasUnfinalized ? 1 : 0);
        _currentTurn.Clear();

        return new CombatSummary(
            CombatIndex:    _combatIndex,
            TotalTurns:     totalTurns,
            Timestamp:      DateTime.UtcNow,
            TotalsByPlayer: _currentCombat.ToDictionary(kv => kv.Key, kv => kv.Value.ToSummary())
        );
    }

    internal static void Reset()
    {
        _combatIndex = 0;
        _turnNumber = 0;
        _currentTurn.Clear();
        _currentCombat.Clear();
    }

    private static MutableTurnData GetOrCreateTurn(string playerId, string playerName)
    {
        if (!_currentTurn.TryGetValue(playerId, out var data))
            _currentTurn[playerId] = data = new MutableTurnData { PlayerName = playerName };
        return data;
    }

    private static MutableCombatData GetOrCreateCombat(string playerId, string playerName)
    {
        if (!_currentCombat.TryGetValue(playerId, out var data))
            _currentCombat[playerId] = data = new MutableCombatData { PlayerId = playerId, PlayerName = playerName };
        return data;
    }

    private static MutableCardStats GetOrCreateCardStats(MutableCombatData combat, CardInfo? card)
    {
        string key = card?.CardId ?? "(indirect)";
        if (!combat.CardStats.TryGetValue(key, out var cs))
            combat.CardStats[key] = cs = new MutableCardStats
            {
                CardId   = card?.CardId   ?? "(indirect)",
                CardName = card?.CardName ?? "(間接ダメージ)",
                CardType = card?.CardType ?? "(indirect)",
            };
        return cs;
    }
}

internal class MutableTurnData
{
    public string PlayerName  = "";
    public int DamageDealt;
    public int DamageReceived;
    public int BlockGainedSelf;
    public int BlockGivenToAllies;
    public int EnergyUsed;
    public int CardsPlayed;
    public int CardsDrawn;
}

internal class MutableCombatData
{
    public string PlayerId    = "";
    public string PlayerName  = "";
    public int DamageDealt;
    public int DamageReceived;
    public int BlockGainedSelf;
    public int BlockGivenToAllies;
    public int EnergyUsed;
    public int CardsPlayed;
    public int CardsDrawn;
    public int PotionsUsed;
    public int MaxSingleHit;
    public Dictionary<string, int>          DebuffsApplied = new();
    public Dictionary<string, MutableCardStats> CardStats  = new();

    public PlayerCombatSummary ToSummary() => new(
        PlayerId, PlayerName,
        DamageDealt, DamageReceived,
        BlockGainedSelf, BlockGivenToAllies,
        EnergyUsed, CardsPlayed, CardsDrawn,
        PotionsUsed,
        new Dictionary<string, int>(DebuffsApplied),
        CardStats.Values.Select(cs => cs.ToRecord()).ToList(),
        MaxSingleHit
    );
}

internal class MutableCardStats
{
    public string CardId   = "";
    public string CardName = "";
    public string CardType = "";
    public int PlayCount;
    public int DamageDealt;
    public int BlockProvided;
    public int MaxSingleHit;
    public Dictionary<string, int> DebuffsApplied = new();

    public CardStatsSummary ToRecord() => new(
        CardId, CardName, CardType,
        PlayCount, DamageDealt, BlockProvided,
        new Dictionary<string, int>(DebuffsApplied),
        MaxSingleHit
    );
}

internal record PlayerTurnStats(
    string PlayerId,
    string PlayerName,
    int DamageDealt,
    int DamageReceived     = 0,
    int BlockGainedSelf    = 0,
    int BlockGivenToAllies = 0,
    int EnergyUsed         = 0,
    int CardsPlayed        = 0,
    int CardsDrawn         = 0
);

internal record CardStatsSummary(
    string CardId,
    string CardName,
    string CardType,
    int PlayCount,
    int DamageDealt,
    int BlockProvided,
    Dictionary<string, int> DebuffsApplied,
    int MaxSingleHit
);

internal record PlayerCombatSummary(
    string PlayerId,
    string PlayerName,
    int DamageDealt,
    int DamageReceived,
    int BlockGainedSelf,
    int BlockGivenToAllies,
    int EnergyUsed,
    int CardsPlayed,
    int CardsDrawn,
    int PotionsUsed,
    Dictionary<string, int> DebuffsApplied,
    List<CardStatsSummary>  CardStats,
    int MaxSingleHit
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
    Dictionary<string, PlayerCombatSummary> TotalsByPlayer
);
