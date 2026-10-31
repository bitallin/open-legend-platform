using System.Collections.Concurrent;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Storage;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private readonly ConcurrentDictionary<string, long> _lastChatUnixMs = new(StringComparer.Ordinal);

    private bool TryGetOnlinePlayer(IClientSession session, out WorldPlayerEntity? player)
    {
        player = null;
        if (string.IsNullOrEmpty(session.EntityId))
            return false;
        foreach (var aoi in _maps.Values)
        {
            if (aoi.TryGetEntity(session.EntityId, out var e) && e != null)
            {
                player = e;
                return true;
            }
        }
        return false;
    }

    private IEnumerable<WorldPlayerEntity> EnumerateOnlinePlayers()
    {
        foreach (var aoi in _maps.Values)
        {
            foreach (var p in aoi.AllPlayers())
                yield return p;
        }
    }

    private GuildConfig GuildCfg => _versionPack.Guild ?? new GuildConfig();

    private async Task HandleGuildCreateAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = GuildCreateReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.GuildCreateResp, frame.Seq, new GuildCreateResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        var cfg = GuildCfg;
        string name = (req.Name ?? string.Empty).Trim();
        if (name.Length < cfg.MinNameLength || name.Length > cfg.MaxNameLength)
        {
            await session.SendAsync(MsgId.GuildCreateResp, frame.Seq, new GuildCreateResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildDenied,
                Message = $"行会名长度须为 {cfg.MinNameLength}–{cfg.MaxNameLength}"
            }, ct);
            return;
        }

        if (!string.IsNullOrEmpty(player.GuildId))
        {
            await session.SendAsync(MsgId.GuildCreateResp, frame.Seq, new GuildCreateResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrAlreadyInGuild,
                Message = "已加入行会，无法创建"
            }, ct);
            return;
        }

        if (player.Gold < cfg.CreateCostGold)
        {
            await session.SendAsync(MsgId.GuildCreateResp, frame.Seq, new GuildCreateResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNotEnoughGold,
                Message = $"创建行会需要 {cfg.CreateCostGold} 金币"
            }, ct);
            return;
        }

        var existing = await _storage.Guilds.GetByNameAsync(name, ct);
        if (existing != null)
        {
            await session.SendAsync(MsgId.GuildCreateResp, frame.Seq, new GuildCreateResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildNameTaken,
                Message = "行会名已存在"
            }, ct);
            return;
        }

        var (ok, err, guild) = await _storage.Guilds.CreateAsync(name, player.CharacterId, ct);
        if (!ok || guild is null)
        {
            await session.SendAsync(MsgId.GuildCreateResp, frame.Seq, new GuildCreateResp
            {
                Success = false,
                ErrorCode = string.Equals(err, "行会名已存在", StringComparison.Ordinal)
                    ? ProtocolErrorCode.ErrGuildNameTaken
                    : ProtocolErrorCode.ErrGuildDenied,
                Message = err ?? "创建失败"
            }, ct);
            return;
        }

        AdjustGold(player, -(cfg.CreateCostGold));
        player.GuildId = guild.GuildId;
        player.GuildName = guild.Name;
        player.GuildRank = "leader";
        await _storage.Characters.SaveGoldAsync(player.CharacterId, player.Gold, ct);
        await _storage.Characters.SaveGuildIdAsync(player.CharacterId, guild.GuildId, ct);

        await session.SendAsync(MsgId.GuildCreateResp, frame.Seq, new GuildCreateResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = $"行会「{guild.Name}」创建成功",
            GuildId = guild.GuildId,
            GuildName = guild.Name,
            GuildRank = "leader",
            Gold = player.Gold,
            MemberCount = guild.MemberCharacterIds.Count
        }, ct);

        await _eventBus.PublishAsync(new GuildCreatedEvent(session, player.Name, player.CharacterId, guild.GuildId, guild.Name), ct);
    }

    private async Task HandleGuildJoinAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = GuildJoinReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.GuildJoinResp, frame.Seq, new GuildJoinResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        if (!string.IsNullOrEmpty(player.GuildId))
        {
            await session.SendAsync(MsgId.GuildJoinResp, frame.Seq, new GuildJoinResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrAlreadyInGuild,
                Message = "已加入行会"
            }, ct);
            return;
        }

        string name = (req.GuildName ?? string.Empty).Trim();
        var guild = await _storage.Guilds.GetByNameAsync(name, ct);
        if (guild is null)
        {
            await session.SendAsync(MsgId.GuildJoinResp, frame.Seq, new GuildJoinResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildNotFound,
                Message = "行会不存在"
            }, ct);
            return;
        }

        var cfg = GuildCfg;
        if (guild.MemberCharacterIds.Count >= cfg.MaxMembers)
        {
            await session.SendAsync(MsgId.GuildJoinResp, frame.Seq, new GuildJoinResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildFull,
                Message = $"行会人数已满（上限 {cfg.MaxMembers}）"
            }, ct);
            return;
        }

        var (ok, err, updated) = await _storage.Guilds.AddMemberAsync(guild.GuildId, player.CharacterId, ct);
        if (!ok || updated is null)
        {
            await session.SendAsync(MsgId.GuildJoinResp, frame.Seq, new GuildJoinResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildDenied,
                Message = err ?? "加入失败"
            }, ct);
            return;
        }

        player.GuildId = updated.GuildId;
        player.GuildName = updated.Name;
        player.GuildRank = "member";
        await _storage.Characters.SaveGuildIdAsync(player.CharacterId, updated.GuildId, ct);

        await session.SendAsync(MsgId.GuildJoinResp, frame.Seq, new GuildJoinResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = $"已加入行会「{updated.Name}」",
            GuildId = updated.GuildId,
            GuildName = updated.Name,
            GuildRank = "member",
            MemberCount = updated.MemberCharacterIds.Count
        }, ct);

        await _eventBus.PublishAsync(new GuildJoinedEvent(session, player.Name, player.CharacterId, updated.GuildId, updated.Name), ct);
    }

    private async Task HandleGuildLeaveAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        _ = GuildLeaveReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.GuildLeaveResp, frame.Seq, new GuildLeaveResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        if (string.IsNullOrEmpty(player.GuildId))
        {
            await session.SendAsync(MsgId.GuildLeaveResp, frame.Seq, new GuildLeaveResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNotInGuild,
                Message = "未加入行会"
            }, ct);
            return;
        }

        string guildId = player.GuildId;
        string guildName = player.GuildName;
        var (ok, err) = await _storage.Guilds.RemoveMemberAsync(guildId, player.CharacterId, ct);
        if (!ok)
        {
            await session.SendAsync(MsgId.GuildLeaveResp, frame.Seq, new GuildLeaveResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildDenied,
                Message = err ?? "离开失败"
            }, ct);
            return;
        }

        // Update online members if leadership changed
        var remaining = await _storage.Guilds.GetByIdAsync(guildId, ct);
        if (remaining != null)
        {
            foreach (var p in EnumerateOnlinePlayers())
            {
                if (string.Equals(p.GuildId, guildId, StringComparison.Ordinal)
                    && string.Equals(p.CharacterId, remaining.LeaderCharacterId, StringComparison.Ordinal))
                {
                    p.GuildRank = "leader";
                }
            }
        }

        player.GuildId = string.Empty;
        player.GuildName = string.Empty;
        player.GuildRank = string.Empty;
        await _storage.Characters.SaveGuildIdAsync(player.CharacterId, string.Empty, ct);

        await session.SendAsync(MsgId.GuildLeaveResp, frame.Seq, new GuildLeaveResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "已离开行会"
        }, ct);

        await _eventBus.PublishAsync(new GuildLeftEvent(session, player.Name, player.CharacterId, guildId, guildName), ct);
    }

    private async Task HandleGuildInfoAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = GuildInfoReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            await session.SendAsync(MsgId.GuildInfoResp, frame.Seq, new GuildInfoResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "未进入世界"
            }, ct);
            return;
        }

        string guildId = string.IsNullOrWhiteSpace(req.GuildId) ? player.GuildId : req.GuildId;
        if (string.IsNullOrEmpty(guildId))
        {
            await session.SendAsync(MsgId.GuildInfoResp, frame.Seq, new GuildInfoResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrNotInGuild,
                Message = "未加入行会"
            }, ct);
            return;
        }

        var guild = await _storage.Guilds.GetByIdAsync(guildId, ct);
        if (guild is null)
        {
            await session.SendAsync(MsgId.GuildInfoResp, frame.Seq, new GuildInfoResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrGuildNotFound,
                Message = "行会不存在"
            }, ct);
            return;
        }

        var resp = new GuildInfoResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "ok",
            GuildId = guild.GuildId,
            GuildName = guild.Name,
            LeaderCharacterId = guild.LeaderCharacterId,
            Notice = ReadGuildNotice(guild.RanksJson)
        };
        foreach (string mid in guild.MemberCharacterIds)
        {
            var ch = await _storage.Characters.GetByIdAsync(mid, ct);
            resp.Members.Add(new GuildMemberInfo
            {
                CharacterId = mid,
                Name = ch?.Name ?? mid,
                Rank = ResolveGuildRank(guild, mid)
            });
        }
        await session.SendAsync(MsgId.GuildInfoResp, frame.Seq, resp, ct);
    }

    private async Task HandleChatAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = ChatReq.Parser.ParseFrom(frame.Payload);
        if (!TryGetOnlinePlayer(session, out var player) || player is null)
        {
            return;
        }

        string accountId = session.AccountId ?? string.Empty;
        if (!string.IsNullOrEmpty(accountId))
        {
            var mod = await _storage.Moderation.GetByAccountIdAsync(accountId, ct);
            if (mod is not null && mod.IsMuteActive())
            {
                await session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice
                {
                    NoticeType = "chat_denied",
                    Title = "禁言",
                    Content = string.IsNullOrWhiteSpace(mod.MuteReason) ? "你已被禁言，无法发言" : $"禁言中：{mod.MuteReason}"
                }, ct);
                return;
            }
        }

        var cfg = GuildCfg;
        string content = (req.Content ?? string.Empty).Trim();
        if (content.Length == 0 || content.Length > cfg.ChatMaxLength)
        {
            await session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice
            {
                NoticeType = "chat_denied",
                Title = "聊天",
                Content = content.Length == 0 ? "内容为空" : $"内容过长（上限 {cfg.ChatMaxLength}）"
            }, ct);
            return;
        }

        if (await TryChatUtilityAsync(player, content, req.Channel, ct))
            return;

        content = PhasePRules.MaskProfanity(content);

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (_lastChatUnixMs.TryGetValue(player.EntityId, out long last) && now - last < cfg.ChatCooldownMs)
        {
            // soft drop for rate limit (tests use low cooldown)
            return;
        }
        _lastChatUnixMs[player.EntityId] = now;

        if (req.Channel == ChatChannel.ChatGuild && string.IsNullOrEmpty(player.GuildId))
        {
            await session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice
            {
                NoticeType = "chat_denied",
                Title = "聊天",
                Content = "未加入行会，无法使用行会频道"
            }, ct);
            return;
        }

        if (req.Channel == ChatChannel.ChatParty && string.IsNullOrEmpty(player.PartyId))
        {
            await session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice
            {
                NoticeType = "chat_denied",
                Title = "聊天",
                Content = "未加入队伍，无法使用组队频道"
            }, ct);
            return;
        }

        if (req.Channel == ChatChannel.ChatWhisper)
        {
            string targetName = (req.TargetName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(targetName))
            {
                await session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice
                {
                    NoticeType = "chat_denied",
                    Title = "私聊",
                    Content = "私聊需要目标角色名"
                }, ct);
                return;
            }
            if (string.Equals(targetName, player.Name, StringComparison.OrdinalIgnoreCase))
            {
                await session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice
                {
                    NoticeType = "chat_denied",
                    Title = "私聊",
                    Content = "不能私聊自己"
                }, ct);
                return;
            }
            var target = FindOnlineByName(targetName);
            if (target is null)
            {
                await session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice
                {
                    NoticeType = "chat_denied",
                    Title = "私聊",
                    Content = "目标不在线"
                }, ct);
                return;
            }
            if (target.Blacklist.Any(n => string.Equals(n, player.Name, StringComparison.OrdinalIgnoreCase)))
            {
                await session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice
                {
                    NoticeType = "chat_denied",
                    Title = "私聊",
                    Content = "对方已屏蔽你"
                }, ct);
                return;
            }
            if (PartyCfg.WhisperRequiresFriend)
            {
                bool isFriend = await _storage.Friends.IsFriendAsync(player.CharacterId, target.CharacterId, ct);
                if (!isFriend)
                {
                    await session.SendAsync(MsgId.ServerNotice, 0, new ServerNotice
                    {
                        NoticeType = "chat_denied",
                        Title = "私聊",
                        Content = "需要先添加好友才能私聊"
                    }, ct);
                    return;
                }
            }

            var whisperNotify = new ChatNotify
            {
                Channel = ChatChannel.ChatWhisper,
                SenderEntityId = player.EntityId,
                SenderName = player.Name,
                Content = content,
                GuildId = player.GuildId ?? string.Empty,
                UnixMs = now,
                TargetName = target.Name
            };
            await target.Session.SendAsync(MsgId.ChatNotify, 0, whisperNotify, ct);
            await player.Session.SendAsync(MsgId.ChatNotify, 0, whisperNotify, ct);
            await _eventBus.PublishAsync(new WhisperSentEvent(
                session, player.Name, player.CharacterId, target.Name, target.CharacterId, content), ct);
            return;
        }

        var notify = new ChatNotify
        {
            Channel = req.Channel,
            SenderEntityId = player.EntityId,
            SenderName = player.Name,
            Content = content,
            GuildId = player.GuildId ?? string.Empty,
            UnixMs = now
        };

        IEnumerable<WorldPlayerEntity> recipients = req.Channel switch
        {
            ChatChannel.ChatWorld => EnumerateOnlinePlayers(),
            ChatChannel.ChatGuild => EnumerateOnlinePlayers()
                .Where(p => !string.IsNullOrEmpty(p.GuildId)
                            && string.Equals(p.GuildId, player.GuildId, StringComparison.Ordinal)),
            ChatChannel.ChatNearby => GetNearbyChatRecipients(player, cfg.NearbyChebyshev),
            ChatChannel.ChatParty => PartyChatRecipients(player),
            _ => Array.Empty<WorldPlayerEntity>()
        };

        var sent = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in recipients)
        {
            if (!sent.Add(r.EntityId))
                continue;
            await r.Session.SendAsync(MsgId.ChatNotify, 0, notify, ct);
        }
        // ensure sender always receives own message if somehow missed
        if (sent.Add(player.EntityId))
            await player.Session.SendAsync(MsgId.ChatNotify, 0, notify, ct);
    }

    private List<WorldPlayerEntity> PartyChatRecipients(WorldPlayerEntity self)
    {
        var list = new List<WorldPlayerEntity> { self };
        if (string.IsNullOrEmpty(self.PartyId) || !_parties.TryGetValue(self.PartyId, out var party))
            return list;
        foreach (string id in party.MemberCharacterIds)
        {
            var member = FindOnlineByCharacterId(id);
            if (member != null && list.All(p => p.EntityId != member.EntityId))
                list.Add(member);
        }
        return list;
    }

    private List<WorldPlayerEntity> GetNearbyChatRecipients(WorldPlayerEntity self, int chebyshev)
    {
        var list = new List<WorldPlayerEntity> { self };
        if (!_maps.TryGetValue(self.MapId, out var aoi))
            return list;
        foreach (var other in aoi.AllPlayers())
        {
            if (other.EntityId == self.EntityId)
                continue;
            int d = Math.Max(Math.Abs(other.X - self.X), Math.Abs(other.Y - self.Y));
            if (d <= chebyshev)
                list.Add(other);
        }
        return list;
    }
}
