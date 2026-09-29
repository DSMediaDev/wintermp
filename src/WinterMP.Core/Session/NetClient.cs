using System;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Net;
using WinterMP.Core.Protocol;
using WinterMP.Core.Wire;

namespace WinterMP.Core.Session;

/// <summary>
/// The client side of a session: join, keepalive and leave. Poll-driven like the server, so the
/// in-game plugin calls <see cref="Update"/> once per frame with no threads of its own.
/// </summary>
public sealed class NetClient
{
    public const int HelloIntervalMs = 500;
    public const int ConnectTimeoutMs = 10000;
    public const int PingIntervalMs = 1000;
    public const int ServerTimeoutMs = 10000;

    private readonly IDatagramTransport _transport;
    private readonly ILog _log;
    private readonly byte[] _receiveBuffer = new byte[2048];
    private readonly PacketWriter _writer = new PacketWriter(ProtocolInfo.MaxDatagramSize);
    private ClientIdentity _identity = new ClientIdentity();
    private ulong _nonce;
    private long _now;
    private long _connectStartedMs;
    private long _nextHelloMs;
    private long _nextPingMs;
    private long _lastHeardMs;
    private uint _pingSequence;

    public NetClient(IDatagramTransport transport, ILog? log = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _log = log ?? NullLog.Instance;
    }

    public ClientState State { get; private set; } = ClientState.Idle;

    /// <summary>One human-readable line describing the current state, ready for a status label.</summary>
    public string StatusText { get; private set; } = "Not connected.";

    public NetEndPoint? Server { get; private set; }
    public ushort PlayerId { get; private set; }
    public string ServerName { get; private set; } = string.Empty;
    public string SessionGameBuild { get; private set; } = string.Empty;
    public int MaxPlayers { get; private set; }
    public int HelloAttempts { get; private set; }

    /// <summary>The server's refusal, when <see cref="State"/> is <see cref="ClientState.Rejected"/>.</summary>
    public RejectMessage? Rejection { get; private set; }

    /// <summary>Latest round trip to the server, or null before the first pong.</summary>
    public int? RttMs { get; private set; }

    public event Action<ClientState>? StateChanged;

    public void Connect(NetEndPoint server, ClientIdentity identity, long nowMs)
    {
        if (State == ClientState.Connecting || State == ClientState.Connected)
        {
            throw new InvalidOperationException("Already " + State + "; disconnect first.");
        }

        Server = server ?? throw new ArgumentNullException(nameof(server));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _nonce = NewNonce();
        _now = nowMs;
        _connectStartedMs = nowMs;
        HelloAttempts = 0;
        Rejection = null;
        RttMs = null;
        SetState(ClientState.Connecting, "Connecting to " + server + "...");
        SendHello();
    }

    /// <summary>Leaves politely. Safe to call in any state.</summary>
    public void Disconnect()
    {
        if (Server != null && (State == ClientState.Connecting || State == ClientState.Connected))
        {
            _writer.Reset();
            new GoodbyeMessage { Reason = GoodbyeReason.Leaving }.Write(_writer);
            Flush();
            SetState(ClientState.Disconnected, "You left the session.");
        }
    }

    public void Update(long nowMs)
    {
        _now = nowMs;
        int count;
        NetEndPoint? from;
        while (_transport.TryReceive(_receiveBuffer, out count, out from))
        {
            // Only the server we dialled gets a say; anything else on this socket is noise.
            if (Server != null && from.Equals(Server)) Handle(count);
        }

        if (State == ClientState.Connecting)
        {
            if (_now - _connectStartedMs >= ConnectTimeoutMs)
            {
                SetState(ClientState.Failed,
                    "No answer from " + Server + ". Check the address, that the server is running, and that its port is open.");
            }
            else if (_now >= _nextHelloMs)
            {
                SendHello();
            }
        }
        else if (State == ClientState.Connected)
        {
            if (_now - _lastHeardMs > ServerTimeoutMs)
            {
                SetState(ClientState.Disconnected, "Lost connection to the server (timed out).");
            }
            else if (_now >= _nextPingMs)
            {
                SendPing();
            }
        }
    }

    private void Handle(int count)
    {
        MessageKind kind;
        if (!Frame.TryReadHeader(_receiveBuffer, count, out kind)) return;
        var reader = Frame.BodyReader(_receiveBuffer, count);
        try
        {
            switch (kind)
            {
                case MessageKind.Welcome when State == ClientState.Connecting:
                    var welcome = WelcomeMessage.Read(reader);
                    PlayerId = welcome.PlayerId;
                    ServerName = welcome.ServerName;
                    SessionGameBuild = welcome.SessionGameBuild;
                    MaxPlayers = welcome.MaxPlayers;
                    _lastHeardMs = _now;
                    _nextPingMs = _now;
                    SetState(ClientState.Connected, "Connected to " + ServerName + " as player " + PlayerId + ".");
                    break;

                case MessageKind.Reject when State == ClientState.Connecting:
                    Rejection = RejectMessage.Read(reader);
                    SetState(ClientState.Rejected, Rejection.ToString());
                    break;

                case MessageKind.Pong when State == ClientState.Connected:
                    var pong = PingMessage.Read(reader);
                    _lastHeardMs = _now;
                    if (pong.Sequence == _pingSequence) RttMs = (int)unchecked((uint)_now - pong.SentAtMs);
                    break;

                case MessageKind.Goodbye when State == ClientState.Connecting || State == ClientState.Connected:
                    var goodbye = GoodbyeMessage.Read(reader);
                    SetState(ClientState.Disconnected, Describe(goodbye.Reason));
                    break;
            }
        }
        catch (WireFormatException e)
        {
            _log.Warn("Dropped a malformed " + kind + " from the server: " + e.Message);
        }
    }

    private void SendHello()
    {
        HelloAttempts++;
        _nextHelloMs = _now + HelloIntervalMs;
        _writer.Reset();
        new HelloMessage
        {
            ModVersion = WireText.Fit(_identity.ModVersion, HelloMessage.MaxModVersionBytes),
            ClientNonce = _nonce,
            GameBuild = WireText.Fit(_identity.GameBuild, HelloMessage.MaxGameBuildBytes),
            PlayerName = PlayerNames.Sanitize(_identity.PlayerName),
        }.Write(_writer);
        Flush();
    }

    private void SendPing()
    {
        _nextPingMs = _now + PingIntervalMs;
        _writer.Reset();
        new PingMessage { Sequence = ++_pingSequence, SentAtMs = unchecked((uint)_now) }.Write(_writer, MessageKind.Ping);
        Flush();
    }

    private void Flush()
    {
        if (Server != null) _transport.Send(Server, _writer.Buffer, 0, _writer.Length);
    }

    private void SetState(ClientState state, string status)
    {
        State = state;
        StatusText = status;
        _log.Info(status);
        StateChanged?.Invoke(state);
    }

    private static string Describe(GoodbyeReason reason)
    {
        switch (reason)
        {
            case GoodbyeReason.ServerShutdown: return "The server shut down.";
            case GoodbyeReason.Kicked: return "You were removed from the session.";
            case GoodbyeReason.Leaving: return "The server closed the connection.";
            default: return "The server no longer knows this connection (it may have restarted).";
        }
    }

    // Guid bytes are random enough to tell connection attempts apart, and unlike Random they do
    // not collide when 2 clients start in the same millisecond.
    private static ulong NewNonce() => BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
}
