using System.Collections.Concurrent;
using System.Net.Sockets;
using Google.Protobuf;
using Legend.Contracts;
using Legend.Contracts.Proto;

namespace Legend.CliClient;

/// <summary>
/// Reusable C# client SDK that connects to Legend.Host over TCP, frames Protobuf messages,
/// awaits request/response pairs, and collects asynchronous server broadcasts (MoveSync, EntityEnterAoi, ServerNotice).
/// </summary>
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

    public event Action<MoveSync>? OnMoveSync;
    public event Action<EntityEnterAoi>? OnEntityEnterAoi;
    public event Action<EntityLeaveAoi>? OnEntityLeaveAoi;
    public event Action<ServerNotice>? OnServerNotice;

    public string? SessionToken { get; private set; }
    public string? AccountId { get; private set; }
    public EntitySnapshot? SelfEntity { get; private set; }

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

                // Server push / broadcast (seq == 0)
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
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            foreach (var kv in _pendingRequests)
            {
                kv.Value.TrySetException(ex);
            }
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
        string clientBuild = "pc-cli-1.0.0",
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

    public async Task<RealmListResp> GetRealmListAsync(CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.RealmListReq, new RealmListReq
        {
            SessionToken = SessionToken ?? string.Empty
        }, ct);

        return RealmListResp.Parser.ParseFrom(frame.Payload);
    }

    public async Task<EnterWorldResp> EnterWorldAsync(string realmId, string characterName, string classId = "warrior", CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.EnterWorldReq, new EnterWorldReq
        {
            SessionToken = SessionToken ?? string.Empty,
            RealmId = realmId,
            CharacterName = characterName,
            ClassId = classId
        }, ct);

        var resp = EnterWorldResp.Parser.ParseFrom(frame.Payload);
        if (resp.Success)
        {
            SelfEntity = resp.SelfEntity;
        }
        return resp;
    }

    public async Task<MoveSync> MoveAsync(int toX, int toY, int direction = 4, CancellationToken ct = default)
    {
        var frame = await SendRequestAsync(MsgId.MoveReq, new MoveReq
        {
            ToX = toX,
            ToY = toY,
            Direction = direction
        }, ct);

        var sync = MoveSync.Parser.ParseFrom(frame.Payload);
        if (sync.Accepted && SelfEntity != null)
        {
            SelfEntity.X = sync.ToX;
            SelfEntity.Y = sync.ToY;
            SelfEntity.Direction = sync.Direction;
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

    public async ValueTask DisposeAsync()
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
        }
        _tcp.Dispose();
        if (_readLoopTask != null)
        {
            try
            {
                await _readLoopTask;
            }
            catch
            {
                // Ignore
            }
        }
        _cts?.Dispose();
    }
}
