using System.Collections.Generic;

namespace StsStats;

/// <summary>
/// 戦闘外で発生する discrete event のバッファ。
/// JSONL ログ出力（StatsLogger）と HTTP 送信（HttpSender）の両方を行う。
/// HTTP 側はセッション未作成時には呼ばれず、JSONL のみ動作する。
/// </summary>
internal static class EventBuffer
{
    private static readonly List<EventRecord> _events = new();
    private static readonly object _lock = new();

    public static void Emit(EventRecord ev)
    {
        lock (_lock) _events.Add(ev);
        StatsLogger.LogEvent(ev);

        // HTTP 送信（セッション準備済みの場合のみ）
        var sender = ModEntry.HttpSender;
        if (sender != null && SessionManager.IsReady)
        {
            sender.EnqueueEvents(SessionManager.SessionId!, SessionManager.WriteToken!, new[] { ev });
        }
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

// EventRecord 型は EventTypes.cs に分離（テスト時に EventBuffer を含めずに参照できるよう）
