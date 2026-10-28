using System.Collections.Concurrent;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Storage;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private readonly object _partyGate = new();
    private readonly ConcurrentDictionary<string, PartyRuntime> _parties = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _charToParty = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingPartyInvite> _pendingInvites = new(StringComparer.Ordinal);

    private PartyConfig PartyCfg => _versionPack.Party ?? new PartyConfig();

    private sealed class PartyRuntime
    {
        public required string PartyId { get; init; }
        public string LeaderCharacterId { get; set; } = string.Empty;
        public List<string> MemberCharacterIds { get; } = new();
    }

    private sealed class PendingPartyInvite
    {
        public required string PartyId { get; init; }
        public required string InviterCharacterId { get; init; }
        public required string InviterName { get; init; }
        public required string InviteeCharacterId { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
    }

    private WorldPlayerEntity? FindOnlineByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string n = name.Trim();
        foreach (var p in EnumerateOnlinePlayers())
        {
            if (string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }

    private WorldPlayerEntity? FindOnlineByCharacterId(string characterId)
    {
        foreach (var p in EnumerateOnlinePlayers())
        {
            if (string.Equals(p.CharacterId, characterId, StringComparison.Ordinal))
                return p;
        }
        return null;
    }

    private async Task HandlePartyInviteAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = PartyInviteReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var leader) || leader is null)
        {
            await session.SendAsync(MsgId.PartyInviteResp, frame.Seq, new PartyInviteResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        string targetName = (req.TargetName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(targetName))
        {
            await session.SendAsync(MsgId.PartyInviteResp, frame.Seq, new PartyInviteResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrPartyDenied,
                Message = "目标名为空"
            }, ct);
            return;
        }

        if (string.Equals(targetName, leader.Name, StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync(MsgId.PartyInviteResp, frame.Seq, new PartyInviteResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrPartyDenied,
                Message = "不能邀请自己"
            }, ct);
            return;
        }

        var target = FindOnlineByName(targetName);
        if (target is null)
        {
            await session.SendAsync(MsgId.PartyInviteResp, frame.Seq, new PartyInviteResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrTargetOffline,
                Message = "目标不在线"
            }, ct);
            return;
        }

        if (_charToParty.ContainsKey(target.CharacterId))
        {
            await session.SendAsync(MsgId.PartyInviteResp, frame.Seq, new PartyInviteResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrAlreadyInParty,
                Message = "目标已在队伍中"
            }, ct);
            return;
        }

        PartyRuntime party;
        lock (_partyGate)
        {
            if (_charToParty.TryGetValue(leader.CharacterId, out string? existingPartyId)
                && _parties.TryGetValue(existingPartyId, out var existing))
            {
                if (!string.Equals(existing.LeaderCharacterId, leader.CharacterId, StringComparison.Ordinal))
                {
                    party = existing; // mark for error below
                    // leave lock then respond
                    goto NotLeader;
                }
                if (existing.MemberCharacterIds.Count >= PartyCfg.MaxMembers)
                {
                    party = existing;
                    goto PartyFull;
                }
                party = existing;
            }
            else
            {
                party = new PartyRuntime
                {
                    PartyId = Guid.NewGuid().ToString("N"),
                    LeaderCharacterId = leader.CharacterId
                };
                party.MemberCharacterIds.Add(leader.CharacterId);
                _parties[party.PartyId] = party;
                _charToParty[leader.CharacterId] = party.PartyId;
                leader.PartyId = party.PartyId;
            }

            _pendingInvites[target.CharacterId] = new PendingPartyInvite
            {
                PartyId = party.PartyId,
                InviterCharacterId = leader.CharacterId,
                InviterName = leader.Name,
                InviteeCharacterId = target.CharacterId,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2)
            };
        }

        await target.Session.SendAsync(MsgId.PartyInviteNotify, 0, new PartyInviteNotify
        {
            PartyId = party.PartyId,
            InviterCharacterId = leader.CharacterId,
            InviterName = leader.Name
        }, ct);

        await session.SendAsync(MsgId.PartyInviteResp, frame.Seq, new PartyInviteResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "邀请已发送",
            PartyId = party.PartyId,
            TargetName = target.Name
        }, ct);
        return;

    NotLeader:
        await session.SendAsync(MsgId.PartyInviteResp, frame.Seq, new PartyInviteResp
        {
            Success = false,
            ErrorCode = ProtocolErrorCode.ErrPartyDenied,
            Message = "只有队长可以邀请"
        }, ct);
        return;

    PartyFull:
        await session.SendAsync(MsgId.PartyInviteResp, frame.Seq, new PartyInviteResp
        {
            Success = false,
            ErrorCode = ProtocolErrorCode.ErrPartyFull,
            Message = $"队伍已满（上限 {PartyCfg.MaxMembers}）"
        }, ct);
    }

    private async Task HandlePartyAcceptAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = PartyAcceptReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.PartyAcceptResp, frame.Seq, new PartyAcceptResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        if (!req.Accept)
        {
            _pendingInvites.TryRemove(player.CharacterId, out _);
            await session.SendAsync(MsgId.PartyAcceptResp, frame.Seq, new PartyAcceptResp
            {
                Success = true,
                ErrorCode = ProtocolErrorCode.ErrNone,
                Message = "已拒绝邀请"
            }, ct);
            return;
        }

        if (_charToParty.ContainsKey(player.CharacterId))
        {
            await session.SendAsync(MsgId.PartyAcceptResp, frame.Seq, new PartyAcceptResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrAlreadyInParty,
                Message = "你已在队伍中"
            }, ct);
            return;
        }

        if (!_pendingInvites.TryGetValue(player.CharacterId, out var invite)
            || (!string.IsNullOrWhiteSpace(req.PartyId)
                && !string.Equals(req.PartyId, invite.PartyId, StringComparison.Ordinal)))
        {
            await session.SendAsync(MsgId.PartyAcceptResp, frame.Seq, new PartyAcceptResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrPartyNotFound,
                Message = "没有待处理的邀请"
            }, ct);
            return;
        }

        if (invite.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _pendingInvites.TryRemove(player.CharacterId, out _);
            await session.SendAsync(MsgId.PartyAcceptResp, frame.Seq, new PartyAcceptResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrPartyDenied,
                Message = "邀请已过期"
            }, ct);
            return;
        }

        PartyRuntime? party;
        string leaderName = invite.InviterName;
        ProtocolErrorCode failCode = ProtocolErrorCode.ErrNone;
        string failMsg = "";
        lock (_partyGate)
        {
            if (!_parties.TryGetValue(invite.PartyId, out party) || party is null)
            {
                party = null;
                failCode = ProtocolErrorCode.ErrPartyNotFound;
                failMsg = "队伍不存在";
            }
            else if (_charToParty.ContainsKey(player.CharacterId))
            {
                failCode = ProtocolErrorCode.ErrAlreadyInParty;
                failMsg = "你已在队伍中";
            }
            else if (party.MemberCharacterIds.Count >= PartyCfg.MaxMembers)
            {
                failCode = ProtocolErrorCode.ErrPartyFull;
                failMsg = $"队伍已满（上限 {PartyCfg.MaxMembers}）";
            }
            else
            {
                party.MemberCharacterIds.Add(player.CharacterId);
                _charToParty[player.CharacterId] = party.PartyId;
                player.PartyId = party.PartyId;
                _pendingInvites.TryRemove(player.CharacterId, out _);
            }
        }

        if (failCode != ProtocolErrorCode.ErrNone)
        {
            await session.SendAsync(MsgId.PartyAcceptResp, frame.Seq, new PartyAcceptResp
            {
                Success = false,
                ErrorCode = failCode,
                Message = failMsg
            }, ct);
            return;
        }

        if (party is null)
        {
            await session.SendAsync(MsgId.PartyAcceptResp, frame.Seq, new PartyAcceptResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrPartyNotFound,
                Message = "队伍不存在"
            }, ct);
            return;
        }

        var leader = FindOnlineByCharacterId(party.LeaderCharacterId);
        if (leader != null) leaderName = leader.Name;

        await session.SendAsync(MsgId.PartyAcceptResp, frame.Seq, new PartyAcceptResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "已加入队伍",
            PartyId = party.PartyId
        }, ct);

        await BroadcastPartyInfoAsync(party, "join", ct);
        await _eventBus.PublishAsync(new PartyJoinedEvent(session, player.Name, player.CharacterId, party.PartyId, leaderName), ct);
    }

    private async Task HandlePartyLeaveAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var _ = PartyLeaveReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.PartyLeaveResp, frame.Seq, new PartyLeaveResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        if (!await LeavePartyInternalAsync(player, "leave", ct))
        {
            await session.SendAsync(MsgId.PartyLeaveResp, frame.Seq, new PartyLeaveResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNotInParty,
                Message = "不在队伍中"
            }, ct);
            return;
        }

        await session.SendAsync(MsgId.PartyLeaveResp, frame.Seq, new PartyLeaveResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "已离开队伍"
        }, ct);
    }

    private async Task HandlePartyKickAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = PartyKickReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var leader) || leader is null)
        {
            await session.SendAsync(MsgId.PartyKickResp, frame.Seq, new PartyKickResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        string targetId = (req.TargetCharacterId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(targetId) || string.Equals(targetId, leader.CharacterId, StringComparison.Ordinal))
        {
            await session.SendAsync(MsgId.PartyKickResp, frame.Seq, new PartyKickResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrPartyDenied,
                Message = "无效的踢人目标"
            }, ct);
            return;
        }

        if (!_charToParty.TryGetValue(leader.CharacterId, out string? partyId)
            || !_parties.TryGetValue(partyId, out var party)
            || !string.Equals(party.LeaderCharacterId, leader.CharacterId, StringComparison.Ordinal))
        {
            await session.SendAsync(MsgId.PartyKickResp, frame.Seq, new PartyKickResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrPartyDenied,
                Message = "只有队长可以踢人"
            }, ct);
            return;
        }

        if (!party.MemberCharacterIds.Contains(targetId))
        {
            await session.SendAsync(MsgId.PartyKickResp, frame.Seq, new PartyKickResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrPartyDenied,
                Message = "目标不在队伍中"
            }, ct);
            return;
        }

        var target = FindOnlineByCharacterId(targetId);
        if (target != null)
        {
            await LeavePartyInternalAsync(target, "kick", ct);
        }
        else
        {
            // offline member cleanup
            lock (_partyGate)
            {
                party.MemberCharacterIds.Remove(targetId);
                _charToParty.TryRemove(targetId, out _);
            }
            await BroadcastPartyInfoAsync(party, "kick", ct);
        }

        await session.SendAsync(MsgId.PartyKickResp, frame.Seq, new PartyKickResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "已踢出",
            KickedCharacterId = targetId
        }, ct);
    }

    /// <summary>
    /// Leaves party. Leader leave transfers leadership to the next member; alone => dissolve.
    /// </summary>
    private async Task<bool> LeavePartyInternalAsync(WorldPlayerEntity player, string reason, CancellationToken ct)
    {
        PartyRuntime? party;
        string partyId;
        bool dissolved = false;
        lock (_partyGate)
        {
            if (!_charToParty.TryGetValue(player.CharacterId, out partyId!)
                || !_parties.TryGetValue(partyId, out party)
                || party is null)
                return false;

            party.MemberCharacterIds.Remove(player.CharacterId);
            _charToParty.TryRemove(player.CharacterId, out _);
            player.PartyId = string.Empty;

            if (party.MemberCharacterIds.Count == 0)
            {
                _parties.TryRemove(partyId, out _);
                dissolved = true;
            }
            else if (string.Equals(party.LeaderCharacterId, player.CharacterId, StringComparison.Ordinal))
            {
                party.LeaderCharacterId = party.MemberCharacterIds[0];
            }
        }

        await _eventBus.PublishAsync(new PartyLeftEvent(player.Session, player.Name, player.CharacterId, partyId, reason), ct);

        if (!dissolved && party is not null)
            await BroadcastPartyInfoAsync(party, reason, ct);
        else
        {
            // send empty party info to leaver so client clears
            await player.Session.SendAsync(MsgId.PartyInfoNotify, 0, new PartyInfoNotify
            {
                PartyId = string.Empty,
                LeaderCharacterId = string.Empty,
                Reason = "dissolved"
            }, ct);
        }

        return true;
    }

    private async Task BroadcastPartyInfoAsync(PartyRuntime party, string reason, CancellationToken ct)
    {
        var notify = new PartyInfoNotify
        {
            PartyId = party.PartyId,
            LeaderCharacterId = party.LeaderCharacterId,
            Reason = reason
        };

        foreach (string mid in party.MemberCharacterIds.ToList())
        {
            var online = FindOnlineByCharacterId(mid);
            var info = new PartyMemberInfo
            {
                CharacterId = mid,
                IsLeader = string.Equals(mid, party.LeaderCharacterId, StringComparison.Ordinal),
                Online = online != null
            };
            if (online != null)
            {
                info.Name = online.Name;
                info.EntityId = online.EntityId;
                info.Level = online.Level;
                info.MapId = online.MapId;
            }
            else
            {
                var rec = await _storage.Characters.GetByIdAsync(mid, ct);
                info.Name = rec?.Name ?? mid;
                info.Level = rec?.Level ?? 0;
                info.MapId = rec?.MapId ?? string.Empty;
            }
            notify.Members.Add(info);
        }

        foreach (string mid in party.MemberCharacterIds)
        {
            var online = FindOnlineByCharacterId(mid);
            if (online != null)
                await online.Session.SendAsync(MsgId.PartyInfoNotify, 0, notify, ct);
        }
    }

    internal List<WorldPlayerEntity> GetPartyExpShareRecipients(WorldPlayerEntity killer)
    {
        var list = new List<WorldPlayerEntity> { killer };
        if (!_charToParty.TryGetValue(killer.CharacterId, out string? partyId)
            || !_parties.TryGetValue(partyId, out var party)
            || party is null)
            return list;

        var cfg = PartyCfg;
        foreach (string mid in party.MemberCharacterIds)
        {
            if (string.Equals(mid, killer.CharacterId, StringComparison.Ordinal))
                continue;
            var m = FindOnlineByCharacterId(mid);
            if (m is null || m.IsDead) continue;
            if (cfg.ExpShareSameMapOnly && !string.Equals(m.MapId, killer.MapId, StringComparison.Ordinal))
                continue;
            list.Add(m);
        }
        return list;
    }

    private async Task HandleFriendAddAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = FriendAddReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.FriendAddResp, frame.Seq, new FriendAddResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        string friendName = (req.FriendName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(friendName))
        {
            await session.SendAsync(MsgId.FriendAddResp, frame.Seq, new FriendAddResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrFriendDenied,
                Message = "好友名为空"
            }, ct);
            return;
        }

        if (string.Equals(friendName, player.Name, StringComparison.OrdinalIgnoreCase))
        {
            await session.SendAsync(MsgId.FriendAddResp, frame.Seq, new FriendAddResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrFriendDenied,
                Message = "不能添加自己为好友"
            }, ct);
            return;
        }

        int count = await _storage.Friends.CountByOwnerAsync(player.CharacterId, ct);
        if (count >= PartyCfg.MaxFriends)
        {
            await session.SendAsync(MsgId.FriendAddResp, frame.Seq, new FriendAddResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrFriendLimit,
                Message = $"好友已满（上限 {PartyCfg.MaxFriends}）"
            }, ct);
            return;
        }

        // Prefer online, else storage by name
        var online = FindOnlineByName(friendName);
        CharacterRecord? rec = null;
        string friendCharId;
        string resolvedName;
        if (online != null)
        {
            friendCharId = online.CharacterId;
            resolvedName = online.Name;
        }
        else
        {
            rec = await _storage.Characters.GetByNameAsync(friendName, ct);
            if (rec is null)
            {
                await session.SendAsync(MsgId.FriendAddResp, frame.Seq, new FriendAddResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrFriendNotFound,
                    Message = "角色不存在"
                }, ct);
                return;
            }
            friendCharId = rec.CharacterId;
            resolvedName = rec.Name;
        }

        var (ok, err, friend) = await _storage.Friends.AddAsync(player.CharacterId, friendCharId, resolvedName, ct);
        if (!ok || friend is null)
        {
            await session.SendAsync(MsgId.FriendAddResp, frame.Seq, new FriendAddResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrFriendDenied,
                Message = err ?? "添加失败"
            }, ct);
            return;
        }

        bool isOnline = online != null || FindOnlineByCharacterId(friendCharId) != null;
        await session.SendAsync(MsgId.FriendAddResp, frame.Seq, new FriendAddResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "已添加好友",
            Friend = new FriendInfo
            {
                CharacterId = friendCharId,
                Name = resolvedName,
                Online = isOnline
            }
        }, ct);

        await _eventBus.PublishAsync(new FriendAddedEvent(session, player.Name, player.CharacterId, friendCharId, resolvedName), ct);
    }

    private async Task HandleFriendRemoveAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = FriendRemoveReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.FriendRemoveResp, frame.Seq, new FriendRemoveResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        string friendId = (req.FriendCharacterId ?? string.Empty).Trim();
        var (ok, err) = await _storage.Friends.RemoveAsync(player.CharacterId, friendId, ct);
        if (!ok)
        {
            await session.SendAsync(MsgId.FriendRemoveResp, frame.Seq, new FriendRemoveResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrFriendNotFound,
                Message = err ?? "好友不存在",
                FriendCharacterId = friendId
            }, ct);
            return;
        }

        await session.SendAsync(MsgId.FriendRemoveResp, frame.Seq, new FriendRemoveResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "已删除好友",
            FriendCharacterId = friendId
        }, ct);
    }

    private async Task HandleFriendListAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var _ = FriendListReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.FriendListResp, frame.Seq, new FriendListResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Not in world."
            }, ct);
            return;
        }

        var friends = await _storage.Friends.ListByOwnerAsync(player.CharacterId, ct);
        var resp = new FriendListResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "OK"
        };
        foreach (var f in friends)
        {
            resp.Friends.Add(new FriendInfo
            {
                CharacterId = f.FriendCharacterId,
                Name = f.FriendName,
                Online = FindOnlineByCharacterId(f.FriendCharacterId) != null
            });
        }
        await session.SendAsync(MsgId.FriendListResp, frame.Seq, resp, ct);
    }

    private async Task CleanupPartyOnDisconnectAsync(WorldPlayerEntity player, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(player.PartyId) && !_charToParty.ContainsKey(player.CharacterId))
            return;
        await LeavePartyInternalAsync(player, "disconnect", ct);
    }
}
