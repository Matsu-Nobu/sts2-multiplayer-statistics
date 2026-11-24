using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace StsStats.Tests;

public class HistoryUploaderTests
{
    private readonly FakeImportApi _api = new();
    private readonly List<string> _built = new();
    private readonly List<(int Done, int Total)> _progress = new();

    private ImportCandidate Run(string seed, string outcome, int events = 3) => new(
        new ImportCheckRun(seed, "2026-06-03T12:14:00Z", new[] { "p1" }, outcome, 10),
        () =>
        {
            _built.Add(seed);
            var list = Enumerable.Range(0, events)
                .Select(i => new EventRecord(Guid.NewGuid(), i == events - 1 ? "run_end" : "floor_snapshot", DateTime.UtcNow, null, i, null, null, null, new { }))
                .ToList();
            return new ImportPayload(new CreateSessionRequest(Seed: seed, Source: "history"), list);
        });

    private Task<ImportResult> Upload(params ImportCandidate[] runs) =>
        HistoryUploader.RunAsync(_api, runs, (d, t) => _progress.Add((d, t)), TimeSpan.Zero);

    [Fact]
    public async Task SendsOnlyMissingVictoryOrDeathRuns()
    {
        _api.Missing = new[] { 0, 1, 3 };

        var result = await Upload(Run("A", "death"), Run("B", "abandoned"), Run("C", "victory"), Run("D", "victory"));

        Assert.Equal(new ImportResult(ImportStatus.Sent, 2), result);
        Assert.Equal(new[] { "A", "D" }, _built);
        Assert.Equal(new[] { "A", "D" }, _api.Created);
    }

    [Fact]
    public async Task ChecksAbandonedRunsToo()
    {
        _api.Missing = Array.Empty<int>();

        await Upload(Run("A", "abandoned"));

        Assert.Equal("abandoned", Assert.Single(_api.Checked).Outcome);
    }

    [Fact]
    public async Task PostsEventsInBatchesWithRunEndLast()
    {
        _api.Missing = new[] { 0 };

        await Upload(Run("A", "death", events: 120));

        Assert.Equal(new[] { 50, 50, 20 }, _api.Batches.Select(b => b.Count));
        Assert.Equal("run_end", _api.Batches.Last().Last().EventType);
    }

    [Fact]
    public async Task RunAlreadyOnServer_IsNotCounted()
    {
        _api.Missing = new[] { 0 };
        _api.Conflict.Add("A");

        var result = await Upload(Run("A", "death"));

        Assert.Equal(new ImportResult(ImportStatus.NothingToSend, 0), result);
        Assert.Empty(_api.Batches);
    }

    [Fact]
    public async Task NothingMissing_IsNothingToSend()
    {
        _api.Missing = Array.Empty<int>();

        var result = await Upload(Run("A", "death"));

        Assert.Equal(new ImportResult(ImportStatus.NothingToSend, 0), result);
        Assert.Empty(_built);
    }

    [Fact]
    public async Task CheckFails_IsFailed()
    {
        _api.Missing = null;

        var result = await Upload(Run("A", "death"));

        Assert.Equal(new ImportResult(ImportStatus.Failed, 0), result);
    }

    [Fact]
    public async Task CreateFails_StopsWithSentSoFar()
    {
        _api.Missing = new[] { 0, 1, 2 };
        _api.FailCreate.Add("B");

        var result = await Upload(Run("A", "death"), Run("B", "death"), Run("C", "death"));

        Assert.Equal(new ImportResult(ImportStatus.Failed, 1), result);
        Assert.Equal(new[] { "A", "B" }, _api.Created);
    }

    [Fact]
    public async Task PostFails_IsFailed()
    {
        _api.Missing = new[] { 0 };
        _api.FailPost = true;

        var result = await Upload(Run("A", "death"));

        Assert.Equal(new ImportResult(ImportStatus.Failed, 0), result);
    }

    [Fact]
    public async Task ReportsProgress()
    {
        _api.Missing = new[] { 0, 1 };

        await Upload(Run("A", "death"), Run("B", "death"));

        Assert.Equal(new[] { (0, 2), (1, 2), (2, 2) }, _progress);
    }

    private sealed class FakeImportApi : IImportApi
    {
        public int[]? Missing = Array.Empty<int>();
        public readonly HashSet<string> Conflict = new();
        public readonly HashSet<string> FailCreate = new();
        public bool FailPost;
        public readonly List<ImportCheckRun> Checked = new();
        public readonly List<string> Created = new();
        public readonly List<IReadOnlyList<EventRecord>> Batches = new();

        public Task<int[]?> CheckImportsAsync(IReadOnlyList<ImportCheckRun> runs)
        {
            Checked.AddRange(runs);
            return Task.FromResult(Missing);
        }

        public Task<HistorySessionResult> CreateHistorySessionAsync(CreateSessionRequest req)
        {
            string seed = req.Seed!;
            Created.Add(seed);
            if (Conflict.Contains(seed)) return Task.FromResult(new HistorySessionResult(null, true));
            if (FailCreate.Contains(seed)) return Task.FromResult(new HistorySessionResult(null, false));
            return Task.FromResult(new HistorySessionResult(new CreateSessionResult("s-" + seed, "t", "u"), false));
        }

        public Task<bool> PostEventsAsync(string sessionId, string writeToken, IReadOnlyList<EventRecord> events)
        {
            Batches.Add(events);
            return Task.FromResult(!FailPost);
        }
    }
}
