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
/// STS2 の ModelDb から Card / Relic / Potion / Enchantment の definitions を
/// ローカル JSON ファイルにダンプする。本番ランタイムでは使わない (ゲームアップデート時のみ
/// 開発者が `make dump-catalog` で起動して dump する)。
///
/// 出力先: ~/Library/.../mods/StsStats/catalog-dump.json
/// この JSON を web/public/catalog.{lang}.json にコピーして commit する。
///
/// description は事前解決済 (DynamicVars で Damage/Block/Cards 等を bind して資料表示用文に展開):
///   - カード: description_base (未 upgrade) + description_upgraded (upgrade preview) の両方
///   - その他: description (DynamicDescription、UI 表示と同じ resolve 経路)
///
/// 装飾タグ ([gold]X[/gold] / [blue]X[/blue] 等) はそのまま残す → web 側で HTML span に変換する。
/// </summary>
internal static class CatalogDumper
{
    private static bool _dumped = false;

    public static void DumpOnce()
    {
        // 環境変数 STS_STATS_DUMP_CATALOG=1 が立ってるときだけ動く。
        // (一般プレイヤーが mod 入れても毎回 dump 走らないように)
        var enabled = Environment.GetEnvironmentVariable("STS_STATS_DUMP_CATALOG");
        if (string.IsNullOrEmpty(enabled) || enabled == "0") return;

        Log.Info($"[StsStats][CatalogDumper] DumpOnce called (already_dumped={_dumped})");
        if (_dumped) return;
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
            var allEnchantments = EnumerateAllEnchantments(modelDbType).ToList();

            // ModelDb がまだ populated されてない (= 早すぎ) ならスキップして次の trigger を待つ。
            // 成功時のみ _dumped = true。
            if (allCards.Count == 0 && allRelics.Count == 0 && allPotions.Count == 0)
            {
                Log.Info("[StsStats][CatalogDumper] skip: ModelDb not ready yet, will retry on next trigger");
                return;
            }
            _dumped = true;

            // 言語コード推定 (LocString.GetCurrentLocale 等が無い場合は "ja" 固定。後で LocaleHelper 探す)
            string lang = TryGetCurrentLocale() ?? "ja";

            var output = new
            {
                schema_version = 1,
                lang           = lang,
                generated_at   = DateTime.UtcNow.ToString("o"),
                cards          = allCards.Select(BuildCardEntry).Where(e => e != null).ToList(),
                relics         = allRelics.Select(BuildRelicEntry).Where(e => e != null).ToList(),
                potions        = allPotions.Select(BuildPotionEntry).Where(e => e != null).ToList(),
                enchantments   = allEnchantments.Select(BuildEnchantmentEntry).Where(e => e != null).ToList(),
            };

            var json = JsonSerializer.Serialize(output, new JsonSerializerOptions
            {
                WriteIndented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });

            var dir = GetDumpDir();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "catalog-dump.json");
            File.WriteAllText(path, json);

            var rawBytes = System.Text.Encoding.UTF8.GetBytes(json);
            var gzipBytes = GzipBytes(rawBytes);

            Log.Info($"[StsStats][CatalogDumper] dumped to {path}");
            Log.Info($"[StsStats][CatalogDumper] lang={lang} cards={output.cards.Count} relics={output.relics.Count} potions={output.potions.Count} enchantments={output.enchantments.Count}");
            Log.Info($"[StsStats][CatalogDumper] raw_size={rawBytes.Length}B gzip_size={gzipBytes.Length}B");
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats][CatalogDumper] dump failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// ModelDb._contentById (private static Dictionary&lt;ModelId, AbstractModel&gt;) から
    /// EnchantmentModel に代入可能なものだけを抽出。
    /// 公開 AllEnchantments プロパティが無いため。
    /// </summary>
    private static IEnumerable<object> EnumerateAllEnchantments(Type modelDbType)
    {
        IDictionary? dict = null;
        try
        {
            dict = AccessTools.Field(modelDbType, "_contentById")?.GetValue(null) as IDictionary;
        }
        catch (Exception ex) { Log.Error($"[StsStats][CatalogDumper] _contentById access error: {ex.Message}"); }
        if (dict == null) yield break;

        var enchantmentBaseType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.EnchantmentModel")
                               ?? AccessTools.TypeByName("EnchantmentModel");
        if (enchantmentBaseType == null) yield break;

        foreach (DictionaryEntry de in dict)
        {
            var v = de.Value;
            if (v != null && enchantmentBaseType.IsAssignableFrom(v.GetType()))
                yield return v;
        }
    }

    private static object? BuildCardEntry(object card)
    {
        try
        {
            string id = GetIdEntry(card);
            if (string.IsNullOrEmpty(id)) return null;

            // canonical CardModel は IsUpgraded=false 固定で DynamicVars も base 値しか返さない。
            // - description_base:    手動で LocString 構築 + DynamicVars + IfUpgradedVar(Normal)
            // - description_upgraded: ゲーム本体と同じパターンで MutableClone → UpgradeInternal()
            //   して実際にアップグレードしたインスタンスから description を取る。
            //   (GetDescriptionForUpgradePreview は IfUpgraded 分岐は解いてくれるが
            //    {Damage} 等の数値は base 値のまま → アップグレード値にならない)
            string descBase     = ResolveCardDescriptionBase(card);
            string descUpgraded = ResolveCardDescriptionUpgraded(card);

            string baseTitle = card.GetType().GetProperty("Title")?.GetValue(card)?.ToString() ?? "";
            string rarity    = card.GetType().GetProperty("Rarity")?.GetValue(card)?.ToString() ?? "";
            int maxUpgrade   = (int?)card.GetType().GetProperty("MaxUpgradeLevel")?.GetValue(card) ?? 1;
            string cardType  = card.GetType().GetProperty("CardType")?.GetValue(card)?.ToString() ?? "";
            int? cost        = TryGetIntProp(card, "Cost");

            return new
            {
                id                   = id,
                name_base            = baseTitle,
                name_upgraded        = $"{baseTitle}+",      // MaxUpgradeLevel>1 は STS2 に現状無し
                description_base     = descBase,
                description_upgraded = descUpgraded,
                rarity               = rarity,
                card_type            = cardType,
                cost                 = cost,
                max_upgrade          = maxUpgrade,
            };
        }
        catch (Exception ex) { Log.Error($"[StsStats][CatalogDumper] card entry error ({card?.GetType().Name}): {ex.Message}"); return null; }
    }

    private static object? BuildRelicEntry(object relic)
    {
        try
        {
            string id = GetIdEntry(relic);
            if (string.IsNullOrEmpty(id)) return null;
            string title = ResolveLoc(relic.GetType().GetProperty("Title")?.GetValue(relic));
            string desc  = ResolveDescription(relic);
            string rarity = relic.GetType().GetProperty("Rarity")?.GetValue(relic)?.ToString() ?? "";
            return new { id, name = title, description = desc, rarity };
        }
        catch (Exception ex) { Log.Error($"[StsStats][CatalogDumper] relic entry error ({relic?.GetType().Name}): {ex.Message}"); return null; }
    }

    private static object? BuildPotionEntry(object potion)
    {
        try
        {
            string id = GetIdEntry(potion);
            if (string.IsNullOrEmpty(id)) return null;
            string title = ResolveLoc(potion.GetType().GetProperty("Title")?.GetValue(potion));
            string desc  = ResolveDescription(potion);
            string rarity = potion.GetType().GetProperty("Rarity")?.GetValue(potion)?.ToString() ?? "";
            return new { id, name = title, description = desc, rarity };
        }
        catch (Exception ex) { Log.Error($"[StsStats][CatalogDumper] potion entry error ({potion?.GetType().Name}): {ex.Message}"); return null; }
    }

    private static object? BuildEnchantmentEntry(object ench)
    {
        try
        {
            string id = GetIdEntry(ench);
            if (string.IsNullOrEmpty(id)) return null;
            string title = ResolveLoc(ench.GetType().GetProperty("Title")?.GetValue(ench));
            string desc  = ResolveDescription(ench);
            return new { id, name = title, description = desc };
        }
        catch (Exception ex) { Log.Error($"[StsStats][CatalogDumper] enchantment entry error ({ench?.GetType().Name}): {ex.Message}"); return null; }
    }

    /// <summary>
    /// CardModel の description を解決する。CardModel.GetDescriptionForPile (private) と
    /// 同じ手順を replicate:
    ///   1. card.Description (private getter)
    ///   2. card.DynamicVars.AddTo(description)        ← Damage / Block / Cards 等 bind
    ///   3. description.Add(IfUpgradedVar(displayMode))← {IfUpgraded:show:A|B} の分岐
    ///   4. description.Add("OnTable", false), ("InCombat", false)  ← preview context
    ///   5. GetFormattedText()
    /// </summary>
    private static string ResolveCardDescriptionBase(object card, string upgradeDisplayName = "Normal")
    {
        try
        {
            var t = card.GetType();
            var descProp = t.GetProperty("Description",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var description = descProp?.GetValue(card);
            if (description == null) return "";

            // DynamicVars.AddTo(description) — Damage / Block / Cards 等 bind
            var dvars = t.GetProperty("DynamicVars")?.GetValue(card);
            dvars?.GetType().GetMethod("AddTo", new[] { description.GetType() })?.Invoke(dvars, new[] { description });

            // AddExtraArgsToDescription (private) を呼んでカード固有 var を bind
            // (BlackHolePower / FeralPower / DoomThreshold / energyPrefix / singleStarIcon 等)
            try
            {
                var addExtraArgs = t.GetMethod("AddExtraArgsToDescription",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { description.GetType() }, null);
                addExtraArgs?.Invoke(card, new[] { description });
            }
            catch { /* private 実装が無い card もあり得る */ }

            // IfUpgradedVar(UpgradeDisplay.{upgradeDisplayName}) を bind
            // IfUpgradedVar は ...Localization.DynamicVars / UpgradeDisplay は ...Localization (デコンパイル確認)
            var ifUpgradedVarType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Localization.DynamicVars.IfUpgradedVar")
                                 ?? AccessTools.TypeByName("IfUpgradedVar");
            var upgradeDisplayType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Localization.UpgradeDisplay")
                                  ?? AccessTools.TypeByName("UpgradeDisplay");
            if (ifUpgradedVarType != null && upgradeDisplayType != null)
            {
                var enumVal = Enum.Parse(upgradeDisplayType, upgradeDisplayName);
                var ctor = ifUpgradedVarType.GetConstructor(new[] { upgradeDisplayType });
                var ifUpgradedVar = ctor?.Invoke(new[] { enumVal });
                if (ifUpgradedVar != null)
                {
                    var addDynVar = description.GetType().GetMethod("Add", new[] { ifUpgradedVarType.BaseType ?? typeof(object) });
                    addDynVar?.Invoke(description, new[] { ifUpgradedVar });
                }
            }

            // 画像系 placeholder にプレースホルダ文字列を bind (web 側で絵文字レンダリング):
            //   energyPrefix:energyIcons(N) → "⚡" 表記。EnergyIconHelper.GetPrefix が本物だが
            //                                 reflection で呼ぶより固定文字で十分
            //   singleStarIcon              → "⭐"
            description.GetType().GetMethod("Add", new[] { typeof(string), typeof(string) })
                ?.Invoke(description, new object[] { "energyPrefix", "⚡" });
            description.GetType().GetMethod("Add", new[] { typeof(string), typeof(string) })
                ?.Invoke(description, new object[] { "singleStarIcon", "⭐" });

            // bool context vars
            description.GetType().GetMethod("Add", new[] { typeof(string), typeof(bool) })
                ?.Invoke(description, new object[] { "OnTable", false });
            description.GetType().GetMethod("Add", new[] { typeof(string), typeof(bool) })
                ?.Invoke(description, new object[] { "InCombat", false });

            return description.GetType().GetMethod("GetFormattedText", Type.EmptyTypes)
                ?.Invoke(description, null) as string ?? "";
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats][CatalogDumper] ResolveCardDescriptionBase error: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// ゲーム本体のアップグレードプレビュー実装と同じパターン:
    ///   var mutable = (CardModel)canonical.MutableClone();
    ///   mutable.UpgradePreviewType = CardUpgradePreviewType.Deck;
    ///   mutable.UpgradeInternal();
    /// → mutable.DynamicVars / Title / Description が upgraded 値を返す。
    /// </summary>
    private static string ResolveCardDescriptionUpgraded(object canonical)
    {
        try
        {
            // 1. IsUpgradable 判定: false なら upgrade 不要 → base と同じ説明
            bool isUpgradable = (bool?)canonical.GetType().GetProperty("IsUpgradable")?.GetValue(canonical) ?? false;
            if (!isUpgradable) return ResolveCardDescriptionBase(canonical);

            // 2. MutableClone() で mutable 複製を取る
            var mutableCloneMethod = canonical.GetType().GetMethod("MutableClone", Type.EmptyTypes);
            if (mutableCloneMethod == null) return ResolveCardDescriptionBase(canonical);
            var clone = mutableCloneMethod.Invoke(canonical, null);
            if (clone == null) return ResolveCardDescriptionBase(canonical);

            // 3. UpgradePreviewType を Deck に設定 (CardUpgradePreviewType enum)
            try
            {
                // CardUpgradePreviewType は MegaCrit.Sts2.Core.Entities.Cards namespace (デコンパイル確認)
                var previewTypeEnum = AccessTools.TypeByName("MegaCrit.Sts2.Core.Entities.Cards.CardUpgradePreviewType")
                                    ?? AccessTools.TypeByName("CardUpgradePreviewType");
                if (previewTypeEnum != null)
                {
                    var deckVal = Enum.Parse(previewTypeEnum, "Deck");
                    clone.GetType().GetProperty("UpgradePreviewType")?.SetValue(clone, deckVal);
                }
            }
            catch { /* fallback: skip preview type */ }

            // 4. UpgradeInternal() を呼ぶ (CurrentUpgradeLevel が +1 になり、Damage 等の dynamic var が更新)
            clone.GetType().GetMethod("UpgradeInternal", Type.EmptyTypes)?.Invoke(clone, null);

            // 5. clone から description を取る (UpgradeDisplay.Upgraded で IfUpgraded 分岐を upgraded ブランチに)
            return ResolveCardDescriptionBase(clone, "Upgraded");
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats][CatalogDumper] ResolveCardDescriptionUpgraded error: {ex.Message}");
            return "";
        }
    }

    private static string? TryCallStringMethod(object instance, string methodName)
    {
        try
        {
            var m = instance.GetType().GetMethod(methodName, Type.EmptyTypes);
            return m?.Invoke(instance, null) as string;
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats][CatalogDumper] {instance.GetType().Name}.{methodName}() error: {ex.Message}");
            return null;
        }
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
                var m = t.GetMethod(name, Type.EmptyTypes);
                if (m == null) continue;
                var v = m.Invoke(loc, null) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }
        catch { }
        return "";
    }

    private static string ResolveDescription(object model)
    {
        // 1. public DynamicDescription (Relic/Potion/Enchantment は内部で DynamicVars を bind 済)
        try
        {
            var dyn = model.GetType().GetProperty("DynamicDescription")?.GetValue(model);
            var s = ResolveLoc(dyn);
            if (!string.IsNullOrEmpty(s)) return s;
        }
        catch { }
        // 2. private Description フォールバック
        try
        {
            var prop = model.GetType().GetProperty("Description",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return ResolveLoc(prop?.GetValue(model));
        }
        catch { return ""; }
    }

    private static int? TryGetIntProp(object o, string name)
    {
        try
        {
            var v = o.GetType().GetProperty(name)?.GetValue(o);
            if (v == null) return null;
            if (v is int i) return i;
            return Convert.ToInt32(v);
        }
        catch { return null; }
    }

    private static string? TryGetCurrentLocale()
    {
        // 注: Godot.TranslationServer.GetLocale() は mod Initialize 早期だと未初期化で
        // TargetInvocationException を投げて DumpOnce 全体を失敗させる。
        // catalog ファイル名に lang 識別が必要だが、ゲーム本体の locale を信頼するより
        // 「ユーザが make dump-catalog LANG=xx で指定する」運用にしたほうが安全。
        // ここでは null を返す (catalog 内 lang フィールドは null になる)。
        // make dump-catalog 側で web/public/catalog.{LANG}.json にコピーする際に
        // 文字列を上書きするのが綺麗だが、当面は null で運用問題なし。
        return null;
    }

    private static string GetDumpDir()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var modDir = Path.GetDirectoryName(asm.Location) ?? "";
            return modDir;
        }
        catch { return Path.GetTempPath(); }
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
