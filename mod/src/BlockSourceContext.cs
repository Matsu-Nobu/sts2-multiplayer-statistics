using System.Threading;

namespace StsStats;

/// <summary>
/// 「いま実行中のブロック発生源（power や relic 由来）」を AsyncLocal で伝搬する。
///
/// <c>Hook.AfterBlockGained</c> の <c>cardSource</c> は CardModel? なので、
/// RampartPower の AfterSideTurnStart や MockGainBlockOnAttackPower の AfterAttack
/// 等から発生する block は cardSource = null となり、出処が分からない。
/// 該当 power の Harmony patch で Prefix に Push、Postfix で Restore することで
/// <see cref="HookPatches.AfterBlockGainedPostfix"/> 内から source を参照できる。
/// </summary>
internal static class BlockSourceContext
{
    private static readonly AsyncLocal<CardInfo?> _current = new();

    public static CardInfo? Current => _current.Value;

    public static CardInfo? Push(CardInfo info)
    {
        var prev = _current.Value;
        _current.Value = info;
        return prev;
    }

    public static void Restore(CardInfo? previous) => _current.Value = previous;
}
