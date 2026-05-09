using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Models;

namespace StsStats;

/// <summary>
/// Hook.ModifyDamage の Postfix で観測した「pre/post/関与 modifier」を記録するスレッドローカルログ。
///
/// AfterDamageGiven の中で <see cref="Drain"/> して payload に含める。
/// pre/post の差分があった呼び出しのみ記録する。
///
/// ゲームのダメージ計算は (Additive→Multiplicative→Cap) の 3 フェーズを 1 回の
/// ModifyDamage 呼び出しで処理しうる（modifyDamageHookType.HasFlag(...) で各パスが流れる）。
/// 個別フェーズの分離精度は 1 phase 1 modifier なら完璧、複数 modifier が同フェーズに
/// 居るときは modifiers 一覧と stacks 比で按分する近似になる。これは web 側で扱う。
/// </summary>
internal static class DamageModificationLog
{
    public sealed record Entry(
        decimal Pre,
        decimal Post,
        List<string> ModifierTypes,    // 例: "VulnerablePower", "PenNibRelic"
        List<string> ModifierIds       // 例: "VULNERABLE_POWER" or relic id（取れない場合は空文字）
    );

    [System.ThreadStatic]
    private static List<Entry>? _log;

    // 「直近 ModifyDamage の最終 post 値」を保持。pre==post で diff 記録を
    // スキップするケースでも常に更新する。AfterDamageGiven で
    // 「pre-block・pre-HP-cap の試行 damage」として使い、overkill 計算に流す。
    [System.ThreadStatic]
    private static decimal _lastPost;
    [System.ThreadStatic]
    private static bool _hasLastPost;

    public static void Record(decimal pre, decimal post, IEnumerable<AbstractModel>? modifiers)
    {
        // 最終 post を必ず更新
        _lastPost = post;
        _hasLastPost = true;

        if (pre == post) return;
        var (types, ids) = ExtractIdentities(modifiers);
        _log ??= new List<Entry>();
        _log.Add(new Entry(pre, post, types, ids));
    }

    /// <summary>直近 ModifyDamage の post を取得し、状態をクリア。</summary>
    public static decimal? TakeLastPost()
    {
        if (!_hasLastPost) return null;
        var v = _lastPost;
        _hasLastPost = false;
        return v;
    }

    public static List<Entry> Drain()
    {
        if (_log == null) return new();
        var snapshot = _log;
        _log = null;
        return snapshot;
    }

    public static void Clear() { _log = null; _hasLastPost = false; }

    private static (List<string> types, List<string> ids) ExtractIdentities(IEnumerable<AbstractModel>? modifiers)
    {
        var types = new List<string>();
        var ids = new List<string>();
        if (modifiers == null) return (types, ids);
        foreach (var m in modifiers)
        {
            if (m == null) continue;
            types.Add(m.GetType().Name);
            // power_id / relic_id は m.Id.Entry に入っていることが多いので reflection で試す
            string id = "";
            try
            {
                var idObj = m.GetType().GetProperty("Id")?.GetValue(m);
                id = idObj?.GetType().GetProperty("Entry")?.GetValue(idObj)?.ToString() ?? "";
            }
            catch { }
            ids.Add(id);
        }
        return (types, ids);
    }
}
