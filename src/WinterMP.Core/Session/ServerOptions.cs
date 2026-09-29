namespace WinterMP.Core.Session;

/// <summary>Knobs for <see cref="NetServer"/>. Plain settable properties so any host can bind config to them.</summary>
public sealed class ServerOptions
{
    public const int HardPlayerCap = 64;

    public string ServerName { get; set; } = "WinterMP Server";

    /// <summary>Clamped to 1..<see cref="HardPlayerCap"/>.</summary>
    public int MaxPlayers { get; set; } = 8;

    /// <summary>
    /// The exact game build every player must run. Null or empty means the first player to join sets
    /// it for the session; after that, everyone must match.
    /// </summary>
    public string? RequiredGameBuild { get; set; }

    /// <summary>A peer that sends nothing for this long is dropped.</summary>
    public int PeerTimeoutMs { get; set; } = 15000;

    /// <summary>This server's own mod version, quoted back in version-mismatch rejects.</summary>
    public string ModVersion { get; set; } = string.Empty;
}
