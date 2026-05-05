using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StsStats;

/// <summary>
/// docs/API.md と一致する JSON ペイロードを構築・直列化する。
/// StatsLogger（JSONL 出力）と ApiClient（HTTP 送信）の両方から使う。
/// </summary>
internal static class PayloadJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy    = null,                                    // dict キー（player_id・power_id 等）はそのまま
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>POST /sessions/{id}/turns の Body 形式。</summary>
    public static object BuildTurnBody(TurnPayload p) => new
    {
        combat_index = p.CombatIndex,
        turn_number  = p.TurnNumber,
        is_final     = p.IsFinal,
        timestamp    = p.Timestamp.ToString("O"),
        players      = p.Players.ToDictionary(
            kv => kv.Key,
            kv => (object)new
            {
                player_id   = kv.Key,
                player_name = kv.Value.PlayerName,
                turn        = BuildTurn(kv.Value.Turn),
                combat      = BuildCombat(kv.Value.Combat),
            })
    };

    /// <summary>POST /sessions/{id}/events の各要素形式。</summary>
    public static object BuildEventBody(EventRecord ev) => new
    {
        event_uuid  = ev.EventUuid.ToString(),
        event_type  = ev.EventType,
        occurred_at = ev.OccurredAt.ToString("O"),
        player_id   = ev.PlayerId,
        floor       = ev.Floor,
        payload     = ev.Payload,
    };

    public static string SerializeTurn(TurnPayload p) =>
        JsonSerializer.Serialize(BuildTurnBody(p), Options);

    public static string SerializeEvents(System.Collections.Generic.IEnumerable<EventRecord> evs) =>
        JsonSerializer.Serialize(evs.Select(BuildEventBody), Options);

    private static object BuildTurn(PlayerTurnSummary t) => new
    {
        damage_dealt        = t.DamageDealt,
        damage_received     = t.DamageReceived,
        block_gained_self   = t.BlockGainedSelf,
        block_given_allies  = t.BlockGivenToAllies,
        energy_used         = t.EnergyUsed,
        cards_played        = t.CardsPlayed,
        cards_drawn         = t.CardsDrawn,
        cards               = t.Cards.Select(BuildCard).ToArray(),
    };

    private static object BuildCombat(PlayerCombatSummary c) => new
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
        card_stats          = c.CardStats.Select(BuildCard).ToArray(),
    };

    private static object BuildCard(CardStatsSummary cs) => new
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
}
