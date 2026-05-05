using System.Collections.Generic;
using System.Linq;

namespace StsStats;

internal record CardInfo(string CardId, string CardName, string CardType);

internal static class StatsCollector
{
    private static int _combatIndex = 0;
    private static int _turnNumber = 0;
    private static readonly Dictionary<string, MutableTurnData>   _currentTurn   = new();
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

        var turn = GetOrCreateTurn(playerId, playerName);
        turn.DamageDealt += amount;
        var turnCs = GetOrCreateCardStats(turn.CardStats, card);
        turnCs.DamageDealt += amount;
        if (amount > turnCs.MaxSingleHit) turnCs.MaxSingleHit = amount;

        var combat = GetOrCreateCombat(playerId, playerName);
        combat.DamageDealt += amount;
        if (amount > combat.MaxSingleHit) combat.MaxSingleHit = amount;
        var combatCs = GetOrCreateCardStats(combat.CardStats, card);
        combatCs.DamageDealt += amount;
        if (amount > combatCs.MaxSingleHit) combatCs.MaxSingleHit = amount;
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
        var turn = GetOrCreateTurn(playerId, playerName);
        turn.BlockGainedSelf += amount;
        GetOrCreateCardStats(turn.CardStats, card).BlockProvided += amount;

        var combat = GetOrCreateCombat(playerId, playerName);
        combat.BlockGainedSelf += amount;
        GetOrCreateCardStats(combat.CardStats, card).BlockProvided += amount;
    }

    public static void RecordBlockGivenToAlly(string playerId, string playerName, int amount, CardInfo? card = null)
    {
        if (amount <= 0) return;
        var turn = GetOrCreateTurn(playerId, playerName);
        turn.BlockGivenToAllies += amount;
        GetOrCreateCardStats(turn.CardStats, card).BlockProvided += amount;

        var combat = GetOrCreateCombat(playerId, playerName);
        combat.BlockGivenToAllies += amount;
        GetOrCreateCardStats(combat.CardStats, card).BlockProvided += amount;
    }

    public static void RecordEnergyUsed(string playerId, string playerName, int amount)
    {
        if (amount <= 0) return;
        GetOrCreateTurn(playerId, playerName).EnergyUsed += amount;
        GetOrCreateCombat(playerId, playerName).EnergyUsed += amount;
    }

    public static void RecordCardPlayed(string playerId, string playerName, CardInfo card)
    {
        var turn = GetOrCreateTurn(playerId, playerName);
        turn.CardsPlayed++;
        GetOrCreateCardStats(turn.CardStats, card).PlayCount++;

        var combat = GetOrCreateCombat(playerId, playerName);
        combat.CardsPlayed++;
        GetOrCreateCardStats(combat.CardStats, card).PlayCount++;
    }

    public static void RecordCardDrawn(string playerId, string playerName)
    {
        GetOrCreateTurn(playerId, playerName).CardsDrawn++;
        GetOrCreateCombat(playerId, playerName).CardsDrawn++;
    }

    public static void RecordDebuffApplied(string playerId, string playerName, string powerId, int stacks, CardInfo? card = null)
    {
        if (stacks <= 0) return;

        var turn = GetOrCreateTurn(playerId, playerName);
        var turnCs = GetOrCreateCardStats(turn.CardStats, card);
        turnCs.DebuffsApplied.TryGetValue(powerId, out int tPrev);
        turnCs.DebuffsApplied[powerId] = tPrev + stacks;

        var combat = GetOrCreateCombat(playerId, playerName);
        combat.DebuffsApplied.TryGetValue(powerId, out int prev);
        combat.DebuffsApplied[powerId] = prev + stacks;
        var combatCs = GetOrCreateCardStats(combat.CardStats, card);
        combatCs.DebuffsApplied.TryGetValue(powerId, out int csPrev);
        combatCs.DebuffsApplied[powerId] = csPrev + stacks;
    }

    public static void RecordPotionUsed(string playerId, string playerName)
    {
        GetOrCreateCombat(playerId, playerName).PotionsUsed++;
    }

    /// <summary>
    /// 現在のターン情報を確定し、API.md 形式の TurnPayload を返す。
    /// _currentTurn はクリアされる。_currentCombat は維持される。
    /// </summary>
    public static TurnPayload? FinalizeTurn(bool isFinal = false)
    {
        // ターン進行が無く、かつ最終フラッシュでもなければスキップ
        if (_currentTurn.Count == 0 && !isFinal) return null;
        // 最終フラッシュだが累計データもない（戦闘がそもそも始まっていない等）→ 何も出さない
        if (_currentTurn.Count == 0 && _currentCombat.Count == 0) return null;

        if (_currentTurn.Count > 0) _turnNumber++;

        // turn 側の player 集合と combat 側の player 集合の和を取る（最終フラッシュで turn が空でも combat 累計は出したい）
        var allPlayerIds = _currentTurn.Keys.Union(_currentCombat.Keys).Distinct().ToList();

        var players = new Dictionary<string, PlayerTurnAndCombat>();
        foreach (var pid in allPlayerIds)
        {
            string playerName =
                (_currentTurn.TryGetValue(pid, out var t) ? t.PlayerName : null) ??
                (_currentCombat.TryGetValue(pid, out var c) ? c.PlayerName : null) ??
                pid;

            var turnStats = (_currentTurn.TryGetValue(pid, out var td))
                ? td.ToSummary()
                : PlayerTurnSummary.Empty();

            var combatStats = (_currentCombat.TryGetValue(pid, out var cd))
                ? cd.ToSummary()
                : PlayerCombatSummary.Empty();

            players[pid] = new PlayerTurnAndCombat(playerName, turnStats, combatStats);
        }

        var payload = new TurnPayload(
            CombatIndex: _combatIndex,
            TurnNumber:  _turnNumber,
            IsFinal:     isFinal,
            Timestamp:   DateTime.UtcNow,
            Players:     players
        );

        _currentTurn.Clear();
        return payload;
    }

    internal static void Reset()
    {
        _combatIndex = 0;
        _turnNumber = 0;
        _currentTurn.Clear();
        _currentCombat.Clear();
    }

    // --- mutable state helpers ---

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

    private static MutableCardStats GetOrCreateCardStats(Dictionary<string, MutableCardStats> bucket, CardInfo? card)
    {
        string key = card?.CardId ?? "(indirect)";
        if (!bucket.TryGetValue(key, out var cs))
            bucket[key] = cs = new MutableCardStats
            {
                CardId   = card?.CardId   ?? "(indirect)",
                CardName = card?.CardName ?? "(間接ダメージ)",
                CardType = card?.CardType ?? "(indirect)",
            };
        return cs;
    }
}

// --- mutable internal state ---

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
    public Dictionary<string, MutableCardStats> CardStats = new();

    public PlayerTurnSummary ToSummary() => new(
        DamageDealt, DamageReceived,
        BlockGainedSelf, BlockGivenToAllies,
        EnergyUsed, CardsPlayed, CardsDrawn,
        CardStats.Values.Select(cs => cs.ToRecord()).ToList()
    );
}

internal class MutableCombatData
{
    public string PlayerId   = "";
    public string PlayerName = "";
    public int DamageDealt;
    public int DamageReceived;
    public int BlockGainedSelf;
    public int BlockGivenToAllies;
    public int EnergyUsed;
    public int CardsPlayed;
    public int CardsDrawn;
    public int PotionsUsed;
    public int MaxSingleHit;
    public Dictionary<string, int>              DebuffsApplied = new();
    public Dictionary<string, MutableCardStats> CardStats      = new();

    public PlayerCombatSummary ToSummary() => new(
        DamageDealt, DamageReceived,
        BlockGainedSelf, BlockGivenToAllies,
        EnergyUsed, CardsPlayed, CardsDrawn,
        PotionsUsed, MaxSingleHit,
        new Dictionary<string, int>(DebuffsApplied),
        CardStats.Values.Select(cs => cs.ToRecord()).ToList()
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

// --- API-shaped immutable DTOs (match docs/API.md) ---

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

internal record PlayerTurnSummary(
    int DamageDealt,
    int DamageReceived,
    int BlockGainedSelf,
    int BlockGivenToAllies,
    int EnergyUsed,
    int CardsPlayed,
    int CardsDrawn,
    List<CardStatsSummary> Cards
)
{
    public static PlayerTurnSummary Empty() =>
        new(0, 0, 0, 0, 0, 0, 0, new List<CardStatsSummary>());
}

internal record PlayerCombatSummary(
    int DamageDealt,
    int DamageReceived,
    int BlockGainedSelf,
    int BlockGivenToAllies,
    int EnergyUsed,
    int CardsPlayed,
    int CardsDrawn,
    int PotionsUsed,
    int MaxSingleHit,
    Dictionary<string, int> DebuffsApplied,
    List<CardStatsSummary>  CardStats
)
{
    public static PlayerCombatSummary Empty() =>
        new(0, 0, 0, 0, 0, 0, 0, 0, 0,
            new Dictionary<string, int>(),
            new List<CardStatsSummary>());
}

internal record PlayerTurnAndCombat(
    string PlayerName,
    PlayerTurnSummary   Turn,
    PlayerCombatSummary Combat
);

internal record TurnPayload(
    int CombatIndex,
    int TurnNumber,
    bool IsFinal,
    DateTime Timestamp,
    Dictionary<string, PlayerTurnAndCombat> Players
);
