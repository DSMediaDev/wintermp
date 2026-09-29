using System;

namespace WinterMP.Core.Net;

/// <summary>
/// An opaque remote address. Transports create these (UDP address, loopback node, Steam id later);
/// sessions only compare and store them. Equality is by <see cref="Key"/>.
/// </summary>
public sealed class NetEndPoint : IEquatable<NetEndPoint>
{
    public NetEndPoint(string key, object? native = null)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Native = native;
    }

    /// <summary>Stable, human-readable identity, e.g. "udp:203.0.113.7:27777".</summary>
    public string Key { get; }

    /// <summary>Transport-private handle (an IPEndPoint for UDP). Never inspected by sessions.</summary>
    public object? Native { get; }

    public bool Equals(NetEndPoint? other) => other != null && string.Equals(Key, other.Key, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as NetEndPoint);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Key);

    public override string ToString() => Key;
}
