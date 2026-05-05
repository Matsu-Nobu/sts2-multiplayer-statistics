using Godot;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StsStats;

// Phase 1 PoC用。Phase 2でHttpSenderに置き換える。
internal static class StatsLogger
{
    private static string _logPath = "/tmp/sts_stats.jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Initialize()
    {
        try
        {
            string userDataDir = OS.GetUserDataDir();
            if (!string.IsNullOrEmpty(userDataDir))
                _logPath = Path.Combine(userDataDir, "sts_stats.jsonl");
        }
        catch
        {
            // GetUserDataDir が失敗した場合は /tmp にフォールバック済み
        }

        GD.Print($"[StsStats] Log path: {_logPath}");
        Log("system", new { message = "StsStats initialized", log_path = _logPath });
    }

    public static void LogTurnEnd(TurnSnapshot snapshot)
    {
        var payload = new
        {
            @event    = "turn_end",
            combat    = snapshot.CombatIndex,
            turn      = snapshot.TurnNumber,
            timestamp = snapshot.Timestamp.ToString("O"),
            players   = snapshot.StatsByPlayer.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    player_name        = kv.Value.PlayerName,
                    damage_dealt       = kv.Value.DamageDealt,
                    damage_received    = kv.Value.DamageReceived,
                    block_gained_self  = kv.Value.BlockGainedSelf,
                    block_given_allies = kv.Value.BlockGivenToAllies,
                    energy_used        = kv.Value.EnergyUsed,
                    cards_played       = kv.Value.CardsPlayed,
                    cards_drawn        = kv.Value.CardsDrawn,
                })
        };
        Log("turn_end", payload);
    }

    public static void LogCombatEnd(CombatSummary summary)
    {
        var payload = new
        {
            @event    = "combat_end",
            combat    = summary.CombatIndex,
            turns     = summary.TotalTurns,
            timestamp = summary.Timestamp.ToString("O"),
            players   = summary.TotalsByPlayer.ToDictionary(
                kv => kv.Key,
                kv => (object)new
                {
                    player_name        = kv.Value.PlayerName,
                    damage_dealt       = kv.Value.DamageDealt,
                    damage_received    = kv.Value.DamageReceived,
                    block_gained_self  = kv.Value.BlockGainedSelf,
                    block_given_allies = kv.Value.BlockGivenToAllies,
                    energy_used        = kv.Value.EnergyUsed,
                    cards_played       = kv.Value.CardsPlayed,
                    cards_drawn        = kv.Value.CardsDrawn,
                    potions_used       = kv.Value.PotionsUsed,
                    max_single_hit     = kv.Value.MaxSingleHit,
                    debuffs_applied    = kv.Value.DebuffsApplied,
                    card_stats         = kv.Value.CardStats.Select(cs => new
                    {
                        card_id         = cs.CardId,
                        card_name       = cs.CardName,
                        card_type       = cs.CardType,
                        play_count      = cs.PlayCount,
                        damage_dealt    = cs.DamageDealt,
                        block_provided  = cs.BlockProvided,
                        debuffs_applied = cs.DebuffsApplied,
                        max_single_hit  = cs.MaxSingleHit,
                    }).ToArray(),
                })
        };
        Log("combat_end", payload);
    }

    private static void Log(string label, object payload)
    {
        try
        {
            string line = JsonSerializer.Serialize(payload, JsonOptions);
            GD.Print($"[StsStats:{label}] {line}");
            File.AppendAllText(_logPath, line + "\n");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[StsStats] Failed to write log: {ex.Message}");
        }
    }
}
