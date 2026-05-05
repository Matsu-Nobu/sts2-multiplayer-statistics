using System.IO;
using System.Reflection;
using System.Text.Json;

namespace StsStats;

/// <summary>
/// バックエンドサーバURLの解決ロジック。
/// 優先順位:
///   1. mods/StsStats/config.json の "backend_url"
///   2. 環境変数 STS_STATS_BACKEND_URL
///   3. デフォルト http://localhost:8080
/// 値が空文字列ならば「HTTP送信無効」とみなし、JSONLログのみ動作する。
/// </summary>
internal static class SessionConfig
{
    // 公開バックエンド。エンドユーザは何も設定しなくてもこれに接続される。
    // セルフホストする場合は config.json または STS_STATS_BACKEND_URL で上書き可能。
    public const string DefaultBackendUrl = "https://sts2stats.fly.dev";

    /// <summary>バックエンドURL。空ならHTTP送信無効。</summary>
    public static string BackendUrl { get; private set; } = DefaultBackendUrl;

    /// <summary>HTTP送信が有効かどうか。</summary>
    public static bool HttpEnabled => !string.IsNullOrWhiteSpace(BackendUrl);

    public static void Load()
    {
        // 1. config.json を試す（mod DLL と同じディレクトリ）
        string? fromFile = TryLoadFromConfigFile();
        if (fromFile != null)
        {
            BackendUrl = NormalizeUrl(fromFile);
            return;
        }

        // 2. 環境変数
        string? fromEnv = System.Environment.GetEnvironmentVariable("STS_STATS_BACKEND_URL");
        if (fromEnv != null)
        {
            BackendUrl = NormalizeUrl(fromEnv);
            return;
        }

        // 3. デフォルト
        BackendUrl = DefaultBackendUrl;
    }

    /// <summary>テスト用に明示的にロードロジックをバイパスする。</summary>
    internal static void OverrideBackendUrl(string url) => BackendUrl = NormalizeUrl(url);

    private static string? TryLoadFromConfigFile()
    {
        try
        {
            string? dllDir = Path.GetDirectoryName(typeof(SessionConfig).Assembly.Location);
            if (string.IsNullOrEmpty(dllDir)) return null;

            string path = Path.Combine(dllDir, "config.json");
            if (!File.Exists(path)) return null;

            string json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("backend_url", out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        }
        catch
        {
            // 設定ファイルが壊れていてもゲームは止めない
        }
        return null;
    }

    /// <summary>末尾スラッシュを削除しトリムする。空文字も許容。</summary>
    internal static string NormalizeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        return url.Trim().TrimEnd('/');
    }
}
