using System;
using System.Collections.Generic;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Net;
using WinterMP.Core.Protocol;
using WinterMP.Core.Wire;

namespace WinterMP.Core.Session;

/// <summary>
/// The server side of a session: admission, identity and liveness. It runs identically inside the
/// dedicated server process and embedded in a host's game. Single-threaded and poll-driven: call
/// <see cref="Update"/> regularly with a monotonic time.
/// </summary>
public sealed class NetServer
{
    private readonly IDatagramTransport _transport;
    private readonly ServerOptions _options;
    private readonly ILog _log;
    private readonly byte[] _receiveBuffer = new byte[2048];
    private readonly PacketWriter _writer = new PacketWriter(ProtocolInfo.MaxDatagramSize);
    private readonly Dictionary<NetEndPoint, PeerInfo> _peersByEndPoint = new Dictionary<NetEndPoint, PeerInfo>();
    private readonly List<PeerInfo> _peers = new List<PeerInfo>();
    private long _now;

    public NetServer(IDatagramTransport transport, ServerOptions options, ILog? log = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _log = log ?? NullLog.Instance;
        MaxPlayers = Math.Max(1, Math.Min(ServerOptions.HardPlayerCap, options.MaxPlayers));
        var required = options.RequiredGameBuild == null ? string.Empty : options.RequiredGameBuild.Trim();
        SessionGameBuild = required.Length == 0 ? null : required;
    }

    public int MaxPlayers { get; }

    /// <summary>The game build this session runs; null until the first player sets it (if not configured).</summary>
    public string? SessionGameBuild { get; private set; }

    /// <summary>Admitted players, in join order.</summary>
    public IList<PeerInfo> Peers => _peers.AsReadOnly();

    public event Action<PeerInfo>? PeerJoined;

    /// <summary>Raised with the player and a short reason ("left", "timed out", "kicked"...).</summary>
    public event Action<PeerInfo, string>? PeerLeft;

    /// <summary>Drains the transport and expires silent peers.</summary>
    public void Update(long nowMs)
    {
        _now = nowMs;
        int count;
        NetEndPoint? from;
        while (_transport.TryReceive(_receiveBuffer, out count, out from))
        {
            Handle(from, count);
        }

        for (var i = _peers.Count - 1; i >= 0; i--)
        {
            var peer = _peers[i];
            if (_now - peer.LastHeardMs > _options.PeerTimeoutMs) RemovePeer(peer, "timed out");
        }
    }

    /// <summary>Tells a player to leave and forgets them.</summary>
    public bool Kick(ushort playerId)
    {
        foreach (var peer in _peers)
        {
            if (peer.Id != playerId) continue;
            SendGoodbye(peer.EndPoint, GoodbyeReason.Kicked);
            RemovePeer(peer, "kicked");
            return true;
        }

        return false;
    }

    /// <summary>Says goodbye to everyone so clients react at once instead of timing out.</summary>
    public void Shutdown()
    {
        for (var i = _peers.Count - 1; i >= 0; i--)
        {
            var peer = _peers[i];
            SendGoodbye(peer.EndPoint, GoodbyeReason.ServerShutdown);
            RemovePeer(peer, "server shutdown");
        }
    }

    private void Handle(NetEndPoint from, int count)
    {
        MessageKind kind;
        if (!Frame.TryReadHeader(_receiveBuffer, count, out kind)) return;
        var reader = Frame.BodyReader(_receiveBuffer, count);
        try
        {
            switch (kind)
            {
                case MessageKind.Hello:
                    HandleHello(from, reader);
                    break;
                case MessageKind.Ping:
                    HandlePing(from, reader);
                    break;
                case MessageKind.Goodbye:
                    PeerInfo? leaving;
                    if (_peersByEndPoint.TryGetValue(from, out leaving)) RemovePeer(leaving, "left");
                    break;
            }
        }
        catch (WireFormatException e)
        {
            _log.Warn("Dropped a malformed " + kind + " from " + from + ": " + e.Message);
        }
    }

    private void HandleHello(NetEndPoint from, PacketReader reader)
    {
        HelloMessage hello;
        try
        {
            hello = HelloMessage.ReadPrefix(reader);
        }
        catch (WireFormatException)
        {
            return; // Not even the frozen prefix parses: not worth a reply.
        }

        if (hello.ProtocolVersion != ProtocolInfo.Version)
        {
            var clientIsOlder = hello.ProtocolVersion < ProtocolInfo.Version;
            Reject(from, RejectKind.ProtocolMismatch,
                clientIsOlder
                    ? "Your WinterMP is older than this server's. Update it to join."
                    : "This server runs an older WinterMP than yours. Ask the host to update.",
                Describe(hello.ModVersion, hello.ProtocolVersion),
                Describe(_options.ModVersion, ProtocolInfo.Version));
            return;
        }

        try
        {
            hello.ReadBody(reader);
        }
        catch (WireFormatException e)
        {
            Reject(from, RejectKind.Malformed, "The join request was malformed: " + e.Message, string.Empty, string.Empty);
            return;
        }

        PeerInfo? existing;
        if (_peersByEndPoint.TryGetValue(from, out existing))
        {
            if (existing.Nonce == hello.ClientNonce)
            {
                // Our Welcome was lost and the client retried: same answer, no second admission.
                existing.LastHeardMs = _now;
                SendWelcome(existing);
                return;
            }

            // Same address, fresh attempt: the old session is dead (game restarted or crashed).
            RemovePeer(existing, "reconnected");
        }

        var build = hello.GameBuild.Trim();
        if (build.Length == 0) build = "unknown";
        if (SessionGameBuild != null && !string.Equals(build, SessionGameBuild, StringComparison.Ordinal))
        {
            Reject(from, RejectKind.GameBuildMismatch,
                "Your game version does not match this server's. Everyone needs the same My Winter Car build.",
                build, SessionGameBuild);
            return;
        }

        if (_peers.Count >= MaxPlayers)
        {
            Reject(from, RejectKind.ServerFull, "The server is full.", _peers.Count + " of " + MaxPlayers + " players", "a free slot");
            return;
        }

        if (SessionGameBuild == null)
        {
            SessionGameBuild = build;
            _log.Info("Session game build set by the first player: " + build);
        }

        var peer = new PeerInfo(NextFreeId(), from, hello.ClientNonce, PlayerNames.Sanitize(hello.PlayerName), hello.ModVersion, build, _now);
        _peers.Add(peer);
        _peersByEndPoint[from] = peer;
        SendWelcome(peer);
        _log.Info(peer.Name + " joined as player " + peer.Id + " from " + from + " (" + _peers.Count + "/" + MaxPlayers + ")");
        PeerJoined?.Invoke(peer);
    }

    private void HandlePing(NetEndPoint from, PacketReader reader)
    {
        var ping = PingMessage.Read(reader);
        PeerInfo? peer;
        if (!_peersByEndPoint.TryGetValue(from, out peer))
        {
            // A client that thinks it is connected but we do not know it (we restarted, or dropped it).
            // Tell it straight away rather than letting it time out.
            SendGoodbye(from, GoodbyeReason.Unknown);
            return;
        }

        peer.LastHeardMs = _now;
        _writer.Reset();
        ping.Write(_writer, MessageKind.Pong);
        Flush(from);
    }

    private void RemovePeer(PeerInfo peer, string reason)
    {
        _peers.Remove(peer);
        _peersByEndPoint.Remove(peer.EndPoint);
        _log.Info(peer.Name + " (player " + peer.Id + ") " + reason + " (" + _peers.Count + "/" + MaxPlayers + ")");
        PeerLeft?.Invoke(peer, reason);
    }

    private ushort NextFreeId()
    {
        for (ushort id = 1; id < ushort.MaxValue; id++)
        {
            var taken = false;
            foreach (var peer in _peers)
            {
                if (peer.Id == id)
                {
                    taken = true;
                    break;
                }
            }

            if (!taken) return id;
        }

        throw new InvalidOperationException("No free player ids.");
    }

    private void SendWelcome(PeerInfo peer)
    {
        _writer.Reset();
        new WelcomeMessage
        {
            PlayerId = peer.Id,
            ServerName = WireText.Fit(_options.ServerName, WelcomeMessage.MaxServerNameBytes),
            SessionGameBuild = SessionGameBuild ?? string.Empty,
            MaxPlayers = (byte)MaxPlayers,
        }.Write(_writer);
        Flush(peer.EndPoint);
    }

    private void Reject(NetEndPoint to, RejectKind kind, string message, string have, string need)
    {
        _log.Info("Rejected " + to + ": " + kind + " (have " + have + ", need " + need + ")");
        _writer.Reset();
        new RejectMessage
        {
            Kind = kind,
            Message = WireText.Fit(message, RejectMessage.MaxMessageBytes),
            Have = WireText.Fit(have, RejectMessage.MaxHaveNeedBytes),
            Need = WireText.Fit(need, RejectMessage.MaxHaveNeedBytes),
        }.Write(_writer);
        Flush(to);
    }

    private void SendGoodbye(NetEndPoint to, GoodbyeReason reason)
    {
        _writer.Reset();
        new GoodbyeMessage { Reason = reason }.Write(_writer);
        Flush(to);
    }

    private void Flush(NetEndPoint to) => _transport.Send(to, _writer.Buffer, 0, _writer.Length);

    private static string Describe(string modVersion, ushort protocol) =>
        (modVersion.Length > 0 ? "WinterMP " + modVersion + ", " : string.Empty) + "protocol " + protocol;
}
