using System.Collections.Generic;

namespace StsStats;

/// <summary>
/// 戦闘外で発生する discrete event の発行点。
/// JSONL ログ出力（StatsLogger）と HTTP 送信（HttpSender）の両方を行う。
/// セッション未準備中に Emit された event は内部バッファに溜め、
/// セッションが準備完了したタイミング（FlushPending）で一括送信する。
/// </summary>
internal static class EventBuffer
{
    private static readonly List<EventRecord> _pending = new();
    private static readonly object _lock = new();

    public static void Emit(EventRecord ev)
    {
        StatsLogger.LogEvent(ev);     // JSONL は常に書く

        var sender = ModEntry.HttpSender;
        if (sender == null) return;   // HTTP無効時は JSONL のみ

        if (SessionManager.IsReady)
        {
            sender.EnqueueEvents(SessionManager.SessionId!, SessionManager.WriteToken!, new[] { ev });
        }
        else
        {
            // session 準備中 — バッファに積んで FlushPending で送る
            lock (_lock) _pending.Add(ev);
        }
    }

    /// <summary>セッション準備完了時に呼び出し、溜まっていた event を一括送信する。</summary>
    public static void FlushPending()
    {
        if (!SessionManager.IsReady) return;
        var sender = ModEntry.HttpSender;
        if (sender == null) return;

        EventRecord[] snapshot;
        lock (_lock)
        {
            if (_pending.Count == 0) return;
            snapshot = _pending.ToArray();
            _pending.Clear();
        }
        sender.EnqueueEvents(SessionManager.SessionId!, SessionManager.WriteToken!, snapshot);
    }

    internal static void Reset()
    {
        lock (_lock) _pending.Clear();
    }
}

// EventRecord 型は EventTypes.cs を参照
