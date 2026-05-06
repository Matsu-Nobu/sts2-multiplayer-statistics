using System;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Logging;

namespace StsStats;

/// <summary>
/// PowerModel から表示名（ローカライズ済み）を reflection で抽出する。
/// CardModel.Title 相当のプロパティを推測で順に試す。
/// 返り値が <c>LocString</c> 等のオブジェクトの場合はさらにそこから
/// 文字列を引き出す（Text / Name / Value / Localized / GetText() など）。
/// 何も取れなければ null（呼び出し側で power_id にフォールバック）。
/// </summary>
internal static class PowerNameResolver
{
    private static readonly string[] CandidateProps =
    {
        "Title",
        "Name",
        "DisplayName",
        "LocalizedName",
    };

    private static readonly string[] LocStringProps =
    {
        "Text", "LocalizedText", "Localized", "Value", "String", "Name",
    };

    private static readonly string[] LocStringMethods =
    {
        // STS2 の LocString が持っている確認済みメソッド（高優先）
        "GetFormattedText", "GetRawText",
        // 念のためのフォールバック候補
        "GetText", "GetLocalized", "GetLocalizedText", "ToLocalizedString",
        "Localize", "Resolve", "Format",
    };

    private static bool _logged;

    public static string? Resolve(object? power)
    {
        if (power == null) return null;
        try
        {
            var t = power.GetType();
            foreach (var name in CandidateProps)
            {
                var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (prop == null) continue;
                var raw = prop.GetValue(power);
                if (raw == null) continue;

                string? str = ExtractString(raw);
                if (!string.IsNullOrEmpty(str))
                {
                    LogOnce($"using PowerModel.{name} → {raw.GetType().Name} → \"{str}\"");
                    return str;
                }
            }

            // 最後の手段: 型の全プロパティを舐めて LocString らしきものを探す
            foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.PropertyType.Name.Contains("LocString")) continue;
                var raw = prop.GetValue(power);
                string? str = ExtractString(raw);
                if (!string.IsNullOrEmpty(str))
                {
                    LogOnce($"using PowerModel.{prop.Name} (LocString) → \"{str}\"");
                    return str;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] PowerNameResolver error: {ex.Message}");
        }
        return null;
    }

    private static string? ExtractString(object? raw)
    {
        if (raw == null) return null;
        if (raw is string s) return s;

        var rt = raw.GetType();

        // 1. プロパティ
        foreach (var name in LocStringProps)
        {
            var p = rt.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p?.PropertyType == typeof(string))
            {
                var v = p.GetValue(raw) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }

        // 2. メソッド（引数なし）
        foreach (var name in LocStringMethods)
        {
            var m = rt.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, binder: null, types: Type.EmptyTypes, modifiers: null);
            if (m?.ReturnType == typeof(string))
            {
                try
                {
                    var v = m.Invoke(raw, null) as string;
                    if (!string.IsNullOrEmpty(v)) return v;
                }
                catch { /* 続行 */ }
            }
        }

        // 3. ToString が型名と異なる文字列を返すなら採用
        var ts = raw.ToString();
        if (!string.IsNullOrEmpty(ts) && !ts.StartsWith("MegaCrit.")) return ts;

        // 4. 失敗時、型の構造をログに出して調査の手掛かりに（1度だけ）
        DumpTypeOnce(rt);
        return null;
    }

    private static bool _dumped;
    private static void DumpTypeOnce(Type t)
    {
        if (_dumped) return;
        _dumped = true;
        try
        {
            var props = string.Join(", ", t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => $"{p.PropertyType.Name} {p.Name}"));
            var methods = string.Join(", ", t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => !m.IsSpecialName && m.GetParameters().Length == 0 && m.ReturnType == typeof(string))
                .Select(m => m.Name));
            Log.Info($"[StsStats] LocString-like type {t.FullName}: props=[{props}] string-methods=[{methods}]");
        }
        catch { }
    }

    private static void LogOnce(string msg)
    {
        if (_logged) return;
        _logged = true;
        Log.Info($"[StsStats] PowerNameResolver: {msg}");
    }
}
