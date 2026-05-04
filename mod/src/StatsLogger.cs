using Godot;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StsStats;

// Phase 1 PoC用。Phase 2でHttpSenderに置き換える。
internal static class StatsLogger
{
    private static string _logPath = "/tmp/sts_stats.jsonl";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void Initialize()
    {
        try
        {
            string userDataDir = OS.GetUserDataDir();
            if (!string.IsNullOrEmpty(userDataDir))
                _logPath = Path.Combine(userDataDir, "sts_stats.jsonl");
        }
        catch
        {
            // GetUserDataDir が失敗した場合は /tmp にフォールバック済み
        }

        GD.Print($"[StsStats] Log path: {_logPath}");
        Log("system", new { message = "StsStats initialized", log_path = _logPath });
    }

    public static void LogTurnEnd(TurnSnapshot snapshot)
    {
        var payload = new
        {
            @event   = "turn_end",
            combat   = snapshot.CombatIndex,
            turn     = snapshot.TurnNumber,
            timestamp = snapshot.Timestamp.ToString("O"),
            damage   = snapshot.StatsByPlayer.ToDictionary(
                kv => kv.Value.PlayerName,
                kv => kv.Value.DamageDealt)
        };
        Log("turn_end", payload);
    }

    public static void LogCombatEnd(CombatSummary summary)
    {
        var payload = new
        {
            @event   = "combat_end",
            combat   = summary.CombatIndex,
            turns    = summary.TotalTurns,
            timestamp = summary.Timestamp.ToString("O"),
            totals   = summary.TotalsByPlayer.ToDictionary(
                kv => kv.Value.PlayerName,
                kv => kv.Value.DamageDealt)
        };
        Log("combat_end", payload);
    }

    private static void Log(string label, object payload)
    {
        try
        {
            string line = JsonSerializer.Serialize(payload, JsonOptions);
            GD.Print($"[StsStats:{label}] {line}");
            File.AppendAllText(_logPath, line + "\n");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[StsStats] Failed to write log: {ex.Message}");
        }
    }
}
