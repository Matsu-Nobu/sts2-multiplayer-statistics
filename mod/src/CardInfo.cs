namespace StsStats;

/// <summary>
/// カード情報の最小レコード（ID / 表示名 / カード種別）。
/// AfterCardPlayed や AfterDamageGiven 等で CardModel から抽出して使う。
///
/// 間接ダメージ（Poison / Doom / Lightning）は CardModel が無いので、
/// IndirectDamagePatches に予約 CardInfo（card_id="(poison)"等）を置いて
/// DamageSourceContext 経由で参照する。
/// </summary>
internal record CardInfo(string CardId, string CardName, string CardType);
