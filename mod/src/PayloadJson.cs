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

    /// <summary>
    /// POST /sessions/{id}/events の各要素形式。
    /// 戦闘外 event は CombatIndex / TurnNumber / Sequence が null になる。
    /// </summary>
    public static object BuildEventBody(EventRecord ev) => new
    {
        event_uuid   = ev.EventUuid.ToString(),
        event_type   = ev.EventType,
        occurred_at  = ev.OccurredAt.ToString("O"),
        player_id    = ev.PlayerId,
        floor        = ev.Floor,
        combat_index = ev.CombatIndex,
        turn_number  = ev.TurnNumber,
        sequence     = ev.Sequence,
        payload      = ev.Payload,
    };

    public static string SerializeEvents(System.Collections.Generic.IEnumerable<EventRecord> evs) =>
        JsonSerializer.Serialize(evs.Select(BuildEventBody), Options);
}
