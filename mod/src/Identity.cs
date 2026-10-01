using System;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace StsStats;

/// <summary>
/// プレイヤー ID の正規化と名前の解決 (docs/redesign-v2.md §1.1)。
///
/// `Player.NetId` は通信方式で意味が変わる (デコンパイル確認済 v0.111.0):
///   - シングルプレイ: 常に 1 (NetSingleplayerGameService.NetId => 1uL)
///   - Steam マルチ:   各自の Steam ID (SteamHost/SteamClient.NetId)
///   - LAN (ENet):     ホストが 1
/// 送信する全 event の player_id は、ここで「NetId == 1 かつ自分がシングルプレイ / ホスト」なら
/// ローカルの Steam ID に置き換えた値に統一する。
/// </summary>
internal static class Identity
{
    private static ulong _localSteamId;

    /// <summary>ローカルの Steam ID。取れなければ 0。</summary>
    public static ulong LocalSteamId
    {
        get
        {
            if (_localSteamId != 0UL) return _localSteamId;
            try { _localSteamId = PlatformUtil.GetLocalPlayerId(PlatformUtil.PrimaryPlatform); }
            catch (Exception ex) { Log.Error($"[StsStats] Identity.LocalSteamId failed: {ex.Message}"); }
            return _localSteamId;
        }
    }

    /// <summary>このクライアントが送信してよいか。マルチプレイの非ホストは送らない (ホストが全員分を送る)。</summary>
    public static bool ShouldSend
    {
        get
        {
            try { return RunManager.Instance.NetService?.Type != NetGameType.Client; }
            catch { return true; }
        }
    }

    public static ulong NormalizeId(ulong netId)
    {
        if (netId != 1UL) return netId;
        NetGameType type;
        try { type = RunManager.Instance.NetService?.Type ?? NetGameType.Singleplayer; }
        catch { type = NetGameType.Singleplayer; }
        if (type == NetGameType.Singleplayer || type == NetGameType.Host)
        {
            ulong local = LocalSteamId;
            if (local != 0UL) return local;
        }
        return netId;
    }

    public static string Of(Player player) => NormalizeId(player.NetId).ToString();

    public static string OfNetId(ulong netId) => NormalizeId(netId).ToString();

    /// <summary>
    /// creature をプレイヤー ID に解決する。
    /// <paramref name="includePets"/> が true ならペット (Osty 等) を持ち主に解決する
    /// (与ダメ・付与の帰属用。被弾側では false)。
    /// </summary>
    public static string? OfCreature(Creature? creature, bool includePets = false)
    {
        if (creature == null) return null;
        var player = creature.Player ?? (includePets ? creature.PetOwner : null);
        return player != null ? Of(player) : null;
    }

    /// <summary>表示名。Steam 名が取れなければキャラクター ID。</summary>
    public static string NameOf(Player player)
    {
        ulong id = NormalizeId(player.NetId);
        try
        {
            string name = PlatformUtil.GetPlayerNameRaw(PlatformUtil.PrimaryPlatform, id);
            if (!string.IsNullOrEmpty(name) && name != id.ToString()) return name;
        }
        catch (Exception ex) { Log.Error($"[StsStats] Identity.NameOf failed: {ex.Message}"); }
        return player.Character?.Id.Entry ?? id.ToString();
    }
}
