using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace StsStats;

/// <summary>
/// セッションのライフサイクル管理。
/// - 初回戦闘開始時に POST /sessions を呼び session_id / write_token / share_url を取得
/// - 共有URLをクリップボードにコピーし、ログにも出力
/// - 取得済みなら再呼び出しは no-op
/// バックエンド到達不能でもゲームは止めない（HTTP送信を諦め、JSONLログだけ動作させる）。
/// </summary>
internal static class SessionManager
{
    public static string? SessionId  { get; private set; }
    public static string? WriteToken { get; private set; }
    public static string? ShareUrl   { get; private set; }

    public static bool   IsReady => SessionId != null && WriteToken != null;
    private static bool  _attempted = false;

    /// <summary>
    /// セッション作成を試みる。run メタが分かっていれば一緒に渡す。
    /// 同 run 内では1度だけ呼ぶ前提。
    /// </summary>
    public static void EnsureSession(IApiClient api, CreateSessionRequest req)
    {
        if (IsReady || _attempted) return;
        _attempted = true;

        // ゲームスレッドをブロックしないようバックグラウンドで実行し、結果が来たら反映
        _ = Task.Run(async () =>
        {
            var result = await api.CreateSessionAsync(req);
            if (result == null)
            {
                Log.Error("[StsStats] Session creation failed; HTTP send disabled for this run");
                return;
            }
            SessionId  = result.SessionId;
            WriteToken = result.WriteToken;
            ShareUrl   = result.ShareUrl;

            CopyToClipboard(result.ShareUrl);
            Log.Info($"[StsStats] Session created: {result.ShareUrl}");

            // JSONL ログにも残す（make log で確認できるように）
            StatsLogger.LogSessionCreated(result.SessionId, result.ShareUrl);
        });
    }

    /// <summary>新しい run が始まる時に呼ぶ。次回 EnsureSession で再作成される。</summary>
    public static void ResetForNewRun()
    {
        SessionId  = null;
        WriteToken = null;
        ShareUrl   = null;
        _attempted = false;
    }

    private static void CopyToClipboard(string text)
    {
        try { DisplayServer.ClipboardSet(text); }
        catch (System.Exception ex)
        {
            Log.Error($"[StsStats] Clipboard copy failed: {ex.Message}");
        }
    }
}
