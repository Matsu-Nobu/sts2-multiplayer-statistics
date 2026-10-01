using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace StsStats;

/// <summary>
/// 「誰が付けたブロックが、どれだけ残っているか」を creature ごとに記録する (rMit 用、spec combat-stats.md §3.5)。
///
///   ブロック獲得 (Hook.AfterBlockGained)  → 付けた人の分に加える
///   被弾 (Hook.AfterDamageGiven)          → 防いだ量を、残っている量の比で各人から引く
///   ブロック消滅 (Hook.AfterBlockCleared) → 全部消す
///
/// ゲームはブロックを 1 つの数字でしか持っていないので、内訳は比例按分による近似。
/// ブロックがほかの理由で減った場合 (敵の効果等) も、被弾時に実際の残量に合わせて縮める。
/// </summary>
internal static class BlockLedger
{
    private static readonly ConditionalWeakTable<Creature, Dictionary<string, decimal>> _ledger = new();
    private static readonly object _lock = new();

    public static void Add(Creature creature, string giverId, decimal amount)
    {
        if (amount <= 0m) return;
        lock (_lock)
        {
            var d = _ledger.GetOrCreateValue(creature);
            d[giverId] = (d.TryGetValue(giverId, out var v) ? v : 0m) + amount;
        }
    }

    public static void Clear(Creature creature)
    {
        lock (_lock) _ledger.Remove(creature);
    }

    /// <summary>
    /// 被弾時。<paramref name="blocked"/> = このヒットで防いだ量、<paramref name="remaining"/> = 被弾後に残ったブロック。
    /// 防いだ量を付けた人ごとに返す。内訳が無ければ (観測できなかったブロック) <paramref name="fallbackId"/> の分にする。
    /// </summary>
    public static List<(string PlayerId, int Amount)> Consume(Creature creature, int blocked, int remaining, string fallbackId)
    {
        var result = new List<(string, int)>();
        if (blocked <= 0) return result;
        lock (_lock)
        {
            var d = _ledger.GetOrCreateValue(creature);
            decimal before = blocked + Math.Max(0, remaining);
            decimal sum = d.Values.Sum();
            if (sum <= 0m) { d.Clear(); d[fallbackId] = before; sum = before; }
            // 実際の残量 (被弾前 = 防いだ量 + 残り) に合わせて縮める (ほかの理由でブロックが減っていた場合)
            if (sum > before) foreach (var k in d.Keys.ToList()) d[k] = d[k] * before / sum;
            else if (sum < before) d[fallbackId] = (d.TryGetValue(fallbackId, out var v) ? v : 0m) + (before - sum);

            // 防いだ量を残量の比で配る (整数。端数は多い順に 1 ずつ)
            var keys = d.Keys.ToList();
            var shares = keys.Select(k => (k, raw: blocked * d[k] / before)).ToList();
            var ints = shares.Select(s => (s.k, n: (int)Math.Floor(s.raw), rem: s.raw - Math.Floor(s.raw))).ToList();
            int left = blocked - ints.Sum(x => x.n);
            foreach (var x in ints.OrderByDescending(x => x.rem).ToList())
            {
                if (left <= 0) break;
                int i = ints.FindIndex(y => y.k == x.k);
                ints[i] = (x.k, x.n + 1, x.rem);
                left--;
            }
            foreach (var x in ints)
            {
                if (x.n > 0) result.Add((x.k, x.n));
                d[x.k] = Math.Max(0m, d[x.k] - x.n);
            }
            foreach (var k in d.Where(kv => kv.Value <= 0m).Select(kv => kv.Key).ToList()) d.Remove(k);
        }
        return result;
    }
}
