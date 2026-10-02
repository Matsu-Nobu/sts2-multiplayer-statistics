using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace StsStats;

/// <summary>パワーのスタックを付けた持ち物 (カード・レリック・ポーション等)。api.md「origin」。</summary>
internal sealed record Origin(string Id, string Name, string Type, string Kind);

/// <summary>
/// 「creature C に乗っている power P のスタックを、誰が・何で付けたか」の内訳を記録する。
///
/// AfterPowerAmountChanged で:
///   - 付けた人が分かる (delta != 0) → (その人, 付けた持ち物) の stacks に加算 (正負どちらも反映)
///   - 付けた人が分からない減少 (自然減衰等) → 今ある内訳の全員から比例で減算 (RecordDecay)
///
/// 1 entry の stacks は符号付き。STRENGTH のように負に振る power も |stacks| 比で按分する。
/// LookupAll は人ごとの合計 (rDPS / rMit 用)、LookupOrigins は (人, 持ち物) ごと (カード別の表用)。
/// </summary>
internal static class PowerOriginRegistry
{
    private sealed class Entry { public string Applier = ""; public Origin? Origin; public int Stacks; }

    private static readonly Dictionary<(Creature, string), Dictionary<string, Entry>> _origin =
        new(new CreaturePowerKeyComparer());

    private static readonly object _lock = new();

    private static string Key(string applier, Origin? o) => applier + "\u001f" + (o?.Id ?? "");

    public static void RecordApply(Creature creature, string powerId, string applierPlayerId, Origin? origin, int delta)
    {
        if (delta == 0) return;
        lock (_lock)
        {
            var key = (creature, powerId);
            if (!_origin.TryGetValue(key, out var dict)) { dict = new(); _origin[key] = dict; }
            string k = Key(applierPlayerId, origin);
            if (!dict.TryGetValue(k, out var e))
            {
                // 付けた人の既存分を減らす操作で、同じ持ち物の分が無ければ、その人の分から比例で減らす
                var mine = dict.Values.Where(x => x.Applier == applierPlayerId && x.Stacks > 0).ToList();
                if (delta < 0 && mine.Count > 0) { Shrink(mine, -delta); Clean(key, dict); return; }
                e = new Entry { Applier = applierPlayerId, Origin = origin };
                dict[k] = e;
            }
            e.Stacks += delta;
            Clean(key, dict);
        }
    }

    /// <summary>付けた人が分からない減少 (自然減衰等)。今ある内訳の全員から |stacks| 比で減らす。</summary>
    public static void RecordDecay(Creature creature, string powerId, int delta)
    {
        if (delta >= 0) return;
        lock (_lock)
        {
            var key = (creature, powerId);
            if (!_origin.TryGetValue(key, out var dict)) return;
            Shrink(dict.Values.ToList(), -delta);
            Clean(key, dict);
        }
    }

    private static void Shrink(List<Entry> entries, int reduction)
    {
        int totalAbs = entries.Sum(x => System.Math.Abs(x.Stacks));
        if (totalAbs <= 0) return;
        if (reduction >= totalAbs) { foreach (var x in entries) x.Stacks = 0; return; }
        int distributed = 0;
        for (int i = 0; i < entries.Count - 1; i++)
        {
            int share = (int)((long)reduction * System.Math.Abs(entries[i].Stacks) / totalAbs);
            entries[i].Stacks -= share * System.Math.Sign(entries[i].Stacks);
            distributed += share;
        }
        var last = entries[^1];
        last.Stacks -= (reduction - distributed) * System.Math.Sign(last.Stacks);
    }

    private static void Clean((Creature, string) key, Dictionary<string, Entry> dict)
    {
        foreach (var k in dict.Where(kv => kv.Value.Stacks == 0).Select(kv => kv.Key).ToList()) dict.Remove(k);
        if (dict.Count == 0) _origin.Remove(key);
    }

    /// <summary>(付けた人, stacks) のリスト。人ごとに合計。stacks は符号付き。</summary>
    public static List<(string Applier, int Stacks)> LookupAll(Creature creature, string powerId)
    {
        lock (_lock)
        {
            if (!_origin.TryGetValue((creature, powerId), out var dict)) return new();
            return dict.Values.GroupBy(e => e.Applier)
                       .Select(g => (g.Key, g.Sum(e => e.Stacks)))
                       .Where(x => x.Item2 != 0)
                       .ToList();
        }
    }

    /// <summary>(付けた人, 付けた持ち物, stacks) のリスト。持ち物が分からない分は Origin = null。</summary>
    public static List<(string Applier, Origin? Origin, int Stacks)> LookupOrigins(Creature creature, string powerId)
    {
        lock (_lock)
        {
            if (!_origin.TryGetValue((creature, powerId), out var dict)) return new();
            return dict.Values.Where(e => e.Stacks != 0).Select(e => (e.Applier, e.Origin, e.Stacks)).ToList();
        }
    }

    public static void ClearForCombat()
    {
        lock (_lock) _origin.Clear();
    }

    private sealed class CreaturePowerKeyComparer : IEqualityComparer<(Creature, string)>
    {
        public bool Equals((Creature, string) x, (Creature, string) y)
            => ReferenceEquals(x.Item1, y.Item1) && x.Item2 == y.Item2;
        public int GetHashCode((Creature, string) k)
            => System.HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k.Item1), k.Item2);
    }
}
