using System.Collections.Concurrent;
using System.Net.Sockets;
using Google.Protobuf;
using Legend.Contracts;
using Legend.Contracts.Proto;

namespace Legend.Playable.Net;

/// <summary>
/// Focused TCP client for the playable slice. Same frame codec and MsgIds as CliClient / Unity MVP.
/// Protocol stays 1.21.0 (<see cref="ProtocolConstants.CurrentVersion"/>).
/// </summary>
public sealed class PlayableNetClient : IDisposable
{
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<PacketFrame>> _pending = new();
    private readonly ConcurrentQueue<Action> _main = new();
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly List<InventoryItem> _inventory = new();
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private uint _seq;
    private bool _disposed;

    public string? SessionToken { get; private set; }
    public string? AccountId { get; private set; }
    public string? CharacterId { get; private set; }
    public EntitySnapshot? SelfEntity { get; private set; }
    public int Gold { get; private set; }
    public int Level { get; private set; } = 1;
    public int Exp { get; private set; }
    public int ExpToNext { get; private set; }
    public int EffectiveAtk { get; private set; }
    public int EffectiveDef { get; private set; }
    public int EffectiveMaxHp { get; private set; }
    public int EffectiveMaxMp { get; private set; }
    public IReadOnlyList<InventoryItem> Inventory => _inventory;

    public event Action<EntitySnapshot>? OnSelfEnteredWorld;
    public event Action<EntitySnapshot>? OnEntityEnter;
    public event Action<string>? OnEntityLeave;
    public event Action<MoveSync>? OnMoveSync;
    public event Action<MapChangeNotify>? OnMapChange;
    public event Action<CombatSync>? OnCombatSync;
    public event Action<EntitySnapshot>? OnEntitySpawn;
    public event Action<string>? OnEntityDespawn;
    public event Action<LootDropNotify>? OnLootDrop;
    public event Action<InventoryNotify>? OnInventory;
    public event Action<ExpGainNotify>? OnExpGain;
    public event Action<LevelUpNotify>? OnLevelUp;
    public event Action<ChatNotify>? OnChat;
    public event Action<ServerNotice>? OnNotice;
    public event Action? OnDisconnected;

    public void Pump()
    {
        while (_main.TryDequeue(out var action))
        {
            try { action(); }
            catch { /* one bad handler must not stall the queue */ }
        }
    }

    public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        _tcp = new TcpClient { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        await _tcp.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
        _stream = _tcp.GetStream();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);
    }

    public async Task HandshakeAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.HandshakeReq, new HandshakeReq
        {
            ProtocolVersion = ProtocolConstants.CurrentVersion,
            ClientBuild = "playable-avalonia",
            DeviceId = "playable-pc"
        }, ct).ConfigureAwait(false);
        var hs = HandshakeResp.Parser.ParseFrom(frame.Payload);
        if (!hs.Accepted)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(hs.Message) ? "握手被拒绝" : hs.Message);
    }

    public async Task RegisterAsync(string username, string password, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.RegisterReq, new RegisterReq
        {
            Username = username,
            Password = password,
            ClientPlatform = "PC"
        }, ct).ConfigureAwait(false);
        var resp = RegisterResp.Parser.ParseFrom(frame.Payload);
        if (!resp.Success && resp.ErrorCode != ProtocolErrorCode.ErrUsernameTaken)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(resp.Message) ? "注册失败" : resp.Message);
    }

    public async Task LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.LoginReq, new LoginReq
        {
            Username = username,
            Password = password,
            ClientPlatform = "PC"
        }, ct).ConfigureAwait(false);
        var login = LoginResp.Parser.ParseFrom(frame.Payload);
        if (!login.Success)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(login.Message) ? "登录失败" : login.Message);
        SessionToken = login.SessionToken;
        AccountId = login.AccountId;
    }

    public async Task<IReadOnlyList<CharacterSummary>> CharListAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.CharListReq, new CharListReq
        {
            SessionToken = SessionToken ?? ""
        }, ct).ConfigureAwait(false);
        var resp = CharListResp.Parser.ParseFrom(frame.Payload);
        if (!resp.Success)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(resp.Message) ? "角色列表失败" : resp.Message);
        return resp.Characters.ToList();
    }

    public async Task<CharacterSummary> CreateCharAsync(string name, string classId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.CreateCharReq, new CreateCharReq
        {
            SessionToken = SessionToken ?? "",
            Name = name,
            ClassId = classId
        }, ct).ConfigureAwait(false);
        var resp = CreateCharResp.Parser.ParseFrom(frame.Payload);
        if (!resp.Success || resp.Character == null)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(resp.Message) ? "创建角色失败" : resp.Message);
        return resp.Character;
    }

    public async Task SelectCharAsync(string characterId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.SelectCharReq, new SelectCharReq
        {
            SessionToken = SessionToken ?? "",
            CharacterId = characterId
        }, ct).ConfigureAwait(false);
        var resp = SelectCharResp.Parser.ParseFrom(frame.Payload);
        if (!resp.Success)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(resp.Message) ? "选择角色失败" : resp.Message);
        CharacterId = resp.Character?.CharacterId ?? characterId;
    }

    public async Task EnterWorldAsync(string realmId, string characterId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.EnterWorldReq, new EnterWorldReq
        {
            SessionToken = SessionToken ?? "",
            RealmId = realmId,
            CharacterId = characterId
        }, ct).ConfigureAwait(false);
        var enter = EnterWorldResp.Parser.ParseFrom(frame.Payload);
        if (!enter.Success || enter.SelfEntity == null)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(enter.Message) ? "进入世界失败" : enter.Message);
        SelfEntity = enter.SelfEntity;
        Gold = enter.Gold;
        Level = enter.Level;
        Exp = enter.Exp;
        ExpToNext = enter.ExpToNext;
        EffectiveAtk = enter.EffectiveAtk;
        EffectiveDef = enter.EffectiveDef;
        EffectiveMaxHp = enter.EffectiveMaxHp;
        EffectiveMaxMp = enter.EffectiveMaxMp;
        ReplaceInventory(enter.Inventory);
        var self = enter.SelfEntity;
        var nearby = enter.NearbyEntities.ToList();
        _main.Enqueue(() =>
        {
            OnSelfEnteredWorld?.Invoke(self);
            foreach (var other in nearby)
                OnEntityEnter?.Invoke(other);
        });
    }

    public async Task<MoveSync> RequestMoveAsync(int toX, int toY, int direction, CancellationToken ct = default)
    {
        string? mapBefore = SelfEntity?.MapId;
        var frame = await SendRequestAsync(MsgId.MoveReq, new MoveReq
        {
            ToX = toX,
            ToY = toY,
            Direction = direction
        }, ct).ConfigureAwait(false);
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

    public async Task<AttackResp> RequestAttackAsync(string targetEntityId, string skillId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.AttackReq, new AttackReq
        {
            TargetEntityId = targetEntityId,
            SkillId = skillId
        }, ct).ConfigureAwait(false);
        return AttackResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<PickupResp> RequestPickupAsync(string lootEntityId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.PickupReq, new PickupReq
        {
            LootEntityId = lootEntityId
        }, ct).ConfigureAwait(false);
        var resp = PickupResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
            ReplaceInventory(resp.Inventory);
        return resp;
    }

    public async Task<TalkResp> RequestTalkAsync(string npcEntityId, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.TalkReq, new TalkReq
        {
            NpcEntityId = npcEntityId
        }, ct).ConfigureAwait(false);
        var resp = TalkResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
            Gold = resp.Gold;
        return resp;
    }

    public async Task<UseItemResp> RequestUseItemAsync(string itemId, int count = 1, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.UseItemReq, new UseItemReq
        {
            ItemId = itemId,
            Count = count
        }, ct).ConfigureAwait(false);
        var resp = UseItemResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success && SelfEntity != null)
        {
            SelfEntity.Hp = resp.Hp;
            SelfEntity.MaxHp = resp.MaxHp;
        }
        return resp;
    }

    public async Task SendChatAsync(ChatChannel channel, string content, CancellationToken ct = default)
    {
        if (_stream == null)
            throw new InvalidOperationException("未连接");
        uint seq = Interlocked.Increment(ref _seq);
        await _write.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await PacketCodec.WriteFrameAsync(_stream, MsgId.ChatReq, seq, new ChatReq
            {
                Channel = channel,
                Content = content
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _write.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { _cts?.Cancel(); } catch { /* already cancelled */ }
        try { _tcp?.Close(); } catch { /* already closed */ }
        foreach (var kv in _pending)
            kv.Value.TrySetCanceled();
        _cts?.Dispose();
        _write.Dispose();
    }

    private async Task<PacketFrame> SendRequestAsync(MsgId msgId, IMessage message, CancellationToken ct)
    {
        if (_stream == null)
            throw new InvalidOperationException("未连接");
        uint seq = Interlocked.Increment(ref _seq);
        var tcs = new TaskCompletionSource<PacketFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[seq] = tcs;
        await _write.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await PacketCodec.WriteFrameAsync(_stream, msgId, seq, message, ct).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(seq, out _);
            throw;
        }
        finally
        {
            _write.Release();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            return await tcs.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            _pending.TryRemove(seq, out _);
            throw;
        }
    }

    private void ReplaceInventory(IEnumerable<InventoryItem> items)
    {
        _inventory.Clear();
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.ItemId) || it.Count <= 0)
                continue;
            _inventory.Add(new InventoryItem { ItemId = it.ItemId, Count = it.Count });
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        if (_stream == null)
            return;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frameOpt = await PacketCodec.ReadFrameAsync(_stream, ct).ConfigureAwait(false);
                if (frameOpt == null)
                    break;
                Dispatch(frameOpt.Value);
            }
        }
        catch (OperationCanceledException)
        {
            /* dispose */
        }
        catch (Exception ex)
        {
            foreach (var kv in _pending)
                kv.Value.TrySetException(ex);
        }
        finally
        {
            _main.Enqueue(() => OnDisconnected?.Invoke());
        }
    }

    private void Dispatch(PacketFrame frame)
    {
        if (frame.Seq != 0 && _pending.TryRemove(frame.Seq, out var tcs))
        {
            if (frame.MsgId == MsgId.MoveSync)
            {
                var sync = MoveSync.Parser.ParseFrom(frame.Payload);
                _main.Enqueue(() => OnMoveSync?.Invoke(sync));
            }
            tcs.TrySetResult(frame);
            return;
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
                    SelfEntity.Direction = sync.Direction;
                    SelfEntity.InSafeZone = sync.InSafeZone;
                }
                _main.Enqueue(() => OnMoveSync?.Invoke(sync));
                break;
            }
            case MsgId.EntityEnterAoi:
            {
                var enter = EntityEnterAoi.Parser.ParseFrom(frame.Payload);
                if (enter.Entity != null)
                    _main.Enqueue(() => OnEntityEnter?.Invoke(enter.Entity));
                break;
            }
            case MsgId.EntityLeaveAoi:
            {
                var leave = EntityLeaveAoi.Parser.ParseFrom(frame.Payload);
                _main.Enqueue(() => OnEntityLeave?.Invoke(leave.EntityId));
                break;
            }
            case MsgId.MapChangeNotify:
            {
                var change = MapChangeNotify.Parser.ParseFrom(frame.Payload);
                if (SelfEntity != null && change.EntityId == SelfEntity.EntityId)
                {
                    SelfEntity.MapId = change.ToMapId;
                    SelfEntity.X = change.ToX;
                    SelfEntity.Y = change.ToY;
                    SelfEntity.InSafeZone = change.InSafeZone;
                }
                _main.Enqueue(() => OnMapChange?.Invoke(change));
                break;
            }
            case MsgId.CombatSync:
            {
                var sync = CombatSync.Parser.ParseFrom(frame.Payload);
                if (SelfEntity != null && sync.TargetEntityId == SelfEntity.EntityId)
                {
                    SelfEntity.Hp = sync.TargetHp;
                    SelfEntity.MaxHp = sync.TargetMaxHp;
                }
                _main.Enqueue(() => OnCombatSync?.Invoke(sync));
                break;
            }
            case MsgId.EntitySpawn:
            {
                var spawn = EntitySpawn.Parser.ParseFrom(frame.Payload);
                if (spawn.Entity != null)
                    _main.Enqueue(() => OnEntitySpawn?.Invoke(spawn.Entity));
                break;
            }
            case MsgId.EntityDespawn:
            {
                var despawn = EntityDespawn.Parser.ParseFrom(frame.Payload);
                _main.Enqueue(() => OnEntityDespawn?.Invoke(despawn.EntityId));
                break;
            }
            case MsgId.LootDropNotify:
            {
                var loot = LootDropNotify.Parser.ParseFrom(frame.Payload);
                _main.Enqueue(() => OnLootDrop?.Invoke(loot));
                break;
            }
            case MsgId.InventoryNotify:
            {
                var inv = InventoryNotify.Parser.ParseFrom(frame.Payload);
                Gold = inv.Gold;
                ReplaceInventory(inv.Items);
                _main.Enqueue(() => OnInventory?.Invoke(inv));
                break;
            }
            case MsgId.ExpGainNotify:
            {
                var exp = ExpGainNotify.Parser.ParseFrom(frame.Payload);
                Exp = exp.TotalExp;
                Level = exp.Level;
                ExpToNext = exp.ExpToNext;
                _main.Enqueue(() => OnExpGain?.Invoke(exp));
                break;
            }
            case MsgId.LevelUpNotify:
            {
                var lu = LevelUpNotify.Parser.ParseFrom(frame.Payload);
                Level = lu.NewLevel;
                Exp = lu.Exp;
                ExpToNext = lu.ExpToNext;
                EffectiveAtk = lu.EffectiveAtk;
                EffectiveDef = lu.EffectiveDef;
                EffectiveMaxHp = lu.EffectiveMaxHp;
                EffectiveMaxMp = lu.BaseMaxMp > 0 ? lu.BaseMaxMp : EffectiveMaxMp;
                if (SelfEntity != null)
                {
                    SelfEntity.Hp = lu.Hp;
                    SelfEntity.MaxHp = lu.EffectiveMaxHp;
                }
                _main.Enqueue(() => OnLevelUp?.Invoke(lu));
                break;
            }
            case MsgId.ChatNotify:
            {
                var chat = ChatNotify.Parser.ParseFrom(frame.Payload);
                _main.Enqueue(() => OnChat?.Invoke(chat));
                break;
            }
            case MsgId.ServerNotice:
            {
                var notice = ServerNotice.Parser.ParseFrom(frame.Payload);
                _main.Enqueue(() => OnNotice?.Invoke(notice));
                break;
            }
        }
    }
}
