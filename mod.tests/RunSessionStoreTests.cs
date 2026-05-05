using System.IO;
using Xunit;

namespace StsStats.Tests;

public class RunSessionStoreTests : System.IDisposable
{
    private readonly string _dir;
    private readonly RunSessionStore _store;

    public RunSessionStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "sts_stats_tests_" + System.Guid.NewGuid().ToString("N"));
        _store = new RunSessionStore(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void Load_NonExistent_ReturnsNull()
    {
        Assert.Null(_store.Load("nope"));
    }

    [Fact]
    public void Save_Then_Load_Roundtrip()
    {
        var s = NewSession(lookup: "76561_seedX", floor: 5);
        _store.Save(s);

        var loaded = _store.Load("76561_seedX");
        Assert.NotNull(loaded);
        Assert.Equal(s.SessionId,  loaded!.SessionId);
        Assert.Equal(s.WriteToken, loaded.WriteToken);
        Assert.Equal(s.RunKey,     loaded.RunKey);
        Assert.Equal(5,            loaded.LastSeenFloor);
        Assert.False(loaded.RunStartEmitted);
    }

    [Fact]
    public void Save_Overwrites_PreviousRecord()
    {
        _store.Save(NewSession("76561_seedX", floor: 5));
        _store.Save(NewSession("76561_seedX", floor: 1, sessionId: "newer"));   // 新規 run（floor 戻った）

        var loaded = _store.Load("76561_seedX");
        Assert.Equal("newer", loaded!.SessionId);
        Assert.Equal(1, loaded.LastSeenFloor);
    }

    [Fact]
    public void Sanitize_AllowsKeysWithSpecialChars()
    {
        var s = NewSession(lookup: "host/seed:weird?", floor: 1);
        _store.Save(s);
        Assert.NotNull(_store.Load("host/seed:weird?"));
    }

    private static StoredSession NewSession(string lookup, int floor, string sessionId = "sess-1") =>
        new(
            LookupKey:       lookup,
            RunKey:          "rkey-" + sessionId,
            SessionId:       sessionId,
            WriteToken:      "tok",
            ShareUrl:        "http://example/s/" + sessionId,
            CharacterId:     "IRONCLAD",
            Ascension:       5,
            Seed:            "seedX",
            GameMode:        "Standard",
            HostSteamId:     76561UL,
            StartedAt:       "2026-05-05T00:00:00Z",
            LastSeenFloor:   floor,
            RunStartEmitted: false
        );
}

public class RunKeyTests
{
    [Fact]
    public void Compute_DiffersWhenStartedAtDiffers()
    {
        var a = RunKey.Compute(76561UL, "seed", "IRONCLAD", 5, "Standard", "2026-05-05T00:00:00Z");
        var b = RunKey.Compute(76561UL, "seed", "IRONCLAD", 5, "Standard", "2026-05-05T00:00:01Z");
        Assert.NotEqual(a, b);   // 開始時刻が違えば別キー（同 seed の再挑戦を区別できる）
    }

    [Fact]
    public void Compute_StableForSameInputs()
    {
        var a = RunKey.Compute(76561UL, "seed", "IRONCLAD", 5, "Standard", "T");
        var b = RunKey.Compute(76561UL, "seed", "IRONCLAD", 5, "Standard", "T");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Lookup_DependsOnHostAndSeed()
    {
        Assert.Equal("123_abc", RunKey.Lookup(123UL, "abc"));
        Assert.NotEqual(RunKey.Lookup(1UL, "x"), RunKey.Lookup(2UL, "x"));
    }
}
