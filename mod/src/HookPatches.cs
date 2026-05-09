using System;
using System.Collections;
using System.Linq;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace StsStats;

/// <summary>
/// Harmony hook の postfix 群。
/// Phase 3.5 ではすべてのゲームイベントを EventBuffer 経由で raw event として
/// 発行する（旧 StatsCollector 集計値方式は廃止）。
///
/// 集計（戦闘単位サマリ・rDPS・カード別統計）は WebUI 側で行う。
/// </summary>
internal static class HookPatches
{
    // 現在ターン中のプレイヤー（AfterPlayerTurnStart で更新）。potion_used 等で使う。
    private static (string id, string name)? _currentTurnPlayer = null;

    // 現在の戦闘で AfterCombatVictory が発火したか（combat_end の victory 判定に使う）
    private static bool _currentCombatWasVictory = false;

    // === ライフサイクル hook ============================================

    public static void BeforeCombatStartPostfix(IRunState? runState, CombatState? combatState)
    {
        try
        {
            if (runState != null)
            {
                EnsureSessionForRun(runState);
                int floor = (int?)runState.GetType().GetProperty("TotalFloor")?.GetValue(runState) ?? 0;
                EventBuffer.UpdateFloor(floor);

                if (!SessionManager.RunStartAlreadyEmitted)
                {
                    EmitRunStartForAllPlayers(runState);
                    if (ModEntry.SessionStore != null)
                        SessionManager.MarkRunStartEmitted(ModEntry.SessionStore);
                }
            }

            EventBuffer.BeginCombat();
            PowerOriginRegistry.ClearForCombat();
            _currentTurnPlayer = null;
            _currentCombatWasVictory = false;

            EmitCombatStart(runState, combatState);

            Log.Info("[StsStats] Combat started");
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] BeforeCombatStart error: {ex.Message}");
        }
    }

    public static void AfterPlayerTurnStartPostfix(CombatState? combatState, object? player)
    {
        try
        {
            _currentTurnPlayer = TryGetPlayerInfo(player);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterPlayerTurnStart error: {ex.Message}");
        }
    }

    /// <summary>
    /// AfterTurnEnd(ICombatState combatState, CombatSide side)
    /// CombatSide.Player のときのみ「プレイヤー側ターン終了 → 次ターンへ」のシグナル
    /// として扱い、turn_number を進める。
    /// </summary>
    public static void AfterTurnEndPostfix(CombatState? combatState, object? side)
    {
        try
        {
            if (side == null) return;
            int sideValue = Convert.ToInt32(side);  // None=0, Player=1, Enemy=2
            if (sideValue != 1) return;

            EventBuffer.BeginTurn();
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterTurnEnd error: {ex.Message}");
        }
    }

    public static void AfterCombatEndPostfix(IRunState? runState, CombatState? combatState, object? room)
    {
        try
        {
            EmitCombatEnd(runState, combatState);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterCombatEnd error: {ex.Message}");
        }
    }

    public static void AfterCombatVictoryPostfix(IRunState? runState, CombatState? combatState, object? room)
    {
        try
        {
            _currentCombatWasVictory = true;
            Log.Info($"[StsStats] AfterCombatVictory fired (combat_index={EventBuffer.CurrentCombatIndex})");

            // 最終ボス突破時の run_end(victory) 発行は維持
            if (runState == null || room == null) return;
            bool isVictoryRoom = (bool?)(room.GetType().GetProperty("IsVictoryRoom")?.GetValue(room)) ?? false;
            if (!isVictoryRoom) return;

            int actIndex = (int?)runState.GetType().GetProperty("CurrentActIndex")?.GetValue(runState) ?? -1;
            int actCount = (runState.GetType().GetProperty("Acts")?.GetValue(runState) as IEnumerable)?.Cast<object>().Count() ?? -1;
            bool isFinalAct = actIndex >= 0 && actCount > 0 && actIndex == actCount - 1;
            if (!isFinalAct) return;

            EmitRunEndForAllPlayers(runState, outcome: "victory");
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterCombatVictory error: {ex.Message}");
        }
    }

    public static void AfterDeathPostfix(
        IRunState?    runState,
        CombatState?  combatState,
        Creature?     creature,
        bool          wasRemovalPrevented,
        float         deathAnimLength)
    {
        try
        {
            if (runState == null || creature == null) return;
            // 蘇生・死亡無効化された場合は run_end を発行しない
            if (wasRemovalPrevented) return;
            var playerInfo = TryFindPlayerForCreature(combatState, creature);
            if (playerInfo == null) return;

            int alive = CountAlivePlayers(runState);
            if (alive > 0)
            {
                Log.Info($"[StsStats] Player {playerInfo.Value.name} fell, {alive} player(s) still alive — run continues");
                return;
            }

            // 全員死亡 = run 終了
            EmitCombatEnd(runState, combatState);   // combat_end (victory=false) を明示送信
            EmitRunEnd(runState, playerId: playerInfo.Value.id, outcome: "death");
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterDeath error: {ex.Message}");
        }
    }

    // === 戦闘内 hook =====================================================

    /// <summary>
    /// 被弾前 HP を snapshot しておく。AfterDamageGiven で overkill を自前計算するため。
    /// STS2 の DamageResult.OverkillDamage は実測上信頼できない（amount を超える値が
    /// 返る等）ので、target.CurrentHealth (この時点では被弾前) を捕捉する。
    /// </summary>
    /// <summary>
    /// Hook.ModifyDamage Postfix: damage 計算の (pre, post, modifiers) を記録する。
    /// preview / UI 起源の呼び出しは target / dealer が null になる傾向があるので除外。
    ///
    /// 注: previewMode (CardPreviewMode 列挙) は Harmony で受け取れない（型情報を mod 側に
    /// 持たないため）。引数省略すれば名前マッチで問題なくスキップされる。
    /// </summary>
    public static void ModifyDamagePostfix(
        Creature? target,
        Creature? dealer,
        decimal damage,
        ref System.Collections.Generic.IEnumerable<MegaCrit.Sts2.Core.Models.AbstractModel> modifiers,
        decimal __result)
    {
        try
        {
            if (target == null || dealer == null) return;
            DamageModificationLog.Record(damage, __result, modifiers);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] ModifyDamage Postfix error: {ex.Message}");
        }
    }

    public static void BeforeDamageReceivedPostfix(CombatState? combatState, Creature? target)
    {
        try
        {
            if (target == null) return;
            // STS2 の Creature は HP を CurrentHp プロパティで持つ (CurrentHealth ではない)
            int hp = (int?)target.GetType().GetProperty("CurrentHp")?.GetValue(target) ?? 0;
            TargetHpSnapshot.Record(target, hp);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] BeforeDamageReceived error: {ex.Message}");
        }
    }

    public static void AfterDamageGivenPostfix(
        CombatState? combatState,
        Creature?    dealer,
        DamageResult? results,
        Creature?    target,
        CardModel?   cardSource)
    {
        try
        {
            if (dealer == null || results == null) return;
            // STS2 の DamageResult は HP cap が既にかかった値を返す:
            //   TotalDamage     = HP loss + 敵 block で吸収された分（= UnblockedDamage + BlockedDamage、cap 込み）
            //   BlockedDamage   = 敵 block で吸収された分
            //   UnblockedDamage = 実際に HP を削った量（HP cap 済）
            //   OverkillDamage  = 信用できない
            //
            // overkill を出すには「pre-block・pre-cap の試行 damage」と「被弾前 HP」が要る:
            //   attempted = Hook.ModifyDamage の最終 post 値（DamageModificationLog.TakeLastPost）
            //   hpBefore  = BeforeDamageReceived で取った target.CurrentHp
            //   overkill  = max(0, attempted − blocked − hpBefore)
            int amount   = (int)results.UnblockedDamage;
            int total    = (int)results.TotalDamage;
            int blocked  = (int)results.BlockedDamage;
            bool wasKilled = results.WasTargetKilled;
            if (total <= 0) return;

            int overkill = 0;
            decimal? lastPost = DamageModificationLog.TakeLastPost();
            int? hpBefore = target != null ? TargetHpSnapshot.Lookup(target) : null;
            if (lastPost.HasValue && hpBefore.HasValue)
            {
                int attempted = (int)lastPost.Value;
                overkill = System.Math.Max(0, attempted - blocked - hpBefore.Value);
            }
            if (target != null) TargetHpSnapshot.Clear(target);

            // dealer がプレイヤーか間接ダメージか判定
            var dealerPlayer = TryFindPlayerForCreature(combatState, dealer);
            string? dealerPlayerId;
            CardInfo? card;

            if (dealerPlayer != null)
            {
                // 直接ダメージ
                dealerPlayerId = dealerPlayer.Value.id;
                card = TryGetCardInfo(cardSource) ?? DamageSourceContext.Current;
            }
            else
            {
                // 間接ダメージ（poison/doom/lightning など）→ DoT applier を探して帰属
                if (target == null) return;
                var applierPlayer = FindIndirectDamageApplier(target, combatState);
                if (applierPlayer == null) return;
                dealerPlayerId = applierPlayer.Value.id;
                card = TryGetCardInfo(cardSource) ?? DamageSourceContext.Current;
            }

            string? targetPlayerId = TryFindPlayerForCreature(combatState, target)?.id;
            // 自傷ダメージ（Hemokinesis 等）や味方への誤爆は damage_dealt にカウントしない。
            // target が player creature（targetPlayerId != null）の場合は damage_received 側で記録される。
            if (targetPlayerId != null) return;

            int hitIndex = (int?)results.GetType().GetProperty("HitIndex")?.GetValue(results) ?? 0;

            // ModifyDamage で観測した (pre, post, modifiers) を drain。
            // 直近 ModifyDamage 群の中で post が total と一致するエントリを「実ヒット由来」として優先採用。
            // 同一内容のエントリは重複除去（cleave 計算等で同じ呼び出しが複数回起きるため）。
            var allMods = DamageModificationLog.Drain();
            var seen = new System.Collections.Generic.HashSet<string>();
            var modList = allMods
                .Where(m => (int)m.Post == total)
                .Where(m => seen.Add($"{(int)m.Pre}->{(int)m.Post}|{string.Join(',', m.ModifierIds)}"))
                .Select(m => new
                {
                    pre  = (int)m.Pre,
                    post = (int)m.Post,
                    modifier_types = m.ModifierTypes,
                    modifier_ids   = m.ModifierIds,
                })
                .ToList();

            EventBuffer.EmitTurnEvent("damage_dealt", dealerPlayerId, new
            {
                amount               = amount,
                total_damage         = total,
                blocked_damage       = blocked,
                overkill_damage      = overkill,
                was_target_killed    = wasKilled,
                target_creature_id   = target != null ? CreatureIdentity(target) : null,
                target_player_id     = targetPlayerId,
                source_card_id       = card?.CardId,
                source_card_name     = card?.CardName,
                source_card_type     = card?.CardType,
                hit_index            = hitIndex,
                active_on_target     = ActivePowersSnapshot.ForCreature(target),
                active_on_dealer     = ActivePowersSnapshot.ForCreature(dealer),
                modifications        = modList,
            });
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterDamageGiven error: {ex.Message}");
        }
    }

    public static void AfterDamageReceivedPostfix(
        CombatState?  combatState,
        Creature?     target,
        DamageResult? result,
        Creature?     dealer,
        CardModel?    cardSource)
    {
        try
        {
            if (target == null || result == null) return;
            int amount   = (int)result.UnblockedDamage;     // HP に通った分
            int total    = (int)result.TotalDamage;          // 試行された総ダメ
            int blocked  = (int)result.BlockedDamage;        // 自分の block で吸収した分（= 有効シールド）
            if (total <= 0) return;

            var playerInfo = TryFindPlayerForCreature(combatState, target);
            if (playerInfo == null) return;

            EventBuffer.EmitTurnEvent("damage_received", playerInfo.Value.id, new
            {
                amount             = amount,
                total_damage       = total,
                blocked_damage     = blocked,
                source_creature_id = dealer != null ? CreatureIdentity(dealer) : null,
                source_card_id     = TryGetCardInfo(cardSource)?.CardId,
                active_on_target   = ActivePowersSnapshot.ForCreature(target),
                active_on_dealer   = ActivePowersSnapshot.ForCreature(dealer),
            });
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterDamageReceived error: {ex.Message}");
        }
    }

    public static void AfterBlockGainedPostfix(
        CombatState? combatState,
        Creature?    creature,
        decimal      amount,
        CardModel?   cardSource)
    {
        try
        {
            if (creature == null || amount <= 0) return;
            int blockAmt = (int)amount;

            var receiver = TryFindPlayerForCreature(combatState, creature);
            if (receiver == null) return;
            var giver = TryFindPlayerForCardOwner(cardSource);
            // cardSource が null の場合は power 由来として BlockSourceContext を fallback
            var source = TryGetCardInfo(cardSource) ?? BlockSourceContext.Current;

            EventBuffer.EmitTurnEvent("block_gained", receiver.Value.id, new
            {
                amount           = blockAmt,
                source_card_id   = source?.CardId,
                source_card_name = source?.CardName,
                source_card_type = source?.CardType,    // "Attack" / "Skill" / "Power" / "Orb" 等
                from_player      = giver?.id ?? receiver.Value.id,
            });
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterBlockGained error: {ex.Message}");
        }
    }

    public static void AfterEnergySpentPostfix(CombatState? combatState, CardModel? card, int amount)
    {
        try
        {
            if (amount <= 0) return;
            var playerInfo = TryFindPlayerForCardOwner(card) ?? _currentTurnPlayer;
            if (playerInfo == null) return;

            EventBuffer.EmitTurnEvent("energy_spent", playerInfo.Value.id, new
            {
                amount         = amount,
                source_card_id = TryGetCardInfo(card)?.CardId,
            });
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterEnergySpent error: {ex.Message}");
        }
    }

    public static void BeforeCardPlayedPostfix()
    {
        try { CardPlayedScope.Enter(); }
        catch (Exception ex) { Log.Error($"[StsStats] BeforeCardPlayed scope enter error: {ex.Message}"); }
    }

    public static void AfterCardPlayedPostfix(CombatState? combatState, object? cardPlay)
    {
        try
        {
            if (cardPlay == null) return;
            var cardModel = cardPlay.GetType().GetProperty("Card")?.GetValue(cardPlay) as CardModel;
            if (cardModel == null) return;

            var playerInfo = TryFindPlayerForCardOwner(cardModel);
            if (playerInfo == null) return;
            var info = TryGetCardInfo(cardModel);
            if (info == null) return;

            // CardPlay.Target は省略可
            string? targetCreatureId = null;
            try
            {
                var t = cardPlay.GetType().GetProperty("Target")?.GetValue(cardPlay) as Creature;
                if (t != null) targetCreatureId = CreatureIdentity(t);
            }
            catch { }

            EventBuffer.EmitTurnEvent("card_played", playerInfo.Value.id, new
            {
                card_id            = info.CardId,
                card_name          = info.CardName,
                card_type          = info.CardType,
                target_creature_id = targetCreatureId,
            });
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterCardPlayed error: {ex.Message}");
        }
        finally
        {
            try { CardPlayedScope.Exit(); } catch { }
        }
    }

    public static void AfterCardDrawnPostfix(CombatState? combatState, CardModel? card, bool fromHandDraw)
    {
        try
        {
            if (card == null) return;
            var playerInfo = TryFindPlayerForCardOwner(card) ?? _currentTurnPlayer;
            if (playerInfo == null) return;
            var info = TryGetCardInfo(card);

            EventBuffer.EmitTurnEvent("card_drawn", playerInfo.Value.id, new
            {
                card_id        = info?.CardId,
                card_name      = info?.CardName,
                from_hand_draw = fromHandDraw,
            });
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterCardDrawn error: {ex.Message}");
        }
    }

    public static void AfterPowerAmountChangedPostfix(
        CombatState? combatState,
        PowerModel?  power,
        decimal      amount,
        Creature?    applier,
        CardModel?   cardSource)
    {
        try
        {
            if (power == null || applier == null) return;
            int delta = (int)amount;
            if (delta == 0) return;

            string powerId = power.Id.Entry;
            var applierPlayer = TryFindPlayerForCreature(combatState, applier);
            string? applierPlayerId = applierPlayer?.id;

            // PowerOriginRegistry に記録:
            //   applier が player → その applier の stacks を delta 分動かす（正負ともに反映）
            //   applier 不明 + delta < 0 → 自然減衰として既存 applier 全員から比例減算
            if (power.Owner != null)
            {
                if (applierPlayer != null)
                    PowerOriginRegistry.RecordApply(power.Owner, powerId, applierPlayer.Value.id, delta);
                else if (delta < 0)
                    PowerOriginRegistry.RecordDecay(power.Owner, powerId, delta);
            }

            string? targetCreatureId = power.Owner != null ? CreatureIdentity(power.Owner) : null;
            string? targetPlayerId = TryFindPlayerForCreature(combatState, power.Owner)?.id;

            EventBuffer.EmitTurnEvent("power_changed", applierPlayerId, new
            {
                power_id           = powerId,
                power_name         = PowerNameResolver.Resolve(power),
                delta              = delta,
                target_creature_id = targetCreatureId,
                target_player_id   = targetPlayerId,
                source_card_id     = TryGetCardInfo(cardSource)?.CardId,
            });
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterPowerAmountChanged error: {ex.Message}");
        }
    }

    public static void AfterPotionUsedPostfix(CombatState? combatState, object? potion, Creature? target)
    {
        try
        {
            // PotionModel.Owner（Player）から netId を取って正しい使用者を特定する。
            // 旧: _currentTurnPlayer を使ってたが、MP では両プレイヤーが順次ターンを進めるため
            //    最後に AfterPlayerTurnStart が発火したプレイヤーに全部寄せられてしまっていた。
            string? userId = null;
            try
            {
                var owner = potion?.GetType().GetProperty("Owner")?.GetValue(potion);
                var netId = owner?.GetType().GetProperty("NetId")?.GetValue(owner);
                if (netId != null) userId = netId.ToString();
            }
            catch { }
            if (string.IsNullOrEmpty(userId))
            {
                // フォールバック: 旧ロジック
                var fallback = _currentTurnPlayer;
                if (fallback == null) return;
                userId = fallback.Value.id;
            }

            string? potionId = null;
            try
            {
                var idObj = potion?.GetType().GetProperty("Id")?.GetValue(potion);
                potionId = idObj?.GetType().GetProperty("Entry")?.GetValue(idObj)?.ToString();
            }
            catch { }

            EventBuffer.EmitTurnEvent("potion_used", userId, new
            {
                potion_id          = potionId,
                target_creature_id = target != null ? CreatureIdentity(target) : null,
            });
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterPotionUsed error: {ex.Message}");
        }
    }

    // === run / combat ライフサイクル emit ヘルパ ==========================

    private static void EmitRunStartForAllPlayers(IRunState runState)
    {
        try
        {
            var players = runState.GetType().GetProperty("Players")?.GetValue(runState) as IEnumerable;
            if (players == null) return;
            int ascension = (int?)runState.GetType().GetProperty("AscensionLevel")?.GetValue(runState) ?? 0;
            string seed = TryGetSeed(runState);

            foreach (var player in players)
            {
                var info = TryGetPlayerInfo(player);
                if (info == null) continue;
                string characterId = TryGetCharacterId(player) ?? "UNKNOWN";

                EventBuffer.EmitGlobalEvent("run_start", info.Value.id, new
                {
                    character_id = characterId,
                    ascension    = ascension,
                    seed         = seed,
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] EmitRunStartForAllPlayers error: {ex.Message}");
        }
    }

    private static void EmitRunEndForAllPlayers(IRunState runState, string outcome)
    {
        try
        {
            var players = runState.GetType().GetProperty("Players")?.GetValue(runState) as IEnumerable;
            if (players == null) return;
            foreach (var player in players)
            {
                var info = TryGetPlayerInfo(player);
                if (info == null) continue;
                EmitRunEnd(runState, info.Value.id, outcome);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] EmitRunEndForAllPlayers error: {ex.Message}");
        }
    }

    private static void EmitRunEnd(IRunState runState, string playerId, string outcome)
    {
        try
        {
            int finalFloor = (int?)runState.GetType().GetProperty("TotalFloor")?.GetValue(runState) ?? 0;
            EventBuffer.EmitGlobalEvent("run_end", playerId, new
            {
                outcome     = outcome,
                final_floor = finalFloor,
            });
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] EmitRunEnd error: {ex.Message}");
        }
    }

    private static void EmitCombatStart(IRunState? runState, CombatState? combatState)
    {
        try
        {
            int combatIndex = EventBuffer.CurrentCombatIndex;

            string? encounterId   = null;
            string? encounterName = null;
            try
            {
                var enc = combatState?.GetType().GetProperty("Encounter")?.GetValue(combatState);
                var encId = enc?.GetType().GetProperty("Id")?.GetValue(enc);
                encounterId = encId?.GetType().GetProperty("Entry")?.GetValue(encId)?.ToString();
                var title = enc?.GetType().GetProperty("Title")?.GetValue(enc);
                encounterName = TryGetLocStringText(title) ?? encounterId;
            }
            catch { }

            string? roomType = TryGetRoomType(runState, combatState);

            EventBuffer.EmitCombatEvent("combat_start", null, new
            {
                combat_index   = combatIndex,
                encounter_id   = encounterId,
                encounter_name = encounterName,
                room_type      = roomType,
            });
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] EmitCombatStart error: {ex.Message}");
        }
    }

    private static void EmitCombatEnd(IRunState? runState, CombatState? combatState)
    {
        try
        {
            int combatIndex = EventBuffer.CurrentCombatIndex;
            bool victory = _currentCombatWasVictory;
            Log.Info($"[StsStats] EmitCombatEnd combat_index={combatIndex} victory={victory}");

            EventBuffer.EmitCombatEvent("combat_end", null, new
            {
                combat_index = combatIndex,
                victory      = victory,
            });
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] EmitCombatEnd error: {ex.Message}");
        }
    }

    // === セッション初期化 ================================================

    /// <summary>
    /// AfterRoomEntered 等の他フックからも呼べるよう object 受け wrapper。
    /// セッション既確立なら即 return（ラン開始直後の最初の room でだけ実行する想定）。
    /// </summary>
    internal static void EnsureSessionFromAnyRoom(object? runState)
    {
        if (SessionManager.IsReady) return;
        if (runState is IRunState r) EnsureSessionForRun(r);
    }

    private static void EnsureSessionForRun(IRunState runState)
    {
        var api   = ModEntry.ApiClient;
        var store = ModEntry.SessionStore;
        if (api == null || store == null) return;

        ulong localId = TryGetLocalPlayerId();
        string? hostName = localId != 0UL ? TryGetSteamName(localId) : null;

        string? characterId = TryGetHostCharacterId(runState, localId);
        int    ascension    = (int?)runState.GetType().GetProperty("AscensionLevel")?.GetValue(runState) ?? 0;
        string seed         = TryGetSeed(runState);
        string seedForKey   = string.IsNullOrEmpty(seed) ? "no-seed" : seed;
        string gameMode     = runState.GetType().GetProperty("GameMode")?.GetValue(runState)?.ToString() ?? "";
        int    totalFloor   = (int?)runState.GetType().GetProperty("TotalFloor")?.GetValue(runState) ?? 0;

        string lookupKey = RunKey.Lookup(localId, seedForKey);
        var meta = new RunMetaForKey(
            HostSteamId: localId,
            Seed:        seedForKey,
            CharacterId: characterId,
            Ascension:   ascension,
            GameMode:    gameMode);

        SessionManager.EnsureSession(
            lookupKey:        lookupKey,
            currentTotalFloor: totalFloor,
            api:              api,
            requestBuilder:   (startedAt, runKey) => new CreateSessionRequest(
                HostName:    hostName,
                HostSteamId: localId != 0UL ? localId.ToString() : null,
                CharacterId: characterId,
                Ascension:   ascension,
                Seed:        string.IsNullOrEmpty(seed) ? null : seed),
            runMeta:          meta,
            store:            store);
    }

    // === ヘルパ群（Phase 2 から踏襲） ====================================

    private static (string id, string name)? TryFindPlayerForCreature(CombatState? combatState, Creature? dealer)
    {
        if (dealer == null) return null;
        try
        {
            var runState = combatState?.RunState;
            if (runState == null) return null;
            var players = runState.GetType().GetProperty("Players")?.GetValue(runState) as IEnumerable;
            if (players == null) return null;
            foreach (var player in players)
            {
                var creature = player.GetType().GetProperty("Creature")?.GetValue(player) as Creature;
                if (creature != dealer) continue;
                return BuildPlayerInfo(player);
            }
        }
        catch (Exception ex) { Log.Error($"[StsStats] TryFindPlayerForCreature error: {ex.Message}"); }
        return null;
    }

    private static (string id, string name)? TryFindPlayerForCardOwner(CardModel? cardModel)
    {
        if (cardModel == null) return null;
        try
        {
            var owner = cardModel.GetType().GetProperty("Owner")?.GetValue(cardModel);
            return TryGetPlayerInfo(owner);
        }
        catch { return null; }
    }

    private static (string id, string name)? TryGetPlayerInfo(object? player)
    {
        if (player == null) return null;
        try { return BuildPlayerInfo(player); }
        catch { return null; }
    }

    private static (string id, string name) BuildPlayerInfo(object player)
    {
        ulong netId = (player.GetType().GetProperty("NetId")?.GetValue(player) as ulong?) ?? 0UL;
        string? steamName = ResolveSteamName(netId);
        ulong resolvedId = netId;

        if (steamName == null)
        {
            ulong localId = TryGetLocalPlayerId();
            if (localId != 0UL)
            {
                steamName = ResolveSteamName(localId);
                if (steamName != null) resolvedId = localId;
            }
        }

        string name = steamName ?? TryGetCharacterId(player) ?? netId.ToString();
        string id   = resolvedId != 0UL ? resolvedId.ToString() : name;
        return (id, name);
    }

    private static string? ResolveSteamName(ulong steamId)
    {
        var raw = TryGetSteamName(steamId);
        if (string.IsNullOrEmpty(raw)) return null;
        if (raw == steamId.ToString()) return null;
        return raw;
    }

    private static string? TryGetCharacterId(object player)
    {
        try
        {
            var character = player.GetType().GetProperty("Character")?.GetValue(player);
            var modelId   = character?.GetType().GetProperty("Id")?.GetValue(character);
            return modelId?.GetType().GetProperty("Entry")?.GetValue(modelId)?.ToString();
        }
        catch { return null; }
    }

    private static ulong TryGetLocalPlayerId()
    {
        try { return PlatformUtil.GetLocalPlayerId(PlatformUtil.PrimaryPlatform); }
        catch { return 0UL; }
    }

    private static string? TryGetSteamName(ulong steamId)
    {
        if (steamId == 0UL) return null;
        try
        {
            string name = PlatformUtil.GetPlayerName(PlatformUtil.PrimaryPlatform, steamId);
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] TryGetSteamName error: {ex.Message}");
            return null;
        }
    }

    private static string? TryGetLocStringText(object? locString)
    {
        if (locString == null) return null;
        try
        {
            var formatted = locString.GetType().GetMethod("GetFormattedText", Type.EmptyTypes)?.Invoke(locString, null) as string;
            if (!string.IsNullOrEmpty(formatted)) return formatted;
            var raw = locString.GetType().GetMethod("GetRawText", Type.EmptyTypes)?.Invoke(locString, null) as string;
            return string.IsNullOrEmpty(raw) ? null : raw;
        }
        catch { return null; }
    }

    private static CardInfo? TryGetCardInfo(CardModel? cardModel)
    {
        if (cardModel == null) return null;
        try
        {
            var idObj  = cardModel.GetType().GetProperty("Id")?.GetValue(cardModel);
            string cardId = idObj?.GetType().GetProperty("Entry")?.GetValue(idObj)?.ToString() ?? "";
            if (string.IsNullOrEmpty(cardId)) return null;
            string cardName = cardModel.GetType().GetProperty("Title")?.GetValue(cardModel)?.ToString() ?? cardId;
            string cardType = cardModel.GetType().GetProperty("Type")?.GetValue(cardModel)?.ToString() ?? "";
            return new CardInfo(cardId, cardName, cardType);
        }
        catch { return null; }
    }

    private static string TryGetSeed(IRunState runState)
    {
        try
        {
            var rng = runState.GetType().GetProperty("Rng")?.GetValue(runState);
            return rng?.GetType().GetProperty("StringSeed")?.GetValue(rng)?.ToString() ?? "";
        }
        catch { return ""; }
    }

    private static string? TryGetHostCharacterId(IRunState runState, ulong localId)
    {
        try
        {
            var players = runState.GetType().GetProperty("Players")?.GetValue(runState) as IEnumerable;
            if (players == null) return null;
            string? fallback = null;
            foreach (var p in players)
            {
                ulong netId = (p.GetType().GetProperty("NetId")?.GetValue(p) as ulong?) ?? 0UL;
                string? cid = TryGetCharacterId(p);
                if (netId == localId) return cid;
                fallback ??= cid;
            }
            return fallback;
        }
        catch { return null; }
    }

    private static string? TryGetRoomType(IRunState? runState, CombatState? combatState)
    {
        try
        {
            var encounter = combatState?.GetType().GetProperty("Encounter")?.GetValue(combatState);
            var rt = encounter?.GetType().GetProperty("RoomType")?.GetValue(encounter);
            if (rt != null)
            {
                string name = rt.ToString() ?? "";
                if (!string.IsNullOrEmpty(name) && name != "Unassigned") return name;
            }
        }
        catch { }
        try
        {
            var encounter = combatState?.GetType().GetProperty("Encounter")?.GetValue(combatState);
            var idObj = encounter?.GetType().GetProperty("Id")?.GetValue(encounter);
            string id = idObj?.GetType().GetProperty("Entry")?.GetValue(idObj)?.ToString() ?? "";
            if (id.EndsWith("_ELITE")) return "Elite";
            if (id.EndsWith("_BOSS"))  return "Boss";
            if (!string.IsNullOrEmpty(id)) return "Monster";
        }
        catch { }
        return null;
    }

    private static int CountAlivePlayers(IRunState runState)
    {
        int alive = 0;
        try
        {
            var players = runState.GetType().GetProperty("Players")?.GetValue(runState) as IEnumerable;
            if (players == null) return 0;
            foreach (var p in players)
            {
                var cr = p.GetType().GetProperty("Creature")?.GetValue(p) as Creature;
                if (cr == null) continue;
                bool isAlive = (bool?)cr.GetType().GetProperty("IsAlive")?.GetValue(cr) ?? false;
                if (isAlive) alive++;
            }
        }
        catch { }
        return alive;
    }

    /// <summary>
    /// 指定 target に乗っている Poison/Burn 等の DoT の applier を探して返す。
    /// 間接ダメージ（dealer がプレイヤーでない場合）の attribution に使う。
    /// </summary>
    private static (string id, string name)? FindIndirectDamageApplier(Creature? target, CombatState? combatState)
    {
        if (target == null) return null;
        try
        {
            var powers = target.GetType().GetProperty("Powers")?.GetValue(target) as IEnumerable;
            if (powers == null) return null;
            foreach (var power in powers)
            {
                string powerId = "";
                try
                {
                    var idObj = power.GetType().GetProperty("Id")?.GetValue(power);
                    powerId = idObj?.GetType().GetProperty("Entry")?.GetValue(idObj)?.ToString() ?? "";
                }
                catch { }
                if (powerId.Contains("POISON") || powerId.Contains("BURN") || powerId.Contains("DOOM"))
                {
                    var applier = power.GetType().GetProperty("Applier")?.GetValue(power) as Creature;
                    var info = TryFindPlayerForCreature(combatState, applier);
                    if (info != null) return info;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>creature を一意に識別する文字列。同一 run 中は object identity で安定。</summary>
    private static string CreatureIdentity(Creature c)
        => "c:" + RuntimeHelpers.GetHashCode(c).ToString();
}
