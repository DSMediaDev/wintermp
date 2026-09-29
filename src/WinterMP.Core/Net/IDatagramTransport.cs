using System;
using System.Diagnostics.CodeAnalysis;

namespace WinterMP.Core.Net;

/// <summary>
/// Unreliable datagrams in, unreliable datagrams out. Everything above this seam (handshake,
/// sessions, and the reliability layer to come) is transport-agnostic, so UDP, the in-memory
/// loopback used by tests, and Steam P2P are interchangeable.
/// </summary>
public interface IDatagramTransport : IDisposable
{
    /// <summary>Where this transport receives, if it has an address (null for some transports).</summary>
    NetEndPoint? LocalEndPoint { get; }

    /// <summary>Fire and forget. Lossy by contract: failures are swallowed, not thrown.</summary>
    void Send(NetEndPoint to, byte[] buffer, int offset, int count);

    /// <summary>Non-blocking. Copies the next waiting datagram into <paramref name="buffer"/>.</summary>
    bool TryReceive(byte[] buffer, out int count, [NotNullWhen(true)] out NetEndPoint? from);
}
