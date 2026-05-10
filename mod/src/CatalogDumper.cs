using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace StsStats;

/// <summary>
/// 実験用: ModelDb から全 CardModel / RelicModel / PotionModel をダンプして
/// ローカルファイルに JSON 出力する。本番で使うかどうか判断するためのサイズ計測。
///
/// run_start 時に 1 回だけ実行される (StatsLogger と同じ ~/Library/.../mods/StsStats/ 配下に書く)。
/// </summary>
internal static class CatalogDumper
{
    private static bool _dumped = false;

    /// <summary>
    /// run_start で 1 回呼ぶ。既にダンプ済みなら no-op。
    /// </summary>
    public static void DumpOnce()
    {
        if (_dumped) return;
        _dumped = true;
        try
        {
            var modelDbType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.ModelDb")
                           ?? AccessTools.TypeByName("ModelDb");
            if (modelDbType == null)
            {
                Log.Error("[StsStats][CatalogDumper] ModelDb type not found");
                return;
            }

            var allCards   = (modelDbType.GetProperty("AllCards"  )?.GetValue(null) as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
            var allRelics  = (modelDbType.GetProperty("AllRelics" )?.GetValue(null) as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();
            var allPotions = (modelDbType.GetProperty("AllPotions")?.GetValue(null) as IEnumerable)?.Cast<object>().ToList() ?? new List<object>();

            var output = new
            {
                version = "experimental-1",
                cards   = allCards.Select(BuildCardEntry).Where(e => e != null).ToList(),
                relics  = allRelics.Select(BuildRelicEntry).Where(e => e != null).ToList(),
                potions = allPotions.Select(BuildPotionEntry).Where(e => e != null).ToList(),
            };

            var json = JsonSerializer.Serialize(output, new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });

            // 出力先: StatsLogger の dir と揃える (~/Library/.../mods/StsStats/catalog-dump.json)
            var dir = GetDumpDir();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "catalog-dump.json");
            File.WriteAllText(path, json);

            // gzip 後サイズも測る
            var rawBytes = System.Text.Encoding.UTF8.GetBytes(json);
            var gzipBytes = GzipBytes(rawBytes);

            Log.Info($"[StsStats][CatalogDumper] dumped to {path}");
            Log.Info($"[StsStats][CatalogDumper] cards={output.cards.Count} relics={output.relics.Count} potions={output.potions.Count}");
            Log.Info($"[StsStats][CatalogDumper] raw_size={rawBytes.Length}B gzip_size={gzipBytes.Length}B");
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats][CatalogDumper] dump failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static object? BuildCardEntry(object card)
    {
        try
        {
            string id = GetIdEntry(card);
            if (string.IsNullOrEmpty(id)) return null;
            string baseTitle = card.GetType().GetProperty("Title")?.GetValue(card)?.ToString() ?? "";
            string baseDesc  = ResolveLoc(card.GetType().GetProperty("Description")?.GetValue(card));
            string rarity    = card.GetType().GetProperty("Rarity")?.GetValue(card)?.ToString() ?? "";
            int maxUpgrade   = (int?)card.GetType().GetProperty("MaxUpgradeLevel")?.GetValue(card) ?? 1;

            // upgraded variant: Title getter は IsUpgraded フラグ依存だが ModelDb の canonical では false 固定。
            // Description は LocString で別 key を引いてる可能性があるので、両方 raw description キーを引く。
            // 簡易的に "cards.<id>.upgrade_description" を直接 LocString で組んで取る方が確実だが、
            // ここは MVP として Description LocString のみ取る。upgraded 表示は後で別 key を試す。
            return new
            {
                id            = id,
                name_base     = baseTitle,
                name_upgraded = $"{baseTitle}+",   // 簡易 (MaxUpgradeLevel>1 は別途)
                description   = baseDesc,
                rarity        = rarity,
                max_upgrade   = maxUpgrade,
            };
        }
        catch (Exception ex) { Log.Error($"[StsStats][CatalogDumper] card entry error: {ex.Message}"); return null; }
    }

    private static object? BuildRelicEntry(object relic)
    {
        try
        {
            string id = GetIdEntry(relic);
            if (string.IsNullOrEmpty(id)) return null;
            string title = ResolveLoc(relic.GetType().GetProperty("Title")?.GetValue(relic));
            string desc  = ResolveLoc(relic.GetType().GetProperty("Description")?.GetValue(relic));
            string rarity = relic.GetType().GetProperty("Rarity")?.GetValue(relic)?.ToString() ?? "";
            return new
            {
                id          = id,
                name        = title,
                description = desc,
                rarity      = rarity,
            };
        }
        catch (Exception ex) { Log.Error($"[StsStats][CatalogDumper] relic entry error: {ex.Message}"); return null; }
    }

    private static object? BuildPotionEntry(object potion)
    {
        try
        {
            string id = GetIdEntry(potion);
            if (string.IsNullOrEmpty(id)) return null;
            string title = ResolveLoc(potion.GetType().GetProperty("Title")?.GetValue(potion));
            string desc  = ResolveLoc(potion.GetType().GetProperty("Description")?.GetValue(potion));
            string rarity = potion.GetType().GetProperty("Rarity")?.GetValue(potion)?.ToString() ?? "";
            return new
            {
                id          = id,
                name        = title,
                description = desc,
                rarity      = rarity,
            };
        }
        catch (Exception ex) { Log.Error($"[StsStats][CatalogDumper] potion entry error: {ex.Message}"); return null; }
    }

    private static string GetIdEntry(object? model)
    {
        try
        {
            var idObj = model?.GetType().GetProperty("Id")?.GetValue(model);
            return idObj?.GetType().GetProperty("Entry")?.GetValue(idObj)?.ToString() ?? "";
        }
        catch { return ""; }
    }

    private static string ResolveLoc(object? loc)
    {
        if (loc == null) return "";
        try
        {
            var t = loc.GetType();
            foreach (var name in new[] { "GetFormattedText", "GetRawText" })
            {
                var m = t.GetMethod(name, System.Type.EmptyTypes);
                if (m == null) continue;
                var v = m.Invoke(loc, null) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }
        catch { }
        return "";
    }

    private static string GetDumpDir()
    {
        // StatsLogger と同じパス規約 (mod 配置 dir 配下)
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var modDir = Path.GetDirectoryName(asm.Location) ?? "";
            return modDir;
        }
        catch
        {
            return Path.GetTempPath();
        }
    }

    private static byte[] GzipBytes(byte[] input)
    {
        using var ms = new MemoryStream();
        using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal))
        {
            gz.Write(input, 0, input.Length);
        }
        return ms.ToArray();
    }
}
