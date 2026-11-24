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
    /// 保存した階より手前で放棄したランは、同じシードで始め直した別のランなので送らない。
    /// </summary>
    public static bool Report(
        RunSessionStore store,
        HttpSender sender,
        ulong localSteamId,
        string seed,
        int totalFloor,
        IReadOnlyDictionary<string, int> finalHp)
    {
        string seedForKey = string.IsNullOrEmpty(seed) ? "no-seed" : seed;
        var stored = store.Load(RunKey.Lookup(localSteamId, seedForKey));
        if (stored == null || totalFloor < stored.LastSeenFloor) return false;

        var badges = new Dictionary<string, List<object>>();
        foreach (var id in finalHp.Keys) badges[id] = new List<object>();

        var ev = new EventRecord(
            EventUuid:   Guid.NewGuid(),
            EventType:   "run_end",
            OccurredAt:  DateTime.UtcNow,
            PlayerId:    null,
            Floor:       totalFloor,
            CombatIndex: null,
            TurnNumber:  null,
            Sequence:    null,
            Payload: new
            {
                outcome     = "abandoned",
                final_floor = totalFloor,
                final_hp    = finalHp,
                // ゲームは放棄したランにバッジを付けない (RunHistoryUtilities.GetBadgesForPlayer)。
                badges      = badges,
            });
        sender.EnqueueEvents(stored.SessionId, stored.WriteToken, new[] { ev });
        return true;
    }
}
