using System.IO;
using System.Text.Json;
using Godot;

namespace StsStats;

/// <summary>
/// API.md と同じ形のペイロードを JSONL に出力する。
/// HTTP送信（HttpSender）と並走させ、バックエンド到達不能時のローカル記録として残す。
/// </summary>
internal static class StatsLogger
{
    private static string _logPath = "/tmp/sts_stats.jsonl";

    public static void Initialize()
    {
        try
        {
            string userDataDir = OS.GetUserDataDir();
            if (!string.IsNullOrEmpty(userDataDir))
                _logPath = Path.Combine(userDataDir, "sts_stats.jsonl");
        }
        catch { /* /tmp フォールバック維持 */ }

        GD.Print($"[StsStats] Log path: {_logPath}");
        Write("system", new { message = "StsStats initialized", log_path = _logPath });
    }

    /// <summary>セッション作成成功時に共有URLを記録する。make log で確認できるように。</summary>
    public static void LogSessionCreated(string sessionId, string shareUrl) =>
        Write("session", new { event_type = "session_created", session_id = sessionId, share_url = shareUrl });

    /// <summary>POST /sessions/{id}/events の各要素と同じ形を出力する。</summary>
    public static void LogEvent(EventRecord ev) =>
        Write("event", PayloadJson.BuildEventBody(ev));

    private static void Write(string label, object body)
    {
        try
        {
            string line = JsonSerializer.Serialize(body, PayloadJson.Options);
            GD.Print($"[StsStats:{label}] {line}");
            File.AppendAllText(_logPath, line + "\n");
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[StsStats] Failed to write log: {ex.Message}");
        }
    }
}
