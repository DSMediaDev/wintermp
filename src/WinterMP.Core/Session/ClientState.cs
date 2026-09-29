namespace WinterMP.Core.Session;

/// <summary>Where a <see cref="NetClient"/> is in its lifecycle.</summary>
public enum ClientState
{
    Idle,
    Connecting,
    Connected,

    /// <summary>The server answered and said no. The reason is in <see cref="NetClient.Rejection"/>.</summary>
    Rejected,

    /// <summary>The server never answered.</summary>
    Failed,

    /// <summary>Was connected (or connecting), then the link ended: left, kicked, shut down or timed out.</summary>
    Disconnected,
}

/// <summary>Who this client says it is when joining.</summary>
public sealed class ClientIdentity
{
    public string ModVersion { get; set; } = string.Empty;
    public string GameBuild { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
}
