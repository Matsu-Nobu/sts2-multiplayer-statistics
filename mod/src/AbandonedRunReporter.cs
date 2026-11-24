using System;
using System.Collections.Generic;

namespace StsStats;

/// <summary>
/// ランを読み込まずに放棄したとき (メインメニュー・マルチプレイのメニューの「放棄」) に、
/// そのランのセッションへ run_end (abandoned) を送る。ゲームの DLL に依存しない部分。
/// </summary>
internal static class AbandonedRunReporter
{
    /// <summary>
    /// 手元に保存したセッションがあれば run_end を送り、true を返す。
    /// <paramref name="saveFloor"/> はセーブの階の記録の数。
    /// ゲームは次の階に入る直前にセーブを書くので、この数はプレイヤーがいた階より 1 少ないことがある (RunManager.EnterMapCoordInternal)。
    /// それより手前で放棄したランは、同じシードで始め直した別のランなので送らない。
    /// </summary>
    public static bool Report(
        RunSessionStore store,
        HttpSender sender,
        ulong localSteamId,
        string seed,
        int saveFloor,
        IReadOnlyDictionary<string, int> finalHp)
    {
        string seedForKey = string.IsNullOrEmpty(seed) ? "no-seed" : seed;
        var stored = store.Load(RunKey.Lookup(localSteamId, seedForKey));
        if (stored == null || saveFloor + 1 < stored.LastSeenFloor) return false;
        int finalFloor = Math.Max(saveFloor, stored.LastSeenFloor);

        var badges = new Dictionary<string, List<object>>();
        foreach (var id in finalHp.Keys) badges[id] = new List<object>();

        var ev = new EventRecord(
            EventUuid:   Guid.NewGuid(),
            EventType:   "run_end",
            OccurredAt:  DateTime.UtcNow,
            PlayerId:    null,
            Floor:       finalFloor,
            CombatIndex: null,
            TurnNumber:  null,
            Sequence:    null,
            Payload: new
            {
                outcome     = "abandoned",
                final_floor = finalFloor,
                final_hp    = finalHp,
                // ゲームは放棄したランにバッジを付けない (RunHistoryUtilities.GetBadgesForPlayer)。
                badges      = badges,
            });
        sender.EnqueueEvents(stored.SessionId, stored.WriteToken, new[] { ev });
        return true;
    }
}
