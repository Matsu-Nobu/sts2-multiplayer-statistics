using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Logging;

namespace StsStats;

/// <summary>
/// IApiClient への送信を非同期キューで行う。
/// 失敗時は指数バックオフでリトライ、最大試行回数到達で破棄。
/// キューが満杯のときは古いものから捨てる（メモリ無限増殖防止）。
/// </summary>
internal sealed class HttpSender : IDisposable
{
    private const int MaxQueueSize = 100;
    public const int DefaultMaxAttempts = 4;     // 初回 + 3 リトライ

    private readonly IApiClient _api;
    private readonly int _maxAttempts;
    private readonly Func<int, TimeSpan> _backoff;
    private readonly Queue<WorkItem> _queue = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _worker;
    private int _pending;   // queued + executing + waiting-for-retry

    public HttpSender(IApiClient api, int maxAttempts = DefaultMaxAttempts, Func<int, TimeSpan>? backoff = null)
    {
        _api          = api;
        _maxAttempts  = maxAttempts;
        _backoff      = backoff ?? (n => TimeSpan.FromSeconds(Math.Pow(2, n)));   // 2,4,8 秒
        _worker       = Task.Run(WorkerLoop);
    }

    public void EnqueueTurn(string sessionId, string writeToken, TurnPayload payload)
    {
        Enqueue(new WorkItem(WorkType.Turn, sessionId, writeToken, payload, null, 0), isRetry: false);
    }

    public void EnqueueEvents(string sessionId, string writeToken, IReadOnlyList<EventRecord> events)
    {
        if (events.Count == 0) return;
        Enqueue(new WorkItem(WorkType.Events, sessionId, writeToken, null, events, 0), isRetry: false);
    }

    /// <summary>
    /// テスト用に「キュー・処理中・リトライ待機」全てが空になるまで待つ。
    /// 戻り値: 残ったペンディング数（0 なら正常完了）。
    /// </summary>
    internal async Task<int> WaitForIdleAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            int remaining = Interlocked.CompareExchange(ref _pending, 0, 0);
            if (remaining == 0) return 0;
            await Task.Delay(20);
        }
        return Interlocked.CompareExchange(ref _pending, 0, 0);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _worker.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
        _signal.Dispose();
    }

    // --- internals ---

    private void Enqueue(WorkItem item, bool isRetry)
    {
        if (!isRetry) Interlocked.Increment(ref _pending);
        lock (_gate)
        {
            if (_queue.Count >= MaxQueueSize)
            {
                _queue.Dequeue();
                Interlocked.Decrement(ref _pending);     // dropped item is no longer pending
                Log.Error("[StsStats] HttpSender queue full, dropping oldest item");
            }
            _queue.Enqueue(item);
        }
        try { _signal.Release(); } catch (ObjectDisposedException) { /* shutting down */ }
    }

    private async Task WorkerLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try { await _signal.WaitAsync(_cts.Token); }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException)    { return; }

            WorkItem item;
            lock (_gate)
            {
                if (_queue.Count == 0) continue;
                item = _queue.Dequeue();
            }

            bool ok = await ExecuteAsync(item);
            if (ok || item.Attempt + 1 >= _maxAttempts)
            {
                Interlocked.Decrement(ref _pending);
            }
            else
            {
                int nextAttempt = item.Attempt + 1;
                try { await Task.Delay(_backoff(nextAttempt), _cts.Token); }
                catch (OperationCanceledException)
                {
                    Interlocked.Decrement(ref _pending);
                    return;
                }
                Enqueue(item with { Attempt = nextAttempt }, isRetry: true);
            }
        }
    }

    private async Task<bool> ExecuteAsync(WorkItem item)
    {
        try
        {
            return item.Type switch
            {
                WorkType.Turn   => await _api.PostTurnAsync(item.SessionId, item.WriteToken, item.Turn!),
                WorkType.Events => await _api.PostEventsAsync(item.SessionId, item.WriteToken, item.Events!),
                _               => false,
            };
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] HttpSender execute error: {ex.Message}");
            return false;
        }
    }

    private enum WorkType { Turn, Events }

    private record WorkItem(
        WorkType Type,
        string   SessionId,
        string   WriteToken,
        TurnPayload? Turn,
        IReadOnlyList<EventRecord>? Events,
        int      Attempt
    );
}
