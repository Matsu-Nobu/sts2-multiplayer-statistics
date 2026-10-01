using System;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace StsStats;

/// <summary>
/// ゲームのモデル (カード・レリック・ポーション等) を、送信用の ID・名前・レアリティに変換する。
/// ゲームの階ごとの記録は ID しか持たないので、送信時に ModelDb から名前を引いて付ける
/// (web がカタログ無しでもチップを描けるようにするため。docs/redesign-v2.md §2.3)。
/// </summary>
internal static class ModelInfo
{
    public static string Text(LocString? s)
    {
        if (s == null) return "";
        try { return s.GetFormattedText() ?? ""; }
        catch
        {
            try { return s.GetRawText() ?? ""; }
            catch { return ""; }
        }
    }

    public static string Id(ModelId? id) => id?.Entry ?? "";

    // === カード ================================================================

    public static object Card(CardModel card) => new
    {
        id             = card.Id.Entry,
        name           = SafeTitle(card),
        rarity         = card.Rarity.ToString(),
        type           = card.Type.ToString(),
        upgrade_level  = card.CurrentUpgradeLevel,
        enchantment_id = card.Enchantment?.Id.Entry,
    };

    /// <summary>
    /// 記録上のカード (SerializableCard) を送信形式に。名前とレアリティは正規のカード定義から引き、
    /// 強化段階に応じて CardModel.Title と同じ規則で「+」「+N」を付ける。
    /// </summary>
    public static object Card(SerializableCard? card)
    {
        if (card?.Id == null) return new { id = "", name = "", rarity = "", type = "", upgrade_level = 0 };
        var canonical = Lookup<CardModel>(card.Id);
        string name = canonical != null ? SafeTitle(canonical) : card.Id.Entry;
        if (canonical != null && card.CurrentUpgradeLevel > 0)
            name = canonical.MaxUpgradeLevel > 1 ? $"{name}+{card.CurrentUpgradeLevel}" : $"{name}+";
        return new
        {
            id             = card.Id.Entry,
            name           = name,
            rarity         = canonical?.Rarity.ToString() ?? "",
            type           = canonical?.Type.ToString() ?? "",
            upgrade_level  = card.CurrentUpgradeLevel,
            enchantment_id = card.Enchantment?.Id?.Entry,
        };
    }

    /// <summary>ID だけで記録されたカード (強化・ダウングレード一覧)。</summary>
    public static object CardRef(ModelId id)
    {
        var canonical = Lookup<CardModel>(id);
        return new
        {
            id     = id.Entry,
            name   = canonical != null ? SafeTitle(canonical) : id.Entry,
            rarity = canonical?.Rarity.ToString() ?? "",
            type   = canonical?.Type.ToString() ?? "",
        };
    }

    // === その他のモデル ==========================================================

    public static object Relic(ModelId id, bool? wasPicked = null)
    {
        var m = Lookup<RelicModel>(id);
        return new { id = id.Entry, name = m != null ? Text(m.Title) : id.Entry, rarity = m?.Rarity.ToString() ?? "", was_picked = wasPicked };
    }

    public static object Potion(ModelId id, bool? wasPicked = null)
    {
        var m = Lookup<PotionModel>(id);
        return new { id = id.Entry, name = m != null ? Text(m.Title) : id.Entry, rarity = m?.Rarity.ToString() ?? "", was_picked = wasPicked };
    }

    public static string EnchantmentName(ModelId? id)
    {
        if (id == null) return "";
        var m = Lookup<EnchantmentModel>(id);
        return m != null ? Text(m.Title) : id.Entry;
    }

    /// <summary>部屋のモデル (遭遇 or イベント) の表示名。</summary>
    public static string RoomModelName(ModelId? id)
    {
        if (id == null) return "";
        var enc = Lookup<EncounterModel>(id);
        if (enc != null) return Text(enc.Title);
        var ev = Lookup<EventModel>(id);
        if (ev != null) return Text(ev.Title);
        return id.Entry;
    }

    /// <summary>種類が分からないモデル ID (クエスト等) の汎用表示。</summary>
    public static object Generic(ModelId id) => new { id = id.Entry, name = id.Entry };

    public static string SafeTitle(CardModel card)
    {
        try { return card.Title ?? card.Id.Entry; }
        catch { return card.Id.Entry; }
    }

    private static T? Lookup<T>(ModelId id) where T : AbstractModel
    {
        try { return ModelDb.GetByIdOrNull<T>(id); }
        catch { return null; }
    }
}
