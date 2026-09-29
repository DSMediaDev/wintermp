using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using WinterMP.Core.Net;
using WinterMP.Core.Session;

namespace WinterMP.Core.Tests;

/// <summary>The same handshake as the loopback tests, over real sockets on this machine.</summary>
public class UdpTransportTests
{
    private static UdpTransport Local() => new UdpTransport(new IPEndPoint(IPAddress.Loopback, 0));

    private static void Pump(Func<bool> done, params Action<long>[] updates)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            if (clock.ElapsedMilliseconds > 5000) throw new Xunit.Sdk.XunitException("Timed out after 5 s of real time.");
            foreach (var update in updates) update(clock.ElapsedMilliseconds);
            Thread.Sleep(2);
        }
    }

    [Fact]
    public void A_client_joins_a_server_over_real_udp()
    {
        using var serverSocket = Local();
        using var clientSocket = Local();
        var server = new NetServer(serverSocket, new ServerOptions { ServerName = "UDP Test" });
        var client = new NetClient(clientSocket);
        var serverAddress = UdpTransport.EndPointFor(new IPEndPoint(IPAddress.Loopback, serverSocket.LocalPort));

        client.Connect(serverAddress, new ClientIdentity { PlayerName = "Udp", GameBuild = "b", ModVersion = "0.0.1" }, 0);
        Pump(() => client.State == ClientState.Connected, server.Update, client.Update);

        Assert.Equal("UDP Test", client.ServerName);
        Assert.Equal("Udp", Assert.Single(server.Peers).Name);
    }

    [Fact]
    public void An_unreachable_peer_does_not_wedge_the_socket()
    {
        // Windows turns the ICMP reply from a closed port into an error on the NEXT receive.
        // The transport must shrug that off and keep serving everyone else.
        int closedPort;
        using (var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            probe.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            closedPort = ((IPEndPoint)probe.LocalEndPoint!).Port;
        }

        using var serverSocket = Local();
        using var clientSocket = Local();
        var dead = UdpTransport.EndPointFor(new IPEndPoint(IPAddress.Loopback, closedPort));
        for (var i = 0; i < 5; i++) serverSocket.Send(dead, new byte[] { 1, 2, 3 }, 0, 3);

        var server = new NetServer(serverSocket, new ServerOptions());
        var client = new NetClient(clientSocket);
        client.Connect(UdpTransport.EndPointFor(new IPEndPoint(IPAddress.Loopback, serverSocket.LocalPort)),
            new ClientIdentity { PlayerName = "Survivor" }, 0);
        Pump(() => client.State == ClientState.Connected, server.Update, client.Update);
    }

    [Theory]
    [InlineData("127.0.0.1:1234", "udp:127.0.0.1:1234")]
    [InlineData("127.0.0.1", "udp:127.0.0.1:27777")]
    [InlineData("  10.0.0.5:9  ", "udp:10.0.0.5:9")]
    public void Addresses_parse_with_and_without_a_port(string text, string expected)
    {
        Assert.True(UdpTransport.TryResolve(text, 27777, out var endPoint, out var error), error);
        Assert.Equal(expected, endPoint!.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("127.0.0.1:notaport")]
    [InlineData("127.0.0.1:70000")]
    public void Bad_addresses_explain_themselves(string text)
    {
        Assert.False(UdpTransport.TryResolve(text, 27777, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Localhost_resolves_to_ipv4()
    {
        Assert.True(UdpTransport.TryResolve("localhost:5000", 27777, out var endPoint, out var error), error);
        Assert.Equal("udp:127.0.0.1:5000", endPoint!.Key);
    }
}
