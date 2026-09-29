using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WinterMP.Core.Wire;

/// <summary>
/// Little-endian binary writer over a growable buffer. Hand-rolled on purpose: the game's runtime
/// (.NET 3.5 profile) cannot load modern serializers, and a fixed schema has no deserialization
/// attack surface. Reuse one writer per sender and call <see cref="Reset"/> between messages.
/// </summary>
public sealed class PacketWriter
{
    private byte[] _buffer;
    private int _length;

    public PacketWriter(int initialCapacity = 256)
    {
        _buffer = new byte[Math.Max(16, initialCapacity)];
    }

    /// <summary>Number of valid bytes written so far.</summary>
    public int Length => _length;

    /// <summary>The backing buffer; only the first <see cref="Length"/> bytes are meaningful.</summary>
    public byte[] Buffer => _buffer;

    public void Reset() => _length = 0;

    public byte[] ToArray()
    {
        var copy = new byte[_length];
        System.Buffer.BlockCopy(_buffer, 0, copy, 0, _length);
        return copy;
    }

    public void WriteByte(byte value)
    {
        Ensure(1);
        _buffer[_length++] = value;
    }

    public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    public void WriteUInt16(ushort value)
    {
        Ensure(2);
        _buffer[_length++] = (byte)value;
        _buffer[_length++] = (byte)(value >> 8);
    }

    public void WriteInt32(int value) => WriteUInt32(unchecked((uint)value));

    public void WriteUInt32(uint value)
    {
        Ensure(4);
        _buffer[_length++] = (byte)value;
        _buffer[_length++] = (byte)(value >> 8);
        _buffer[_length++] = (byte)(value >> 16);
        _buffer[_length++] = (byte)(value >> 24);
    }

    public void WriteUInt64(ulong value)
    {
        WriteUInt32((uint)value);
        WriteUInt32((uint)(value >> 32));
    }

    public void WriteSingle(float value) => WriteUInt32(new FloatBits { Float = value }.Bits);

    /// <summary>
    /// Writes a UTF-8 string with a 16-bit byte-length prefix. <paramref name="maxBytes"/> is the
    /// same cap the reader enforces; exceeding it here is a local bug, so it throws immediately.
    /// A null string is written as empty.
    /// </summary>
    public void WriteString(string? value, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        if (bytes.Length > maxBytes || bytes.Length > ushort.MaxValue)
        {
            throw new ArgumentException("String is " + bytes.Length + " bytes; the cap is " + maxBytes + ".");
        }

        WriteUInt16((ushort)bytes.Length);
        WriteBytes(bytes, 0, bytes.Length);
    }

    public void WriteBytes(byte[] source, int offset, int count)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (offset < 0 || count < 0 || offset + count > source.Length) throw new ArgumentOutOfRangeException(nameof(count));
        Ensure(count);
        System.Buffer.BlockCopy(source, offset, _buffer, _length, count);
        _length += count;
    }

    private void Ensure(int extra)
    {
        var needed = _length + extra;
        if (needed <= _buffer.Length) return;
        var size = _buffer.Length;
        while (size < needed) size *= 2;
        var grown = new byte[size];
        System.Buffer.BlockCopy(_buffer, 0, grown, 0, _length);
        _buffer = grown;
    }
}

/// <summary>Reinterprets float bits without allocating (BitConverter.SingleToInt32Bits is newer than .NET 3.5).</summary>
[StructLayout(LayoutKind.Explicit)]
internal struct FloatBits
{
    [FieldOffset(0)] public float Float;
    [FieldOffset(0)] public uint Bits;
}
