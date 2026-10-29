using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Storage;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    public readonly record struct OnlinePlayerSummary(
        string CharacterId,
        string Name,
        string AccountId,
        string MapId,
        int Level,
        string GuildId,
        string ClassId);

    public void ReplaceVersionPack(LoadedVersionPack pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        _versionPack = pack;
    }

    public IReadOnlyList<OnlinePlayerSummary> GetOnlineSummaries()
    {
        var list = new List<OnlinePlayerSummary>();
        foreach (var p in EnumerateOnlinePlayers())
        {
            list.Add(new OnlinePlayerSummary(
                p.CharacterId,
                p.Name,
                p.Session.AccountId ?? string.Empty,
                p.MapId,
                p.Level,
                p.GuildId,
                p.ClassId));
        }
        return list;
    }

    public async Task<(bool Ok, string Message, bool IsActive, string OwnerGuildId, string OwnerGuildName)> AdminSetSiegeAsync(
        string action, CancellationToken ct = default)
    {
        action = (action ?? "start").Trim().ToLowerInvariant();
        if (action is "start" or "open")
        {
            await _siegeLock.WaitAsync(ct);
            try { _siegeIsActive = true; }
            finally { _siegeLock.Release(); }
            await BroadcastSiegeStateNotifyAsync("started", ct);
            return (true, "沙巴克攻城战已开启！", true, _siegeOwnerGuildId, _siegeOwnerGuildName);
        }

        if (action is "stop" or "end" or "close")
        {
            string winnerId;
            string winnerName;
            await _siegeLock.WaitAsync(ct);
            try
            {
                _siegeIsActive = false;
                winnerId = _siegeOwnerGuildId;
                winnerName = _siegeOwnerGuildName;
                await PersistSiegeStateLockedAsync(ct);
            }
            finally { _siegeLock.Release(); }

            if (!string.IsNullOrEmpty(winnerId))
                await RefreshOnlineCastleLordBuffsAsync(winnerId, ct);

            await BroadcastSiegeStateNotifyAsync("ended", ct);
            await _eventBus.PublishAsync(new SiegeEndedEvent(_versionPack.Siege.CastleId, winnerId, winnerName), ct);
            string msg = string.IsNullOrEmpty(winnerId)
                ? "沙巴克攻城战已结束（无占领行会）。"
                : $"沙巴克攻城战已结束，占领行会：[{winnerName}]！";
            return (true, msg, false, winnerId, winnerName);
        }

        return (false, $"未知攻城控制指令: {action}", _siegeIsActive, _siegeOwnerGuildId, _siegeOwnerGuildName);
    }

    public (bool Active, string OwnerGuildId, string OwnerGuildName) GetSiegeSnapshot()
        => (_siegeIsActive, _siegeOwnerGuildId, _siegeOwnerGuildName);

    /// <summary>Notify + kick online sessions belonging to the account (best-effort).</summary>
    public async Task KickAccountAsync(string accountId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            return;
        foreach (var p in EnumerateOnlinePlayers().ToList())
        {
            if (!string.Equals(p.Session.AccountId, accountId, StringComparison.Ordinal))
                continue;
            try
            {
                await p.Session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice
                {
                    NoticeType = "banned",
                    Title = "封禁",
                    Content = string.IsNullOrWhiteSpace(reason) ? "账号已被封禁" : reason
                }, ct);
            }
            catch { /* ignore */ }
            p.Session.RequestClose();
        }
    }

    public async Task NotifyMuteAsync(string accountId, bool muted, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            return;
        foreach (var p in EnumerateOnlinePlayers().ToList())
        {
            if (!string.Equals(p.Session.AccountId, accountId, StringComparison.Ordinal))
                continue;
            try
            {
                await p.Session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice
                {
                    NoticeType = muted ? "muted" : "unmuted",
                    Title = muted ? "禁言" : "解除禁言",
                    Content = string.IsNullOrWhiteSpace(reason)
                        ? (muted ? "你已被禁言" : "禁言已解除")
                        : reason
                }, ct);
            }
            catch { /* ignore */ }
        }
    }
}
