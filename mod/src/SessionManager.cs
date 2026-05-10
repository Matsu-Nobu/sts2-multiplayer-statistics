using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace StsStats;

/// <summary>
/// セッションのライフサイクル管理。
/// HookPatches 側が「現在の run の lookup_key と TotalFloor」を提供し、
/// SessionManager が「同じ run の再開ならストアから復元、新規 run なら POST /sessions」を判定する。
/// バックエンド到達不能でもゲームは止めない（HTTP送信を諦め、JSONLログだけ動作させる）。
/// </summary>
internal static class SessionManager
{
    public static string? SessionId             { get; private set; }
    public static string? WriteToken            { get; private set; }
    public static string? ShareUrl              { get; private set; }
    public static string? CurrentLookupKey      { get; private set; }
    public static string? CurrentRunKey         { get; private set; }
    public static int     CurrentLastSeenFloor  { get; private set; }
    public static bool    RunStartAlreadyEmitted { get; private set; }

    public static bool IsReady => SessionId != null && WriteToken != null;
    private static bool _attempted = false;

    /// <summary>
    /// セッションを確保する。
    ///
    ///   - ストアに lookup_key の保存があり、現在の TotalFloor &gt;= stored.LastSeenFloor
    ///     → 同じ run の継続。session_id を復元、POST 不要、IsReady=true 即時。
    ///   - 保存があるが現在の TotalFloor &lt; stored.LastSeenFloor
    ///     → 同じ seed の新規 run。POST して上書き保存。
    ///   - 保存が無い
    ///     → POST して新規保存。
    /// </summary>
    public static void EnsureSession(
        string lookupKey,
        int currentTotalFloor,
        IApiClient api,
        Func<string /*startedAt*/, string /*runKey*/, CreateSessionRequest> requestBuilder,
        RunMetaForKey runMeta,
        RunSessionStore store)
    {
        // 同じ lookup_key で既に成立済みなら、TotalFloor だけ更新して終わる
        if (CurrentLookupKey == lookupKey && IsReady)
        {
            UpdateLastSeenFloor(currentTotalFloor, store);
            return;
        }

        // lookup_key が変わっていればこれまでの状態を捨てる。
        // EventBuffer 側の combat_index / pending / outgoing も new run へ
        // 持ち越さないようにここで一緒にリセットする。これをやらないと、
        // 旧 run の AfterCombatEnd 等が遅れて発火したときに、新セッション側に
        // combat_index = 旧最後値 の空戦闘として残ってしまう。
        if (CurrentLookupKey != lookupKey)
        {
            // 旧 SessionId がまだ参照可能な状態で _outgoing を吐き切る
            EventBuffer.Reset();
            ResetState();
            CurrentLookupKey = lookupKey;
        }

        if (_attempted) return;
        _attempted = true;

        // 1. ストアから復元できないか試す
        var stored = store.Load(lookupKey);
        if (stored != null && currentTotalFloor >= stored.LastSeenFloor)
        {
            ApplyStored(stored);
            UpdateLastSeenFloor(currentTotalFloor, store);
            Log.Info($"[StsStats] Resumed session for run (floor {stored.LastSeenFloor} → {currentTotalFloor}): {stored.ShareUrl}");
            StatsLogger.LogSessionCreated(stored.SessionId, stored.ShareUrl);
            EventBuffer.FlushPending();
            return;
        }

        // 2. 新規作成（バックグラウンド）
        // started_at は「mod が検出した時刻」をその run の開始時刻と扱う
        string startedAt = DateTime.UtcNow.ToString("O");
        string runKey = RunKey.Compute(
            hostSteamId: runMeta.HostSteamId,
            seed:        runMeta.Seed,
            characterId: runMeta.CharacterId,
            ascension:   runMeta.Ascension,
            gameMode:    runMeta.GameMode,
            startedAtIso: startedAt
        );
        var req = requestBuilder(startedAt, runKey);

        _ = Task.Run(async () =>
        {
            var result = await api.CreateSessionAsync(req);
            if (result == null)
            {
                Log.Error("[StsStats] Session creation failed; will retry on next combat");
                _attempted = false;        // 次の BeforeCombatStart で再試行
                return;
            }

            SessionId            = result.SessionId;
            WriteToken           = result.WriteToken;
            ShareUrl             = result.ShareUrl;
            CurrentRunKey        = runKey;
            CurrentLastSeenFloor = currentTotalFloor;
            RunStartAlreadyEmitted = false;

            store.Save(new StoredSession(
                LookupKey:       lookupKey,
                RunKey:          runKey,
                SessionId:       result.SessionId,
                WriteToken:      result.WriteToken,
                ShareUrl:        result.ShareUrl,
                CharacterId:     runMeta.CharacterId,
                Ascension:       runMeta.Ascension,
                Seed:            runMeta.Seed,
                GameMode:        runMeta.GameMode,
                HostSteamId:     runMeta.HostSteamId,
                StartedAt:       startedAt,
                LastSeenFloor:   currentTotalFloor,
                RunStartEmitted: false
            ));

            CopyToClipboard(result.ShareUrl);
            string action = (stored != null) ? "Replaced previous run session (fresh re-run detected)" : "Session created";
            Log.Info($"[StsStats] {action}: {result.ShareUrl}");
            StatsLogger.LogSessionCreated(result.SessionId, result.ShareUrl);
            EventBuffer.FlushPending();
        });
    }

    /// <summary>run_start を emit したことを記録（永続化）。</summary>
    public static void MarkRunStartEmitted(RunSessionStore store)
    {
        if (RunStartAlreadyEmitted || CurrentLookupKey == null) return;
        RunStartAlreadyEmitted = true;

        var existing = store.Load(CurrentLookupKey);
        if (existing != null)
            store.Save(existing with { RunStartEmitted = true });
    }

    private static void UpdateLastSeenFloor(int currentTotalFloor, RunSessionStore store)
    {
        if (CurrentLookupKey == null) return;
        if (currentTotalFloor <= CurrentLastSeenFloor) return;
        CurrentLastSeenFloor = currentTotalFloor;
        var existing = store.Load(CurrentLookupKey);
        if (existing != null)
            store.Save(existing with { LastSeenFloor = currentTotalFloor });
    }


    private static void ApplyStored(StoredSession s)
    {
        SessionId             = s.SessionId;
        WriteToken            = s.WriteToken;
        ShareUrl              = s.ShareUrl;
        CurrentRunKey         = s.RunKey;
        CurrentLastSeenFloor  = s.LastSeenFloor;
        RunStartAlreadyEmitted = s.RunStartEmitted;
        // 注: combat_index は floor と同値 (BeginCombat で _combatIndex = _floor)。
        // floor は STS2 のセーブに乗ってて resume 後 BeforeCombatStart で正しい値が
        // 来るので、別途永続化不要。
    }

    private static void ResetState()
    {
        SessionId            = null;
        WriteToken           = null;
        ShareUrl             = null;
        CurrentRunKey        = null;
        CurrentLastSeenFloor = 0;
        RunStartAlreadyEmitted = false;
        _attempted = false;
    }

    private static void CopyToClipboard(string text)
    {
        try { DisplayServer.ClipboardSet(text); }
        catch (Exception ex) { Log.Error($"[StsStats] Clipboard copy failed: {ex.Message}"); }
    }
}

/// <summary>SessionManager に渡す run のメタ情報（run_key 計算と StoredSession 構築に使う）。</summary>
internal record RunMetaForKey(
    ulong   HostSteamId,
    string  Seed,
    string? CharacterId,
    int     Ascension,
    string  GameMode
);
