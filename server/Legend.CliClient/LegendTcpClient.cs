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
        string clientBuild = "pc-cli-1.5.0",
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
            Gold = resp.Gold;
        if (resp.Success)
            SelfEntity = resp.SelfEntity;
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
