using System.Threading;

namespace StsStats;

/// <summary>
/// 「いま実行中の間接ダメージソース」を AsyncLocal で伝搬するコンテキスト。
///
/// 公式の <c>Hook.AfterDamageGiven</c> は <c>cardSource: CardModel?</c> しか
/// 持たないため、Lightning Orb の Evoke/Passive、Poison の tick、Doom 等は
/// すべて null となり区別できない。これらは「ゲーム内部の特定メソッドが
/// 実行中である」というコンテキストでのみ識別可能。
///
/// Harmony で対象メソッド（例: <c>LightningOrb.Evoke</c>）を Prefix patch で
/// この AsyncLocal に値を入れ、Postfix patch で元の値に戻す。
/// AsyncLocal は <c>await</c> 境界を跨いで継続コンテキストに引き継がれるため、
/// 非同期のダメージ適用にも追従する。
///
/// <see cref="HookPatches.AfterDamageGivenPostfix"/> 内で <c>cardSource == null</c>
/// のときに <see cref="Current"/> を参照して fallback CardInfo として使う。
/// </summary>
internal static class DamageSourceContext
{
    private static readonly AsyncLocal<CardInfo?> _current = new();

    /// <summary>現在のソース（または null）。</summary>
    public static CardInfo? Current => _current.Value;

    /// <summary>新しいソースをセットし、以前の値を返す（Postfix で復元するために使う）。</summary>
    public static CardInfo? Push(CardInfo info)
    {
        var prev = _current.Value;
        _current.Value = info;
        return prev;
    }

    /// <summary>以前の値に戻す（典型的には Push の戻り値を渡す）。</summary>
    public static void Restore(CardInfo? previous) => _current.Value = previous;
}
