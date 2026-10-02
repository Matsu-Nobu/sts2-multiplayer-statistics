using MegaCrit.Sts2.Core.Models;

namespace StsStats;

/// <summary>
/// 「今プレイ中のカード」(spec data-sources.md、api.md「triggered_by」)。
/// CardModel.OnPlayWrapper (カードのプレイ処理の本体。デコンパイル確認済 v0.111.0:
/// (PlayerChoiceContext, Creature?, bool isAutoPlay, ResourceInfo, bool skipCardPileVisuals)) の間だけ値を持つ。
/// AsyncLocal なので、プレイ処理から呼ばれた先 (毒の発動など) にだけ伝わる。
/// プレイの間は SourceContext の実行中モデルを空から始める (パワー等がカードを自動プレイした場合も、カードの効果はカードのもの)。
/// </summary>
internal static class CardPlayScope
{
    private static readonly System.Threading.AsyncLocal<CardModel?> _current = new();

    public static CardModel? Current => _current.Value;

    internal sealed class State { public CardModel? Card; public (SourceContext.Frame? Frame, bool InHook, bool InChannel) Saved; }

    public static void Prefix(CardModel __instance, out State __state)
    {
        __state = new State { Card = _current.Value, Saved = SourceContext.EnterCardPlay() };
        _current.Value = __instance;
    }

    public static void Postfix(State __state)
    {
        _current.Value = __state.Card;
        SourceContext.ExitCardPlay(__state.Saved);
    }
}
