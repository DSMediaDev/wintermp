using WinterMP.Core.Net;
using WinterMP.Core.Protocol;
using WinterMP.Core.Session;
using WinterMP.Core.Wire;

namespace WinterMP.Core.Tests;

/// <summary>
/// A server and any number of clients on one in-memory network, with time stepped by hand, so
/// every handshake test is deterministic and runs in microseconds.
/// </summary>
internal sealed class SessionRig
{
    public const string ServerNode = "server";
    public const string ModVersion = "0.0.1";

    public SessionRig(ServerOptions? options = null)
    {
        Options = options ?? new ServerOptions { ServerName = "Test Server", ModVersion = ModVersion };
        Server = new NetServer(Network.CreateNode(ServerNode), Options);
        Server.PeerJoined += p => Events.Add("joined:" + p.Name);
        Server.PeerLeft += (p, reason) => Events.Add("left:" + p.Name + ":" + reason);
    }

    public LoopbackNetwork Network { get; } = new LoopbackNetwork();
    public ServerOptions Options { get; }
    public NetServer Server { get; }
    public List<NetClient> Clients { get; } = new List<NetClient>();
    public List<string> Events { get; } = new List<string>();
    public long Now { get; private set; } = 1000;

    public NetClient AddClient(string node)
    {
        var client = new NetClient(Network.CreateNode(node));
        Clients.Add(client);
        return client;
    }

    public void Connect(NetClient client, string player = "Tester", string build = "build-a") =>
        client.Connect(LoopbackNetwork.AddressOf(ServerNode),
            new ClientIdentity { ModVersion = ModVersion, GameBuild = build, PlayerName = player }, Now);

    public void Step(int ms = 50, int times = 1)
    {
        for (var i = 0; i < times; i++)
        {
            Now += ms;
            Server.Update(Now);
            foreach (var client in Clients) client.Update(Now);
        }
    }

    /// <summary>Steps until the condition holds; fails the test if it never does.</summary>
    public void RunUntil(Func<bool> condition, int maxMs = 30000, int stepMs = 50)
    {
        var deadline = Now + maxMs;
        while (!condition())
        {
            if (Now >= deadline) throw new Xunit.Sdk.XunitException("Condition not met within " + maxMs + " ms of simulated time.");
            Step(stepMs);
        }
    }

    /// <summary>Sends raw bytes to the server from a bare node and returns the server's replies.</summary>
    public List<byte[]> SendRaw(LoopbackTransport from, byte[] datagram)
    {
        from.Send(LoopbackNetwork.AddressOf(ServerNode), datagram, 0, datagram.Length);
        Step();
        var replies = new List<byte[]>();
        var buffer = new byte[4096];
        while (from.TryReceive(buffer, out var count, out _)) replies.Add(buffer.Take(count).ToArray());
        return replies;
    }

    public static RejectMessage DecodeReject(byte[] datagram)
    {
        Assert.True(Frame.TryReadHeader(datagram, datagram.Length, out var kind));
        Assert.Equal(MessageKind.Reject, kind);
        return RejectMessage.Read(Frame.BodyReader(datagram, datagram.Length));
    }

    public static byte[] Encode(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return writer.ToArray();
    }
}
