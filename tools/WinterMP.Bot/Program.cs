using WinterMP.Core.Diagnostics;
using WinterMP.Core.Net;
using WinterMP.Core.Protocol;
using WinterMP.Core.Session;

namespace WinterMP.Bot;

/// <summary>
/// Joins a server, reports what happened, optionally idles for a while, then leaves politely.
/// Exit codes: 0 joined, 3 rejected, 4 no answer / lost, 2 bad arguments.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string address = "127.0.0.1";
        string name = "Bot";
        string build = "unknown";
        string modVersion = "0.0.1";
        var stayMs = 0;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException(args[i] + " needs a value.");
            switch (args[i])
            {
                case "--connect": address = Next(); break;
                case "--name": name = Next(); break;
                case "--build": build = Next(); break;
                case "--mod-version": modVersion = Next(); break;
                case "--stay": stayMs = int.Parse(Next()) * 1000; break;
                default:
                    Console.Error.WriteLine("Usage: WinterMP.Bot [--connect host[:port]] [--name text] [--build ver] [--mod-version ver] [--stay seconds]");
                    return 2;
            }
        }

        if (!UdpTransport.TryResolve(address, ProtocolInfo.DefaultPort, out var server, out var error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        var log = new BotLog();
        using var transport = UdpTransport.Bind(0);
        var client = new NetClient(transport, log);
        client.Connect(server, new ClientIdentity { ModVersion = modVersion, GameBuild = build, PlayerName = name }, MonotonicClock.NowMs);

        while (client.State == ClientState.Connecting) Tick(client);
        if (client.State != ClientState.Connected) return client.State == ClientState.Rejected ? 3 : 4;

        var leaveAt = MonotonicClock.NowMs + stayMs;
        while (client.State == ClientState.Connected && MonotonicClock.NowMs < leaveAt) Tick(client);
        if (client.State != ClientState.Connected) return 4;

        Console.WriteLine("Round trip: " + (client.RttMs.HasValue ? client.RttMs + " ms" : "not measured (stay longer)"));
        client.Disconnect();
        Thread.Sleep(50);
        return 0;
    }

    private static void Tick(NetClient client)
    {
        client.Update(MonotonicClock.NowMs);
        Thread.Sleep(10);
    }

    private sealed class BotLog : ILog
    {
        public void Info(string message) => Console.WriteLine(message);
        public void Warn(string message) => Console.WriteLine("warning: " + message);
        public void Error(string message) => Console.Error.WriteLine("error: " + message);
    }
}
