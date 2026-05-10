using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;

namespace StsStats;

/// <summary>
/// ラン全体ビュー用に取得する events。階遷移、HP 変動、報酬、ショップ購入、Rest 選択、
/// ポーション入手等。多くは公式 hook を Postfix patch するだけで取れる。
///
/// 値の取り方は reflection でゆるく。プロパティが見つからなければ空文字 / 0。
/// </summary>
internal static class RunOverviewPatches
{
    // === AfterRoomEntered(IRunState, AbstractRoom) ===
    public static void AfterRoomEnteredPostfix(object? runState, object? room)
    {
        try
        {
            // ラン開始の最初の room（floor 1 の Neow / Event 等）でセッション作成 + URL コピー。
            // BeforeCombatStart まで待たずに済むので、ラン開始直後に共有 URL がクリップボードへ。
            // SessionManager は冪等なので戦闘側からの呼び出しと併存して問題ない。
            HookPatches.EnsureSessionFromAnyRoom(runState);

            int floor = GetIntProp(runState, "TotalFloor");
            string roomType = GetStringProp(room, "RoomType");
            string roomTypeViaType = room?.GetType()?.Name ?? "";
            int hp     = GetPlayerHp(runState);
            int maxHp  = GetPlayerMaxHp(runState);
            int gold   = GetPlayerGold(runState);
            int actIdx = GetIntProp(runState, "CurrentActIndex");

            EventBuffer.UpdateFloor(floor);
            EventBuffer.EmitGlobalEvent("room_entered", null, new
            {
                floor          = floor,
                act_index      = actIdx,
                room_type      = roomType,
                room_class     = roomTypeViaType,
                hp             = hp,
                max_hp         = maxHp,
                gold           = gold,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterRoomEntered error: {ex.Message}"); }
    }

    // === AfterCurrentHpChanged(IRunState, ICombatState?, Creature, decimal delta) ===
    public static void AfterCurrentHpChangedPostfix(object? runState, object? combatState, Creature? creature, decimal delta)
    {
        try
        {
            if (creature == null) return;
            int current = (int?)creature.GetType().GetProperty("CurrentHp")?.GetValue(creature) ?? 0;
            int max     = (int?)creature.GetType().GetProperty("MaxHp")?.GetValue(creature) ?? 0;
            // 戦闘外（Event/Rest/Shop）でも player_id を取れるよう combatState → runState の順で探す
            string playerId = TryFindPlayerIdForCreature(combatState, creature)
                          ?? TryFindPlayerIdForCreature(runState, creature)
                          ?? "";

            EventBuffer.EmitGlobalEvent("hp_changed", string.IsNullOrEmpty(playerId) ? null : playerId, new
            {
                delta      = (int)delta,
                current_hp = current,
                max_hp     = max,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterCurrentHpChanged error: {ex.Message}"); }
    }

    // === AfterGoldGained(IRunState, Player) ===
    public static void AfterGoldGainedPostfix(object? runState, object? player)
    {
        try
        {
            int gold = GetIntProp(player, "Gold");
            string pid = GetStringProp(player, "NetId");
            EventBuffer.EmitGlobalEvent("gold_changed", string.IsNullOrEmpty(pid) ? null : pid, new
            {
                current_gold = gold,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterGoldGained error: {ex.Message}"); }
    }

    // === AfterActEntered(IRunState) ===
    public static void AfterActEnteredPostfix(object? runState)
    {
        try
        {
            int actIndex = GetIntProp(runState, "CurrentActIndex");
            EventBuffer.EmitGlobalEvent("act_entered", null, new { act_index = actIndex });
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterActEntered error: {ex.Message}"); }
    }

    // === AfterRestSiteHeal(IRunState, Player, bool isMimicked) ===
    public static void AfterRestSiteHealPostfix(object? runState, object? player, bool isMimicked)
    {
        try
        {
            string pid = GetStringProp(player, "NetId");
            EventBuffer.EmitGlobalEvent("rest_action", string.IsNullOrEmpty(pid) ? null : pid, new
            {
                option       = "heal",
                is_mimicked  = isMimicked,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterRestSiteHeal error: {ex.Message}"); }
    }

    // === AfterRestSiteSmith(IRunState, Player) ===
    public static void AfterRestSiteSmithPostfix(object? runState, object? player)
    {
        try
        {
            string pid = GetStringProp(player, "NetId");
            EventBuffer.EmitGlobalEvent("rest_action", string.IsNullOrEmpty(pid) ? null : pid, new
            {
                option = "smith",
            });
            // smith したカード本体は CardCmd.Upgrade Postfix が emit する (canonical path)
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterRestSiteSmith error: {ex.Message}"); }
    }

    private static ulong GetUlong(object? o, string prop)
    {
        if (o == null) return 0UL;
        try
        {
            var v = o.GetType().GetProperty(prop)?.GetValue(o);
            return v is ulong u ? u : 0UL;
        }
        catch { return 0UL; }
    }

    // === CardCmd.Upgrade(IEnumerable<CardModel>, CardPreviewStyle) Postfix ===
    // STS2 のカードアップグレードは全部この経路 (smith / Apotheosis / Falling event /
    // EnchantedKnowledge 等)。内部で deck pile のカードだけ UpgradedCards.Add される
    // 仕組みなので、ここで pile.Type==Deck のカードに限って emit する。
    // +1 報酬カードの生成は pile が Hand / その他になるため自動で除外される。
    public static void CardCmdUpgradePostfix(System.Collections.IEnumerable cards)
    {
        try
        {
            if (!SessionManager.IsReady) return;
            foreach (var card in cards)
            {
                if (card == null) continue;
                var pile = card.GetType().GetProperty("Pile")?.GetValue(card);
                var pileTypeObj = pile?.GetType().GetProperty("Type")?.GetValue(pile);
                if (pile == null || pileTypeObj?.ToString() != "Deck") continue;
                string cardId = GetIdEntry(card);
                string cardName = ResolveLocString(GetPropValue(card, "Title"));
                if (string.IsNullOrEmpty(cardName)) cardName = GetPropValue(card, "Title")?.ToString() ?? "";
                string? ownerId = TryGetCardOwnerId(card);
                EventBuffer.EmitGlobalEvent("card_upgraded", ownerId, new
                {
                    card_id   = cardId,
                    card_name = cardName,
                });
            }
        }
        catch (Exception ex) { Log.Error($"[StsStats] CardCmd.Upgrade Postfix error: {ex.Message}"); }
    }

    // CardCmd.Add は async Task で Postfix のタイミングが state-machine 開始時に
    // なってしまうため、deck 追加完了前に走る。代わりに CardModel.FloorAddedToDeck
    // setter を patch する: CardCmd.Add 内で deck 追加成功時に同期的に setter が
    // 呼ばれる (`result.cardAdded.FloorAddedToDeck = runState.TotalFloor;`)。
    // この瞬間 = カードが master deck に確実に入った瞬間 + sync。
    public static void FloorAddedToDeckSetterPostfix(object __instance, int value)
    {
        try
        {
            if (!SessionManager.IsReady) return;
            if (value < 0) return;            // 初期化時の -1 は無視
            string cardId = GetIdEntry(__instance);
            if (string.IsNullOrEmpty(cardId)) return;
            string cardName = ResolveLocString(GetPropValue(__instance, "Title"));
            if (string.IsNullOrEmpty(cardName)) cardName = GetPropValue(__instance, "Title")?.ToString() ?? "";
            string? ownerId = TryGetCardOwnerId(__instance);
            EventBuffer.EmitGlobalEvent("card_obtained", ownerId, new
            {
                card_id   = cardId,
                card_name = cardName,
                floor     = value,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] FloorAddedToDeck setter Postfix error: {ex.Message}"); }
    }

    // === Merchant***Entry.OnTryPurchase Postfix ===
    // Hook.AfterItemPurchased は ClearAfterPurchase() の後に呼ばれるため、
    // その時点で MerchantCardEntry.CreationResult は null にされていて取れない。
    // 各派生型の OnTryPurchase 内では CreationResult/Model がまだ valid。
    // ここで購入対象を読み取って item_purchased event を直接 emit する。
    //
    // OnTryPurchase は失敗しても呼ばれるが、失敗時は OnTryPurchase 内部で false を
    // 返すだけで CreationResult は変わらない。我々の event は「試行ログ」として許容。
    // (実用上ほぼ成功する: gold 不足や満杯時はゲーム UI 側で押せない)

    public static void MerchantCardEntryOnTryPurchasePostfix(object __instance)
    {
        TryEmitMerchantPurchase(__instance, "MerchantCardEntry");
    }
    public static void MerchantPotionEntryOnTryPurchasePostfix(object __instance)
    {
        TryEmitMerchantPurchase(__instance, "MerchantPotionEntry");
    }
    public static void MerchantRelicEntryOnTryPurchasePostfix(object __instance)
    {
        TryEmitMerchantPurchase(__instance, "MerchantRelicEntry");
    }
    public static void MerchantCardRemovalEntryOnTryPurchasePostfix(object __instance)
    {
        TryEmitMerchantPurchase(__instance, "MerchantCardRemovalEntry");
    }

    private static void TryEmitMerchantPurchase(object entry, string kind)
    {
        try
        {
            int cost = GetIntProp(entry, "Cost");
            object? player = GetField(entry, "_player");
            string pid = GetStringProp(player, "NetId");

            string cardId = "";
            string cardName = "";
            string relicId = "";
            string relicName = "";
            string potionId = "";
            string potionName = "";

            switch (kind)
            {
                case "MerchantCardEntry":
                    {
                        var creationResult = GetPropValue(entry, "CreationResult");
                        var card = GetPropValue(creationResult, "Card");
                        cardId = GetIdEntry(card);
                        cardName = card?.GetType().GetProperty("Title")?.GetValue(card)?.ToString() ?? "";
                    }
                    break;
                case "MerchantPotionEntry":
                    {
                        var potion = GetPropValue(entry, "Model");
                        potionId = GetIdEntry(potion);
                        potionName = ResolveLocString(GetPropValue(potion, "Title"));
                    }
                    break;
                case "MerchantRelicEntry":
                    {
                        var relic = GetPropValue(entry, "Model");
                        relicId = GetIdEntry(relic);
                        relicName = ResolveLocString(GetPropValue(relic, "Title"));
                    }
                    break;
            }

            EventBuffer.EmitGlobalEvent("item_purchased", string.IsNullOrEmpty(pid) ? null : pid, new
            {
                item_kind   = kind,
                card_id     = cardId,
                card_name   = cardName,
                relic_id    = relicId,
                relic_name  = relicName,
                potion_id   = potionId,
                potion_name = potionName,
                gold_spent  = cost,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] {kind}.OnTryPurchase Postfix error: {ex.Message}"); }
    }

    // === AfterRewardTaken(IRunState, Player, Reward) ===
    // 派生型ごとの property（実 DLL で確認済）:
    //   GoldReward.Amount         (int)
    //   PotionReward.Potion       (PotionModel)
    //   RelicReward.Relic         (RelicModel)
    //   SpecialCardReward._card   (private CardModel)
    //   CardReward                — chosen card は reward 自体に保持されず、
    //                               RunState.CurrentMapPointHistoryEntry の CardChoices に
    //                               wasPicked=true で追加される。これを reflection で拾う。
    public static void AfterRewardTakenPostfix(object? runState, object? player, object? reward)
    {
        try
        {
            string pid = GetStringProp(player, "NetId");
            string rewardKind = reward?.GetType()?.Name ?? "";

            int goldAmount = 0;
            string cardId = "";
            string cardName = "";
            string potionId = "";
            string potionName = "";
            string relicId = "";
            string relicName = "";
            // CardReward の場合、提示された全選択肢 (picked/skipped 両方) を一緒に送る。
            List<object>? cardChoices = null;

            switch (rewardKind)
            {
                case "GoldReward":
                    goldAmount = GetIntProp(reward, "Amount");
                    break;
                case "PotionReward":
                    {
                        var potion = GetPropValue(reward, "Potion");
                        potionId = GetIdEntry(potion);
                        potionName = ResolveLocString(GetPropValue(potion, "Title"));
                    }
                    break;
                case "RelicReward":
                    {
                        var relic = GetPropValue(reward, "Relic");
                        relicId = GetIdEntry(relic);
                        relicName = ResolveLocString(GetPropValue(relic, "Title"));
                    }
                    break;
                case "SpecialCardReward":
                    {
                        var card = GetField(reward, "_card");
                        cardId = GetIdEntry(card);
                        cardName = card?.GetType().GetProperty("Title")?.GetValue(card)?.ToString() ?? "";
                    }
                    break;
                case "CardReward":
                    {
                        ulong netId = GetUlong(player, "NetId");
                        var picked = TryFindRecentCardChoices(runState, netId, out cardChoices);
                        cardId = picked;
                    }
                    break;
            }

            EventBuffer.EmitGlobalEvent("reward_taken", string.IsNullOrEmpty(pid) ? null : pid, new
            {
                reward_kind  = rewardKind,
                gold_amount  = goldAmount,
                card_id      = cardId,
                card_name    = cardName,
                potion_id    = potionId,
                potion_name  = potionName,
                relic_id     = relicId,
                relic_name   = relicName,
                card_choices = cardChoices,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterRewardTaken error: {ex.Message}"); }
    }

    /// <summary>
    /// CardReward 選択直後の chosen card を取り出す。
    ///
    /// 構造: runState.CurrentMapPointHistoryEntry.GetEntry(playerId).CardChoices
    ///         → List&lt;CardChoiceHistoryEntry&gt; （CardChoiceHistoryEntry { Card, WasPicked }）
    /// 末尾から WasPicked=true のものを 1 件取る。
    /// </summary>
    /// <summary>
    /// 直近の CardReward について「提示された全カード」(picked/skipped 両方) を choices に詰めて返し、
    /// 戻り値として picked card_id を返す。
    /// 構造: runState.CurrentMapPointHistoryEntry.GetEntry(ulong NetId).CardChoices
    ///        : List&lt;CardChoiceHistoryEntry { Card, WasPicked }&gt;
    /// </summary>
    private static string TryFindRecentCardChoices(object? runState, ulong playerId, out List<object>? choices)
    {
        choices = null;
        try
        {
            var entry = GetPropValue(runState, "CurrentMapPointHistoryEntry");
            if (entry == null) return "";
            var getEntry = entry.GetType().GetMethod("GetEntry", new[] { typeof(ulong) });
            if (getEntry == null) return "";
            var perPlayer = getEntry.Invoke(entry, new object?[] { playerId });
            if (perPlayer == null) return "";
            var choicesObj = perPlayer.GetType().GetProperty("CardChoices")?.GetValue(perPlayer)
                          as System.Collections.IEnumerable;
            if (choicesObj == null) return "";
            string pickedId = "";
            var list = new List<object>();
            foreach (var c in choicesObj)
            {
                bool picked = (bool?)c.GetType().GetProperty("WasPicked")?.GetValue(c) ?? false;
                var card = c.GetType().GetProperty("Card")?.GetValue(c);
                string cId = GetIdEntry(card);
                // Title は LocString。ToString() だと localize されないものがあるので
                // ResolveLocString 経由で日本語を取る。
                string cName = ResolveLocString(card?.GetType().GetProperty("Title")?.GetValue(card));
                list.Add(new
                {
                    card_id    = cId,
                    card_name  = cName,
                    was_picked = picked,
                });
                if (picked) pickedId = cId;
            }
            choices = list;
            return pickedId;
        }
        catch { return ""; }
    }

    private static object? GetPropValue(object? o, string name)
    {
        if (o == null) return null;
        try { return o.GetType().GetProperty(name)?.GetValue(o); }
        catch { return null; }
    }
    private static object? GetField(object? o, string name)
    {
        if (o == null) return null;
        try
        {
            return o.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)?.GetValue(o);
        }
        catch { return null; }
    }

    // === AfterPotionProcured(IRunState, ICombatState?, PotionModel) ===
    public static void AfterPotionProcuredPostfix(object? runState, object? combatState, object? potion)
    {
        try
        {
            string id = GetIdEntry(potion);
            string name = ResolveLocString(GetPropValue(potion, "Title"));
            EventBuffer.EmitGlobalEvent("potion_obtained", null, new
            {
                potion_id   = id,
                potion_name = name,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterPotionProcured error: {ex.Message}"); }
    }

    // CardModel.OnUpgrade Postfix は廃止 — +1 カード報酬の生成時にも発火するため
    // 信頼できない (アップグレード操作ではないのに card_upgraded が emit される)。
    // 現在は AfterRestSiteSmithPostfix が CurrentMapPointHistoryEntry.UpgradedCards を
    // 読み出して鍛治アップグレードを emit する一本化された経路を担当している。

    // === RelicCmd.Obtain(RelicModel, Player, int) Postfix ===
    // STS2 のレリック取得は全部この 1 メソッド経由 (treasure, reward, event,
    // 戦闘ドロップ等)。Hook.AfterRewardTaken の RelicReward 分岐や宝箱の
    // 別経路に頼らず、ここを単一の正規経路として relic_obtained を emit する。
    public static void RelicCmdObtainPostfix(object relic, object player)
    {
        try
        {
            if (!SessionManager.IsReady) return;
            string relicId = GetIdEntry(relic);
            string relicName = ResolveLocString(GetPropValue(relic, "Title"));
            string pid = GetStringProp(player, "NetId");
            EventBuffer.EmitGlobalEvent("relic_obtained", string.IsNullOrEmpty(pid) ? null : pid, new
            {
                relic_id   = relicId,
                relic_name = relicName,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] RelicCmd.Obtain Postfix error: {ex.Message}"); }
    }

    private static string? TryGetCardOwnerId(object? card)
    {
        if (card == null) return null;
        try
        {
            var owner = card.GetType().GetProperty("Owner")?.GetValue(card);
            var netId = owner?.GetType().GetProperty("NetId")?.GetValue(owner);
            return netId?.ToString();
        }
        catch { return null; }
    }

    // === CardModel.EnchantInternal(EnchantmentModel, decimal) Postfix ===
    // カードへのエンチャ付与（イベント・レリック・カード効果由来 すべて）。
    // ただし EnchantInternal はカードのインスタンスが作り直されるたびに呼ばれる
    // (deck reload, save resume, クローン等)。同じ (card instance, enchantment_id)
    // ペアでの再 emit を防ぐため重複検出する。
    private static readonly HashSet<long> _seenEnchants = new();
    public static void EnchantInternalPostfix(object __instance, object enchantment, decimal amount)
    {
        try
        {
            if (!SessionManager.IsReady) return;
            string cardId = GetIdEntry(__instance);
            string cardName = __instance?.GetType().GetProperty("Title")?.GetValue(__instance)?.ToString() ?? "";
            string enchantmentId = GetIdEntry(enchantment);
            if (string.IsNullOrEmpty(cardId) || string.IsNullOrEmpty(enchantmentId)) return;

            // (card instance, enchantment_id) の dedup key
            int cardHash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(__instance);
            long key = ((long)cardHash << 32) ^ enchantmentId.GetHashCode();
            lock (_seenEnchants) { if (!_seenEnchants.Add(key)) return; }

            string? ownerId = TryGetCardOwnerId(__instance);
            EventBuffer.EmitGlobalEvent("card_enchanted", ownerId, new
            {
                card_id        = cardId,
                card_name      = cardName,
                enchantment_id = enchantmentId,
                amount         = (int)amount,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] EnchantInternal Postfix error: {ex.Message}"); }
    }

    // 戦闘間で dedup state をクリア（戦闘終了時に呼ぶ）
    internal static void ClearEnchantDedup()
    {
        lock (_seenEnchants) _seenEnchants.Clear();
    }

    // === EventOption.Chosen() Postfix ===
    // ランダムイベントの選択肢が選ばれたタイミング。Chosen() を呼ぶのは
    // 「自クライアント上で選択操作した player」なので LocalContext.NetId を採用。
    public static void EventOptionChosenPostfix(object __instance)
    {
        try
        {
            if (!SessionManager.IsReady) return;
            string textKey = GetStringProp(__instance, "TextKey");
            string title       = ResolveLocString(GetPropValue(__instance, "Title"));
            string historyName = ResolveLocString(GetPropValue(__instance, "HistoryName"));

            string? playerId = null;
            try
            {
                ulong? localId = MegaCrit.Sts2.Core.Context.LocalContext.NetId;
                if (localId.HasValue && localId.Value != 0UL) playerId = localId.Value.ToString();
            }
            catch { }

            EventBuffer.EmitGlobalEvent("event_choice", playerId, new
            {
                text_key      = textKey,
                title         = title,
                history_name  = historyName,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] EventOption.Chosen Postfix error: {ex.Message}"); }
    }

    // LocString → 文字列（PowerNameResolver と同じパターン）
    private static string ResolveLocString(object? loc)
    {
        if (loc == null) return "";
        try
        {
            var t = loc.GetType();
            // GetFormattedText / GetRawText など
            foreach (var name in new[] { "GetFormattedText", "GetRawText" })
            {
                var m = t.GetMethod(name, System.Type.EmptyTypes);
                if (m == null) continue;
                var v = m.Invoke(loc, null) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }
        catch { }
        return "";
    }

    // === BeforeCardRemoved(IRunState, CardModel) ===
    public static void BeforeCardRemovedPostfix(object? runState, object? card)
    {
        try
        {
            string cardId = GetIdEntry(card);
            string cardName = card?.GetType().GetProperty("Title")?.GetValue(card)?.ToString() ?? "";
            EventBuffer.EmitGlobalEvent("card_removed", null, new
            {
                card_id   = cardId,
                card_name = cardName,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] BeforeCardRemoved error: {ex.Message}"); }
    }

    // === AfterPotionDiscarded(IRunState, ICombatState?, PotionModel) ===
    public static void AfterPotionDiscardedPostfix(object? runState, object? combatState, object? potion)
    {
        try
        {
            string id = GetIdEntry(potion);
            EventBuffer.EmitGlobalEvent("potion_discarded", null, new
            {
                potion_id = id,
            });
        }
        catch (Exception ex) { Log.Error($"[StsStats] AfterPotionDiscarded error: {ex.Message}"); }
    }

    // === ヘルパ ============================================================

    private static int GetIntProp(object? o, string name)
    {
        if (o == null) return 0;
        try { return (int?)o.GetType().GetProperty(name)?.GetValue(o) ?? 0; }
        catch { return 0; }
    }
    private static string GetStringProp(object? o, string name)
    {
        if (o == null) return "";
        try { return o.GetType().GetProperty(name)?.GetValue(o)?.ToString() ?? ""; }
        catch { return ""; }
    }
    private static string GetIdEntry(object? o)
    {
        if (o == null) return "";
        try
        {
            var idObj = o.GetType().GetProperty("Id")?.GetValue(o);
            return idObj?.GetType().GetProperty("Entry")?.GetValue(idObj)?.ToString() ?? "";
        }
        catch { return ""; }
    }
    // ローカルプレイヤー（自分）のフィールドだけを取る。MP では各クライアントが自分の値を記録する
    private static int GetPlayerHp(object? runState)     => GetLocalPlayerCreatureInt(runState, "CurrentHp");
    private static int GetPlayerMaxHp(object? runState)  => GetLocalPlayerCreatureInt(runState, "MaxHp");
    private static int GetPlayerGold(object? runState)
    {
        var local = FindLocalPlayer(runState);
        if (local == null) return 0;
        try { return (int?)local.GetType().GetProperty("Gold")?.GetValue(local) ?? 0; }
        catch { return 0; }
    }
    private static int GetLocalPlayerCreatureInt(object? runState, string propName)
    {
        var local = FindLocalPlayer(runState);
        if (local == null) return 0;
        try
        {
            var creature = local.GetType().GetProperty("Creature")?.GetValue(local);
            if (creature == null) return 0;
            return (int?)creature.GetType().GetProperty(propName)?.GetValue(creature) ?? 0;
        }
        catch { return 0; }
    }
    private static object? FindLocalPlayer(object? runState)
    {
        if (runState == null) return null;
        try
        {
            // LocalContext.NetId と一致する player を探す。取れなければ最初の player にフォールバック
            ulong localId = 0;
            try { localId = MegaCrit.Sts2.Core.Context.LocalContext.NetId ?? 0UL; } catch { }
            var players = runState.GetType().GetProperty("Players")?.GetValue(runState) as System.Collections.IEnumerable;
            if (players == null) return null;
            object? fallback = null;
            foreach (var p in players)
            {
                fallback ??= p;
                ulong netId = (p.GetType().GetProperty("NetId")?.GetValue(p) as ulong?) ?? 0UL;
                if (localId != 0UL && netId == localId) return p;
            }
            return fallback;
        }
        catch { return null; }
    }
    private static string? TryFindPlayerIdForCreature(object? combatState, Creature creature)
    {
        if (combatState == null) return null;
        try
        {
            var players = combatState.GetType().GetProperty("Players")?.GetValue(combatState) as System.Collections.IEnumerable;
            if (players == null) return null;
            foreach (var p in players)
            {
                var pc = p.GetType().GetProperty("Creature")?.GetValue(p);
                if (ReferenceEquals(pc, creature))
                {
                    return p.GetType().GetProperty("NetId")?.GetValue(p)?.ToString();
                }
            }
        }
        catch { }
        return null;
    }
}
