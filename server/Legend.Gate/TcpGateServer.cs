using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Google.Protobuf;
using Legend.Contracts;
using Legend.Core;

namespace Legend.Gate;

public sealed class ClientSession : IClientSession
{
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public ClientSession(string connectionId, TcpClient tcpClient)
    {
        ConnectionId = connectionId;
        _tcpClient = tcpClient;
        _stream = tcpClient.GetStream();
    }

    public string ConnectionId { get; }
    public bool IsHandshaked { get; set; }
    public string? AccountId { get; set; }
    public string? SessionToken { get; set; }
    public string? EntityId { get; set; }
    public string? CharacterId { get; set; }
    public string? CharacterName { get; set; }
    public string? ClassId { get; set; }

    public async Task SendAsync(MsgId msgId, uint seq, IMessage message, CancellationToken ct = default)
    {
        if (!_tcpClient.Connected)
            return;

        await _writeLock.WaitAsync(ct);
        try
        {
            await PacketCodec.WriteFrameAsync(_stream, msgId, seq, message, ct);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            // Ignore write errors on disconnecting sockets
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void RequestClose()
    {
        try { _tcpClient.Close(); } catch { /* ignore */ }
    }
}

public sealed class TcpGateServer : IAsyncDisposable
{
    private readonly IPAddress _bindAddress;
    private readonly int _requestedPort;
    private readonly IReadOnlyList<IGameModule> _modules;
    private readonly ConcurrentDictionary<string, ClientSession> _sessions = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    public TcpGateServer(IPAddress bindAddress, int port, IEnumerable<IGameModule> modules)
    {
        _bindAddress = bindAddress;
        _requestedPort = port;
        _modules = modules.ToList();
    }

    public int BoundPort { get; private set; }
    public int ActiveSessionCount => _sessions.Count;

    public async Task StartAsync(CancellationToken ct = default)
    {
        foreach (var module in _modules)
        {
            await module.InitializeAsync(ct);
        }

        _listener = new TcpListener(_bindAddress, _requestedPort);
        _listener.Start();
        BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);

        StructuredLogger.Info("TcpGateServer", $"Gate server listening on {_bindAddress}:{BoundPort}");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            try
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                string connId = $"conn-{Guid.NewGuid():N}"[..13];
                var session = new ClientSession(connId, client);
                _sessions[connId] = session;

                StructuredLogger.Info("TcpGateServer", "Client connected", connId: connId);
                _ = Task.Run(() => HandleClientLoopAsync(session, client, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested)
                {
                    StructuredLogger.Error("TcpGateServer", $"Accept error: {ex.Message}");
                }
            }
        }
    }

    private async Task HandleClientLoopAsync(ClientSession session, TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            var stream = client.GetStream();
            try
            {
                while (!ct.IsCancellationRequested && client.Connected)
                {
                    var frame = await PacketCodec.ReadFrameAsync(stream, ct);
                    if (frame == null)
                        break;

                    var f = frame.Value;
                    var handler = _modules.FirstOrDefault(m => m.CanHandle(f.MsgId));
                    if (handler == null)
                    {
                        StructuredLogger.Warn("TcpGateServer", $"Unhandled MsgId: {f.MsgId}", connId: session.ConnectionId);
                        continue;
                    }

                    await handler.HandlePacketAsync(session, f, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
                // Client disconnected abruptly
            }
            catch (Exception ex)
            {
                StructuredLogger.Error("TcpGateServer", $"Session error: {ex.Message}", connId: session.ConnectionId, playerId: session.EntityId);
            }
            finally
            {
                _sessions.TryRemove(session.ConnectionId, out _);
                foreach (var module in _modules)
                {
                    try
                    {
                        await module.OnSessionDisconnectedAsync(session, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        StructuredLogger.Error("TcpGateServer", $"Disconnect cleanup error in {module.ModuleName}: {ex.Message}", connId: session.ConnectionId);
                    }
                }
                StructuredLogger.Info("TcpGateServer", "Client disconnected", connId: session.ConnectionId, playerId: session.EntityId);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        var cts = Interlocked.Exchange(ref _cts, null);
        if (cts is null)
            return;
        try
        {
            await cts.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down
        }
        try { _listener?.Stop(); } catch { /* ignore */ }
        if (_acceptLoopTask != null)
        {
            try { await _acceptLoopTask; }
            catch { /* ignore cancellation */ }
        }
        cts.Dispose();
    }
}
