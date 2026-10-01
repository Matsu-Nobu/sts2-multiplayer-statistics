namespace StsStats;

/// <summary>
/// カード情報の最小レコード（ID / 表示名 / カード種別）。
/// AfterCardPlayed や AfterDamageGiven 等で CardModel から抽出して使う。
///
/// カードが無いダメージ・ブロック (毒・オーブ・レリック等) は SourceContext が
/// 実行中モデルの ID・名前・種類 (Power / Relic / Orb …) をこの形で返す。
/// </summary>
internal record CardInfo(string CardId, string CardName, string CardType);
