using System;
using System.IO;
using System.Text.Json;
using Godot;

namespace StsStats;

/// <summary>
/// ゲームのバージョン (例 "v0.113.1")。ゲームの実行ファイルと同じフォルダにある release_info.json の "version" を読む。
/// 読めなければ null で、サーバにはバージョンを送らない。
/// </summary>
internal static class GameVersion
{
    private static bool _loaded;
    private static string? _value;

    public static string? Current
    {
        get
        {
            if (_loaded) return _value;
            _loaded = true;
            try
            {
                string? dir = Path.GetDirectoryName(OS.GetExecutablePath());
                if (dir == null) return null;
                string path = Path.Combine(dir, "release_info.json");
                if (!File.Exists(path)) return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String)
                    _value = v.GetString();
            }
            catch (Exception ex)
            {
                MegaCrit.Sts2.Core.Logging.Log.Error($"[StsStats] GameVersion read failed: {ex.Message}");
            }
            return _value;
        }
    }
}
