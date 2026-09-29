using WinterMP.Core.Protocol;
using WinterMP.Core.Session;
using WinterMP.Core.Wire;

namespace WinterMP.Core.Tests;

/// <summary>
/// The promise these tests keep: any 2 builds, however far apart, can always get as far as a
/// readable "you have X, the server needs Y". Nobody ever gets a silent timeout for a version gap.
/// </summary>
public class VersioningTests
{
    private static byte[] HelloPrefix(ushort protocol, string modVersion, params byte[] rest) => SessionRig.Encode(w =>
    {
        Frame.WriteHeader(w, MessageKind.Hello);
        w.WriteUInt16(protocol);
        w.WriteString(modVersion, HelloMessage.MaxModVersionBytes);
        w.WriteBytes(rest, 0, rest.Length);
    });

    [Fact]
    public void A_client_from_the_future_gets_a_structured_reject()
    {
        var rig = new SessionRig();
        var raw = rig.Network.CreateNode("future");
        // A newer build's Hello: our frozen prefix, then a body layout we have never seen.
        var replies = rig.SendRaw(raw, HelloPrefix(999, "0.9.0", 0xFF, 0x00, 0x13, 0x37));

        var reject = SessionRig.DecodeReject(Assert.Single(replies));
        Assert.Equal(RejectKind.ProtocolMismatch, reject.Kind);
        Assert.Contains("0.9.0", reject.Have);
        Assert.Contains("protocol 999", reject.Have);
        Assert.Contains("protocol " + ProtocolInfo.Version, reject.Need);
        Assert.Contains("older WinterMP than yours", reject.Message);
        Assert.Empty(rig.Server.Peers);
    }

    [Fact]
    public void A_client_from_the_past_is_told_to_update()
    {
        var rig = new SessionRig();
        var replies = rig.SendRaw(rig.Network.CreateNode("past"), HelloPrefix(0, "0.0.0"));
        var reject = SessionRig.DecodeReject(Assert.Single(replies));
        Assert.Equal(RejectKind.ProtocolMismatch, reject.Kind);
        Assert.Contains("Update it", reject.Message);
    }

    [Fact]
    public void The_reject_layout_is_frozen_byte_for_byte()
    {
        var bytes = SessionRig.Encode(w => new RejectMessage
        {
            Kind = RejectKind.ServerFull, Message = "x", Have = "h", Need = "n",
        }.Write(w));
        // 'W' 'M' kind=3 | reason=3 | "x" | "h" | "n"  (u16 little-endian length prefixes)
        Assert.Equal(new byte[] { 0x57, 0x4D, 3, 3, 1, 0, 0x78, 1, 0, 0x68, 1, 0, 0x6E }, bytes);
    }

    [Fact]
    public void The_hello_prefix_is_frozen_byte_for_byte()
    {
        var bytes = SessionRig.Encode(w => new HelloMessage { ModVersion = "1.2", ClientNonce = 0, GameBuild = "", PlayerName = "" }.Write(w));
        // 'W' 'M' kind=1 | protocol (u16) | "1.2" ...then the version-specific body.
        var prefix = new byte[] { 0x57, 0x4D, 1, (byte)ProtocolInfo.Version, (byte)(ProtocolInfo.Version >> 8), 3, 0, 0x31, 0x2E, 0x32 };
        Assert.Equal(prefix, bytes.Take(prefix.Length).ToArray());
    }

    [Fact]
    public void A_reject_with_fields_appended_by_a_future_build_still_reads()
    {
        var bytes = SessionRig.Encode(w =>
        {
            new RejectMessage { Kind = RejectKind.Malformed, Message = "m", Have = "h", Need = "n" }.Write(w);
            w.WriteUInt32(12345); // a field this build does not know about
        });
        var reject = SessionRig.DecodeReject(bytes);
        Assert.Equal("m", reject.Message);
    }

    [Fact]
    public void A_same_version_hello_with_a_broken_body_is_rejected_as_malformed()
    {
        var rig = new SessionRig();
        var replies = rig.SendRaw(rig.Network.CreateNode("broken"), HelloPrefix(ProtocolInfo.Version, SessionRig.ModVersion, 1, 2, 3));
        Assert.Equal(RejectKind.Malformed, SessionRig.DecodeReject(Assert.Single(replies)).Kind);
    }

    [Fact]
    public void The_server_cleans_names_it_did_not_write()
    {
        var rig = new SessionRig();
        var raw = rig.Network.CreateNode("raw");
        var hello = SessionRig.Encode(w => new HelloMessage
        {
            ModVersion = SessionRig.ModVersion, ClientNonce = 42, GameBuild = "b", PlayerName = "\u0007Evil\nName  ",
        }.Write(w));
        rig.SendRaw(raw, hello);
        Assert.Equal("Evil Name", Assert.Single(rig.Server.Peers).Name);
    }

    [Fact]
    public void Garbage_never_crashes_the_server_and_never_blocks_real_players()
    {
        var rig = new SessionRig();
        var raw = rig.Network.CreateNode("fuzz");
        var rng = new Random(1234);
        for (var i = 0; i < 3000; i++)
        {
            var junk = new byte[rng.Next(0, 80)];
            rng.NextBytes(junk);
            if (i % 2 == 0 && junk.Length >= 3)
            {
                junk[0] = ProtocolInfo.Magic0; // half the time, dress it up as ours
                junk[1] = ProtocolInfo.Magic1;
                junk[2] = (byte)rng.Next(0, 8);
            }

            rig.SendRaw(raw, junk);
        }

        var client = rig.AddClient("real");
        rig.Connect(client);
        rig.RunUntil(() => client.State == ClientState.Connected);
    }

    [Fact]
    public void Every_decoder_fails_only_with_wire_format_errors()
    {
        var rng = new Random(99);
        var decoders = new Action<PacketReader>[]
        {
            r => HelloMessage.ReadPrefix(r).ReadBody(r),
            r => WelcomeMessage.Read(r),
            r => RejectMessage.Read(r),
            r => PingMessage.Read(r),
            r => GoodbyeMessage.Read(r),
        };
        for (var i = 0; i < 5000; i++)
        {
            var junk = new byte[rng.Next(0, 64)];
            rng.NextBytes(junk);
            foreach (var decode in decoders)
            {
                try
                {
                    decode(new PacketReader(junk, 0, junk.Length));
                }
                catch (WireFormatException)
                {
                    // Expected for most junk. Anything else escapes and fails the test.
                }
            }
        }
    }
}
