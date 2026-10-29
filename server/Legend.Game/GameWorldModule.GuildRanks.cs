using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Storage;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    public static class GuildRankNames
    {
        public const string Leader = "leader";
        public const string ViceLeader = "vice_leader";
        public const string WarehouseAdmin = "warehouse_admin";
        public const string Member = "member";

        public static bool IsAssignable(string rank) =>
            string.Equals(rank, ViceLeader, StringComparison.OrdinalIgnoreCase)
            || string.Equals(rank, WarehouseAdmin, StringComparison.OrdinalIgnoreCase)
            || string.Equals(rank, Member, StringComparison.OrdinalIgnoreCase);

        public static string Normalize(string rank)
        {
            if (string.Equals(rank, Leader, StringComparison.OrdinalIgnoreCase)) return Leader;
            if (string.Equals(rank, ViceLeader, StringComparison.OrdinalIgnoreCase)) return ViceLeader;
            if (string.Equals(rank, WarehouseAdmin, StringComparison.OrdinalIgnoreCase)) return WarehouseAdmin;
            return Member;
        }
    }

    internal static string ResolveGuildRank(GuildRecord guild, string characterId)
    {
        if (string.Equals(guild.LeaderCharacterId, characterId, StringComparison.Ordinal))
            return GuildRankNames.Leader;
        var map = GuildRanksJson.Parse(guild.RanksJson);
        if (map.TryGetValue(characterId, out var r) && !string.IsNullOrWhiteSpace(r))
            return GuildRankNames.Normalize(r);
        return GuildRankNames.Member;
    }

    private static int CountRank(GuildRecord guild, string rank)
    {
        var map = GuildRanksJson.Parse(guild.RanksJson);
        int n = 0;
        foreach (var kv in map)
        {
            if (string.Equals(guild.LeaderCharacterId, kv.Key, StringComparison.Ordinal))
                continue;
            if (!guild.MemberCharacterIds.Contains(kv.Key, StringComparer.Ordinal))
                continue;
            if (string.Equals(GuildRankNames.Normalize(kv.Value), rank, StringComparison.Ordinal))
                n++;
        }
        return n;
    }

    private bool CanClearByRank(WorldPlayerEntity player, GuildRecord guild, GuildConfig cfg)
    {
        string rank = ResolveGuildRank(guild, player.CharacterId);
        return rank switch
        {
            GuildRankNames.Leader => cfg.LeaderCanClear,
            GuildRankNames.ViceLeader => cfg.ViceLeaderCanClear,
            GuildRankNames.WarehouseAdmin => cfg.WarehouseAdminCanClear,
            _ => cfg.MemberCanClear
        };
    }

    private async Task AppendWarehouseAuditAndPublishAsync(
        IClientSession session,
        WorldPlayerEntity player,
        string guildId,
        string guildName,
        string action,
        string itemId,
        int count,
        CancellationToken ct)
    {
        var entry = new GuildWarehouseAuditRecord
        {
            AuditId = Guid.NewGuid().ToString("N"),
            GuildId = guildId,
            ActorCharacterId = player.CharacterId,
            ActorName = player.Name,
            Action = action,
            ItemId = itemId ?? "",
            Count = count,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _storage.Guilds.AppendWarehouseAuditAsync(entry, ct);
        await _eventBus.PublishAsync(new GuildWarehouseAuditEvent(
            session, player.Name, player.CharacterId, guildId, guildName, action, entry.ItemId, count, entry.AuditId), ct);
    }

    private async Task HandleGuildSetRankAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = GuildSetRankReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.GuildSetRankResp, frame.Seq, new GuildSetRankResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        if (string.IsNullOrEmpty(player.GuildId))
        {
            await session.SendAsync(MsgId.GuildSetRankResp, frame.Seq, new GuildSetRankResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNotInGuild,
                Message = "未加入行会"
            }, ct);
            return;
        }

        string targetId = (req.TargetCharacterId ?? string.Empty).Trim();
        string newRankRaw = (req.Rank ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(targetId) || !GuildRankNames.IsAssignable(newRankRaw))
        {
            await session.SendAsync(MsgId.GuildSetRankResp, frame.Seq, new GuildSetRankResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildInvalidRank,
                Message = "职级无效（仅 vice_leader / warehouse_admin / member）"
            }, ct);
            return;
        }

        string newRank = GuildRankNames.Normalize(newRankRaw);
        var cfg = GuildCfg;

        var guild = await _storage.Guilds.GetByIdAsync(player.GuildId, ct);
        if (guild is null)
        {
            await session.SendAsync(MsgId.GuildSetRankResp, frame.Seq, new GuildSetRankResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildNotFound,
                Message = "行会不存在"
            }, ct);
            return;
        }

        if (!cfg.LeaderCanSetRank || !string.Equals(guild.LeaderCharacterId, player.CharacterId, StringComparison.Ordinal))
        {
            await session.SendAsync(MsgId.GuildSetRankResp, frame.Seq, new GuildSetRankResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildRankDenied,
                Message = "仅会长可任命/撤职"
            }, ct);
            return;
        }

        if (string.Equals(targetId, guild.LeaderCharacterId, StringComparison.Ordinal))
        {
            await session.SendAsync(MsgId.GuildSetRankResp, frame.Seq, new GuildSetRankResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildRankDenied,
                Message = "不可变更会长职级"
            }, ct);
            return;
        }

        if (!guild.MemberCharacterIds.Contains(targetId, StringComparer.Ordinal))
        {
            await session.SendAsync(MsgId.GuildSetRankResp, frame.Seq, new GuildSetRankResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildRankDenied,
                Message = "目标不是本会成员"
            }, ct);
            return;
        }

        string oldRank = ResolveGuildRank(guild, targetId);
        if (string.Equals(oldRank, newRank, StringComparison.Ordinal))
        {
            var chSame = await _storage.Characters.GetByIdAsync(targetId, ct);
            await session.SendAsync(MsgId.GuildSetRankResp, frame.Seq, new GuildSetRankResp
            {
                Success = true,
                ErrorCode = ProtocolErrorCode.ErrNone,
                Message = "职级未变",
                TargetCharacterId = targetId,
                TargetName = chSame?.Name ?? targetId,
                OldRank = oldRank,
                NewRank = newRank
            }, ct);
            return;
        }

        // Enforce capacity when promoting into a limited rank
        if (string.Equals(newRank, GuildRankNames.ViceLeader, StringComparison.Ordinal)
            && !string.Equals(oldRank, GuildRankNames.ViceLeader, StringComparison.Ordinal)
            && CountRank(guild, GuildRankNames.ViceLeader) >= cfg.MaxViceLeaders)
        {
            await session.SendAsync(MsgId.GuildSetRankResp, frame.Seq, new GuildSetRankResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildRankDenied,
                Message = $"副会长名额已满（最多 {cfg.MaxViceLeaders}）"
            }, ct);
            return;
        }

        if (string.Equals(newRank, GuildRankNames.WarehouseAdmin, StringComparison.Ordinal)
            && !string.Equals(oldRank, GuildRankNames.WarehouseAdmin, StringComparison.Ordinal)
            && CountRank(guild, GuildRankNames.WarehouseAdmin) >= cfg.MaxWarehouseAdmins)
        {
            await session.SendAsync(MsgId.GuildSetRankResp, frame.Seq, new GuildSetRankResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildRankDenied,
                Message = $"仓管名额已满（最多 {cfg.MaxWarehouseAdmins}）"
            }, ct);
            return;
        }

        var map = GuildRanksJson.Parse(guild.RanksJson);
        if (string.Equals(newRank, GuildRankNames.Member, StringComparison.Ordinal))
            map.Remove(targetId);
        else
            map[targetId] = newRank;
        string ranksJson = GuildRanksJson.Serialize(map);
        await _storage.Guilds.SaveRanksAsync(guild.GuildId, ranksJson, ct);

        var ch = await _storage.Characters.GetByIdAsync(targetId, ct);
        string targetName = ch?.Name ?? targetId;

        // Update online target's GuildRank if present
        foreach (var online in EnumerateOnlinePlayers())
        {
            if (string.Equals(online.CharacterId, targetId, StringComparison.Ordinal))
            {
                online.GuildRank = newRank;
                break;
            }
        }

        await session.SendAsync(MsgId.GuildSetRankResp, frame.Seq, new GuildSetRankResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = $"已将 {targetName} 设为 {newRank}",
            TargetCharacterId = targetId,
            TargetName = targetName,
            OldRank = oldRank,
            NewRank = newRank
        }, ct);

        await _eventBus.PublishAsync(new GuildRankChangedEvent(
            session, player.Name, player.CharacterId, guild.GuildId, guild.Name,
            targetId, targetName, oldRank, newRank), ct);
    }

    private async Task HandleGuildWarehouseListAuditAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = GuildWarehouseListAuditReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.GuildWarehouseListAuditResp, frame.Seq, new GuildWarehouseListAuditResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        if (string.IsNullOrEmpty(player.GuildId))
        {
            await session.SendAsync(MsgId.GuildWarehouseListAuditResp, frame.Seq, new GuildWarehouseListAuditResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNotInGuild,
                Message = "未加入行会"
            }, ct);
            return;
        }

        var guild = await _storage.Guilds.GetByIdAsync(player.GuildId, ct);
        if (guild is null)
        {
            await session.SendAsync(MsgId.GuildWarehouseListAuditResp, frame.Seq, new GuildWarehouseListAuditResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildNotFound,
                Message = "行会不存在"
            }, ct);
            return;
        }

        int limit = req.Limit > 0 ? req.Limit : GuildCfg.AuditLogLimit;
        if (limit > 200) limit = 200;
        var entries = await _storage.Guilds.ListWarehouseAuditAsync(guild.GuildId, limit, ct);
        var resp = new GuildWarehouseListAuditResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "ok",
            GuildId = guild.GuildId
        };
        foreach (var e in entries)
        {
            resp.Entries.Add(new GuildWarehouseAuditEntry
            {
                AuditId = e.AuditId,
                ActorCharacterId = e.ActorCharacterId,
                ActorName = e.ActorName,
                Action = e.Action,
                ItemId = e.ItemId,
                Count = e.Count,
                CreatedUnixMs = e.CreatedAt.ToUnixTimeMilliseconds()
            });
        }
        await session.SendAsync(MsgId.GuildWarehouseListAuditResp, frame.Seq, resp, ct);
    }
}
