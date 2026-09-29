using WinterMP.Core.Wire;

namespace WinterMP.Core.Protocol;

/// <summary>
/// Join request. The first 2 fields (protocol version, mod version) are the frozen prefix: every
/// build, past and future, writes them first, so a server can always name the mismatch.
/// </summary>
public sealed class HelloMessage
{
    public const int MaxModVersionBytes = 32;
    public const int MaxGameBuildBytes = 64;
    public const int MaxPlayerNameBytes = 64;

    public ushort ProtocolVersion { get; set; } = ProtocolInfo.Version;
    public string ModVersion { get; set; } = string.Empty;

    /// <summary>Random per connection attempt; lets the server tell a retry from a reconnect.</summary>
    public ulong ClientNonce { get; set; }
    public string GameBuild { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;

    public void Write(PacketWriter writer)
    {
        Frame.WriteHeader(writer, MessageKind.Hello);
        writer.WriteUInt16(ProtocolVersion);
        writer.WriteString(ModVersion, MaxModVersionBytes);
        writer.WriteUInt64(ClientNonce);
        writer.WriteString(GameBuild, MaxGameBuildBytes);
        writer.WriteString(PlayerName, MaxPlayerNameBytes);
    }

    /// <summary>Reads only the frozen prefix. Safe for a Hello from any build.</summary>
    public static HelloMessage ReadPrefix(PacketReader reader) => new HelloMessage
    {
        ProtocolVersion = reader.ReadUInt16(),
        ModVersion = reader.ReadString(MaxModVersionBytes),
    };

    /// <summary>Reads the rest of a same-version Hello after <see cref="ReadPrefix"/>.</summary>
    public void ReadBody(PacketReader reader)
    {
        ClientNonce = reader.ReadUInt64();
        GameBuild = reader.ReadString(MaxGameBuildBytes);
        PlayerName = reader.ReadString(MaxPlayerNameBytes);
        reader.ExpectEnd();
    }
}

/// <summary>Admission. Everything the client needs to know about the session it just joined.</summary>
public sealed class WelcomeMessage
{
    public const int MaxServerNameBytes = 64;

    public ushort PlayerId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string SessionGameBuild { get; set; } = string.Empty;
    public byte MaxPlayers { get; set; }

    public void Write(PacketWriter writer)
    {
        Frame.WriteHeader(writer, MessageKind.Welcome);
        writer.WriteUInt16(PlayerId);
        writer.WriteString(ServerName, MaxServerNameBytes);
        writer.WriteString(SessionGameBuild, HelloMessage.MaxGameBuildBytes);
        writer.WriteByte(MaxPlayers);
    }

    public static WelcomeMessage Read(PacketReader reader)
    {
        var message = new WelcomeMessage
        {
            PlayerId = reader.ReadUInt16(),
            ServerName = reader.ReadString(MaxServerNameBytes),
            SessionGameBuild = reader.ReadString(HelloMessage.MaxGameBuildBytes),
            MaxPlayers = reader.ReadByte(),
        };
        reader.ExpectEnd();
        return message;
    }
}

/// <summary>
/// Refusal. The layout is frozen forever (kind, message, have, need) so a client of any version
/// can show a real reason, phrased from the joining player's side: "you have X, the server needs Y".
/// </summary>
public sealed class RejectMessage
{
    public const int MaxMessageBytes = 256;
    public const int MaxHaveNeedBytes = 96;

    public RejectKind Kind { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Have { get; set; } = string.Empty;
    public string Need { get; set; } = string.Empty;

    public void Write(PacketWriter writer)
    {
        Frame.WriteHeader(writer, MessageKind.Reject);
        writer.WriteByte((byte)Kind);
        writer.WriteString(Message, MaxMessageBytes);
        writer.WriteString(Have, MaxHaveNeedBytes);
        writer.WriteString(Need, MaxHaveNeedBytes);
    }

    /// <summary>Tolerates trailing bytes: a future build may append fields after the frozen 4.</summary>
    public static RejectMessage Read(PacketReader reader) => new RejectMessage
    {
        Kind = (RejectKind)reader.ReadByte(),
        Message = reader.ReadString(MaxMessageBytes),
        Have = reader.ReadString(MaxHaveNeedBytes),
        Need = reader.ReadString(MaxHaveNeedBytes),
    };

    public override string ToString() =>
        Kind + ": " + Message + (Have.Length > 0 || Need.Length > 0 ? " (you have " + Have + ", the server needs " + Need + ")" : string.Empty);
}

/// <summary>Keepalive + round-trip probe. Pong echoes both fields back unchanged.</summary>
public sealed class PingMessage
{
    public uint Sequence { get; set; }
    public uint SentAtMs { get; set; }

    public void Write(PacketWriter writer, MessageKind kind)
    {
        Frame.WriteHeader(writer, kind);
        writer.WriteUInt32(Sequence);
        writer.WriteUInt32(SentAtMs);
    }

    public static PingMessage Read(PacketReader reader)
    {
        var message = new PingMessage { Sequence = reader.ReadUInt32(), SentAtMs = reader.ReadUInt32() };
        reader.ExpectEnd();
        return message;
    }
}

/// <summary>A deliberate departure, so the other side can react at once instead of waiting for a timeout.</summary>
public sealed class GoodbyeMessage
{
    public GoodbyeReason Reason { get; set; }

    public void Write(PacketWriter writer)
    {
        Frame.WriteHeader(writer, MessageKind.Goodbye);
        writer.WriteByte((byte)Reason);
    }

    public static GoodbyeMessage Read(PacketReader reader)
    {
        var message = new GoodbyeMessage { Reason = (GoodbyeReason)reader.ReadByte() };
        reader.ExpectEnd();
        return message;
    }
}
