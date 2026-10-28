using Legend.Contracts;
using Legend.Contracts.Proto;
using Legend.Core;
using Legend.Storage;

namespace Legend.Login;

public sealed class LoginModule : IGameModule
{
    private readonly IEventBus _eventBus;
    private readonly LoadedVersionPack _versionPack;
    private readonly ILegendStorage _storage;
    private readonly Func<int> _onlineCountProvider;
    private readonly Func<int> _boundPortProvider;

    public LoginModule(
        IEventBus eventBus,
        LoadedVersionPack versionPack,
        ILegendStorage storage,
        Func<int> onlineCountProvider,
        Func<int> boundPortProvider)
    {
        _eventBus = eventBus;
        _versionPack = versionPack;
        _storage = storage;
        _onlineCountProvider = onlineCountProvider;
        _boundPortProvider = boundPortProvider;
    }

    public string ModuleName => "LoginModule";

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

    public bool CanHandle(MsgId msgId) => msgId is
        MsgId.HandshakeReq or
        MsgId.RegisterReq or
        MsgId.LoginReq or
        MsgId.RealmListReq or
        MsgId.PingReq or
        MsgId.CharListReq or
        MsgId.CreateCharReq or
        MsgId.DeleteCharReq or
        MsgId.SelectCharReq;

    public async Task HandlePacketAsync(IClientSession session, PacketFrame frame, CancellationToken ct = default)
    {
        switch (frame.MsgId)
        {
            case MsgId.HandshakeReq:
                await HandleHandshakeAsync(session, frame, ct);
                break;
            case MsgId.RegisterReq:
                await HandleRegisterAsync(session, frame, ct);
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
            case MsgId.CharListReq:
                await HandleCharListAsync(session, frame, ct);
                break;
            case MsgId.CreateCharReq:
                await HandleCreateCharAsync(session, frame, ct);
                break;
            case MsgId.DeleteCharReq:
                await HandleDeleteCharAsync(session, frame, ct);
                break;
            case MsgId.SelectCharReq:
                await HandleSelectCharAsync(session, frame, ct);
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
                $"Handshake rejected: client={req.ProtocolVersion}, server={ProtocolConstants.CurrentVersion}",
                connId: session.ConnectionId);

            await session.SendAsync(MsgId.HandshakeResp, frame.Seq, new HandshakeResp
            {
                Accepted = false,
                ErrorCode = ProtocolErrorCode.ErrVersionMismatch,
                ServerProtocolVersion = ProtocolConstants.CurrentVersion,
                MinCompatibleVersion = ProtocolConstants.MinCompatibleVersion,
                SessionNonce = string.Empty,
                ServerTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Message = $"Protocol version '{req.ProtocolVersion}' is incompatible with server '{ProtocolConstants.CurrentVersion}'."
            }, ct);
            return;
        }

        session.IsHandshaked = true;
        await session.SendAsync(MsgId.HandshakeResp, frame.Seq, new HandshakeResp
        {
            Accepted = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            ServerProtocolVersion = ProtocolConstants.CurrentVersion,
            MinCompatibleVersion = ProtocolConstants.MinCompatibleVersion,
            SessionNonce = Guid.NewGuid().ToString("N")[..12],
            ServerTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Message = "Handshake OK"
        }, ct);
    }

    private async Task HandleRegisterAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        if (!session.IsHandshaked)
        {
            await session.SendAsync(MsgId.RegisterResp, frame.Seq, new RegisterResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrVersionMismatch,
                Message = "Must complete Handshake before Register."
            }, ct);
            return;
        }

        var req = RegisterReq.Parser.ParseFrom(frame.Payload);
        var (ok, error, account) = await _storage.Accounts.RegisterAsync(req.Username, req.Password, ct);
        if (!ok || account is null)
        {
            var code = error?.Contains("已存在") == true
                ? ProtocolErrorCode.ErrUsernameTaken
                : ProtocolErrorCode.ErrAuthFailed;
            await session.SendAsync(MsgId.RegisterResp, frame.Seq, new RegisterResp
            {
                Success = false,
                ErrorCode = code,
                Message = error ?? "注册失败"
            }, ct);
            return;
        }

        StructuredLogger.Info("LoginModule", $"Account registered: {account.AccountId}", connId: session.ConnectionId, playerId: account.AccountId);
        await session.SendAsync(MsgId.RegisterResp, frame.Seq, new RegisterResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            AccountId = account.AccountId,
            Message = "注册成功"
        }, ct);
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
        var (ok, error, account) = await _storage.Accounts.AuthenticateAsync(req.Username, req.Password, ct);
        if (!ok || account is null)
        {
            await session.SendAsync(MsgId.LoginResp, frame.Seq, new LoginResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrAuthFailed,
                Message = error ?? "用户名或密码错误"
            }, ct);
            return;
        }

        string token = await _storage.Sessions.CreateSessionAsync(account.AccountId, account.Username, TimeSpan.FromHours(12), ct);
        session.AccountId = account.AccountId;
        session.SessionToken = token;

        await _eventBus.PublishAsync(new PlayerLoggedInEvent(
            session.ConnectionId,
            account.AccountId,
            account.Username,
            string.IsNullOrWhiteSpace(req.ClientPlatform) ? "PC" : req.ClientPlatform), ct);

        StructuredLogger.Info("LoginModule", $"Account logged in: {account.AccountId}", connId: session.ConnectionId, playerId: account.AccountId);

        await session.SendAsync(MsgId.LoginResp, frame.Seq, new LoginResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            AccountId = account.AccountId,
            SessionToken = token,
            Message = "Login successful"
        }, ct);
    }

    private async Task<SessionRecord?> RequireSessionAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;
        return await _storage.Sessions.GetSessionAsync(token, ct);
    }

    private async Task HandleRealmListAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = RealmListReq.Parser.ParseFrom(frame.Payload);
        var sess = await RequireSessionAsync(req.SessionToken, ct);
        if (sess is null)
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
        await session.SendAsync(MsgId.PongResp, frame.Seq, new PongResp
        {
            ClientTimeUnixMs = req.ClientTimeUnixMs,
            ServerTimeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }, ct);
    }

    private static CharacterSummary ToSummary(CharacterRecord c) => new()
    {
        CharacterId = c.CharacterId,
        Name = c.Name,
        ClassId = c.ClassId,
        Level = c.Level,
        MapId = c.MapId,
        X = c.X,
        Y = c.Y,
        Hp = c.Hp,
        MaxHp = c.MaxHp,
        Mp = c.Mp,
        MaxMp = c.MaxMp
    };

    private async Task HandleCharListAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = CharListReq.Parser.ParseFrom(frame.Payload);
        var sess = await RequireSessionAsync(req.SessionToken, ct);
        if (sess is null)
        {
            await session.SendAsync(MsgId.CharListResp, frame.Seq, new CharListResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Invalid session"
            }, ct);
            return;
        }

        var chars = await _storage.Characters.ListByAccountAsync(sess.AccountId, ct);
        var resp = new CharListResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = $"共 {chars.Count} 个角色"
        };
        foreach (var c in chars)
            resp.Characters.Add(ToSummary(c));
        await session.SendAsync(MsgId.CharListResp, frame.Seq, resp, ct);
    }

    private async Task HandleCreateCharAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = CreateCharReq.Parser.ParseFrom(frame.Payload);
        var sess = await RequireSessionAsync(req.SessionToken, ct);
        if (sess is null)
        {
            await session.SendAsync(MsgId.CreateCharResp, frame.Seq, new CreateCharResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Invalid session"
            }, ct);
            return;
        }

        string classId = (req.ClassId ?? string.Empty).Trim().ToLowerInvariant();
        if (!_versionPack.Classes.TryGetValue(classId, out var classConfig))
        {
            await session.SendAsync(MsgId.CreateCharResp, frame.Seq, new CreateCharResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidClass,
                Message = $"职业 '{req.ClassId}' 不存在，允许: {string.Join(",", _versionPack.Classes.Keys)}"
            }, ct);
            return;
        }

        var map = _versionPack.Map;
        var character = new CharacterRecord
        {
            AccountId = sess.AccountId,
            Name = req.Name ?? string.Empty,
            ClassId = classConfig.ClassId,
            Level = 1,
            MapId = map.MapId,
            X = map.SpawnX,
            Y = map.SpawnY,
            Hp = classConfig.BaseHp,
            MaxHp = classConfig.BaseHp,
            Mp = classConfig.BaseMp,
            MaxMp = classConfig.BaseMp,
            InventoryJson = InventoryJson.Empty
        };

        int maxChars = _versionPack.Manifest.MaxCharactersPerAccount > 0
            ? _versionPack.Manifest.MaxCharactersPerAccount
            : 3;

        var (ok, error, created) = await _storage.Characters.CreateAsync(character, maxChars, ct);
        if (!ok || created is null)
        {
            var code = error?.Contains("最多") == true ? ProtocolErrorCode.ErrCharLimit
                : error?.Contains("已存在") == true ? ProtocolErrorCode.ErrCharNameTaken
                : ProtocolErrorCode.ErrInternal;
            await session.SendAsync(MsgId.CreateCharResp, frame.Seq, new CreateCharResp
            {
                Success = false,
                ErrorCode = code,
                Message = error ?? "创角失败"
            }, ct);
            return;
        }

        StructuredLogger.Info("LoginModule", $"Character created: {created.Name} ({created.CharacterId})", connId: session.ConnectionId, playerId: sess.AccountId);
        await session.SendAsync(MsgId.CreateCharResp, frame.Seq, new CreateCharResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Character = ToSummary(created),
            Message = "创角成功"
        }, ct);
    }

    private async Task HandleDeleteCharAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = DeleteCharReq.Parser.ParseFrom(frame.Payload);
        var sess = await RequireSessionAsync(req.SessionToken, ct);
        if (sess is null)
        {
            await session.SendAsync(MsgId.DeleteCharResp, frame.Seq, new DeleteCharResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Invalid session"
            }, ct);
            return;
        }

        var (ok, error) = await _storage.Characters.DeleteAsync(sess.AccountId, req.CharacterId, ct);
        if (!ok)
        {
            await session.SendAsync(MsgId.DeleteCharResp, frame.Seq, new DeleteCharResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrCharNotFound,
                Message = error ?? "角色不存在"
            }, ct);
            return;
        }

        await session.SendAsync(MsgId.DeleteCharResp, frame.Seq, new DeleteCharResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = "删角成功"
        }, ct);
    }

    private async Task HandleSelectCharAsync(IClientSession session, PacketFrame frame, CancellationToken ct)
    {
        var req = SelectCharReq.Parser.ParseFrom(frame.Payload);
        var sess = await RequireSessionAsync(req.SessionToken, ct);
        if (sess is null)
        {
            await session.SendAsync(MsgId.SelectCharResp, frame.Seq, new SelectCharResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrInvalidToken,
                Message = "Invalid session"
            }, ct);
            return;
        }

        var character = await _storage.Characters.GetByIdAsync(req.CharacterId, ct);
        if (character is null || character.AccountId != sess.AccountId)
        {
            await session.SendAsync(MsgId.SelectCharResp, frame.Seq, new SelectCharResp
            {
                Success = false,
                ErrorCode = ProtocolErrorCode.ErrCharNotFound,
                Message = "角色不存在"
            }, ct);
            return;
        }

        await _storage.Sessions.BindCharacterAsync(req.SessionToken, character.CharacterId, ct);
        session.AccountId = sess.AccountId;
        session.SessionToken = req.SessionToken;
        session.CharacterId = character.CharacterId;
        session.CharacterName = character.Name;
        session.ClassId = character.ClassId;

        await session.SendAsync(MsgId.SelectCharResp, frame.Seq, new SelectCharResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Character = ToSummary(character),
            Message = "选角成功"
        }, ct);
    }

    public async Task OnSessionDisconnectedAsync(IClientSession session, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(session.AccountId))
            await _storage.Sessions.SetOnlineAsync(session.AccountId, false, ct);
    }
}
