using System.Collections;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace StsStats;

internal static class HookPatches
{
    // Tracks the player whose turn is currently active (set in AfterPlayerTurnStart)
    private static (string id, string name)? _currentTurnPlayer = null;

    // run_start emit 状態は SessionManager.RunStartAlreadyEmitted（永続化される）で管理する。

    // 現在の戦闘で AfterCombatVictory が発火したか。BeforeCombatStart でリセット、
    // AfterCombatVictory で true、AfterCombatEnd で combat_end イベントの victory として送る。
    // room.IsVictoryRoom は「ラン全体の最終勝利部屋」フラグなので使えない。
    private static bool _currentCombatWasVictory = false;

    // BeforeCombatStart(IRunState runState, CombatState? combatState)
    public static void BeforeCombatStartPostfix(IRunState? runState, CombatState? combatState)
    {
        try
        {
            if (runState != null)
            {
                // セッションを確保（同 run なら復元、新 run なら作成）
                EnsureSessionForRun(runState);

                // run_start: 過去にこの run で emit していなければ発行
                if (!SessionManager.RunStartAlreadyEmitted)
                {
                    EmitRunStartForAllPlayers(runState);
                    if (ModEntry.SessionStore != null)
                        SessionManager.MarkRunStartEmitted(ModEntry.SessionStore);
                }
            }

            StatsCollector.BeginCombat();
            _currentTurnPlayer = null;
            _currentCombatWasVictory = false;

            // combat_start イベント発行（タブラベル等で使用）
            EmitCombatStart(runState, combatState);

            Log.Info("[StsStats] Combat started");
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] BeforeCombatStart error: {ex.Message}");
        }
    }

    // AfterPlayerTurnStart(ICombatState combatState, PlayerChoiceContext choiceContext, Player player)
    // 現在のターンプレイヤーを記憶するだけ。ターン確定は AfterTurnEnd で行う。
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

    // AfterTurnEnd(ICombatState combatState, CombatSide side)
    // プレイヤー側ターン終了時に finalize。AfterPlayerTurnStart 起点だと
    // 「ターン開始ドローだけの空ターン」が先頭に挟まる off-by-one のため、こちらに切替。
    public static void AfterTurnEndPostfix(CombatState? combatState, object? side)
    {
        try
        {
            // CombatSide enum: None=0, Player=1, Enemy=2 ; Player のみで finalize
            if (side == null) return;
            int sideValue = Convert.ToInt32(side);
            if (sideValue != 1) return;

            var payload = StatsCollector.FinalizeTurn(isFinal: false);
            if (payload != null) DispatchTurn(payload);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterTurnEnd error: {ex.Message}");
        }
    }

    // AfterCombatEnd(IRunState runState, CombatState? combatState, CombatRoom room)
    public static void AfterCombatEndPostfix(IRunState? runState, CombatState? combatState, object? room)
    {
        try
        {
            var payload = StatsCollector.FinalizeTurn(isFinal: true);
            if (payload != null) DispatchTurn(payload);

            // combat_end イベント発行（勝敗を含む）
            EmitCombatEnd(runState, combatState, room);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterCombatEnd error: {ex.Message}");
        }
    }

    // AfterCombatVictory(IRunState runState, ICombatState combatState, CombatRoom room)
    // 最終ボス戦突破などで run 全体勝利を判定したい場合に拡張する。
    // 現状は IsVictoryRoom かつ Act が最終 act の場合に run_end(victory) を emit。
    public static void AfterCombatVictoryPostfix(IRunState? runState, CombatState? combatState, object? room)
    {
        try
        {
            // この戦闘は勝利した（後段の AfterCombatEnd で combat_end イベントの victory に使う）
            _currentCombatWasVictory = true;
            Log.Info($"[StsStats] AfterCombatVictory fired (combat_index={StatsCollector.CurrentCombatIndex})");

            if (runState == null || room == null) return;

            bool isVictoryRoom = (bool?)(room.GetType().GetProperty("IsVictoryRoom")?.GetValue(room)) ?? false;
            if (!isVictoryRoom) return;

            // 最終 act の判定: CurrentActIndex が Acts.Count - 1
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

    // AfterDeath(IRunState runState, ICombatState combatState, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
    public static void AfterDeathPostfix(IRunState? runState, CombatState? combatState, Creature? creature)
    {
        try
        {
            if (runState == null || creature == null) return;

            // 死亡したのがプレイヤー創物か
            var playerInfo = TryFindPlayerForCreature(combatState, creature);
            if (playerInfo == null) return;

            // マルチプレイで他プレイヤーがまだ生きていれば run は続く。run_end は emit しない。
            // 戦闘自体も他プレイヤーが続行するので combat_end / turn flush もここではやらない。
            int alive = CountAlivePlayers(runState);
            if (alive > 0)
            {
                Log.Info($"[StsStats] Player {playerInfo.Value.name} fell, {alive} player(s) still alive — run continues");
                return;
            }

            // 全員死亡 = run 終了（シングルプレイは常にこちらに来る）
            // STS2 は player death では AfterCombatEnd を発火しないようなので、明示的に flush する。
            var payload = StatsCollector.FinalizeTurn(isFinal: true);
            if (payload != null) DispatchTurn(payload);

            // 戦闘の負けでもあるので combat_end も明示送信（victory=false）
            EmitCombatEnd(runState, combatState, room: null);

            EmitRunEnd(runState, playerId: playerInfo.Value.id, outcome: "death");
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterDeath error: {ex.Message}");
        }
    }

    // AfterDamageGiven(PlayerChoiceContext? choiceContext, CombatState? combatState,
    //                  Creature? dealer, DamageResult? results, ValueProp props,
    //                  Creature? target, CardModel? cardSource)
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

            int amount = (int)results.UnblockedDamage;
            if (amount <= 0) return;

            // ケース1: プレイヤーが直接与えたダメージ
            var dealerPlayer = TryFindPlayerForCreature(combatState, dealer);
            if (dealerPlayer != null)
            {
                // cardSource が無い間接ダメージは DamageSourceContext から拾う
                var card = TryGetCardInfo(cardSource) ?? DamageSourceContext.Current;
                StatsCollector.RecordDamageDealt(dealerPlayer.Value.id, dealerPlayer.Value.name, amount, card);
                return;
            }

            // ケース2: 間接ダメージ（Poison/Burn 等の DoT tick）
            // dealer は通常 enemy 自身、target も enemy 自身。target に乗っている DoT の applier を探して帰属。
            if (target != null)
            {
                var applierPlayer = FindIndirectDamageApplier(target, combatState);
                if (applierPlayer != null)
                {
                    var card = TryGetCardInfo(cardSource) ?? DamageSourceContext.Current;
                    StatsCollector.RecordDamageDealt(applierPlayer.Value.id, applierPlayer.Value.name, amount, card);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterDamageGiven error: {ex.Message}");
        }
    }

    // AfterDamageReceived(PlayerChoiceContext choiceContext, IRunState runState, ICombatState combatState,
    //                     Creature target, DamageResult result, ValueProp props, Creature dealer, CardModel cardSource)
    public static void AfterDamageReceivedPostfix(
        CombatState?  combatState,
        Creature?     target,
        DamageResult? result)
    {
        try
        {
            if (target == null || result == null) return;

            int amount = (int)result.UnblockedDamage;
            if (amount <= 0) return;

            var playerInfo = TryFindPlayerForCreature(combatState, target);
            if (playerInfo == null) return;

            StatsCollector.RecordDamageReceived(playerInfo.Value.id, playerInfo.Value.name, amount);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterDamageReceived error: {ex.Message}");
        }
    }

    // AfterBlockGained(ICombatState combatState, Creature creature, Decimal amount, ValueProp props, CardModel cardSource)
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

            // Who received the block?
            var receiver = TryFindPlayerForCreature(combatState, creature);
            if (receiver == null) return;

            // Who gave the block? (the player who played the card)
            var giver = TryFindPlayerForCardOwner(cardSource);

            if (giver != null && giver.Value.id != receiver.Value.id)
            {
                // A player gave block to a different player
                StatsCollector.RecordBlockGainedSelf(receiver.Value.id, receiver.Value.name, blockAmt,
                    TryGetCardInfo(cardSource));
                StatsCollector.RecordBlockGivenToAlly(giver.Value.id, giver.Value.name, blockAmt,
                    TryGetCardInfo(cardSource));
            }
            else
            {
                // Self-block (or giver unknown)
                StatsCollector.RecordBlockGainedSelf(receiver.Value.id, receiver.Value.name, blockAmt,
                    TryGetCardInfo(cardSource));
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterBlockGained error: {ex.Message}");
        }
    }

    // AfterEnergySpent(ICombatState combatState, CardModel card, Int32 amount)
    public static void AfterEnergySpentPostfix(CombatState? combatState, CardModel? card, int amount)
    {
        try
        {
            if (amount <= 0) return;

            var playerInfo = TryFindPlayerForCardOwner(card) ?? _currentTurnPlayer;
            if (playerInfo == null) return;

            StatsCollector.RecordEnergyUsed(playerInfo.Value.id, playerInfo.Value.name, amount);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterEnergySpent error: {ex.Message}");
        }
    }

    // AfterCardPlayed(ICombatState combatState, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    public static void AfterCardPlayedPostfix(CombatState? combatState, object? cardPlay)
    {
        try
        {
            if (cardPlay == null) return;

            var cardModel = cardPlay.GetType().GetProperty("Card")?.GetValue(cardPlay) as CardModel;
            if (cardModel == null) return;

            var playerInfo = TryFindPlayerForCardOwner(cardModel);
            if (playerInfo == null) return;

            var cardInfo = TryGetCardInfo(cardModel);
            if (cardInfo == null) return;

            StatsCollector.RecordCardPlayed(playerInfo.Value.id, playerInfo.Value.name, cardInfo);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterCardPlayed error: {ex.Message}");
        }
    }

    // AfterCardDrawn(ICombatState combatState, PlayerChoiceContext choiceContext, CardModel card, Boolean fromHandDraw)
    public static void AfterCardDrawnPostfix(CombatState? combatState, CardModel? card)
    {
        try
        {
            if (card == null) return;

            var playerInfo = TryFindPlayerForCardOwner(card) ?? _currentTurnPlayer;
            if (playerInfo == null) return;

            StatsCollector.RecordCardDrawn(playerInfo.Value.id, playerInfo.Value.name);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterCardDrawn error: {ex.Message}");
        }
    }

    // AfterPowerAmountChanged(ICombatState combatState, PlayerChoiceContext choiceContext,
    //                         PowerModel power, Decimal amount, Creature applier, CardModel cardSource)
    public static void AfterPowerAmountChangedPostfix(
        CombatState? combatState,
        PowerModel?  power,
        decimal      amount,
        Creature?    applier,
        CardModel?   cardSource)
    {
        try
        {
            if (power == null || applier == null || amount <= 0) return;

            // applier must be a player
            var playerInfo = TryFindPlayerForCreature(combatState, applier);
            if (playerInfo == null) return;

            // power.Owner must NOT be a player (i.e., it's an enemy getting debuffed)
            var ownerIsPlayer = TryFindPlayerForCreature(combatState, power.Owner);
            if (ownerIsPlayer != null) return;

            string powerId = power.Id.Entry;
            int stacks = (int)amount;

            StatsCollector.RecordDebuffApplied(playerInfo.Value.id, playerInfo.Value.name, powerId, stacks,
                TryGetCardInfo(cardSource));
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterPowerAmountChanged error: {ex.Message}");
        }
    }

    // AfterPotionUsed(IRunState runState, ICombatState combatState, PotionModel potion, Creature target)
    public static void AfterPotionUsedPostfix(CombatState? combatState, object? target)
    {
        try
        {
            // target is the Creature the potion was used on; find the acting player via current turn
            var playerInfo = _currentTurnPlayer;
            if (playerInfo == null) return;

            StatsCollector.RecordPotionUsed(playerInfo.Value.id, playerInfo.Value.name);
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] AfterPotionUsed error: {ex.Message}");
        }
    }

    // --- helpers ---

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
        catch (Exception ex)
        {
            Log.Error($"[StsStats] TryFindPlayerForCreature error: {ex.Message}");
        }
        return null;
    }

    // CardModel.Owner is of type Player — extract player info from it directly
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

        // GetPlayerName が解決できなかった（NetIdが合成IDなど）→ ローカルSteam IDで再試行
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

    // GetPlayerName は未解決時に netId の文字列をそのまま返してくるので、
    // 入力IDの文字列化と一致する場合は「解決失敗」とみなす。
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

    /// <summary>LocString からローカライズ済み実テキストを取得。失敗 / 空 → null。</summary>
    private static string? TryGetLocStringText(object? locString)
    {
        if (locString == null) return null;
        try
        {
            var formatted = locString.GetType()
                .GetMethod("GetFormattedText", Type.EmptyTypes)?.Invoke(locString, null) as string;
            if (!string.IsNullOrEmpty(formatted)) return formatted;
            var raw = locString.GetType()
                .GetMethod("GetRawText", Type.EmptyTypes)?.Invoke(locString, null) as string;
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

            // CardModel.Title is String (not LocString)
            string cardName = cardModel.GetType().GetProperty("Title")?.GetValue(cardModel)?.ToString() ?? cardId;
            string cardType = cardModel.GetType().GetProperty("Type")?.GetValue(cardModel)?.ToString() ?? "";

            return new CardInfo(cardId, cardName, cardType);
        }
        catch { return null; }
    }

    // --- run lifecycle event helpers ---

    private static void EmitRunStartForAllPlayers(IRunState runState)
    {
        try
        {
            var players = runState.GetType().GetProperty("Players")?.GetValue(runState) as IEnumerable;
            if (players == null) return;

            int ascension = (int?)runState.GetType().GetProperty("AscensionLevel")?.GetValue(runState) ?? 0;
            string seed = TryGetSeed(runState);
            int floor = (int?)runState.GetType().GetProperty("TotalFloor")?.GetValue(runState) ?? 0;

            foreach (var player in players)
            {
                var info = TryGetPlayerInfo(player);
                if (info == null) continue;

                string characterId = TryGetCharacterId(player) ?? "UNKNOWN";

                EventBuffer.Emit(new EventRecord(
                    EventUuid:   Guid.NewGuid(),
                    EventType:   "run_start",
                    OccurredAt:  DateTime.UtcNow,
                    PlayerId:    info.Value.id,
                    Floor:       floor,
                    Payload:     new
                    {
                        character_id = characterId,
                        ascension    = ascension,
                        seed         = seed,
                    }
                ));
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
            EventBuffer.Emit(new EventRecord(
                EventUuid:   Guid.NewGuid(),
                EventType:   "run_end",
                OccurredAt:  DateTime.UtcNow,
                PlayerId:    playerId,
                Floor:       finalFloor,
                Payload:     new
                {
                    outcome     = outcome,
                    final_floor = finalFloor,
                }
            ));
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] EmitRunEnd error: {ex.Message}");
        }
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

    // --- HTTP dispatch helpers ---

    /// <summary>turn ペイロードを JSONL と HTTP の両方へ送る。</summary>
    private static void DispatchTurn(TurnPayload payload)
    {
        StatsLogger.LogTurn(payload);
        var sender = ModEntry.HttpSender;
        if (sender != null && SessionManager.IsReady)
            sender.EnqueueTurn(SessionManager.SessionId!, SessionManager.WriteToken!, payload);
    }

    /// <summary>初回戦闘開始時にセッションを確保する（run メタ込みで POST または store から復元）。</summary>
    private static void EnsureSessionForRun(IRunState runState)
    {
        var api   = ModEntry.ApiClient;
        var store = ModEntry.SessionStore;
        if (api == null || store == null) return;

        // ホストは「ローカルのプレイヤー」とみなす（mod を入れているクライアント = 自分）
        ulong localId = TryGetLocalPlayerId();
        string? hostName = localId != 0UL ? TryGetSteamName(localId) : null;

        // 自分のキャラクター ID
        string? characterId = TryGetHostCharacterId(runState, localId);
        int    ascension    = (int?)runState.GetType().GetProperty("AscensionLevel")?.GetValue(runState) ?? 0;
        string seed         = TryGetSeed(runState);
        string seedForKey   = string.IsNullOrEmpty(seed) ? "no-seed" : seed;
        string gameMode     = runState.GetType().GetProperty("GameMode")?.GetValue(runState)?.ToString() ?? "";
        int    totalFloor   = (int?)runState.GetType().GetProperty("TotalFloor")?.GetValue(runState) ?? 0;

        // 「同じ run か」は (host, seed) ペアで lookup し、TotalFloor の monotonicity で判断する
        string lookupKey = RunKey.Lookup(localId, seedForKey);

        var meta = new RunMetaForKey(
            HostSteamId: localId,
            Seed:        seedForKey,
            CharacterId: characterId,
            Ascension:   ascension,
            GameMode:    gameMode
        );

        SessionManager.EnsureSession(
            lookupKey:        lookupKey,
            currentTotalFloor: totalFloor,
            api:              api,
            requestBuilder:   (startedAt, runKey) => new CreateSessionRequest(
                HostName:    hostName,
                HostSteamId: localId != 0UL ? localId.ToString() : null,
                CharacterId: characterId,
                Ascension:   ascension,
                Seed:        string.IsNullOrEmpty(seed) ? null : seed
            ),
            runMeta:          meta,
            store:            store
        );
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
                if (netId == localId) return cid;       // ローカルプレイヤー一致
                fallback ??= cid;                       // 最初に見つかった character を保険として保持
            }
            return fallback;
        }
        catch { return null; }
    }

    /// <summary>combat_start イベントを emit。encounter 情報があれば付ける。</summary>
    private static void EmitCombatStart(IRunState? runState, CombatState? combatState)
    {
        try
        {
            int combatIndex = StatsCollector.CurrentCombatIndex;
            int floor = (int?)runState?.GetType().GetProperty("TotalFloor")?.GetValue(runState) ?? 0;

            string? encounterId   = null;
            string? encounterName = null;
            try
            {
                var enc = combatState?.GetType().GetProperty("Encounter")?.GetValue(combatState);
                var encId = enc?.GetType().GetProperty("Id")?.GetValue(enc);
                encounterId = encId?.GetType().GetProperty("Entry")?.GetValue(encId)?.ToString();
                // EncounterModel.Title は LocString。GetFormattedText() で実テキストを取る
                var title = enc?.GetType().GetProperty("Title")?.GetValue(enc);
                encounterName = TryGetLocStringText(title) ?? encounterId;
            }
            catch { /* encounter 取得失敗時は ID/Name なしでイベント発行 */ }

            string? roomType = TryGetRoomType(runState, combatState);

            EventBuffer.Emit(new EventRecord(
                EventUuid:  Guid.NewGuid(),
                EventType:  "combat_start",
                OccurredAt: DateTime.UtcNow,
                PlayerId:   null,
                Floor:      floor,
                Payload:    new
                {
                    combat_index   = combatIndex,
                    encounter_id   = encounterId,
                    encounter_name = encounterName,
                    room_type      = roomType,
                }
            ));
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] EmitCombatStart error: {ex.Message}");
        }
    }

    /// <summary>combat_end イベントを emit。AfterCombatVictory が事前に発火していれば victory=true。</summary>
    private static void EmitCombatEnd(IRunState? runState, CombatState? combatState, object? room)
    {
        try
        {
            int combatIndex = StatsCollector.CurrentCombatIndex;
            int floor = (int?)runState?.GetType().GetProperty("TotalFloor")?.GetValue(runState) ?? 0;

            // combat の勝敗: AfterCombatVictory hook の発火有無で判定する。
            // room.IsVictoryRoom はラン全体の最終勝利部屋専用なので使えない。
            bool victory = _currentCombatWasVictory;
            Log.Info($"[StsStats] EmitCombatEnd combat_index={combatIndex} victory={victory}");

            EventBuffer.Emit(new EventRecord(
                EventUuid:  Guid.NewGuid(),
                EventType:  "combat_end",
                OccurredAt: DateTime.UtcNow,
                PlayerId:   null,
                Floor:      floor,
                Payload:    new
                {
                    combat_index = combatIndex,
                    victory      = victory,
                }
            ));
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] EmitCombatEnd error: {ex.Message}");
        }
    }

    private static string? TryGetRoomType(IRunState? runState, CombatState? combatState)
    {
        // 1. CombatState.Encounter.RoomType (enum) を優先（"Monster" / "Elite" / "Boss"）
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
        // 2. フォールバック: encounter_id の suffix
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

    /// <summary>run の生存プレイヤー数を返す。マルチプレイで全員死亡判定に使う。</summary>
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
    /// 指定された target に乗っている Poison（または同類の DoT）の applier を探す。
    /// 自傷ダメージ（DoT tick）を applier プレイヤーに帰属させるために使う。
    /// </summary>
    private static (string id, string name)? FindIndirectDamageApplier(Creature? target, CombatState? combatState)
    {
        if (target == null) return null;
        try
        {
            var powersProp = target.GetType().GetProperty("Powers");
            var powers = powersProp?.GetValue(target) as IEnumerable;
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
                // Poison / 他 DoT 系（Burn / etc.）は名前に POISON / BURN を含むことを想定
                if (powerId.Contains("POISON") || powerId.Contains("BURN"))
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
}
