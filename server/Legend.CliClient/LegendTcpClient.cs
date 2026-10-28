using System.Collections.Concurrent;
using System.Net.Sockets;
using Google.Protobuf;
using Legend.Contracts;
using Legend.Contracts.Proto;

namespace Legend.CliClient;

public sealed class LegendTcpClient : IAsyncDisposable
{
    private readonly TcpClient _tcp = new();
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private Task? _readLoopTask;
    private uint _seq;

    private readonly ConcurrentDictionary<uint, TaskCompletionSource<PacketFrame>> _pendingRequests = new();

    public ConcurrentQueue<MoveSync> ReceivedMoveSyncs { get; } = new();
    public ConcurrentQueue<EntityEnterAoi> ReceivedAoiEnters { get; } = new();
    public ConcurrentQueue<EntityLeaveAoi> ReceivedAoiLeaves { get; } = new();
    public ConcurrentQueue<ServerNotice> ReceivedServerNotices { get; } = new();
    public ConcurrentQueue<MapChangeNotify> ReceivedMapChanges { get; } = new();
    public ConcurrentQueue<CombatSync> ReceivedCombatSyncs { get; } = new();
    public ConcurrentQueue<EntitySpawn> ReceivedEntitySpawns { get; } = new();
    public ConcurrentQueue<EntityDespawn> ReceivedEntityDespawns { get; } = new();
    public ConcurrentQueue<LootDropNotify> ReceivedLootDrops { get; } = new();
    public ConcurrentQueue<InventoryNotify> ReceivedInventoryNotifies { get; } = new();
    public ConcurrentQueue<QuestProgressNotify> ReceivedQuestProgress { get; } = new();
    public ConcurrentQueue<ExpGainNotify> ReceivedExpGains { get; } = new();
    public ConcurrentQueue<LevelUpNotify> ReceivedLevelUps { get; } = new();
    public ConcurrentQueue<ChatNotify> ReceivedChatNotifies { get; } = new();
    public ConcurrentQueue<StallSoldNotify> ReceivedStallSoldNotifies { get; } = new();
    public ConcurrentQueue<PartyInfoNotify> ReceivedPartyInfoNotifies { get; } = new();
    public ConcurrentQueue<PartyInviteNotify> ReceivedPartyInviteNotifies { get; } = new();
    public ConcurrentQueue<SkillListNotify> ReceivedSkillListNotifies { get; } = new();
    public ConcurrentQueue<SkillLevelUpNotify> ReceivedSkillLevelUpNotifies { get; } = new();
    public ConcurrentQueue<BuffApplyNotify> ReceivedBuffApplyNotifies { get; } = new();
    public ConcurrentQueue<BuffRemoveNotify> ReceivedBuffRemoveNotifies { get; } = new();
    public ConcurrentQueue<SiegeStateNotify> ReceivedSiegeStateNotifies { get; } = new();
    public ConcurrentQueue<MailNotify> ReceivedMailNotifies { get; } = new();
    public ConcurrentQueue<DungeonProgressNotify> ReceivedDungeonProgress { get; } = new();
    public ConcurrentQueue<DungeonCompleteNotify> ReceivedDungeonCompletes { get; } = new();
    public ConcurrentQueue<DungeonPartyEnterNotify> ReceivedDungeonPartyEnters { get; } = new();

    public event Action<MoveSync>? OnMoveSync;
    public event Action<EntityEnterAoi>? OnEntityEnterAoi;
    public event Action<EntityLeaveAoi>? OnEntityLeaveAoi;
    public event Action<ServerNotice>? OnServerNotice;
    public event Action<MapChangeNotify>? OnMapChange;
    public event Action<CombatSync>? OnCombatSync;
    public event Action<LootDropNotify>? OnLootDrop;

    public string? SessionToken { get; private set; }
    public string? AccountId { get; private set; }
    public string? CharacterId { get; private set; }
    public EntitySnapshot? SelfEntity { get; private set; }
    public int Gold { get; private set; }
    public string GuildId { get; private set; } = string.Empty;
    public string GuildName { get; private set; } = string.Empty;
    public string GuildRank { get; private set; } = string.Empty;

    public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        await _tcp.ConnectAsync(host, port, ct);
        _tcp.NoDelay = true;
        _stream = _tcp.GetStream();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _readLoopTask = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        if (_stream == null)
            return;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frameOpt = await PacketCodec.ReadFrameAsync(_stream, ct);
                if (frameOpt == null)
                    break;

                var frame = frameOpt.Value;

                if (frame.Seq != 0 && _pendingRequests.TryRemove(frame.Seq, out var tcs))
                {
                    if (frame.MsgId == MsgId.MoveSync)
                    {
                        var sync = MoveSync.Parser.ParseFrom(frame.Payload);
                        ReceivedMoveSyncs.Enqueue(sync);
                        OnMoveSync?.Invoke(sync);
                    }
                    tcs.TrySetResult(frame);
                    continue;
                }

                switch (frame.MsgId)
                {
                    case MsgId.MoveSync:
                    {
                        var sync = MoveSync.Parser.ParseFrom(frame.Payload);
                        if (sync.Accepted && SelfEntity != null && sync.EntityId == SelfEntity.EntityId)
                        {
                            SelfEntity.X = sync.ToX;
                            SelfEntity.Y = sync.ToY;
                            SelfEntity.InSafeZone = sync.InSafeZone;
                        }
                        ReceivedMoveSyncs.Enqueue(sync);
                        OnMoveSync?.Invoke(sync);
                        break;
                    }
                    case MsgId.EntityEnterAoi:
                    {
                        var enter = EntityEnterAoi.Parser.ParseFrom(frame.Payload);
                        ReceivedAoiEnters.Enqueue(enter);
                        OnEntityEnterAoi?.Invoke(enter);
                        break;
                    }
                    case MsgId.EntityLeaveAoi:
                    {
                        var leave = EntityLeaveAoi.Parser.ParseFrom(frame.Payload);
                        ReceivedAoiLeaves.Enqueue(leave);
                        OnEntityLeaveAoi?.Invoke(leave);
                        break;
                    }
                    case MsgId.ServerNotice:
                    {
                        var notice = ServerNotice.Parser.ParseFrom(frame.Payload);
                        ReceivedServerNotices.Enqueue(notice);
                        OnServerNotice?.Invoke(notice);
                        break;
                    }
                    case MsgId.MapChangeNotify:
                    {
                        var change = MapChangeNotify.Parser.ParseFrom(frame.Payload);
                        ReceivedMapChanges.Enqueue(change);
                        OnMapChange?.Invoke(change);
                        if (SelfEntity != null && change.EntityId == SelfEntity.EntityId)
                        {
                            SelfEntity.MapId = change.ToMapId;
                            SelfEntity.X = change.ToX;
                            SelfEntity.Y = change.ToY;
                            SelfEntity.InSafeZone = change.InSafeZone;
                        }
                        break;
                    }
                    case MsgId.CombatSync:
                    {
                        var sync = CombatSync.Parser.ParseFrom(frame.Payload);
                        ReceivedCombatSyncs.Enqueue(sync);
                        OnCombatSync?.Invoke(sync);
                        if (SelfEntity != null && sync.TargetEntityId == SelfEntity.EntityId)
                        {
                            SelfEntity.Hp = sync.TargetHp;
                            SelfEntity.MaxHp = sync.TargetMaxHp;
                        }
                        break;
                    }
                    case MsgId.EntitySpawn:
                    {
                        var spawn = EntitySpawn.Parser.ParseFrom(frame.Payload);
                        ReceivedEntitySpawns.Enqueue(spawn);
                        break;
                    }
                    case MsgId.EntityDespawn:
                    {
                        var despawn = EntityDespawn.Parser.ParseFrom(frame.Payload);
                        ReceivedEntityDespawns.Enqueue(despawn);
                        break;
                    }
                    case MsgId.LootDropNotify:
                    {
                        var loot = LootDropNotify.Parser.ParseFrom(frame.Payload);
                        ReceivedLootDrops.Enqueue(loot);
                        OnLootDrop?.Invoke(loot);
                        break;
                    }
                    case MsgId.InventoryNotify:
                    {
                        var inv = InventoryNotify.Parser.ParseFrom(frame.Payload);
                        ReceivedInventoryNotifies.Enqueue(inv);
                        Gold = inv.Gold;
                        break;
                    }
                    case MsgId.QuestProgressNotify:
                    {
                        var qp = QuestProgressNotify.Parser.ParseFrom(frame.Payload);
                        ReceivedQuestProgress.Enqueue(qp);
                        break;
                    }
                    case MsgId.ExpGainNotify:
                    {
                        var eg = ExpGainNotify.Parser.ParseFrom(frame.Payload);
                        ReceivedExpGains.Enqueue(eg);
                        break;
                    }
                    case MsgId.LevelUpNotify:
                    {
                        var lu = LevelUpNotify.Parser.ParseFrom(frame.Payload);
                        ReceivedLevelUps.Enqueue(lu);
                        break;
                    }
                    case MsgId.ChatNotify:
                    {
                        var ch = ChatNotify.Parser.ParseFrom(frame.Payload);
                        ReceivedChatNotifies.Enqueue(ch);
                        break;
                    }
                    case MsgId.PartyInfoNotify:
                    {
                        var pi = PartyInfoNotify.Parser.ParseFrom(frame.Payload);
                        ReceivedPartyInfoNotifies.Enqueue(pi);
                        break;
                    }
                    case MsgId.PartyInviteNotify:
                    {
                        var inv = PartyInviteNotify.Parser.ParseFrom(frame.Payload);
                        ReceivedPartyInviteNotifies.Enqueue(inv);
                        break;
                    }
                    case MsgId.SkillListNotify:
                    {
                        ReceivedSkillListNotifies.Enqueue(SkillListNotify.Parser.ParseFrom(frame.Payload));
                        break;
                    }
                    case MsgId.SkillLevelUpNotify:
                    {
                        ReceivedSkillLevelUpNotifies.Enqueue(SkillLevelUpNotify.Parser.ParseFrom(frame.Payload));
                        break;
                    }
                    case MsgId.BuffApplyNotify:
                    {
                        ReceivedBuffApplyNotifies.Enqueue(BuffApplyNotify.Parser.ParseFrom(frame.Payload));
                        break;
                    }
                    case MsgId.BuffRemoveNotify:
                    {
                        ReceivedBuffRemoveNotifies.Enqueue(BuffRemoveNotify.Parser.ParseFrom(frame.Payload));
                        break;
                    }
                    case MsgId.SiegeStateNotify:
                    {
                        ReceivedSiegeStateNotifies.Enqueue(SiegeStateNotify.Parser.ParseFrom(frame.Payload));
                        break;
                    }
                    case MsgId.MailNotify:
                    {
                        ReceivedMailNotifies.Enqueue(MailNotify.Parser.ParseFrom(frame.Payload));
                        break;
                    }
                    case MsgId.StallSoldNotify:
                    {
                        var sold = StallSoldNotify.Parser.ParseFrom(frame.Payload);
                        Gold = sold.NewGold;
                        ReceivedStallSoldNotifies.Enqueue(sold);
                        break;
                    }
                    case MsgId.DungeonProgressNotify:
                    {
                        var dp = DungeonProgressNotify.Parser.ParseFrom(frame.Payload);
                        ReceivedDungeonProgress.Enqueue(dp);
                        break;
                    }
                    case MsgId.DungeonPartyEnterNotify:
                    {
                        var dpe = DungeonPartyEnterNotify.Parser.ParseFrom(frame.Payload);
                        ReceivedDungeonPartyEnters.Enqueue(dpe);
                        break;
                    }
                    case MsgId.DungeonCompleteNotify:
                    {
                        var dc = DungeonCompleteNotify.Parser.ParseFrom(frame.Payload);
                        Gold = dc.Gold;
                        ReceivedDungeonCompletes.Enqueue(dc);
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            foreach (var kv in _pendingRequests)
                kv.Value.TrySetException(ex);
        }
    }

    public async Task<PacketFrame> SendRequestAsync(MsgId reqId, IMessage req, CancellationToken ct = default)
    {
        if (_stream == null)
            throw new InvalidOperationException("Client is not connected.");

        uint seq = Interlocked.Increment(ref _seq);
        var tcs = new TaskCompletionSource<PacketFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[seq] = tcs;

        using var reg = ct.Register(() =>
        {
            if (_pendingRequests.TryRemove(seq, out var removed))
                removed.TrySetCanceled(ct);
        });

        await PacketCodec.WriteFrameAsync(_stream, reqId, seq, req, ct);
        return await tcs.Task;
    }

    public async Task<HandshakeResp> HandshakeAsync(
        string protocolVersion = ProtocolConstants.CurrentVersion,
        string clientBuild = "pc-cli-1.6.0",
        CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.HandshakeReq, new HandshakeReq
        {
            ProtocolVersion = protocolVersion,
            ClientBuild = clientBuild,
            DeviceId = "dev-smoke-01"
        }, ct);
        return HandshakeResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<RegisterResp> RegisterAsync(string username, string password, string platform = "PC", CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.RegisterReq, new RegisterReq
        {
            Username = username,
            Password = password,
            ClientPlatform = platform
        }, ct);
        return RegisterResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<LoginResp> LoginAsync(string username, string password, string platform = "PC", CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.LoginReq, new LoginReq
        {
            Username = username,
            Password = password,
            ClientPlatform = platform
        }, ct);

        var resp = LoginResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
        {
            SessionToken = resp.SessionToken;
            AccountId = resp.AccountId;
        }
        return resp;
    }

    public async Task RegisterAndLoginAsync(string username, string password, string platform = "PC", CancellationToken ct = default)
    {
        await RegisterAsync(username, password, platform, ct);
        var login = await LoginAsync(username, password, platform, ct);
        if (!login.Success)
            throw new InvalidOperationException($"Login failed: {login.Message}");
    }

    public async Task<RealmListResp> GetRealmListAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.RealmListReq, new RealmListReq
        {
            SessionToken = SessionToken ?? string.Empty
        }, ct);
        return RealmListResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<CharListResp> CharListAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.CharListReq, new CharListReq
        {
            SessionToken = SessionToken ?? string.Empty
        }, ct);
        return CharListResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<CreateCharResp> CreateCharAsync(string name, string classId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.CreateCharReq, new CreateCharReq
        {
            SessionToken = SessionToken ?? string.Empty,
            Name = name,
            ClassId = classId
        }, ct);
        return CreateCharResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<DeleteCharResp> DeleteCharAsync(string characterId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.DeleteCharReq, new DeleteCharReq
        {
            SessionToken = SessionToken ?? string.Empty,
            CharacterId = characterId
        }, ct);
        return DeleteCharResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<SelectCharResp> SelectCharAsync(string characterId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.SelectCharReq, new SelectCharReq
        {
            SessionToken = SessionToken ?? string.Empty,
            CharacterId = characterId
        }, ct);
        var resp = SelectCharResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
            CharacterId = resp.Character.CharacterId;
        return resp;
    }

    public async Task<EnterWorldResp> EnterWorldAsync(string realmId, string? characterId = null, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.EnterWorldReq, new EnterWorldReq
        {
            SessionToken = SessionToken ?? string.Empty,
            RealmId = realmId,
            CharacterId = characterId ?? CharacterId ?? string.Empty
        }, ct);

        var resp = EnterWorldResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
        {
            Gold = resp.Gold;
            SelfEntity = resp.SelfEntity;
            GuildId = resp.GuildId ?? string.Empty;
            GuildName = resp.GuildName ?? string.Empty;
            GuildRank = resp.GuildRank ?? string.Empty;
        }
        return resp;
    }

    public async Task<MoveSync> MoveAsync(int toX, int toY, int direction = 4, CancellationToken ct = default)
    {
        // Capture map before request: a portal MapChangeNotify may arrive before this
        // MoveSync is applied; never overwrite post-teleport coordinates with the pre-portal sync.
        string? mapBefore = SelfEntity?.MapId;
        var frame = await SendRequestAsync(MsgId.MoveReq, new MoveReq
        {
            ToX = toX,
            ToY = toY,
            Direction = direction
        }, ct);

        var sync = MoveSync.Parser.ParseFrom(frame.Payload);
        if (sync.Accepted && SelfEntity != null &&
            (mapBefore is null || string.Equals(SelfEntity.MapId, mapBefore, StringComparison.Ordinal)))
        {
            SelfEntity.X = sync.ToX;
            SelfEntity.Y = sync.ToY;
            SelfEntity.Direction = sync.Direction;
            SelfEntity.InSafeZone = sync.InSafeZone;
        }
        return sync;
    }

    public async Task<PongResp> PingAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.PingReq, new PingReq
        {
            ClientTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }, ct);
        return PongResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<(AttackResp Resp, CombatSync? Combat)> AttackAsync(string targetEntityId, string skillId, CancellationToken ct = default)
    {
        // Drain stale combat events
        while (ReceivedCombatSyncs.TryDequeue(out _)) { }

        var frame = await SendRequestAsync(MsgId.AttackReq, new AttackReq
        {
            TargetEntityId = targetEntityId,
            SkillId = skillId
        }, ct);
        var resp = AttackResp.Parser.ParseFrom(frame.Payload);
        CombatSync? combat = null;
        string selfId = SelfEntity?.EntityId ?? "";
        for (int i = 0; i < 40 && combat is null; i++)
        {
            while (ReceivedCombatSyncs.TryDequeue(out var c))
            {
                if (c.AttackerEntityId == selfId && c.TargetEntityId == targetEntityId)
                {
                    combat = c;
                    break;
                }
            }
            if (combat is null)
                await Task.Delay(25, ct);
        }
        return (resp, combat);
    }

    public async Task<PickupResp> PickupAsync(string lootEntityId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.PickupReq, new PickupReq
        {
            LootEntityId = lootEntityId
        }, ct);
        return PickupResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<TalkResp> TalkAsync(string npcEntityId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.TalkReq, new TalkReq { NpcEntityId = npcEntityId }, ct);
        var resp = TalkResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
            Gold = resp.Gold;
        return resp;
    }

    public async Task<ShopBuyResp> ShopBuyAsync(string npcEntityId, string itemId, int count = 1, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.ShopBuyReq, new ShopBuyReq
        {
            NpcEntityId = npcEntityId,
            ItemId = itemId,
            Count = count
        }, ct);
        var resp = ShopBuyResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
            Gold = resp.Gold;
        return resp;
    }

    public async Task<QuestAcceptResp> QuestAcceptAsync(string npcEntityId, string questId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.QuestAcceptReq, new QuestAcceptReq
        {
            NpcEntityId = npcEntityId,
            QuestId = questId
        }, ct);
        return QuestAcceptResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<QuestTurnInResp> QuestTurnInAsync(string npcEntityId, string questId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.QuestTurnInReq, new QuestTurnInReq
        {
            NpcEntityId = npcEntityId,
            QuestId = questId
        }, ct);
        var resp = QuestTurnInResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
            Gold = resp.Gold;
        return resp;
    }

    public async Task<EquipResp> EquipAsync(string itemId, string slot = "", CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.EquipReq, new EquipReq
        {
            ItemId = itemId,
            Slot = slot
        }, ct);
        return EquipResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<UnequipResp> UnequipAsync(string slot, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.UnequipReq, new UnequipReq
        {
            Slot = slot
        }, ct);
        return UnequipResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<EnhanceEquipResp> EnhanceEquipAsync(string slot = "weapon", CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.EnhanceEquipReq, new EnhanceEquipReq
        {
            Slot = slot
        }, ct);
        var resp = EnhanceEquipResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
            Gold = resp.Gold;
        return resp;
    }


    public async Task<UseItemResp> UseItemAsync(string itemId, int count = 1, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.UseItemReq, new UseItemReq
        {
            ItemId = itemId,
            Count = count
        }, ct);
        var resp = UseItemResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success && SelfEntity != null)
        {
            SelfEntity.Hp = resp.Hp;
            SelfEntity.MaxHp = resp.MaxHp;
        }
        return resp;
    }

    public async Task<GuildCreateResp> GuildCreateAsync(string name, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.GuildCreateReq, new GuildCreateReq { Name = name }, ct);
        var resp = GuildCreateResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
        {
            GuildId = resp.GuildId;
            GuildName = resp.GuildName;
            GuildRank = resp.GuildRank;
            Gold = resp.Gold;
        }
        return resp;
    }

    public async Task<GuildJoinResp> GuildJoinAsync(string guildName, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.GuildJoinReq, new GuildJoinReq { GuildName = guildName }, ct);
        var resp = GuildJoinResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
        {
            GuildId = resp.GuildId;
            GuildName = resp.GuildName;
            GuildRank = resp.GuildRank;
        }
        return resp;
    }

    public async Task<GuildLeaveResp> GuildLeaveAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.GuildLeaveReq, new GuildLeaveReq(), ct);
        var resp = GuildLeaveResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
        {
            GuildId = string.Empty;
            GuildName = string.Empty;
            GuildRank = string.Empty;
        }
        return resp;
    }

    public async Task<GuildInfoResp> GuildInfoAsync(string guildId = "", CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.GuildInfoReq, new GuildInfoReq { GuildId = guildId ?? "" }, ct);
        return GuildInfoResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task ChatAsync(ChatChannel channel, string content, string targetName = "", CancellationToken ct = default)
    {
        // fire-and-forget request; replies arrive as ChatNotify (seq may not match)
        if (_stream == null)
            throw new InvalidOperationException("Client is not connected.");
        uint seq = Interlocked.Increment(ref _seq);
        var tcs = new TaskCompletionSource<PacketFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[seq] = tcs;
        // Chat has no ChatResp — remove pending so ReadLoop doesn't hang
        _pendingRequests.TryRemove(seq, out _);
        await PacketCodec.WriteFrameAsync(_stream, MsgId.ChatReq, seq, new ChatReq
        {
            Channel = channel,
            Content = content,
            TargetName = targetName ?? ""
        }, ct);
    }

    public async Task WaitChatAsync(Func<ChatNotify, bool> pred, int timeoutMs = 3000, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var held = new List<ChatNotify>();
        try
        {
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                while (ReceivedChatNotifies.TryDequeue(out var n))
                {
                    if (pred(n))
                        return;
                    held.Add(n);
                }
                await Task.Delay(20, ct);
            }
            throw new TimeoutException("ChatNotify not received in time");
        }
        finally
        {
            foreach (var n in held)
                ReceivedChatNotifies.Enqueue(n);
        }
    }

        public async Task<StallListResp> StallListAsync(string itemId, int count, int unitPriceGold, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.StallListReq, new StallListReq
        {
            ItemId = itemId,
            Count = count,
            UnitPriceGold = unitPriceGold
        }, ct);
        return StallListResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<StallBrowseResp> StallBrowseAsync(string itemIdFilter = "", CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.StallBrowseReq, new StallBrowseReq
        {
            ItemIdFilter = itemIdFilter ?? ""
        }, ct);
        return StallBrowseResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<StallBuyResp> StallBuyAsync(string listingId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.StallBuyReq, new StallBuyReq
        {
            ListingId = listingId
        }, ct);
        var resp = StallBuyResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
            Gold = resp.Gold;
        return resp;
    }

    public async Task<StallCancelResp> StallCancelAsync(string listingId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.StallCancelReq, new StallCancelReq
        {
            ListingId = listingId
        }, ct);
        return StallCancelResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<StallSoldNotify> WaitStallSoldAsync(Func<StallSoldNotify, bool> pred, int timeoutMs = 3000, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var held = new List<StallSoldNotify>();
        try
        {
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                while (ReceivedStallSoldNotifies.TryDequeue(out var n))
                {
                    if (pred(n))
                        return n;
                    held.Add(n);
                }
                await Task.Delay(20, ct);
            }
            throw new TimeoutException("StallSoldNotify not received in time");
        }
        finally
        {
            foreach (var n in held)
                ReceivedStallSoldNotifies.Enqueue(n);
        }
    }


    public async Task<PartyInviteResp> PartyInviteAsync(string targetName, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.PartyInviteReq, new PartyInviteReq { TargetName = targetName }, ct);
        return PartyInviteResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<PartyAcceptResp> PartyAcceptAsync(string partyId, bool accept = true, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.PartyAcceptReq, new PartyAcceptReq { PartyId = partyId, Accept = accept }, ct);
        return PartyAcceptResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<PartyLeaveResp> PartyLeaveAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.PartyLeaveReq, new PartyLeaveReq(), ct);
        return PartyLeaveResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<PartyKickResp> PartyKickAsync(string targetCharacterId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.PartyKickReq, new PartyKickReq { TargetCharacterId = targetCharacterId }, ct);
        return PartyKickResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<PartyInviteNotify> WaitPartyInviteAsync(int timeoutMs = 3000, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (ReceivedPartyInviteNotifies.TryDequeue(out var n))
                return n;
            await Task.Delay(20, ct);
        }
        throw new TimeoutException("PartyInviteNotify not received in time");
    }

    public async Task<PartyInfoNotify> WaitPartyInfoAsync(Func<PartyInfoNotify, bool> pred, int timeoutMs = 3000, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var held = new List<PartyInfoNotify>();
        try
        {
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                while (ReceivedPartyInfoNotifies.TryDequeue(out var n))
                {
                    if (pred(n))
                        return n;
                    held.Add(n);
                }
                await Task.Delay(20, ct);
            }
            throw new TimeoutException("PartyInfoNotify not received in time");
        }
        finally
        {
            foreach (var n in held)
                ReceivedPartyInfoNotifies.Enqueue(n);
        }
    }

    public async Task<FriendAddResp> FriendAddAsync(string friendName, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.FriendAddReq, new FriendAddReq { FriendName = friendName }, ct);
        return FriendAddResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<FriendRemoveResp> FriendRemoveAsync(string friendCharacterId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.FriendRemoveReq, new FriendRemoveReq { FriendCharacterId = friendCharacterId }, ct);
        return FriendRemoveResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<FriendListResp> FriendListAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.FriendListReq, new FriendListReq(), ct);
        return FriendListResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task WhisperAsync(string targetName, string content, CancellationToken ct = default)
        => await ChatAsync(ChatChannel.ChatWhisper, content, targetName, ct);


    public async Task<LearnSkillResp> LearnSkillAsync(string itemId, string skillId = "", CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.LearnSkillReq, new LearnSkillReq { ItemId = itemId, SkillId = skillId }, ct);
        return LearnSkillResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<SkillListResp> SkillListAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.SkillListReq, new SkillListReq(), ct);
        return SkillListResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<BuffApplyNotify> WaitBuffApplyAsync(string? buffId = null, int timeoutMs = 3000, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            while (ReceivedBuffApplyNotifies.TryDequeue(out var n))
            {
                if (buffId is null || string.Equals(n.Buff.BuffId, buffId, StringComparison.OrdinalIgnoreCase))
                    return n;
            }
            await Task.Delay(20, ct);
        }
        throw new TimeoutException("BuffApplyNotify not received in time");
    }

    public async Task<SkillLevelUpNotify> WaitSkillLevelUpAsync(string? skillId = null, int timeoutMs = 8000, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            while (ReceivedSkillLevelUpNotifies.TryDequeue(out var n))
            {
                if (skillId is null || string.Equals(n.SkillId, skillId, StringComparison.OrdinalIgnoreCase))
                    return n;
            }
            await Task.Delay(20, ct);
        }
        throw new TimeoutException("SkillLevelUpNotify not received in time");
    }

    public async Task<SiegeDeclareResp> SiegeDeclareAsync(string castleId = "sabuk_castle", CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.SiegeDeclareReq, new SiegeDeclareReq { CastleId = castleId }, ct);
        var resp = SiegeDeclareResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
            Gold = resp.NewGold;
        return resp;
    }

    public async Task<SiegeStatusResp> SiegeStatusAsync(string castleId = "sabuk_castle", CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.SiegeStatusReq, new SiegeStatusReq { CastleId = castleId }, ct);
        return SiegeStatusResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<SiegeControlResp> SiegeControlAsync(string action, string castleId = "sabuk_castle", CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.SiegeControlReq, new SiegeControlReq { CastleId = castleId, Action = action }, ct);
        return SiegeControlResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<SiegeCaptureResp> SiegeCaptureAsync(string castleId = "sabuk_castle", CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.SiegeCaptureReq, new SiegeCaptureReq { CastleId = castleId }, ct);
        return SiegeCaptureResp.Parser.ParseFrom(frame.Payload);
    }


    public async Task<MailListResp> MailListAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.MailListReq, new MailListReq(), ct);
        return MailListResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<MailReadResp> MailReadAsync(string mailId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.MailReadReq, new MailReadReq { MailId = mailId }, ct);
        return MailReadResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<MailClaimAttachmentResp> MailClaimAsync(string mailId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.MailClaimAttachmentReq, new MailClaimAttachmentReq { MailId = mailId }, ct);
        var resp = MailClaimAttachmentResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
            Gold = resp.Gold;
        return resp;
    }

    public async Task<MailDeleteResp> MailDeleteAsync(string mailId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.MailDeleteReq, new MailDeleteReq { MailId = mailId }, ct);
        return MailDeleteResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<MailSendResp> MailSendAsync(string toName, string subject, string body, int attachGold = 0, string? itemId = null, int itemCount = 0, CancellationToken ct = default)
    {
        var req = new MailSendReq
        {
            ToCharacterName = toName,
            Subject = subject,
            Body = body,
            AttachGold = attachGold,
            AttachItemId = itemId ?? "",
            AttachItemCount = itemCount
        };
        var frame = await SendRequestAsync(MsgId.MailSendReq, req, ct);
        return MailSendResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<MailNotify> WaitMailNotifyAsync(int timeoutMs = 3000, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (ReceivedMailNotifies.TryDequeue(out var n))
                return n;
            await Task.Delay(20, ct);
        }
        throw new TimeoutException("MailNotify not received in time");
    }


    public async Task<DungeonEnterResp> DungeonEnterAsync(string dungeonId, CancellationToken ct = default, bool partyEnter = false)
    {
        var frame = await SendRequestAsync(MsgId.DungeonEnterReq, new DungeonEnterReq { DungeonId = dungeonId, PartyEnter = partyEnter }, ct);
        var resp = DungeonEnterResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
            Gold = resp.Gold;
        return resp;
    }

    public async Task<DungeonLeaveResp> DungeonLeaveAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.DungeonLeaveReq, new DungeonLeaveReq(), ct);
        return DungeonLeaveResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<DungeonStatusResp> DungeonStatusAsync(string dungeonId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.DungeonStatusReq, new DungeonStatusReq { DungeonId = dungeonId }, ct);
        return DungeonStatusResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<DungeonPartyEnterNotify> WaitDungeonPartyEnterAsync(int timeoutMs = 5000, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            ct.ThrowIfCancellationRequested();
            if (ReceivedDungeonPartyEnters.TryDequeue(out var n))
                return n;
            await Task.Delay(20, ct);
        }
        throw new TimeoutException("DungeonPartyEnterNotify not received");
    }

    public async Task<DungeonProgressNotify> WaitDungeonProgressAsync(int timeoutMs = 5000, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (ReceivedDungeonProgress.TryDequeue(out var n))
                return n;
            await Task.Delay(20, ct);
        }
        throw new TimeoutException("DungeonProgressNotify not received");
    }

    public async Task<DungeonCompleteNotify> WaitDungeonCompleteAsync(int timeoutMs = 30000, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (ReceivedDungeonCompletes.TryDequeue(out var n))
                return n;
            await Task.Delay(20, ct);
        }
        throw new TimeoutException("DungeonCompleteNotify not received");
    }

    public async ValueTask DisposeAsync()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is null)
            return;
        try { await cts.CancelAsync(); } catch (ObjectDisposedException) { }
        try { _tcp.Dispose(); } catch { }
        if (_readLoopTask != null)
        {
            try { await _readLoopTask; } catch { }
        }
        cts.Dispose();
    }
}
