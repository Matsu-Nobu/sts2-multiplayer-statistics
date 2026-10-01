using System.Linq;
using MegaCrit.Sts2.Core.Runs;

namespace StsStats;

/// <summary>
/// 今のランに対応するサーバのセッションを確保する (SessionManager の入口)。
/// マルチプレイの非ホストは送信しないのでセッションも作らない (Identity.ShouldSend)。
/// </summary>
internal static class Session
{
    public static void Ensure(IRunState runState)
    {
        var api   = ModEntry.ApiClient;
        var store = ModEntry.SessionStore;
        if (api == null || store == null) return;
        if (!Identity.ShouldSend) return;

        ulong localId = Identity.LocalSteamId;
        var me = runState.Players.FirstOrDefault(p => Identity.Of(p) == localId.ToString()) ?? runState.Players.FirstOrDefault();
        string? hostName    = me != null ? Identity.NameOf(me) : null;
        string? characterId = me?.Character?.Id.Entry;
        int    ascension    = runState.AscensionLevel;
        string seed         = Lifecycle.SafeSeed(runState);
        string seedForKey   = string.IsNullOrEmpty(seed) ? "no-seed" : seed;
        string gameMode     = runState.GameMode.ToString();

        SessionManager.EnsureSession(
            lookupKey:         RunKey.Lookup(localId, seedForKey),
            currentTotalFloor: runState.TotalFloor,
            api:               api,
            requestBuilder:    (startedAt, runKey) => new CreateSessionRequest(
                HostName:    hostName,
                HostSteamId: localId != 0UL ? localId.ToString() : null,
                CharacterId: characterId,
                Ascension:   ascension,
                Seed:        string.IsNullOrEmpty(seed) ? null : seed),
            runMeta:           new RunMetaForKey(localId, seedForKey, characterId, ascension, gameMode),
            store:             store);
    }
}
