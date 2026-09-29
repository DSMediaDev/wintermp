namespace WinterMP.Core.Wire;

/// <summary>
/// Fits text inside a UTF-8 byte budget without splitting a character, so a long or multi-byte
/// string (Finnish place names, emoji in a player name) is shortened instead of failing a send.
/// </summary>
public static class WireText
{
    public static string Fit(string? text, int maxBytes)
    {
        if (text == null || text.Length == 0 || maxBytes <= 0) return string.Empty;
        var bytes = 0;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            int width;
            int chars;
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                width = 4;
                chars = 2;
            }
            else
            {
                // Lone surrogates encode as U+FFFD (3 bytes), same as Encoding.UTF8 does.
                width = c < 0x80 ? 1 : c < 0x800 ? 2 : 3;
                chars = 1;
            }

            if (bytes + width > maxBytes) break;
            bytes += width;
            i += chars;
        }

        return i == text.Length ? text : text.Substring(0, i);
    }
}
