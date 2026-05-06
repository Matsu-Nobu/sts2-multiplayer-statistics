using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace StsStats;

/// <summary>
/// 「creature C に乗っている power P の各 applier_player_id ごとの stacks 量」を記録する。
///
/// AfterPowerAmountChanged で:
///   - applier がプレイヤーで delta != 0 → その applier の stacks に加算（正負どちらも反映）
///   - applier 不明 + delta &lt; 0（自然減衰等）→ 既存 applier 全員から比例配分で減算（RecordDecay）
///
/// 1 entry の stacks 値は applier がもたらした正味量。STRENGTH のように負に振る power は
/// 各 applier ごとの符号付き値を保持し、|stacks| 比で按分する想定。
///
/// AfterDamageGiven 等で active_on_target / active_on_dealer を作る際に
/// LookupAll で「(applier, stacks) のリスト」を取り、stacks 加重で帰属させる。
///
/// Power の発火は通常ゲームのメインスレッドだが、Harmony patch 経由で AsyncLocal 越しに
/// 別スレッドへ伝播するケースを考慮し、_lock で同期する。
/// </summary>
internal static class PowerOriginRegistry
{
    private static readonly Dictionary<(Creature, string), Dictionary<string, int>> _origin =
        new(new CreaturePowerKeyComparer());

    private static readonly object _lock = new();

    /// <summary>
    /// applier が delta 分の stacks を変動させた。delta は正負どちらでも可。
    /// VULN/WEAK のような正のみ伸びる power でも、上書き処理で負 delta が来ることがある。
    /// STRENGTH のような正負どちらも取りうる power でも安全に積める。
    /// </summary>
    public static void RecordApply(Creature creature, string powerId, string applierPlayerId, int delta)
    {
        if (delta == 0) return;
        lock (_lock)
        {
            var key = (creature, powerId);
            if (!_origin.TryGetValue(key, out var dict))
            {
                dict = new Dictionary<string, int>();
                _origin[key] = dict;
            }
            int prev = dict.TryGetValue(applierPlayerId, out var v) ? v : 0;
            int next = prev + delta;
            if (next == 0) dict.Remove(applierPlayerId);
            else dict[applierPlayerId] = next;
            if (dict.Count == 0) _origin.Remove(key);
        }
    }

    /// <summary>
    /// applier 不明な減衰（VULN/WEAK のターン頭 -1 等）。
    /// 既存 applier 全員から |stacks| 比で配分減算する。
    /// </summary>
    public static void RecordDecay(Creature creature, string powerId, int delta)
    {
        if (delta >= 0) return;
        lock (_lock)
        {
            if (!_origin.TryGetValue((creature, powerId), out var dict)) return;
            int reduction = -delta;
            int totalAbs = dict.Values.Sum(v => System.Math.Abs(v));
            if (totalAbs <= 0) { _origin.Remove((creature, powerId)); return; }
            if (reduction >= totalAbs) { _origin.Remove((creature, powerId)); return; }

            // 整数誤差は最後の applier に押し付ける
            var keys = dict.Keys.ToList();
            int distributed = 0;
            for (int i = 0; i < keys.Count - 1; i++)
            {
                int absV = System.Math.Abs(dict[keys[i]]);
                int share = (int)((long)reduction * absV / totalAbs);
                int sign = System.Math.Sign(dict[keys[i]]);
                dict[keys[i]] -= share * sign;
                distributed += share;
            }
            int lastSign = System.Math.Sign(dict[keys[^1]]);
            dict[keys[^1]] -= (reduction - distributed) * lastSign;

            // 0 になったエントリを掃除
            foreach (var k in dict.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList())
                dict.Remove(k);
            if (dict.Count == 0) _origin.Remove((creature, powerId));
        }
    }

    /// <summary>(applier_player_id, stacks) のリストを返す。stacks は符号付き。</summary>
    public static List<(string Applier, int Stacks)> LookupAll(Creature creature, string powerId)
    {
        lock (_lock)
        {
            if (!_origin.TryGetValue((creature, powerId), out var dict)) return new();
            return dict.Where(kv => kv.Value != 0)
                       .Select(kv => (kv.Key, kv.Value))
                       .ToList();
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
