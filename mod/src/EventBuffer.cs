using System;
using System.Collections.Generic;

namespace StsStats;

/// <summary>
/// すべての StsStats event の発行点（戦闘内・外いずれも）。
///
/// 戦闘内 event は (combat_index, turn_number, sequence) で順序付ける。
/// HookPatches は BeginCombat / BeginTurn / UpdateFloor で context を更新し、
/// EmitTurnEvent / EmitCombatEvent / EmitGlobalEvent で event を発行する。
///
/// 各 Emit は:
///   1) StatsLogger 経由で JSONL に書き込む（ローカル記録）
///   2) HTTP 送信。セッション準備済なら HttpSender に enqueue、未準備なら
///      内部バッファに溜めて FlushPending で一括送信する。
/// </summary>
internal static class EventBuffer
{
    private static int _combatIndex = 0;
    private static int _turnNumber  = 0;
    private static int _sequence    = 0;
    private static int _floor       = 0;

    private static readonly List<EventRecord> _pending = new();
    private static readonly object _lock = new();

    /// <summary>戦闘開始時。combat_index を増やし、turn_number=1（1ターン目が即時開始する想定）、sequence=0 にリセット。</summary>
    public static void BeginCombat()
    {
        _combatIndex++;
        _turnNumber = 1;
        _sequence   = 0;
    }

    /// <summary>
    /// プレイヤー側ターン終了時に呼ぶ。次ターン用に turn_number を進め sequence をリセット。
    /// AfterTurnEnd(side=Player) hook 起点。
    /// </summary>
    public static void BeginTurn()
    {
        _turnNumber++;
        _sequence = 0;
    }

    /// <summary>マップ移動・戦闘開始等で更新する。</summary>
    public static void UpdateFloor(int floor) => _floor = floor;

    public static int CurrentCombatIndex => _combatIndex;
    public static int CurrentTurnNumber  => _turnNumber;

    /// <summary>戦闘内 event（combat_index / turn_number / sequence を自動付与）。</summary>
    public static void EmitTurnEvent(string eventType, string? playerId, object payload)
        => Emit(eventType, playerId, payload, withCombat: true, withTurn: true);

    /// <summary>戦闘単位 event（combat_index は付くが turn は付かない、combat_start/end 等）。</summary>
    public static void EmitCombatEvent(string eventType, string? playerId, object payload)
        => Emit(eventType, playerId, payload, withCombat: true, withTurn: false);

    /// <summary>戦闘外 event（floor のみ、run_start/end や card_picked 等）。</summary>
    public static void EmitGlobalEvent(string eventType, string? playerId, object payload)
        => Emit(eventType, playerId, payload, withCombat: false, withTurn: false);

    private static void Emit(string eventType, string? playerId, object payload, bool withCombat, bool withTurn)
    {
        int? combat = withCombat ? _combatIndex : (int?)null;
        int? turn   = withTurn   ? _turnNumber  : (int?)null;
        int? seq    = withTurn   ? _sequence++  : (int?)null;

        var ev = new EventRecord(
            EventUuid:    Guid.NewGuid(),
            EventType:    eventType,
            OccurredAt:   DateTime.UtcNow,
            PlayerId:     playerId,
            Floor:        _floor,
            CombatIndex:  combat,
            TurnNumber:   turn,
            Sequence:     seq,
            Payload:      payload
        );

        StatsLogger.LogEvent(ev);

        var sender = ModEntry.HttpSender;
        if (sender == null) return;
        if (SessionManager.IsReady)
        {
            sender.EnqueueEvents(SessionManager.SessionId!, SessionManager.WriteToken!, new[] { ev });
        }
        else
        {
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
        _combatIndex = 0;
        _turnNumber  = 0;
        _sequence    = 0;
        _floor       = 0;
        lock (_lock) _pending.Clear();
    }
}
