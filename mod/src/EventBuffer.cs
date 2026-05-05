using System.Collections.Generic;

namespace StsStats;

/// <summary>
/// 戦闘外で発生する discrete event のバッファ。Phase 2 ではローカル JSONL に出力するだけ、
/// Phase 2 後半で HttpSender を経由して /sessions/{id}/events に bulk POST する想定。
/// </summary>
internal static class EventBuffer
{
    private static readonly List<EventRecord> _events = new();
    private static readonly object _lock = new();

    public static void Emit(EventRecord ev)
    {
        lock (_lock) _events.Add(ev);
        StatsLogger.LogEvent(ev);
    }

    public static IReadOnlyList<EventRecord> DrainAll()
    {
        lock (_lock)
        {
            var snapshot = _events.ToArray();
            _events.Clear();
            return snapshot;
        }
    }

    internal static void Reset()
    {
        lock (_lock) _events.Clear();
    }
}

internal record EventRecord(
    Guid     EventUuid,
    string   EventType,
    DateTime OccurredAt,
    string?  PlayerId,
    int?     Floor,
    object   Payload
);
