using Godot;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StsStats;

/// <summary>
/// API.md と同じ形のペイロードを JSONL に出力する。Phase 2 後半で HttpSender に置き換え予定。
/// </summary>
internal static class StatsLogger
{
    private static string _logPath = "/tmp/sts_stats.jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy    = null,                                  // dict キー（player_id・power_id 等）は変換しない
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
        catch { /* /tmp フォールバック維持 */ }

        GD.Print($"[StsStats] Log path: {_logPath}");
        Write("system", new { message = "StsStats initialized", log_path = _logPath });
    }

    /// <summary>POST /sessions/{id}/turns の Body と同じ形を出力する。</summary>
    public static void LogTurn(TurnPayload payload)
    {
        var body = new
        {
            combat_index = payload.CombatIndex,
            turn_number  = payload.TurnNumber,
            is_final     = payload.IsFinal,
            timestamp    = payload.Timestamp.ToString("O"),
            players      = payload.Players.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    player_id   = kv.Key,
                    player_name = kv.Value.PlayerName,
                    turn        = SerializeTurn(kv.Value.Turn),
                    combat      = SerializeCombat(kv.Value.Combat),
                })
        };
        Write("turn", body);
    }

    /// <summary>POST /sessions/{id}/events の各要素と同じ形を出力する。</summary>
    public static void LogEvent(EventRecord ev)
    {
        var body = new
        {
            event_uuid  = ev.EventUuid.ToString(),
            event_type  = ev.EventType,
            occurred_at = ev.OccurredAt.ToString("O"),
            player_id   = ev.PlayerId,
            floor       = ev.Floor,
            payload     = ev.Payload,
        };
        Write("event", body);
    }

    private static object SerializeTurn(PlayerTurnSummary t) => new
    {
        damage_dealt        = t.DamageDealt,
        damage_received     = t.DamageReceived,
        block_gained_self   = t.BlockGainedSelf,
        block_given_allies  = t.BlockGivenToAllies,
        energy_used         = t.EnergyUsed,
        cards_played        = t.CardsPlayed,
        cards_drawn         = t.CardsDrawn,
        cards               = t.Cards.Select(SerializeCard).ToArray(),
    };

    private static object SerializeCombat(PlayerCombatSummary c) => new
    {
        damage_dealt        = c.DamageDealt,
        damage_received     = c.DamageReceived,
        block_gained_self   = c.BlockGainedSelf,
        block_given_allies  = c.BlockGivenToAllies,
        energy_used         = c.EnergyUsed,
        cards_played        = c.CardsPlayed,
        cards_drawn         = c.CardsDrawn,
        potions_used        = c.PotionsUsed,
        max_single_hit      = c.MaxSingleHit,
        debuffs_applied     = c.DebuffsApplied,
        card_stats          = c.CardStats.Select(SerializeCard).ToArray(),
    };

    private static object SerializeCard(CardStatsSummary cs) => new
    {
        card_id          = cs.CardId,
        card_name        = cs.CardName,
        card_type        = cs.CardType,
        play_count       = cs.PlayCount,
        damage_dealt     = cs.DamageDealt,
        block_provided   = cs.BlockProvided,
        debuffs_applied  = cs.DebuffsApplied,
        max_single_hit   = cs.MaxSingleHit,
    };

    private static void Write(string label, object body)
    {
        try
        {
            string line = JsonSerializer.Serialize(body, JsonOptions);
            GD.Print($"[StsStats:{label}] {line}");
            File.AppendAllText(_logPath, line + "\n");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[StsStats] Failed to write log: {ex.Message}");
        }
    }
}
