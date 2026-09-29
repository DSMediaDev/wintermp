using System;

namespace WinterMP.Core.Wire;

/// <summary>
/// Thrown when incoming bytes do not match the expected layout (truncated, oversized, out of range).
/// Every read from the network is untrusted, so decoders throw this instead of guessing.
/// </summary>
public sealed class WireFormatException : Exception
{
    public WireFormatException(string message) : base(message) { }
}
