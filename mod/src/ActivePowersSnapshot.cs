using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;

namespace StsStats;

/// <summary>
/// ダメージの event に、攻撃した側・受けた側に付いている主なパワーとその付与者を添える (戦闘詳細のタイムライン表示用)。
/// 貢献スコアは補正 1 つずつの内訳 (modifications) から計算するので、ここは表示にしか使わない。
/// 付与者は PowerOriginRegistry から引く。
/// </summary>
internal static class ActivePowersSnapshot
{
    /// <summary>タイムラインに出すパワー。1 回のダメージの event が大きくなりすぎないよう、よく効くものに絞る。</summary>
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
            foreach (var p in c.Powers)
            {
                string powerId = p.Id.Entry;
                if (!Whitelist.Contains(powerId)) continue;

                int stacks = p.Amount;
                if (stacks == 0) continue;

                var appliers = PowerOriginRegistry.LookupAll(c, powerId);
                string? powerName = PowerNameResolver.Resolve(p);

                // PayloadJson は SnakeCaseLower で property 名を変換するため、PascalCase にしておく
                result.Add(new
                {
                    PowerId   = powerId,
                    PowerName = powerName,
                    Stacks    = stacks,
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
