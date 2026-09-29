using System.Text;
using WinterMP.Core.Wire;

namespace WinterMP.Core.Protocol;

/// <summary>Display-name rules, enforced by the server on every join.</summary>
public static class PlayerNames
{
    public const int MaxLength = 32;
    public const string Fallback = "Player";

    /// <summary>Strips control characters, collapses whitespace, trims and clamps to <see cref="MaxLength"/>.</summary>
    public static string Sanitize(string? name)
    {
        if (name == null) return Fallback;
        var builder = new StringBuilder(name.Length);
        var lastWasSpace = true;
        foreach (var c in name)
        {
            // Whitespace first: tab and newline are control characters too, but they separate words.
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            if (char.IsControl(c)) continue;

            builder.Append(c);
            lastWasSpace = false;
        }

        var clean = builder.ToString().Trim();
        if (clean.Length > MaxLength) clean = clean.Substring(0, MaxLength).TrimEnd();
        clean = WireText.Fit(clean, HelloMessage.MaxPlayerNameBytes).TrimEnd();
        return clean.Length == 0 ? Fallback : clean;
    }
}
