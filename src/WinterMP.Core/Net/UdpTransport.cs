using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace WinterMP.Core.Net;

/// <summary>
/// Raw UDP over System.Net sockets: the dedicated server's transport and the client's way to reach
/// one. Non-blocking and poll-driven, so it runs on the game's main thread with no extra threads.
/// Written against the .NET 3.5 socket API so the same code runs in-game and on the server.
/// </summary>
public sealed class UdpTransport : IDatagramTransport
{
    // Windows reports an ICMP "port unreachable" as a ConnectionReset on the NEXT receive, which
    // would wedge a UDP server the moment one client vanishes. This ioctl turns that off.
    private const int SioUdpConnReset = -1744830452;
    private const int MaxCachedEndPoints = 4096;

    private readonly Socket _socket;
    private readonly Dictionary<IPEndPoint, NetEndPoint> _endPoints = new Dictionary<IPEndPoint, NetEndPoint>();
    private readonly int _localPort;
    private EndPoint _receiveFrom = new IPEndPoint(IPAddress.Any, 0);
    private bool _disposed;

    public UdpTransport(IPEndPoint bindTo)
    {
        if (bindTo == null) throw new ArgumentNullException(nameof(bindTo));
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            _socket.Blocking = false;
            TryDisableConnectionReset(_socket);
            TrySetBufferSizes(_socket);
            _socket.Bind(bindTo);
            var bound = _socket.LocalEndPoint as IPEndPoint ?? throw new InvalidOperationException("The socket did not report its bound address.");
            _localPort = bound.Port;
            LocalEndPoint = EndPointFor(bound);
        }
        catch
        {
            _socket.Close();
            throw;
        }
    }

    /// <summary>Binds all IPv4 interfaces on <paramref name="port"/> (0 = any free port, for clients).</summary>
    public static UdpTransport Bind(int port) => new UdpTransport(new IPEndPoint(IPAddress.Any, port));

    public NetEndPoint? LocalEndPoint { get; }

    /// <summary>The port actually bound (useful after binding port 0).</summary>
    public int LocalPort => _localPort;

    public static NetEndPoint EndPointFor(IPEndPoint address) => new NetEndPoint("udp:" + address, address);

    /// <summary>
    /// Parses "host", "host:port", "1.2.3.4" or "1.2.3.4:port". Host names resolve via DNS (blocking,
    /// so call it when the player presses Connect, not every frame). IPv4 only for now.
    /// </summary>
    public static bool TryResolve(string text, int defaultPort, [NotNullWhen(true)] out NetEndPoint? endPoint, out string error)
    {
        endPoint = null;
        error = string.Empty;
        if (text == null || text.Trim().Length == 0)
        {
            error = "No address given.";
            return false;
        }

        var host = text.Trim();
        var port = defaultPort;
        var colon = host.LastIndexOf(':');
        if (colon >= 0)
        {
            var portText = host.Substring(colon + 1);
            host = host.Substring(0, colon);
            if (!int.TryParse(portText, out port) || port < 1 || port > 65535)
            {
                error = "Port must be a number from 1 to 65535.";
                return false;
            }
        }

        IPAddress? address;
        if (!IPAddress.TryParse(host, out address))
        {
            try
            {
                address = null;
                foreach (var candidate in Dns.GetHostAddresses(host))
                {
                    if (candidate.AddressFamily == AddressFamily.InterNetwork)
                    {
                        address = candidate;
                        break;
                    }
                }
            }
            catch (SocketException e)
            {
                error = "Could not resolve " + host + ": " + e.Message;
                return false;
            }
        }

        if (address == null || address.AddressFamily != AddressFamily.InterNetwork)
        {
            error = "No IPv4 address found for " + host + ".";
            return false;
        }

        endPoint = EndPointFor(new IPEndPoint(address, port));
        return true;
    }

    public void Send(NetEndPoint to, byte[] buffer, int offset, int count)
    {
        if (_disposed) return;
        if (!(to.Native is IPEndPoint address)) throw new ArgumentException("Not a UDP endpoint: " + to, nameof(to));
        try
        {
            _socket.SendTo(buffer, offset, count, SocketFlags.None, address);
        }
        catch (SocketException)
        {
            // Lossy by contract: a full send buffer or an unreachable host just loses this datagram.
        }
    }

    public bool TryReceive(byte[] buffer, out int count, [NotNullWhen(true)] out NetEndPoint? from)
    {
        count = 0;
        from = null;
        while (!_disposed)
        {
            try
            {
                if (_socket.Available == 0) return false;
                count = _socket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref _receiveFrom);
            }
            catch (SocketException e)
            {
                // No exception filters on purpose: old Mono runtimes are shaky with them.
                var code = e.SocketErrorCode;
                if (code == SocketError.WouldBlock) return false;
                if (code == SocketError.ConnectionReset || code == SocketError.MessageSize) continue;
                throw;
            }

            from = Cached((IPEndPoint)_receiveFrom);
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _socket.Close();
    }

    private NetEndPoint Cached(IPEndPoint address)
    {
        NetEndPoint? known;
        if (_endPoints.TryGetValue(address, out known)) return known;
        // Spoofed source addresses could grow this without bound; a flush is cheaper than a policy.
        if (_endPoints.Count >= MaxCachedEndPoints) _endPoints.Clear();
        var copy = new IPEndPoint(address.Address, address.Port);
        known = EndPointFor(copy);
        _endPoints[copy] = known;
        return known;
    }

    private static void TryDisableConnectionReset(Socket socket)
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
        try
        {
            socket.IOControl(SioUdpConnReset, new byte[4], null);
        }
        catch (Exception)
        {
            // Unsupported on this runtime; TryReceive also skips ConnectionReset, so it is a nicety.
        }
    }

    private static void TrySetBufferSizes(Socket socket)
    {
        try
        {
            socket.ReceiveBufferSize = 1 << 20;
            socket.SendBufferSize = 1 << 20;
        }
        catch (SocketException)
        {
            // The OS default still works; bigger buffers only smooth out bursts.
        }
    }
}
