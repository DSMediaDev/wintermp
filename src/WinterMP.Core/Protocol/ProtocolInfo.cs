namespace WinterMP.Core.Protocol;

/// <summary>Wire-level constants shared by every build.</summary>
public static class ProtocolInfo
{
    /// <summary>
    /// The network protocol version. Bumped deliberately whenever the wire format changes; peers
    /// must match exactly. Independent of the mod's release version.
    /// </summary>
    public const ushort Version = 1;

    /// <summary>
    /// Every datagram starts with these 2 bytes. They are frozen forever and never versioned, so
    /// any 2 builds can always get far enough to explain a mismatch instead of silently timing out.
    /// </summary>
    public const byte Magic0 = (byte)'W';
    public const byte Magic1 = (byte)'M';

    /// <summary>Magic (2 bytes) + message kind (1 byte).</summary>
    public const int HeaderSize = 3;

    /// <summary>Largest datagram we send: stays under common internet MTUs once IP/UDP headers are added.</summary>
    public const int MaxDatagramSize = 1200;

    /// <summary>Default UDP port for dedicated servers.</summary>
    public const int DefaultPort = 27777;
}
