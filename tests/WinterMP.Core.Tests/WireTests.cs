using System.Text;
using WinterMP.Core.Protocol;
using WinterMP.Core.Wire;

namespace WinterMP.Core.Tests;

public class WireTests
{
    [Fact]
    public void Primitives_round_trip()
    {
        var w = new PacketWriter(16);
        w.WriteByte(0xAB);
        w.WriteBool(true);
        w.WriteBool(false);
        w.WriteUInt16(0xBEEF);
        w.WriteInt32(-123456789);
        w.WriteUInt32(0xDEADBEEF);
        w.WriteUInt64(0x0123456789ABCDEF);
        w.WriteSingle(-3.25f);
        w.WriteString("Peräkylä, Suomi", 64);

        var r = new PacketReader(w.Buffer, 0, w.Length);
        Assert.Equal(0xAB, r.ReadByte());
        Assert.True(r.ReadBool());
        Assert.False(r.ReadBool());
        Assert.Equal(0xBEEF, r.ReadUInt16());
        Assert.Equal(-123456789, r.ReadInt32());
        Assert.Equal(0xDEADBEEFu, r.ReadUInt32());
        Assert.Equal(0x0123456789ABCDEFul, r.ReadUInt64());
        Assert.Equal(-3.25f, r.ReadSingle());
        Assert.Equal("Peräkylä, Suomi", r.ReadString(64));
        r.ExpectEnd();
    }

    [Fact]
    public void Layout_is_little_endian_regardless_of_host()
    {
        var w = new PacketWriter();
        w.WriteUInt32(0x04030201);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, w.ToArray());
    }

    [Fact]
    public void Writer_grows_past_its_initial_capacity()
    {
        var w = new PacketWriter(16);
        for (var i = 0; i < 10000; i++) w.WriteByte((byte)i);
        Assert.Equal(10000, w.Length);
        Assert.Equal(unchecked((byte)9999), w.ToArray()[9999]);
    }

    [Fact]
    public void Every_read_on_a_short_buffer_throws_wire_format_not_index_errors()
    {
        var empty = new byte[0];
        Assert.Throws<WireFormatException>(() => new PacketReader(empty, 0, 0).ReadByte());
        Assert.Throws<WireFormatException>(() => new PacketReader(new byte[1], 0, 1).ReadUInt16());
        Assert.Throws<WireFormatException>(() => new PacketReader(new byte[3], 0, 3).ReadUInt32());
        Assert.Throws<WireFormatException>(() => new PacketReader(new byte[7], 0, 7).ReadUInt64());
        // A string header that promises more bytes than exist.
        Assert.Throws<WireFormatException>(() => new PacketReader(new byte[] { 10, 0, 65 }, 0, 3).ReadString(64));
    }

    [Fact]
    public void Reader_respects_the_slice_it_was_given()
    {
        var buffer = new byte[] { 9, 1, 2, 9 };
        var r = new PacketReader(buffer, 1, 2);
        Assert.Equal(0x0201, r.ReadUInt16());
        Assert.Throws<WireFormatException>(() => r.ReadByte());
    }

    [Fact]
    public void String_caps_are_enforced_on_both_sides()
    {
        Assert.Throws<ArgumentException>(() => new PacketWriter().WriteString(new string('x', 65), 64));
        var w = new PacketWriter();
        w.WriteString(new string('x', 65), 100);
        Assert.Throws<WireFormatException>(() => new PacketReader(w.Buffer, 0, w.Length).ReadString(64));
    }

    [Fact]
    public void Bool_bytes_other_than_0_and_1_are_rejected()
    {
        Assert.Throws<WireFormatException>(() => new PacketReader(new byte[] { 2 }, 0, 1).ReadBool());
    }

    [Fact]
    public void ExpectEnd_flags_trailing_bytes()
    {
        var r = new PacketReader(new byte[] { 1, 2 }, 0, 2);
        r.ReadByte();
        Assert.Throws<WireFormatException>(() => r.ExpectEnd());
    }

    [Theory]
    [InlineData("plain ascii", 64, "plain ascii")]
    [InlineData("abcdef", 3, "abc")]
    [InlineData("ääää", 5, "ää")] // 2 bytes each: a third would need 6
    [InlineData("", 10, "")]
    public void WireText_fits_whole_characters_inside_the_byte_budget(string input, int budget, string expected)
    {
        Assert.Equal(expected, WireText.Fit(input, budget));
    }

    [Fact]
    public void WireText_never_splits_a_surrogate_pair_and_always_fits()
    {
        var text = "ab\U0001F697cd"; // a car emoji is a surrogate pair, 4 bytes in UTF-8
        Assert.Equal("ab", WireText.Fit(text, 5));
        Assert.Equal("ab\U0001F697", WireText.Fit(text, 6));
        var rng = new Random(7);
        for (var i = 0; i < 500; i++)
        {
            var chars = new char[rng.Next(0, 40)];
            for (var j = 0; j < chars.Length; j++) chars[j] = (char)rng.Next(0, 0xFFFF);
            var budget = rng.Next(0, 64);
            var fitted = WireText.Fit(new string(chars), budget);
            Assert.True(Encoding.UTF8.GetByteCount(fitted) <= budget);
        }
    }

    [Fact]
    public void Frame_header_accepts_only_our_magic()
    {
        var hello = SessionRig.Encode(w => Frame.WriteHeader(w, MessageKind.Hello));
        Assert.True(Frame.TryReadHeader(hello, hello.Length, out var kind));
        Assert.Equal(MessageKind.Hello, kind);
        Assert.False(Frame.TryReadHeader(new byte[] { (byte)'W', (byte)'M' }, 2, out _));
        Assert.False(Frame.TryReadHeader(new byte[] { (byte)'X', (byte)'M', 1 }, 3, out _));
        Assert.False(Frame.TryReadHeader(Encoding.ASCII.GetBytes("GET / HTTP/1.1"), 14, out _));
    }

    [Theory]
    [InlineData("  Cody   Moran ", "Cody Moran")]
    [InlineData("tab\tand\nnewline", "tab and newline")]
    [InlineData("bell\u0007less", "bellless")]
    [InlineData("   ", PlayerNames.Fallback)]
    [InlineData(null, PlayerNames.Fallback)]
    public void Player_names_are_cleaned(string? input, string expected)
    {
        Assert.Equal(expected, PlayerNames.Sanitize(input));
    }

    [Fact]
    public void Player_names_fit_both_the_character_and_byte_caps()
    {
        Assert.Equal(PlayerNames.MaxLength, PlayerNames.Sanitize(new string('a', 100)).Length);
        var wide = PlayerNames.Sanitize(new string('ä', 40));
        Assert.True(Encoding.UTF8.GetByteCount(wide) <= HelloMessage.MaxPlayerNameBytes);
    }
}
