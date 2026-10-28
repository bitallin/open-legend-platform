using System.Collections.Concurrent;
using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;

namespace Legend.Login;

public sealed class SessionTokenRegistry
{
    private readonly ConcurrentDictionary<string, string> _tokenToAccount = new();

    public string IssueToken(string accountId)
    {
        string token = $"tk_{accountId}_{Guid.NewGuid():N}";
        _tokenToAccount[token] = accountId;
        return token;
    }

    public bool TryValidateToken(string token, out string accountId)
    {
        if (!string.IsNullOrWhiteSpace(token) && _tokenToAccount.TryGetValue(token, out var acc))
        {
            accountId = acc;
            return true;
        }
        accountId = string.Empty;
        return false;
    }
}

public sealed class LoginModule : IGameModule
{
    private readonly IEventBus _eventBus;
    private readonly LoadedVersionPack _versionPack;
    private readonly SessionTokenRegistry _tokenRegistry;
    private readonly Func<int> _onlineCountProvider;
    private readonly Func<int> _boundPortProvider;

    public LoginModule(
        IEventBus eventBus,
        LoadedVersionPack versionPack,
        SessionTokenRegistry tokenRegistry,
        Func<int> onlineCountProvider,
        Func<int> boundPortProvider)
    {
        _eventBus = eventBus;
        _versionPack = versionPack;
        _tokenRegistry = tokenRegistry;
        _onlineCountProvider = onlineCountProvider;
        _boundPortProvider = boundPortProvider;
    }

    public string ModuleName => "LoginModule";

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public bool CanHandle(MsgId msgId) => msgId is
        MsgId.HandshakeReq or
        MsgId.LoginReq or
        MsgId.RealmListReq or
        MsgId.PingReq;

    public async Task HandlePacketAsync(IClientSession session, PacketFrame frame, CancellationToken ct = default)
    {
        switch (frame.MsgId)
        {
            case MsgId.HandshakeReq:
                await HandleHandshakeAsync(session, frame, ct);
                break;
            case MsgId.LoginReq:
                await HandleLoginAsync(session, frame, ct);
                break;
            case MsgId.RealmListReq:
                await HandleRealmListAsync(session, frame, ct);
                break;
            case MsgId.PingReq:
                await HandlePingAsync(session, frame, ct);
                break;
        }
    }

    private async Task HandleHandshakeAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = HandshakeReq.Parser.ParseFrom(frame.Payload);
        bool compatible = ProtocolConstants.IsVersionCompatible(req.ProtocolVersion);

        if (!compatible)
        {
            StructuredLogger.Warn("LoginModule",
                $"Handshake rejected due to version mismatch: client={req.ProtocolVersion}, server={ProtocolConstants.CurrentVersion}",
                connId: session.ConnectionId);

            var rejectResp = new HandshakeResp
            {
                Accepted = false,
                ErrorCode = ProtocolErrorCode.ErrVersionMismatch,
                ServerProtocolVersion = ProtocolConstants.CurrentVersion,
                MinCompatibleVersion = ProtocolConstants.MinCompatibleVersion,
                SessionNonce = string.Empty,
                ServerTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Message = $"Protocol version '{req.ProtocolVersion}' is incompatible with server '{ProtocolConstants.CurrentVersion}'."
            };
            await session.SendAsync(MsgId.HandshakeResp, frame.Seq, rejectResp, ct);
            return;
        }

        session.IsHandshaked = true;
        var resp = new HandshakeResp
        {
            Accepted = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            ServerProtocolVersion = ProtocolConstants.CurrentVersion,
            MinCompatibleVersion = ProtocolConstants.MinCompatibleVersion,
            SessionNonce = Guid.NewGuid().ToString("N")[..12],
            ServerTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Message = "Handshake OK"
        };

        StructuredLogger.Info("LoginModule", $"Handshake accepted (client={req.ProtocolVersion}, build={req.ClientBuild})", connId: session.ConnectionId);
        await session.SendAsync(MsgId.HandshakeResp, frame.Seq, resp, ct);
    }

    private async Task HandleLoginAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        if (!session.IsHandshaked)
        {
            await session.SendAsync(MsgId.LoginResp, frame.Seq, new LoginResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrVersionMismatch,
                Message = "Must complete Handshake before Login."
            }, ct);
            return;
        }

        var req = LoginReq.Parser.ParseFrom(frame.Payload);
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
        {
            await session.SendAsync(MsgId.LoginResp, frame.Seq, new LoginResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrAuthFailed,
                Message = "Username or password cannot be empty."
            }, ct);
            return;
        }

        string accountId = $"acc_{req.Username.Trim().ToLowerInvariant()}";
        string token = _tokenRegistry.IssueToken(accountId);
        session.AccountId = accountId;
        session.SessionToken = token;

        await _eventBus.PublishAsync(new PlayerLoggedInEvent(
            session.ConnectionId,
            accountId,
            req.Username,
            string.IsNullOrWhiteSpace(req.ClientPlatform) ? "PC" : req.ClientPlatform), ct);

        StructuredLogger.Info("LoginModule", $"Account logged in: {accountId} ({req.ClientPlatform})", connId: session.ConnectionId, playerId: accountId);

        await session.SendAsync(MsgId.LoginResp, frame.Seq, new LoginResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            AccountId = accountId,
            SessionToken = token,
            Message = "Login successful"
        }, ct);
    }

    private async Task HandleRealmListAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = RealmListReq.Parser.ParseFrom(frame.Payload);
        if (!_tokenRegistry.TryValidateToken(req.SessionToken, out _))
        {
            await session.SendAsync(MsgId.RealmListResp, frame.Seq, new RealmListResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken
            }, ct);
            return;
        }

        var resp = new RealmListResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone
        };
        resp.Realms.Add(new RealmInfo
        {
            RealmId = _versionPack.Manifest.RealmId,
            RealmName = _versionPack.Manifest.RealmName,
            VersionPackId = _versionPack.Manifest.VersionId,
            Status = "OPEN",
            Host = "127.0.0.1",
            Port = _boundPortProvider(),
            OnlineCount = _onlineCountProvider()
        });

        await session.SendAsync(MsgId.RealmListResp, frame.Seq, resp, ct);
    }

    private async Task HandlePingAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = PingReq.Parser.ParseFrom(frame.Payload);
        var resp = new PongResp
        {
            ClientTimeUnixMs = req.ClientTimeUnixMs,
            ServerTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        await session.SendAsync(MsgId.PongResp, frame.Seq, resp, ct);
    }

    public Task OnSessionDisconnectedAsync(IClientSession session, CancellationToken ct = default)
        => Task.CompletedTask;
}
