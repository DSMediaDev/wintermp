using WinterMP.Core.Net;

namespace WinterMP.Core.Session;

/// <summary>One admitted player, as the server sees them.</summary>
public sealed class PeerInfo
{
    internal PeerInfo(ushort id, NetEndPoint endPoint, ulong nonce, string name, string modVersion, string gameBuild, long nowMs)
    {
        Id = id;
        EndPoint = endPoint;
        Nonce = nonce;
        Name = name;
        ModVersion = modVersion;
        GameBuild = gameBuild;
        JoinedAtMs = nowMs;
        LastHeardMs = nowMs;
    }

    /// <summary>Session-scoped id, 1 and up. Reused after a player leaves.</summary>
    public ushort Id { get; }
    public NetEndPoint EndPoint { get; }
    public string Name { get; }
    public string ModVersion { get; }
    public string GameBuild { get; }
    public long JoinedAtMs { get; }

    internal ulong Nonce { get; }
    internal long LastHeardMs { get; set; }

    public override string ToString() => Name + " (#" + Id + ", " + EndPoint + ")";
}
