using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Logging;

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
///   2) HTTP 送信。セッション準備済なら _outgoing バッファに溜め、
///      time-window（<see cref="BatchIntervalMs"/>）か件数閾値で
///      <see cref="HttpSender"/> に bulk enqueue する。
///      未準備なら _pending に溜め、FlushPending で一括送信。
/// </summary>
internal static class EventBuffer
{
    /// <summary>バッチ送信の最大待機時間（ms）。WebUI は 10s polling なのでこの程度の遅延は無視できる。</summary>
    private const int BatchIntervalMs = 2000;

    /// <summary>これ以上溜まったら待たずに即送信する閾値。</summary>
    private const int BatchSizeThreshold = 50;

    private static int _combatIndex = 0;
    private static int _turnNumber  = 0;
    private static int _sequence    = 0;
    private static int _floor       = 0;

    private static bool _exitHookRegistered = false;

    /// <summary>セッション未確定時の先行バッファ。FlushPending で吐き出す。</summary>
    private static readonly List<EventRecord> _pending = new();

    /// <summary>セッション確定後の送信用バッチ。BatchIntervalMs ごと、または閾値超で flush。</summary>
    private static readonly List<EventRecord> _outgoing = new();

    private static readonly object _lock = new();
    private static Task? _batchWorker;
    private static CancellationTokenSource? _batchCts;

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

    /// <summary>現在の floor 番号 (0 = 未開始)。setter patch から「今追加されたか」判定に使う。</summary>
    public static int CurrentFloor => _floor;

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
            EventRecord[]? toSend = null;
            lock (_lock)
            {
                _outgoing.Add(ev);
                EnsureBatchWorker();
                if (_outgoing.Count >= BatchSizeThreshold)
                {
                    toSend = _outgoing.ToArray();
                    _outgoing.Clear();
                }
            }
            // 即時 flush は閾値超のときのみ。それ以外は worker が時間で吐く。
            if (toSend != null)
                sender.EnqueueEvents(SessionManager.SessionId!, SessionManager.WriteToken!, toSend);

            // run_end / combat_end はタイミングを逃さず即送信
            if (eventType == "run_end" || eventType == "combat_end")
                FlushOutgoing();
        }
        else
        {
            lock (_lock) _pending.Add(ev);
        }
    }

    /// <summary>セッション準備完了時に呼び出し、溜まっていた pending event を一括送信する。</summary>
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

    /// <summary>送信バッチを即 flush する（combat_end / run_end / Dispose 等の境界で使う）。</summary>
    public static void FlushOutgoing()
    {
        var sender = ModEntry.HttpSender;
        if (sender == null) return;
        if (!SessionManager.IsReady) return;

        EventRecord[] snapshot;
        lock (_lock)
        {
            if (_outgoing.Count == 0) return;
            snapshot = _outgoing.ToArray();
            _outgoing.Clear();
        }
        sender.EnqueueEvents(SessionManager.SessionId!, SessionManager.WriteToken!, snapshot);
    }

    private static void EnsureBatchWorker()
    {
        if (_batchWorker != null) return;
        EnsureExitFlushHook();
        _batchCts = new CancellationTokenSource();
        var ct = _batchCts.Token;
        _batchWorker = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(BatchIntervalMs, ct);
                    FlushOutgoing();
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (Exception ex)
            {
                Log.Error($"[StsStats] EventBuffer batch worker error: {ex.Message}");
            }
        }, ct);
    }

    internal static void Reset()
    {
        // 残バッファを最後に一回送り出してから状態クリア
        try { FlushOutgoing(); } catch { }
        _combatIndex = 0;
        _turnNumber  = 0;
        _sequence    = 0;
        _floor       = 0;
        lock (_lock)
        {
            _pending.Clear();
            _outgoing.Clear();
        }
        try { _batchCts?.Cancel(); } catch { }
        _batchCts = null;
        _batchWorker = null;
    }

    /// <summary>
    /// プロセス終了時に未送信バッファを flush するため、AppDomain.ProcessExit に hook を 1 度だけ登録。
    /// クラッシュや alt-F4 でのデータ消失を最小化する。
    /// </summary>
    private static void EnsureExitFlushHook()
    {
        if (_exitHookRegistered) return;
        _exitHookRegistered = true;
        try
        {
            System.AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { FlushOutgoing(); }
                catch (System.Exception ex) { Log.Error($"[StsStats] ProcessExit flush error: {ex.Message}"); }
            };
            // SIGINT 等の正常停止経路にも一応乗せる
            System.AppDomain.CurrentDomain.UnhandledException += (_, _) =>
            {
                try { FlushOutgoing(); } catch { }
            };
        }
        catch (System.Exception ex)
        {
            Log.Error($"[StsStats] EnsureExitFlushHook error: {ex.Message}");
        }
    }
}
