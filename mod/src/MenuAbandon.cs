using System;
using System.Linq;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace StsStats;

/// <summary>
/// メインメニューとマルチプレイのメニューの「放棄」(docs/spec/data-sources.md「ラン・階」)。
/// この 2 つは RunManager を通らず、セーブデータから直接 RunHistoryUtilities.CreateRunHistoryEntry を呼ぶ
/// (NMainMenu.AbandonRun / NMultiplayerSubmenu.TryAbandonMultiplayerRun)。
/// </summary>
internal static class MenuAbandon
{
    /// <summary>RunHistoryUtilities.CreateRunHistoryEntry(run, victory, isAbandoned, platformType) の後。</summary>
    public static void CreateRunHistoryEntryPostfix(SerializableRun run, bool isAbandoned)
    {
        try
        {
            if (!isAbandoned) return;
            // ラン中の放棄は RunManager.OnEnded から同じメソッドが呼ばれ、Lifecycle が run_end を送る。
            if (RunManager.Instance.IsInProgress) return;

            var store  = ModEntry.SessionStore;
            var sender = ModEntry.HttpSender;
            if (store == null || sender == null) return;

            int totalFloor = run.MapPointHistory.Sum(act => act.Count);
            var finalHp = run.Players.ToDictionary(p => Identity.OfNetId(p.NetId), p => p.CurrentHp);
            bool sent = AbandonedRunReporter.Report(
                store, sender, Identity.LocalSteamId, run.SerializableRng?.Seed ?? "", totalFloor, finalHp);
            Log.Info($"[StsStats] Run abandoned from menu (floor {totalFloor}): run_end {(sent ? "sent" : "skipped, no stored session")}");
        }
        catch (Exception ex) { Log.Error($"[StsStats] menu abandon postfix error: {ex.Message}"); }
    }
}
