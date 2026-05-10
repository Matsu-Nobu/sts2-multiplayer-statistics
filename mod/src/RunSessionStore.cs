using System;
using System.IO;
using System.Text.Json;

namespace StsStats;

/// <summary>
/// 「同じ run（seed + host）」が再開されたときに過去のセッションを復元するためのストア。
///
/// lookup_key = "{host_steam_id}_{seed}" （ファイル名）
///
/// 同一 seed + host で前回保存があった場合でも、それが「セーブから再開した同じ run」か
/// 「同じ seed で再挑戦した別 run」かを区別する必要がある。判定は TotalFloor の
/// monotonicity を使う:
///   - 現在の TotalFloor &gt;= stored.LastSeenFloor → 同じ run の続き（reuse）
///   - 現在の TotalFloor &lt;  stored.LastSeenFloor → 新しい run（overwrite）
///
/// セッションIDは run ごとに一意。run_key は「この run の不変ハッシュ」として
/// {host}|{seed}|{character}|{ascension}|{gameMode}|{started_at} の SHA256 から生成し、
/// 後で別所と突き合わせる用途に使える（現状はログ用途のみ）。
/// </summary>
internal sealed class RunSessionStore
{
    private readonly string _dir;

    public RunSessionStore(string dir)
    {
        _dir = dir;
        try { Directory.CreateDirectory(dir); }
        catch (Exception ex)
        {
            MegaCrit.Sts2.Core.Logging.Log.Error($"[StsStats] RunSessionStore mkdir failed: {ex.Message}");
        }
    }

    public StoredSession? Load(string lookupKey)
    {
        try
        {
            string path = PathFor(lookupKey);
            if (!File.Exists(path)) return null;
            string body = File.ReadAllText(path);
            return JsonSerializer.Deserialize<StoredSession>(body, Options);
        }
        catch (Exception ex)
        {
            MegaCrit.Sts2.Core.Logging.Log.Error($"[StsStats] RunSessionStore load failed: {ex.Message}");
            return null;
        }
    }

    public void Save(StoredSession session)
    {
        try
        {
            string body = JsonSerializer.Serialize(session, Options);
            File.WriteAllText(PathFor(session.LookupKey), body);
        }
        catch (Exception ex)
        {
            MegaCrit.Sts2.Core.Logging.Log.Error($"[StsStats] RunSessionStore save failed: {ex.Message}");
        }
    }

    private string PathFor(string lookupKey) => Path.Combine(_dir, Sanitize(lookupKey) + ".json");

    private static string Sanitize(string key)
    {
        var chars = key.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            bool ok = char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.';
            if (!ok) chars[i] = '_';
        }
        return new string(chars);
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
}

/// <summary>RunSessionStore に保存される1ラン分の情報。</summary>
internal record StoredSession(
    string LookupKey,             // "{host_steam_id}_{seed}"
    string RunKey,                // start_time 込みのハッシュ（不変ID）
    string SessionId,
    string WriteToken,
    string ShareUrl,
    string? CharacterId,
    int    Ascension,
    string Seed,
    string GameMode,
    ulong  HostSteamId,
    string StartedAt,             // run の開始時刻（mod 検出時刻、UTC ISO8601）
    int    LastSeenFloor,         // 最後に観測した TotalFloor
    bool   RunStartEmitted = false
);

/// <summary>
/// run_key の計算ヘルパ。
/// {host}|{seed}|{character}|{ascension}|{gameMode}|{started_at} の SHA256 上位 64bit を hex 化。
/// started_at を含めることで、同じ seed/character/ascension の別 run が異なるキーを持てる。
/// </summary>
internal static class RunKey
{
    public static string Compute(
        ulong   hostSteamId,
        string  seed,
        string? characterId,
        int     ascension,
        string  gameMode,
        string  startedAtIso)
    {
        string composite = $"{hostSteamId}|{seed}|{characterId ?? "?"}|{ascension}|{gameMode}|{startedAtIso}";
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(composite));
        return Convert.ToHexString(hash).Substring(0, 16).ToLowerInvariant();
    }

    /// <summary>lookup_key（per-seed ファイル名用）。host と seed の組。</summary>
    public static string Lookup(ulong hostSteamId, string seed) =>
        $"{hostSteamId}_{seed}";
}
