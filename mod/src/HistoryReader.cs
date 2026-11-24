using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Migrations;

namespace StsStats;

/// <summary>
/// セーブデータの履歴ファイルを読み、送る候補にする (docs/spec/data-sources.md「セーブデータの履歴の取り込み」)。
/// </summary>
internal static class HistoryReader
{
    private const int ProfileCount = 3;

    /// <summary>SaveManager._migrationManager (private)。起動時に存在を確認する (tools/verify-game-api/members.txt)。</summary>
    internal static readonly AccessTools.FieldRef<SaveManager, MigrationManager>? MigrationManagerField = TryField<MigrationManager>("_migrationManager");

    /// <summary>SaveManager._saveStore (private)。</summary>
    internal static readonly AccessTools.FieldRef<SaveManager, ISaveStore>? SaveStoreField = TryField<ISaveStore>("_saveStore");

    private static AccessTools.FieldRef<SaveManager, T>? TryField<T>(string name)
    {
        try { return AccessTools.FieldRefAccess<SaveManager, T>(name); }
        catch (Exception ex) { Log.Error($"[StsStats] SaveManager.{name} not found: {ex.Message}"); return null; }
    }

    /// <summary>mod ありと mod なしの、全部のプロファイルの履歴。同じランが両方にあれば 1 つにする。</summary>
    public static List<ImportCandidate> ReadAll()
    {
        if (MigrationManagerField == null || SaveStoreField == null)
            throw new InvalidOperationException("SaveManager fields are not available");
        var migration = MigrationManagerField(SaveManager.Instance);
        var store     = SaveStoreField(SaveManager.Instance);

        var seen = new HashSet<string>();
        var result = new List<ImportCandidate>();
        foreach (bool modded in new[] { true, false })
        {
            for (int profile = 1; profile <= ProfileCount; profile++)
            {
                string dir = RunHistorySaveManager.GetHistoryPath(profile, modded);
                if (!store.DirectoryExists(dir)) continue;
                foreach (string name in store.GetFilesInDirectory(dir).Where(n => n.EndsWith(".run")))
                {
                    var read = migration.LoadSave<RunHistory>(dir + "/" + name);
                    if (!read.Success || read.SaveData == null)
                    {
                        Log.Warn($"[StsStats] history skipped: {dir}/{name} ({read.Status})");
                        continue;
                    }
                    var history = read.SaveData;
                    if (!seen.Add($"{history.Seed}|{history.StartTime}")) continue;
                    result.Add(new ImportCandidate(Check(history), () => Build(history)));
                }
            }
        }
        return result;
    }

    private static string PlayerId(ulong netId) => netId == 1UL ? Identity.LocalSteamId.ToString() : netId.ToString();

    private static DateTime StartedAt(RunHistory h) => DateTimeOffset.FromUnixTimeSeconds(h.StartTime).UtcDateTime;

    private static int FloorCount(RunHistory h) => h.MapPointHistory.Sum(act => act.Count);

    private static string Outcome(RunHistory h) => h.Win ? "victory" : h.WasAbandoned ? "abandoned" : "death";

    private static ImportCheckRun Check(RunHistory h) => new(
        Seed:       h.Seed,
        StartedAt:  StartedAt(h).ToString("yyyy-MM-ddTHH:mm:ssZ"),
        PlayerIds:  h.Players.Select(p => PlayerId(p.Id)).ToList(),
        Outcome:    Outcome(h),
        FinalFloor: FloorCount(h));

    private static ImportPayload Build(RunHistory h)
    {
        var started = StartedAt(h);
        var floors  = h.MapPointHistory.SelectMany((act, actIndex) => act.Select(entry => (entry, actIndex))).ToList();
        var first   = floors.FirstOrDefault().entry;
        var last    = floors.LastOrDefault().entry;
        var events  = new List<EventRecord>();

        EventRecord Event(string type, string? playerId, int floor, DateTime at, object payload) =>
            new(Guid.NewGuid(), type, at, playerId, floor, null, null, null, payload);

        foreach (var p in h.Players)
        {
            var stats = first?.PlayerStats.FirstOrDefault(s => s.PlayerId == p.Id);
            events.Add(Event("run_start", PlayerId(p.Id), 0, started, new
            {
                character_id   = p.Character.Entry,
                character_name = ModelInfo.Text(new LocString("characters", p.Character.Entry + ".title")),
                ascension      = h.Ascension,
                seed           = h.Seed,
                game_mode      = h.GameMode.ToString(),
                player_name    = PlayerName(p),
                // 最初の階の記録から、その階で増減した分を戻して、ランを始めた時点の値にする
                hp             = stats != null ? stats.CurrentHp + stats.DamageTaken - stats.HpHealed : 0,
                max_hp         = stats != null ? stats.MaxHp - stats.MaxHpGained + stats.MaxHpLost : 0,
                gold           = stats != null ? stats.CurrentGold - stats.GoldGained + stats.GoldSpent + stats.GoldLost + stats.GoldStolen : 0,
            }));
        }

        for (int i = 0; i < floors.Count; i++)
        {
            var (entry, actIndex) = floors[i];
            bool isLast = i == floors.Count - 1;
            int floor = i + 1;
            events.Add(Event("floor_snapshot", null, floor, started.AddSeconds(floor), new
            {
                floor          = floor,
                act_index      = actIndex,
                is_final       = true,
                map_point_type = entry.MapPointType.ToString(),
                rooms          = entry.Rooms.Select(r => new
                {
                    room_type   = r.RoomType.ToString(),
                    model_id    = r.ModelId?.Entry,
                    model_name  = ModelInfo.RoomModelName(r.ModelId),
                    monster_ids = r.MonsterIds.Select(m => m.Entry).ToList(),
                    turns_taken = r.TurnsTaken,
                }).ToList(),
                players = entry.PlayerStats.Select(e =>
                {
                    // 履歴ファイルには途中の階の時点のデッキが無いので、ラン終了時点の持ち物を最後の階にだけ入れる
                    var owner = isLast ? h.Players.FirstOrDefault(p => p.Id == e.PlayerId) : null;
                    return FloorRecorder.BuildPlayer(e, PlayerId(e.PlayerId),
                        owner?.Deck.Select(ModelInfo.Card).ToList(),
                        owner?.Relics.Where(r => r.Id != null).Select(r => ModelInfo.Relic(r.Id!)).ToList(),
                        owner?.Potions.Where(po => po.Id != null).Select(po => ModelInfo.Potion(po.Id!)).ToList());
                }).ToList(),
            }));
        }

        events.Add(Event("run_end", null, floors.Count, started.AddSeconds(Math.Max(h.RunTime, floors.Count + 1)), new
        {
            outcome     = Outcome(h),
            final_floor = floors.Count,
            final_hp    = h.Players.ToDictionary(p => PlayerId(p.Id), p => last?.PlayerStats.FirstOrDefault(s => s.PlayerId == p.Id)?.CurrentHp ?? 0),
            badges      = h.Players.ToDictionary(p => PlayerId(p.Id), p => p.Badges.Select(b => Lifecycle.BadgeInfo(b.Id, b.Rarity)).ToList()),
        }));

        var me = h.Players.FirstOrDefault(p => PlayerId(p.Id) == Identity.LocalSteamId.ToString()) ?? h.Players.FirstOrDefault();
        var session = new CreateSessionRequest(
            HostName:    me != null && PlayerName(me) is { Length: > 0 } name ? name : null,
            HostSteamId: Identity.LocalSteamId.ToString(),
            CharacterId: me?.Character.Entry,
            Ascension:   h.Ascension,
            Seed:        h.Seed,
            GameVersion: h.BuildId,
            Source:      "history",
            StartedAt:   Check(h).StartedAt,
            PlayerIds:   h.Players.Select(p => PlayerId(p.Id)).ToList());
        return new ImportPayload(session, events);
    }

    /// <summary>
    /// Steam の名前。取れなければ空文字。
    /// 空ならサーバはプレイヤー名を書き換えないので、過去のゲームで記録した名前が使われる (docs/spec/api.md の run_start)。
    /// </summary>
    private static string PlayerName(RunHistoryPlayer p)
    {
        ulong id = ulong.Parse(PlayerId(p.Id));
        try
        {
            string name = MegaCrit.Sts2.Core.Platform.PlatformUtil.GetPlayerNameRaw(MegaCrit.Sts2.Core.Platform.PlatformUtil.PrimaryPlatform, id);
            if (!string.IsNullOrEmpty(name) && name != id.ToString()) return name;
        }
        catch (Exception ex) { Log.Error($"[StsStats] history player name failed: {ex.Message}"); }
        return "";
    }
}
