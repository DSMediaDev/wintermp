using System;
using System.Text;

namespace WinterMP.Core.Wire;

/// <summary>
/// Bounds-checked little-endian reader over a received datagram. Every method validates length
/// before touching the buffer and throws <see cref="WireFormatException"/> rather than reading past
/// the end, so a hostile packet can fail a decode but never corrupt state.
/// </summary>
public sealed class PacketReader
{
    private readonly byte[] _buffer;
    private readonly int _end;
    private int _position;

    public PacketReader(byte[] buffer, int offset, int count)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));
        _buffer = buffer;
        _position = offset;
        _end = offset + count;
    }

    public int Remaining => _end - _position;

    public byte ReadByte()
    {
        Need(1);
        return _buffer[_position++];
    }

    public bool ReadBool()
    {
        var value = ReadByte();
        if (value > 1) throw new WireFormatException("Bool byte out of range: " + value + ".");
        return value == 1;
    }

    public ushort ReadUInt16()
    {
        Need(2);
        var value = (ushort)(_buffer[_position] | (_buffer[_position + 1] << 8));
        _position += 2;
        return value;
    }

    public int ReadInt32() => unchecked((int)ReadUInt32());

    public uint ReadUInt32()
    {
        Need(4);
        var value = (uint)(_buffer[_position]
            | (_buffer[_position + 1] << 8)
            | (_buffer[_position + 2] << 16)
            | (_buffer[_position + 3] << 24));
        _position += 4;
        return value;
    }

    public ulong ReadUInt64()
    {
        ulong low = ReadUInt32();
        ulong high = ReadUInt32();
        return low | (high << 32);
    }

    public float ReadSingle() => new FloatBits { Bits = ReadUInt32() }.Float;

    /// <summary>Reads a UTF-8 string written by <see cref="PacketWriter.WriteString"/>, enforcing the cap.</summary>
    public string ReadString(int maxBytes)
    {
        int length = ReadUInt16();
        if (length > maxBytes) throw new WireFormatException("String of " + length + " bytes exceeds the cap of " + maxBytes + ".");
        Need(length);
        var value = Encoding.UTF8.GetString(_buffer, _position, length);
        _position += length;
        return value;
    }

    /// <summary>Asserts the message was consumed exactly; trailing bytes mean a layout mismatch.</summary>
    public void ExpectEnd()
    {
        if (_position != _end) throw new WireFormatException(Remaining + " unexpected trailing bytes.");
    }

    private void Need(int count)
    {
        if (_end - _position < count)
        {
            throw new WireFormatException("Truncated: needed " + count + " bytes, " + Remaining + " left.");
        }
    }
}
