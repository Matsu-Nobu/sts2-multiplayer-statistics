using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StsStats.Tests;

public class HttpSenderTests
{
    [Fact]
    public async Task EnqueueTurn_DeliversToApiClient()
    {
        var api = new RecordingApi();
        using var sender = new HttpSender(api);

        StatsCollector.Reset();
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "PlayerA", 10);
        var payload = StatsCollector.FinalizeTurn()!;

        sender.EnqueueTurn("sess-1", "tok", payload);
        await sender.WaitForIdleAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, api.TurnsDelivered);
    }

    [Fact]
    public async Task EnqueueEvents_DeliversToApiClient()
    {
        var api = new RecordingApi();
        using var sender = new HttpSender(api);

        var ev = new EventRecord(Guid.NewGuid(), "run_start", DateTime.UtcNow,
            "p1", 0, new { character_id = "IRONCLAD" });
        sender.EnqueueEvents("sess-1", "tok", new[] { ev });
        await sender.WaitForIdleAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, api.EventBatchesDelivered);
        Assert.Equal(1, api.EventsDelivered);
    }

    [Fact]
    public async Task FailingApi_RetriesUpToMax()
    {
        var api = new FlakeyApi(failTimes: 2);  // fail twice, succeed on 3rd
        using var sender = new HttpSender(api, backoff: _ => TimeSpan.FromMilliseconds(20));

        StatsCollector.Reset();
        StatsCollector.BeginCombat();
        StatsCollector.RecordDamageDealt("p1", "P", 5);
        var payload = StatsCollector.FinalizeTurn()!;

        sender.EnqueueTurn("s", "t", payload);
        await sender.WaitForIdleAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(3, api.Attempts);   // 初回 + リトライ 2回
    }

    [Fact]
    public async Task FailingApi_DropsAfterMaxAttempts()
    {
        var api = new FlakeyApi(failTimes: 100);  // 常に失敗
        using var sender = new HttpSender(api, backoff: _ => TimeSpan.FromMilliseconds(20));

        var ev = new EventRecord(Guid.NewGuid(), "run_end", DateTime.UtcNow, "p", 0, new { });
        sender.EnqueueEvents("s", "t", new[] { ev });

        await sender.WaitForIdleAsync(TimeSpan.FromSeconds(2));

        // 初回 + 3 リトライ = 4 試行で諦め
        Assert.Equal(4, api.Attempts);
    }

    // --- mocks ---

    private sealed class RecordingApi : IApiClient
    {
        public int TurnsDelivered;
        public int EventBatchesDelivered;
        public int EventsDelivered;

        public Task<CreateSessionResult?> CreateSessionAsync(CreateSessionRequest req)
            => Task.FromResult<CreateSessionResult?>(new CreateSessionResult("s", "t", "u"));

        public Task<bool> PostTurnAsync(string sessionId, string writeToken, TurnPayload payload)
        {
            Interlocked.Increment(ref TurnsDelivered);
            return Task.FromResult(true);
        }

        public Task<bool> PostEventsAsync(string sessionId, string writeToken, IReadOnlyList<EventRecord> events)
        {
            Interlocked.Increment(ref EventBatchesDelivered);
            Interlocked.Add(ref EventsDelivered, events.Count);
            return Task.FromResult(true);
        }
    }

    private sealed class FlakeyApi : IApiClient
    {
        private int _failTimes;
        public int Attempts;

        public FlakeyApi(int failTimes) { _failTimes = failTimes; }

        public Task<CreateSessionResult?> CreateSessionAsync(CreateSessionRequest req)
            => Task.FromResult<CreateSessionResult?>(null);

        public Task<bool> PostTurnAsync(string sessionId, string writeToken, TurnPayload payload)
            => Common();

        public Task<bool> PostEventsAsync(string sessionId, string writeToken, IReadOnlyList<EventRecord> events)
            => Common();

        private Task<bool> Common()
        {
            int n = Interlocked.Increment(ref Attempts);
            if (n <= _failTimes) return Task.FromResult(false);
            return Task.FromResult(true);
        }
    }
}
