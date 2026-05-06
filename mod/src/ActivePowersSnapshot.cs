using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;

namespace StsStats;

/// <summary>
/// damage_dealt event 発火時に「dealer / target に乗っている rDPS 関連 power」を
/// snapshot する。whitelist で絞っているので 1 イベントに乗る要素は通常 0〜2 個。
///
/// applier は PowerOriginRegistry から逆引き。passive / 不明は null。
/// </summary>
internal static class ActivePowersSnapshot
{
    /// <summary>
    /// rDPS 計算で意味のある power のみ snapshot する。それ以外は無視。
    /// 拡張は ROADMAP / PHASE35_PLAN に従って行う。
    /// </summary>
    private static readonly HashSet<string> Whitelist = new()
    {
        // 与ダメ攻撃強化 / 間接ダメ
        "VULNERABLE_POWER",
        "POISON_POWER",
        "DOOM_POWER",
        // 被ダメ軽減（dealer に乗ってると被ダメが減る）
        "WEAK_POWER",
        "STRENGTH_POWER",     // 負の stacks のときに軽減として扱う（rMit 側で判定）
    };

    public static List<object> ForCreature(Creature? c)
    {
        var result = new List<object>();
        if (c == null) return result;
        try
        {
            var powers = c.GetType().GetProperty("Powers")?.GetValue(c) as IEnumerable;
            if (powers == null) return result;

            foreach (var p in powers)
            {
                var idObj = p.GetType().GetProperty("Id")?.GetValue(p);
                string? powerId = idObj?.GetType().GetProperty("Entry")?.GetValue(idObj)?.ToString();
                if (powerId == null || !Whitelist.Contains(powerId)) continue;

                int stacks = (int?)p.GetType().GetProperty("Amount")?.GetValue(p) ?? 0;
                if (stacks == 0) continue;

                var appliers = PowerOriginRegistry.LookupAll(c, powerId);
                string? powerName = PowerNameResolver.Resolve(p);

                // 後方互換: 単一 applier 想定の旧フィールドは「最大 stacks の applier」を入れる
                string? primaryApplier = appliers.Count == 0
                    ? null
                    : appliers.OrderByDescending(a => a.Stacks).First().Applier;

                // PayloadJson は SnakeCaseLower で property 名を変換するため、PascalCase にしておく
                result.Add(new
                {
                    PowerId   = powerId,
                    PowerName = powerName,
                    Stacks    = stacks,
                    Applier   = primaryApplier,
                    Appliers  = appliers.Select(a => new { PlayerId = a.Applier, Stacks = a.Stacks }).ToList(),
                });
            }
        }
        catch (System.Exception ex)
        {
            Log.Error($"[StsStats] ActivePowersSnapshot error: {ex.Message}");
        }
        return result;
    }
}
