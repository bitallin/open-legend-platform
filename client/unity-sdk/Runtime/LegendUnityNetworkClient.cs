using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Legend.Contracts;
using Legend.Contracts.Proto;

namespace Legend.UnitySdk
{
    /// <summary>
    /// Unity 2022 LTS Compatible Pure C# Network Client (PC + Android).
    /// Marshals all network callbacks onto Unity's main thread via PumpMainThreadEvents() called in MonoBehaviour.Update().
    /// </summary>
    public sealed class LegendUnityNetworkClient : IDisposable
    {
        private TcpClient? _tcp;
        private NetworkStream? _stream;
        private CancellationTokenSource? _cts;
        private uint _seq;

        private readonly ConcurrentDictionary<uint, TaskCompletionSource<PacketFrame>> _pending = new();
        private readonly ConcurrentQueue<Action> _mainThreadActions = new();

        public string? SessionToken { get; private set; }
        public string? AccountId { get; private set; }
        public EntitySnapshot? SelfEntity { get; private set; }

        public event Action<EntitySnapshot>? OnSelfEnteredWorld;
        public event Action<EntitySnapshot>? OnEntityEnterAoi;
        public event Action<string, string>? OnEntityLeaveAoi;
        public event Action<MoveSync>? OnMoveSync;
        public event Action<ServerNotice>? OnServerNotice;

        /// <summary>
        /// Call this inside Unity MonoBehaviour.Update() every frame to dispatch network events on the Unity main thread.
        /// </summary>
        public void PumpMainThreadEvents()
        {
            while (_mainThreadActions.TryDequeue(out var action))
            {
                action.Invoke();
            }
        }

        public async Task ConnectAndEnterAsync(
            string host,
            int port,
            string username,
            string password,
            string characterName,
            string classId = "warrior",
            string platform = "PC",
            CancellationToken ct = default)
        {
            _tcp = new TcpClient { NoDelay = true };
            await _tcp.ConnectAsync(host, port, ct);
            _stream = _tcp.GetStream();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);

            var hs = await SendHandshakeAsync(ProtocolConstants.CurrentVersion, $"{platform}-unity-2022", ct);
            if (!hs.Accepted)
                throw new InvalidOperationException($"Handshake rejected ({hs.ErrorCode}): {hs.Message}");

            var login = await SendLoginAsync(username, password, platform, ct);
            if (!login.Success)
                throw new InvalidOperationException($"Login failed ({login.ErrorCode}): {login.Message}");

            SessionToken = login.SessionToken;
            AccountId = login.AccountId;

            var realms = await SendRealmListAsync(ct);
            if (!realms.Success || realms.Realms.Count == 0)
                throw new InvalidOperationException("No available realms returned by server.");

            string realmId = realms.Realms[0].RealmId;
            var enter = await SendEnterWorldAsync(realmId, characterName, classId, ct);
            if (!enter.Success)
                throw new InvalidOperationException($"EnterWorld failed ({enter.ErrorCode}): {enter.Message}");

            SelfEntity = enter.SelfEntity;
            _mainThreadActions.Enqueue(() =>
            {
                OnSelfEnteredWorld?.Invoke(enter.SelfEntity);
                foreach (var other in enter.NearbyEntities)
                {
                    OnEntityEnterAoi?.Invoke(other);
                }
            });
        }

        public async Task<MoveSync> RequestMoveAsync(int toX, int toY, int direction = 4, CancellationToken ct = default)
        {
            var frame = await SendRequestAsync(MsgId.MoveReq, new MoveReq
            {
                ToX = toX,
                ToY = toY,
                Direction = direction
            }, ct);
            return MoveSync.Parser.ParseFrom(frame.Payload);
        }

        private async Task<HandshakeResp> SendHandshakeAsync(string version, string build, CancellationToken ct)
        {
            var frame = await SendRequestAsync(MsgId.HandshakeReq, new HandshakeReq
            {
                ProtocolVersion = version,
                ClientBuild = build,
                DeviceId = "unity-device"
            }, ct);
            return HandshakeResp.Parser.ParseFrom(frame.Payload);
        }

        private async Task<LoginResp> SendLoginAsync(string username, string password, string platform, CancellationToken ct)
        {
            var frame = await SendRequestAsync(MsgId.LoginReq, new LoginReq
            {
                Username = username,
                Password = password,
                ClientPlatform = platform
            }, ct);
            return LoginResp.Parser.ParseFrom(frame.Payload);
        }

        private async Task<RealmListResp> SendRealmListAsync(CancellationToken ct)
        {
            var frame = await SendRequestAsync(MsgId.RealmListReq, new RealmListReq
            {
                SessionToken = SessionToken ?? string.Empty
            }, ct);
            return RealmListResp.Parser.ParseFrom(frame.Payload);
        }

        private async Task<EnterWorldResp> SendEnterWorldAsync(string realmId, string charName, string classId, CancellationToken ct)
        {
            var frame = await SendRequestAsync(MsgId.EnterWorldReq, new EnterWorldReq
            {
                SessionToken = SessionToken ?? string.Empty,
                RealmId = realmId,
                CharacterName = charName,
                ClassId = classId
            }, ct);
            return EnterWorldResp.Parser.ParseFrom(frame.Payload);
        }

        private async Task<PacketFrame> SendRequestAsync(MsgId msgId, IMessage message, CancellationToken ct)
        {
            if (_stream == null)
                throw new InvalidOperationException("Not connected");

            uint seq = Interlocked.Increment(ref _seq);
            var tcs = new TaskCompletionSource<PacketFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[seq] = tcs;

            await PacketCodec.WriteFrameAsync(_stream, msgId, seq, message, ct);
            return await tcs.Task;
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
                    if (frame.Seq != 0 && _pending.TryRemove(frame.Seq, out var tcs))
                    {
                        if (frame.MsgId == MsgId.MoveSync)
                        {
                            var sync = MoveSync.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnMoveSync?.Invoke(sync));
                        }
                        tcs.TrySetResult(frame);
                        continue;
                    }

                    switch (frame.MsgId)
                    {
                        case MsgId.MoveSync:
                        {
                            var sync = MoveSync.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnMoveSync?.Invoke(sync));
                            break;
                        }
                        case MsgId.EntityEnterAoi:
                        {
                            var enter = EntityEnterAoi.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnEntityEnterAoi?.Invoke(enter.Entity));
                            break;
                        }
                        case MsgId.EntityLeaveAoi:
                        {
                            var leave = EntityLeaveAoi.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnEntityLeaveAoi?.Invoke(leave.EntityId, leave.Reason));
                            break;
                        }
                        case MsgId.ServerNotice:
                        {
                            var notice = ServerNotice.Parser.ParseFrom(frame.Payload);
                            _mainThreadActions.Enqueue(() => OnServerNotice?.Invoke(notice));
                            break;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Disconnected
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _tcp?.Dispose();
            _cts?.Dispose();
        }
    }
}
