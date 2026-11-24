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

    /// <summary>サーバの POST /imports/run の回数の上限 (1 分 120 件) に収まる間隔で送る。</summary>
    private static readonly TimeSpan GapBetweenRuns = TimeSpan.FromSeconds(0.5);
    /// <summary>それでも上限で断られたら、この時間待って送り直す。上限は直近 1 分の件数なので、10 秒待てば数件分空く。</summary>
    private static readonly TimeSpan RateLimitWait = TimeSpan.FromSeconds(10);
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
            var result = await Task.Run(() => HistoryUploader.RunAsync(api, candidates, progress, GapBetweenRuns, RateLimitWait));
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
