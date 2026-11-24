using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace StsStats;

/// <summary>セーブデータの履歴にある 1 ランの、送る前の情報。Build は送ると決めたランにだけ呼ぶ。</summary>
internal record ImportCandidate(ImportCheckRun Check, Func<ImportPayload> Build);

/// <summary>1 ランを送るときのセッションの内容と、送るイベント。</summary>
internal record ImportPayload(ImportSession Session, IReadOnlyList<EventRecord> Events);

internal enum ImportStatus { Sent, NothingToSend, Failed }

internal record ImportResult(ImportStatus Status, int Sent);

/// <summary>
/// セーブデータの履歴をサーバに送る (docs/spec/data-sources.md「セーブデータの履歴の取り込み」)。ゲームの DLL に依存しない部分。
/// </summary>
internal static class HistoryUploader
{
    public const int MaxCheckRuns = 1000;
    /// <summary>サーバの回数の上限で断られたときに、同じランを送り直す回数。</summary>
    public const int RateLimitRetries = 6;

    public static async Task<ImportResult> RunAsync(
        IImportApi api,
        IReadOnlyList<ImportCandidate> candidates,
        Action<int, int> progress,
        TimeSpan gapBetweenRuns,
        TimeSpan rateLimitWait)
    {
        var missing = new List<ImportCandidate>();
        for (int start = 0; start < candidates.Count; start += MaxCheckRuns)
        {
            var chunk = candidates.Skip(start).Take(MaxCheckRuns).ToList();
            int[]? idx = await api.CheckImportsAsync(chunk.Select(c => c.Check).ToList());
            if (idx == null) return new ImportResult(ImportStatus.Failed, 0);
            missing.AddRange(idx.Where(i => i >= 0 && i < chunk.Count).Select(i => chunk[i]));
        }

        // 放棄したランは、サーバにあるセッションの結果を埋めるためだけに /imports/check に送り、ランそのものは送らない。
        var toSend = missing.Where(c => c.Check.Outcome is "victory" or "death").ToList();
        int sent = 0;
        for (int i = 0; i < toSend.Count; i++)
        {
            progress(i, toSend.Count);
            if (i > 0) await Task.Delay(gapBetweenRuns);

            var payload = toSend[i].Build();
            var status = await api.ImportRunAsync(payload.Session, payload.Events);
            for (int retry = 0; status == ImportRunStatus.RateLimited && retry < RateLimitRetries; retry++)
            {
                await Task.Delay(rateLimitWait);
                status = await api.ImportRunAsync(payload.Session, payload.Events);
            }
            if (status == ImportRunStatus.AlreadyExists) continue;
            if (status != ImportRunStatus.Created) return new ImportResult(ImportStatus.Failed, sent);
            sent++;
        }
        progress(toSend.Count, toSend.Count);
        return new ImportResult(sent > 0 ? ImportStatus.Sent : ImportStatus.NothingToSend, sent);
    }
}
