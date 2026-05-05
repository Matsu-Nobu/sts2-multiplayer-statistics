using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace StsStats;

/// <summary>
/// 「creature C に乗っている power P を最後に付与した applier_player_id」を記録する。
///
/// AfterPowerAmountChanged が「applier がプレイヤー」のとき呼ばれた時点で
/// (creature, power_id) をキーに player_id を登録しておき、後段の
/// AfterDamageGiven で active_on_target / active_on_dealer を作る際に
/// この registry を引いて applier を埋める。
///
/// applier が不明（passive / レリック / 敵→敵）な場合は登録しない → Lookup は null を返す
/// → rDPS 計算では除外される（仕様）。
/// </summary>
internal static class PowerOriginRegistry
{
    // (creature instance reference, power_id_string) → applier_player_id
    // Creature 自身を辞書キーに使うため ReferenceEqualityComparer を使う
    private static readonly Dictionary<(Creature, string), string> _origin =
        new(new CreaturePowerKeyComparer());

    public static void Record(Creature creature, string powerId, string applierPlayerId)
    {
        _origin[(creature, powerId)] = applierPlayerId;
    }

    public static string? Lookup(Creature creature, string powerId)
        => _origin.TryGetValue((creature, powerId), out var p) ? p : null;

    public static void ClearForCombat() => _origin.Clear();

    private sealed class CreaturePowerKeyComparer : IEqualityComparer<(Creature, string)>
    {
        public bool Equals((Creature, string) x, (Creature, string) y)
            => ReferenceEquals(x.Item1, y.Item1) && x.Item2 == y.Item2;
        public int GetHashCode((Creature, string) k)
            => System.HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k.Item1), k.Item2);
    }
}
