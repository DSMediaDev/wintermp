using WinterMP.Core.Net;
using WinterMP.Core.Protocol;
using WinterMP.Core.Session;

namespace WinterMP.Core.Tests;

public class HandshakeTests
{
    [Fact]
    public void Client_joins_and_both_sides_agree_on_the_session()
    {
        var rig = new SessionRig();
        var client = rig.AddClient("a");
        rig.Connect(client, player: "Cody");
        rig.RunUntil(() => client.State == ClientState.Connected);

        Assert.Equal(1, client.PlayerId);
        Assert.Equal("Test Server", client.ServerName);
        Assert.Equal("build-a", client.SessionGameBuild);
        var peer = Assert.Single(rig.Server.Peers);
        Assert.Equal("Cody", peer.Name);
        Assert.Equal(new[] { "joined:Cody" }, rig.Events);
    }

    [Fact]
    public void First_player_sets_the_session_build_and_everyone_else_must_match()
    {
        var rig = new SessionRig();
        var first = rig.AddClient("a");
        var mismatched = rig.AddClient("b");
        var matching = rig.AddClient("c");

        rig.Connect(first, build: "v.260516-01");
        rig.RunUntil(() => first.State == ClientState.Connected);
        Assert.Equal("v.260516-01", rig.Server.SessionGameBuild);

        rig.Connect(mismatched, build: "v.260601-02");
        rig.Connect(matching, build: "v.260516-01");
        rig.RunUntil(() => mismatched.State == ClientState.Rejected && matching.State == ClientState.Connected);

        Assert.Equal(RejectKind.GameBuildMismatch, mismatched.Rejection!.Kind);
        Assert.Equal("v.260601-02", mismatched.Rejection.Have);
        Assert.Equal("v.260516-01", mismatched.Rejection.Need);
        Assert.Equal(2, rig.Server.Peers.Count);
    }

    [Fact]
    public void A_configured_build_is_enforced_from_the_very_first_join()
    {
        var rig = new SessionRig(new ServerOptions { RequiredGameBuild = "v.1", ModVersion = SessionRig.ModVersion });
        var client = rig.AddClient("a");
        rig.Connect(client, build: "v.2");
        rig.RunUntil(() => client.State == ClientState.Rejected);
        Assert.Equal(RejectKind.GameBuildMismatch, client.Rejection!.Kind);
        Assert.Empty(rig.Server.Peers);
    }

    [Fact]
    public void A_full_server_says_so_instead_of_going_quiet()
    {
        var rig = new SessionRig(new ServerOptions { MaxPlayers = 1, ModVersion = SessionRig.ModVersion });
        var first = rig.AddClient("a");
        var second = rig.AddClient("b");
        rig.Connect(first);
        rig.RunUntil(() => first.State == ClientState.Connected);
        rig.Connect(second);
        rig.RunUntil(() => second.State == ClientState.Rejected);
        Assert.Equal(RejectKind.ServerFull, second.Rejection!.Kind);
    }

    [Fact]
    public void A_lost_hello_is_retried_until_it_lands()
    {
        var rig = new SessionRig();
        var dropped = 0;
        rig.Network.Drop = (from, to, bytes) => bytes[2] == (byte)MessageKind.Hello && dropped++ < 3;
        var client = rig.AddClient("a");
        rig.Connect(client);
        rig.RunUntil(() => client.State == ClientState.Connected);
        Assert.Equal(4, client.HelloAttempts);
    }

    [Fact]
    public void A_retry_after_a_lost_welcome_gets_the_same_admission_not_a_second_one()
    {
        var rig = new SessionRig();
        var dropped = false;
        rig.Network.Drop = (from, to, bytes) =>
        {
            if (bytes[2] != (byte)MessageKind.Welcome || dropped) return false;
            dropped = true;
            return true;
        };
        var client = rig.AddClient("a");
        rig.Connect(client);
        rig.RunUntil(() => client.State == ClientState.Connected);

        Assert.True(dropped);
        var peer = Assert.Single(rig.Server.Peers);
        Assert.Equal(client.PlayerId, peer.Id);
        Assert.Equal(new[] { "joined:Tester" }, rig.Events);
    }

    [Fact]
    public void An_unreachable_server_fails_with_a_useful_message()
    {
        var rig = new SessionRig();
        var client = rig.AddClient("a");
        client.Connect(LoopbackNetwork.AddressOf("nobody-home"), new ClientIdentity { PlayerName = "x" }, rig.Now);
        rig.RunUntil(() => client.State == ClientState.Failed);
        Assert.Contains("No answer", client.StatusText);
    }

    [Fact]
    public void A_silent_client_is_dropped_by_the_server()
    {
        var rig = new SessionRig(new ServerOptions { PeerTimeoutMs = 5000, ModVersion = SessionRig.ModVersion });
        var client = rig.AddClient("a");
        rig.Connect(client);
        rig.RunUntil(() => client.State == ClientState.Connected);

        rig.Clients.Clear(); // the client stops running (a frozen game, a pulled cable)
        rig.RunUntil(() => rig.Server.Peers.Count == 0);
        Assert.Contains("left:Tester:timed out", rig.Events);
    }

    [Fact]
    public void Leaving_is_seen_at_once_not_after_a_timeout()
    {
        var rig = new SessionRig();
        var client = rig.AddClient("a");
        rig.Connect(client);
        rig.RunUntil(() => client.State == ClientState.Connected);

        client.Disconnect();
        rig.Step();
        Assert.Empty(rig.Server.Peers);
        Assert.Contains("left:Tester:left", rig.Events);
        Assert.Equal(ClientState.Disconnected, client.State);
    }

    [Fact]
    public void Server_shutdown_tells_every_client_why()
    {
        var rig = new SessionRig();
        var a = rig.AddClient("a");
        var b = rig.AddClient("b");
        rig.Connect(a);
        rig.Connect(b);
        rig.RunUntil(() => a.State == ClientState.Connected && b.State == ClientState.Connected);

        rig.Server.Shutdown();
        rig.Step();
        Assert.All(new[] { a, b }, c => Assert.Equal(ClientState.Disconnected, c.State));
        Assert.Contains("shut down", a.StatusText);
    }

    [Fact]
    public void A_kicked_client_is_told()
    {
        var rig = new SessionRig();
        var client = rig.AddClient("a");
        rig.Connect(client);
        rig.RunUntil(() => client.State == ClientState.Connected);

        Assert.True(rig.Server.Kick(client.PlayerId));
        rig.Step();
        Assert.Equal(ClientState.Disconnected, client.State);
        Assert.Contains("removed", client.StatusText);
    }

    [Fact]
    public void Pings_keep_the_link_alive_and_measure_the_round_trip()
    {
        var rig = new SessionRig(new ServerOptions { PeerTimeoutMs = 3000, ModVersion = SessionRig.ModVersion });
        var client = rig.AddClient("a");
        rig.Connect(client);
        rig.RunUntil(() => client.State == ClientState.Connected);

        rig.Step(50, 200); // 10 s: far past both timeouts, so only pings keep it up
        Assert.Equal(ClientState.Connected, client.State);
        Assert.Single(rig.Server.Peers);
        Assert.Equal(50, client.RttMs); // 1 simulated step there and back
    }

    [Fact]
    public void After_a_server_restart_a_client_learns_it_is_forgotten_quickly()
    {
        var rig = new SessionRig();
        var client = rig.AddClient("a");
        rig.Connect(client);
        rig.RunUntil(() => client.State == ClientState.Connected);

        // Replace the server with a fresh one on the same address, as a restart would.
        var restarted = new NetServer(RecreateServerNode(rig), new ServerOptions());
        var start = rig.Now;
        while (client.State == ClientState.Connected && rig.Now - start < 5000)
        {
            rig.Step();
            restarted.Update(rig.Now);
        }

        Assert.Equal(ClientState.Disconnected, client.State);
        Assert.True(rig.Now - start < NetClient.ServerTimeoutMs, "should not need the full timeout");
        Assert.Contains("restarted", client.StatusText);
    }

    [Fact]
    public void Rejoining_from_the_same_address_replaces_the_stale_player()
    {
        var rig = new SessionRig();
        var client = rig.AddClient("a");
        rig.Connect(client);
        rig.RunUntil(() => client.State == ClientState.Connected);

        // The game crashed and came back on the same address: new client, new nonce.
        rig.Clients.Remove(client);
        var node = rig.Network.CreateNodeReplacing("a");
        var again = new NetClient(node);
        rig.Clients.Add(again);
        rig.Connect(again);
        rig.RunUntil(() => again.State == ClientState.Connected);

        Assert.Single(rig.Server.Peers);
        Assert.Equal(new[] { "joined:Tester", "left:Tester:reconnected", "joined:Tester" }, rig.Events);
    }

    [Fact]
    public void Player_ids_are_reused_lowest_first()
    {
        var rig = new SessionRig();
        var a = rig.AddClient("a");
        var b = rig.AddClient("b");
        rig.Connect(a);
        rig.RunUntil(() => a.State == ClientState.Connected);
        rig.Connect(b);
        rig.RunUntil(() => b.State == ClientState.Connected);
        Assert.Equal(2, b.PlayerId);

        a.Disconnect();
        var c = rig.AddClient("c");
        rig.Connect(c);
        rig.RunUntil(() => c.State == ClientState.Connected);
        Assert.Equal(1, c.PlayerId);
    }

    private static LoopbackTransport RecreateServerNode(SessionRig rig) =>
        rig.Network.CreateNodeReplacing(SessionRig.ServerNode);
}
