using MegaCrit.Sts2.Core.Models;

namespace StsStats;

/// <summary>
/// 「今プレイ中のカード」(spec data-sources.md、api.md「triggered_by」)。
/// CardModel.OnPlayWrapper (カードのプレイ処理の本体。デコンパイル確認済 v0.111.0:
/// (PlayerChoiceContext, Creature?, bool isAutoPlay, ResourceInfo, bool skipCardPileVisuals)) の間だけ値を持つ。
/// AsyncLocal なので、プレイ処理から呼ばれた先 (毒の発動など) にだけ伝わる。
/// </summary>
internal static class CardPlayScope
{
    private static readonly System.Threading.AsyncLocal<CardModel?> _current = new();

    public static CardModel? Current => _current.Value;

    public static void Prefix(CardModel __instance, out CardModel? __state)
    {
        __state = _current.Value;
        _current.Value = __instance;
    }

    public static void Postfix(CardModel? __state)
    {
        _current.Value = __state;
    }
}
