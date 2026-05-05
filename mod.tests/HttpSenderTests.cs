using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace StsStats.Tests;

public class HttpSenderTests
{
    [Fact]
    public async Task EnqueueEvents_DeliversToApiClient()
    {
        var api = new RecordingApi();
        using var sender = new HttpSender(api);

        var ev = NewEvent("run_start");
        sender.EnqueueEvents("sess-1", "tok", new[] { ev });
        await sender.WaitForIdleAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, api.EventBatchesDelivered);
        Assert.Equal(1, api.EventsDelivered);
    }

    [Fact]
    public async Task EnqueueEvents_BulkBatch_DeliveredAtomically()
    {
        var api = new RecordingApi();
        using var sender = new HttpSender(api);

        var batch = new[] { NewEvent("a"), NewEvent("b"), NewEvent("c") };
        sender.EnqueueEvents("sess-1", "tok", batch);
        await sender.WaitForIdleAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, api.EventBatchesDelivered);
        Assert.Equal(3, api.EventsDelivered);
    }

    [Fact]
    public async Task FailingApi_RetriesUpToMax()
    {
        var api = new FlakeyApi(failTimes: 2);  // fail twice, succeed on 3rd
        using var sender = new HttpSender(api, backoff: _ => TimeSpan.FromMilliseconds(20));

        sender.EnqueueEvents("s", "t", new[] { NewEvent("x") });
        await sender.WaitForIdleAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(3, api.Attempts);   // 初回 + リトライ 2回
    }

    [Fact]
    public async Task FailingApi_DropsAfterMaxAttempts()
    {
        var api = new FlakeyApi(failTimes: 100);  // 常に失敗
        using var sender = new HttpSender(api, backoff: _ => TimeSpan.FromMilliseconds(20));

        sender.EnqueueEvents("s", "t", new[] { NewEvent("y") });
        await sender.WaitForIdleAsync(TimeSpan.FromSeconds(2));

        // 初回 + 3 リトライ = 4 試行で諦め
        Assert.Equal(4, api.Attempts);
    }

    private static EventRecord NewEvent(string type) =>
        new(Guid.NewGuid(), type, DateTime.UtcNow, "p1", 0, null, null, null, new { });

    // --- mocks ---

    private sealed class RecordingApi : IApiClient
    {
        public int EventBatchesDelivered;
        public int EventsDelivered;

        public Task<CreateSessionResult?> CreateSessionAsync(CreateSessionRequest req)
            => Task.FromResult<CreateSessionResult?>(new CreateSessionResult("s", "t", "u"));

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

        public Task<bool> PostEventsAsync(string sessionId, string writeToken, IReadOnlyList<EventRecord> events)
        {
            int n = Interlocked.Increment(ref Attempts);
            if (n <= _failTimes) return Task.FromResult(false);
            return Task.FromResult(true);
        }
    }
}
