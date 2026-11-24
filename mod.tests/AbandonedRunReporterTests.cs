using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace StsStats.Tests;

public class AbandonedRunReporterTests : IDisposable
{
    private const ulong Local = 76561UL;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sts_stats_tests_" + Guid.NewGuid().ToString("N"));
    private readonly RunSessionStore _store;
    private readonly CapturingApi _api = new();
    private readonly HttpSender _sender;
    private readonly Dictionary<string, int> _finalHp = new() { ["76561"] = 40, ["99"] = 0 };

    public AbandonedRunReporterTests()
    {
        _store  = new RunSessionStore(_dir);
        _sender = new HttpSender(_api);
        _store.Save(Stored(seed: "SEED1", lastSeenFloor: 10));
    }

    public void Dispose()
    {
        _sender.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private bool Report(string seed, int floor) =>
        AbandonedRunReporter.Report(_store, _sender, Local, seed, floor, _finalHp);

    [Fact]
    public async Task StoredSession_SendsRunEndAbandoned()
    {
        Assert.True(Report("SEED1", 12));
        await _sender.WaitForIdleAsync(TimeSpan.FromSeconds(2));

        var (sessionId, token, ev) = Assert.Single(_api.Sent);
        Assert.Equal("sess-SEED1", sessionId);
        Assert.Equal("tok", token);
        Assert.Equal("run_end", ev.EventType);
        Assert.Equal(12, ev.Floor);
        var payload = JsonSerializer.SerializeToElement(ev.Payload);
        Assert.Equal("abandoned", payload.GetProperty("outcome").GetString());
        Assert.Equal(12, payload.GetProperty("final_floor").GetInt32());
        Assert.Equal(40, payload.GetProperty("final_hp").GetProperty("76561").GetInt32());
        Assert.Equal(0, payload.GetProperty("badges").GetProperty("99").GetArrayLength());
    }

    [Fact]
    public async Task SaveOneFloorBehindStored_SendsStoredFloor()
    {
        Assert.True(Report("SEED1", 9));
        await _sender.WaitForIdleAsync(TimeSpan.FromSeconds(2));

        var ev = Assert.Single(_api.Sent).Event;
        Assert.Equal(10, ev.Floor);
        Assert.Equal(10, JsonSerializer.SerializeToElement(ev.Payload).GetProperty("final_floor").GetInt32());
    }

    [Fact]
    public async Task NoStoredSession_SendsNothing()
    {
        Assert.False(Report("OTHER", 12));
        await _sender.WaitForIdleAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(_api.Sent);
    }

    [Fact]
    public async Task FloorBeforeStored_IsAnotherRun_SendsNothing()
    {
        Assert.False(Report("SEED1", 8));
        await _sender.WaitForIdleAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(_api.Sent);
    }

    [Fact]
    public async Task EmptySeed_UsesNoSeedKey()
    {
        _store.Save(Stored(seed: "no-seed", lastSeenFloor: 3));
        Assert.True(Report("", 2));
        await _sender.WaitForIdleAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("sess-no-seed", Assert.Single(_api.Sent).SessionId);
    }

    private static StoredSession Stored(string seed, int lastSeenFloor) => new(
        LookupKey:     RunKey.Lookup(Local, seed),
        RunKey:        "rkey",
        SessionId:     "sess-" + seed,
        WriteToken:    "tok",
        ShareUrl:      "http://example/s/x",
        CharacterId:   "IRONCLAD",
        Ascension:     0,
        Seed:          seed,
        GameMode:      "Standard",
        HostSteamId:   Local,
        StartedAt:     "2026-05-05T00:00:00Z",
        LastSeenFloor: lastSeenFloor);

    private sealed class CapturingApi : IApiClient
    {
        public readonly List<(string SessionId, string Token, EventRecord Event)> Sent = new();

        public Task<CreateSessionResult?> CreateSessionAsync(CreateSessionRequest req)
            => Task.FromResult<CreateSessionResult?>(null);

        public Task<bool> PostEventsAsync(string sessionId, string writeToken, IReadOnlyList<EventRecord> events)
        {
            lock (Sent) foreach (var e in events) Sent.Add((sessionId, writeToken, e));
            return Task.FromResult(true);
        }
    }
}
