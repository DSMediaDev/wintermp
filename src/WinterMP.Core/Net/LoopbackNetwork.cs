using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace WinterMP.Core.Net;

/// <summary>
/// An in-memory network for tests and the headless harness: deterministic, instant, and able to
/// drop chosen datagrams to exercise retries. Not thread-safe; drive it from one thread.
/// </summary>
public sealed class LoopbackNetwork
{
    private readonly Dictionary<string, LoopbackTransport> _nodes = new Dictionary<string, LoopbackTransport>(StringComparer.Ordinal);

    /// <summary>Return true to drop a datagram in flight. Gets (from, to, bytes).</summary>
    public Func<NetEndPoint, NetEndPoint, byte[], bool>? Drop { get; set; }

    /// <summary>Datagrams delivered so far (after drops). Handy for asserting traffic volume.</summary>
    public int Delivered { get; private set; }

    public LoopbackTransport CreateNode(string name)
    {
        var endPoint = AddressOf(name);
        if (_nodes.ContainsKey(endPoint.Key)) throw new ArgumentException("Node already exists: " + name, nameof(name));
        var node = new LoopbackTransport(this, endPoint);
        _nodes[endPoint.Key] = node;
        return node;
    }

    /// <summary>
    /// Simulates a process restarting on the same address: the old node goes dark and a new one takes
    /// its place. Anything still holding the old node just stops hearing traffic.
    /// </summary>
    public LoopbackTransport CreateNodeReplacing(string name)
    {
        LoopbackTransport? old;
        if (_nodes.TryGetValue(AddressOf(name).Key, out old)) old.Dispose();
        return CreateNode(name);
    }

    /// <summary>An address for a node that may not exist (sending to it just loses the datagram).</summary>
    public static NetEndPoint AddressOf(string name) => new NetEndPoint("loop:" + name);

    internal void Deliver(NetEndPoint from, NetEndPoint to, byte[] buffer, int offset, int count)
    {
        var copy = new byte[count];
        Buffer.BlockCopy(buffer, offset, copy, 0, count);
        if (Drop != null && Drop(from, to, copy)) return;
        LoopbackTransport? target;
        if (!_nodes.TryGetValue(to.Key, out target)) return;
        target.Enqueue(from, copy);
        Delivered++;
    }

    // Identity-checked, so a replaced node disposing late cannot unregister its successor.
    internal void Remove(LoopbackTransport node)
    {
        LoopbackTransport? current;
        var key = node.LocalEndPoint!.Key;
        if (_nodes.TryGetValue(key, out current) && ReferenceEquals(current, node)) _nodes.Remove(key);
    }
}

/// <summary>One node on a <see cref="LoopbackNetwork"/>.</summary>
public sealed class LoopbackTransport : IDatagramTransport
{
    private readonly LoopbackNetwork _network;
    private readonly NetEndPoint _endPoint;
    private readonly Queue<Packet> _inbox = new Queue<Packet>();
    private bool _disposed;

    internal LoopbackTransport(LoopbackNetwork network, NetEndPoint endPoint)
    {
        _network = network;
        _endPoint = endPoint;
    }

    public NetEndPoint? LocalEndPoint => _endPoint;

    public void Send(NetEndPoint to, byte[] buffer, int offset, int count)
    {
        if (!_disposed) _network.Deliver(_endPoint, to, buffer, offset, count);
    }

    public bool TryReceive(byte[] buffer, out int count, [NotNullWhen(true)] out NetEndPoint? from)
    {
        count = 0;
        from = null;
        if (_disposed || _inbox.Count == 0) return false;
        var packet = _inbox.Dequeue();
        count = Math.Min(packet.Data.Length, buffer.Length);
        Buffer.BlockCopy(packet.Data, 0, buffer, 0, count);
        from = packet.From;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _inbox.Clear();
        _network.Remove(this);
    }

    internal void Enqueue(NetEndPoint from, byte[] data) => _inbox.Enqueue(new Packet(from, data));

    private sealed class Packet
    {
        public Packet(NetEndPoint from, byte[] data)
        {
            From = from;
            Data = data;
        }

        public NetEndPoint From { get; }
        public byte[] Data { get; }
    }
}
