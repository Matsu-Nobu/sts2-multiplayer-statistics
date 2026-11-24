using System;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Logging;

namespace StsStats;

/// <summary>
/// 「セーブデータを送る」のボタンから呼ぶ入口。BaseLib 用の DLL (StsStatsBaseLib.dll) が呼ぶので public にする。
/// </summary>
public static class HistoryImport
{
    /// <summary>送れたら 0 以上の件数、送るランが無ければ 0、失敗したら -1。</summary>
    public const int Failed = -1;

    /// <summary>セーブの取り込みは 1 回に 1 つだけ。サーバの POST /sessions の回数の上限 (1 分 30 件) に収まる間隔で送る。</summary>
    private static readonly TimeSpan GapBetweenRuns = TimeSpan.FromSeconds(2);
    private static int _running;

    public static bool IsRunning => Volatile.Read(ref _running) == 1;

    /// <summary>
    /// 履歴を読んで送る。progress(送った数, 送る数) はゲームのスレッドとは別のスレッドから呼ばれる。
    /// 既に送っている途中なら何もせずに Failed を返す。
    /// </summary>
    public static async Task<int> RunAsync(Action<int, int> progress)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return Failed;
        try
        {
            if (ModEntry.ApiClient is not IImportApi api) return Failed;
            var candidates = HistoryReader.ReadAll();
            Log.Info($"[StsStats] history import: {candidates.Count} runs in save data");
            var result = await Task.Run(() => HistoryUploader.RunAsync(api, candidates, progress, GapBetweenRuns));
            Log.Info($"[StsStats] history import: {result.Status}, sent {result.Sent}");
            return result.Status == ImportStatus.Failed ? Failed : result.Sent;
        }
        catch (Exception ex)
        {
            Log.Error($"[StsStats] history import error: {ex}");
            return Failed;
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
