using System;

namespace StsStats;

/// <summary>
/// 「いまカードプレイの最中か」を追跡するスレッドローカル深さスコープ。
///
/// オーブの Evoke が「カード起因の手動発動 (Zap 等)」か「ターン終了時の自動発動」
/// かを区別するために使う。BeforeCardPlayed で Enter、AfterCardPlayed の finally で Exit。
///
/// AsyncLocal にすると Harmony patch をまたぐ非同期文脈境界で意図せず flow しないことが
/// あるため、ゲームロジックが事実上シングルスレッドで回ることを利用して
/// [ThreadStatic] による単純な int カウンタを採用している。多重カードプレイ
/// （Burst 等）に備えて深さで管理する。
/// </summary>
internal static class CardPlayedScope
{
    [ThreadStatic]
    private static int _depth;

    public static bool Active => _depth > 0;
    public static void Enter() => _depth++;
    public static void Exit()  => _depth = Math.Max(0, _depth - 1);
}
