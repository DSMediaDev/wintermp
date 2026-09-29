namespace WinterMP.Core.Protocol;

/// <summary>
/// The byte after the magic. Values are append-only: never renumber or reuse one, because the
/// Hello and Reject layouts must stay readable by every past and future build.
/// </summary>
public enum MessageKind : byte
{
    /// <summary>Client asks to join. Starts with a frozen prefix (protocol version + mod version).</summary>
    Hello = 1,

    /// <summary>Server admits the client.</summary>
    Welcome = 2,

    /// <summary>Server refuses the client. Layout frozen forever so any build can read the reason.</summary>
    Reject = 3,

    Ping = 4,
    Pong = 5,

    /// <summary>Either side is leaving on purpose.</summary>
    Goodbye = 6,
}

/// <summary>Why a join was refused. Append-only, same rule as <see cref="MessageKind"/>.</summary>
public enum RejectKind : byte
{
    Unknown = 0,
    ProtocolMismatch = 1,
    GameBuildMismatch = 2,
    ServerFull = 3,
    Malformed = 4,
}

/// <summary>Why a peer said goodbye. Append-only.</summary>
public enum GoodbyeReason : byte
{
    Unknown = 0,
    Leaving = 1,
    ServerShutdown = 2,
    Kicked = 3,
}
