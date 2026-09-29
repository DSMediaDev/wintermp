using WinterMP.Core.Wire;

namespace WinterMP.Core.Protocol;

/// <summary>Reads and writes the 3-byte datagram header (magic + kind).</summary>
public static class Frame
{
    public static void WriteHeader(PacketWriter writer, MessageKind kind)
    {
        writer.WriteByte(ProtocolInfo.Magic0);
        writer.WriteByte(ProtocolInfo.Magic1);
        writer.WriteByte((byte)kind);
    }

    /// <summary>
    /// True when the datagram starts with our magic. Anything else (port scanners, other games on the
    /// same port) is dropped silently by the caller; it is not worth a reply.
    /// </summary>
    public static bool TryReadHeader(byte[] buffer, int count, out MessageKind kind)
    {
        kind = 0;
        if (buffer == null || count < ProtocolInfo.HeaderSize || count > buffer.Length) return false;
        if (buffer[0] != ProtocolInfo.Magic0 || buffer[1] != ProtocolInfo.Magic1) return false;
        kind = (MessageKind)buffer[2];
        return true;
    }

    /// <summary>A reader positioned just after the header.</summary>
    public static PacketReader BodyReader(byte[] buffer, int count) =>
        new PacketReader(buffer, ProtocolInfo.HeaderSize, count - ProtocolInfo.HeaderSize);
}
