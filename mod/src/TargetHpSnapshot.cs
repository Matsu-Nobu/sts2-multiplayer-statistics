using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace StsStats;

/// <summary>
/// 「これから被弾する creature の被弾前 HP」を BeforeDamageReceived 時点で snapshot し、
/// AfterDamageGiven で overkill を自前計算するためのバッファ。
///
/// STS2 の <c>DamageResult.OverkillDamage</c> は実測値が我々の期待する意味と
/// 一致しない（amount より大きい値が返る等）ため信用できない。代わりに
/// <c>overkill = max(0, amount - hp_before)</c> として導出する。
///
/// 本来は同期的な BeforeDamageReceived → AfterDamageGiven の 1:1 対応なので
/// AsyncLocal や lock は不要。ThreadStatic + Creature のオブジェクト identity で
/// 引く。多重ヒット中（ループ）も hit ごとに上書きされるので衝突しない。
/// </summary>
internal static class TargetHpSnapshot
{
    [System.ThreadStatic]
    private static Dictionary<int, int>? _snapshot;

    public static void Record(Creature target, int hpBefore)
    {
        _snapshot ??= new Dictionary<int, int>();
        _snapshot[RuntimeHelpers.GetHashCode(target)] = hpBefore;
    }

    public static int? Lookup(Creature target)
    {
        if (_snapshot == null) return null;
        return _snapshot.TryGetValue(RuntimeHelpers.GetHashCode(target), out var v) ? v : (int?)null;
    }

    public static void Clear(Creature target)
    {
        _snapshot?.Remove(RuntimeHelpers.GetHashCode(target));
    }
}
