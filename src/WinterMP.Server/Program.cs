using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using WinterMP.Core.Net;
using WinterMP.Core.Protocol;
using WinterMP.Core.Session;

namespace WinterMP.Server;

internal static class Program
{
    private const int TickMs = 16;
    private static volatile bool _stopRequested;

    private static int Main(string[] args)
    {
        var settings = ServerArgs.Parse(args, out var error);
        if (error != null)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(ServerArgs.Usage);
            return 2;
        }

        if (settings.ShowHelp)
        {
            Console.WriteLine(ServerArgs.Usage);
            return 0;
        }

        var log = new ConsoleLog();
        var version = ModVersion(out var buildInfo);
        UdpTransport transport;
        try
        {
            transport = UdpTransport.Bind(settings.Port);
        }
        catch (SocketException e)
        {
            log.Error("Could not open UDP port " + settings.Port + ": " + e.Message + " (is another server already using it?)");
            return 1;
        }

        using (transport)
        {
            var server = new NetServer(transport, new ServerOptions
            {
                ServerName = settings.Name,
                MaxPlayers = settings.MaxPlayers,
                RequiredGameBuild = settings.GameBuild,
                ModVersion = version,
            }, log);

            log.Info("WinterMP Server " + buildInfo + " (protocol " + ProtocolInfo.Version + ")");
            log.Info("Listening on UDP port " + transport.LocalPort + " as \"" + settings.Name + "\", up to " + server.MaxPlayers + " players, "
                + (server.SessionGameBuild != null ? "game build " + server.SessionGameBuild : "game build set by the first player") + ".");
            log.Info("Type help for console commands. Ctrl+C stops the server cleanly.");

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                _stopRequested = true;
            };
            using var sigterm = TryRegisterSigterm();
            var commands = StartConsoleReader();

            while (!_stopRequested)
            {
                server.Update(MonotonicClock.NowMs);
                while (commands.TryDequeue(out var line))
                {
                    if (Execute(line.Trim(), server, log)) _stopRequested = true;
                }

                Thread.Sleep(TickMs);
            }

            log.Info("Shutting down...");
            server.Shutdown();
            Thread.Sleep(100); // let the goodbyes leave the machine before the socket closes
        }

        log.Info("Stopped.");
        return 0;
    }

    /// <summary>Runs one console command. Returns true when the server should stop.</summary>
    private static bool Execute(string line, NetServer server, ConsoleLog log)
    {
        if (line.Length == 0) return false;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (parts[0].ToLowerInvariant())
        {
            case "quit":
            case "stop":
            case "exit":
                return true;

            case "status":
                log.Info(server.Peers.Count + "/" + server.MaxPlayers + " players, game build " + (server.SessionGameBuild ?? "not set yet") + ".");
                foreach (var peer in server.Peers)
                {
                    log.Info("  #" + peer.Id + " " + peer.Name + " from " + peer.EndPoint + " (WinterMP " + peer.ModVersion + ")");
                }

                return false;

            case "kick":
                if (parts.Length < 2 || !ushort.TryParse(parts[1], out var id))
                {
                    log.Warn("Usage: kick <player id>  (see status for ids)");
                }
                else if (!server.Kick(id))
                {
                    log.Warn("No player with id " + id + ".");
                }

                return false;

            case "help":
                log.Info("Commands: status, kick <id>, quit");
                return false;

            default:
                log.Warn("Unknown command: " + parts[0] + ". Try help.");
                return false;
        }
    }

    private static ConcurrentQueue<string> StartConsoleReader()
    {
        var queue = new ConcurrentQueue<string>();
        var thread = new Thread(() =>
        {
            try
            {
                string? line;
                while ((line = Console.ReadLine()) != null) queue.Enqueue(line);
                // stdin closed (a service, or a container started without -i): keep serving, just no console.
            }
            catch (IOException)
            {
            }
        })
        {
            IsBackground = true,
            Name = "console-input",
        };
        thread.Start();
        return queue;
    }

    // docker stop and systemd send SIGTERM: treat it like Ctrl+C so players get a goodbye, not a timeout.
    private static IDisposable? TryRegisterSigterm()
    {
        try
        {
            return PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                _stopRequested = true;
            });
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static string ModVersion(out string buildInfo)
    {
        buildInfo = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = buildInfo.IndexOf('+');
        var version = plus > 0 ? buildInfo.Substring(0, plus) : buildInfo;
        if (plus > 0 && buildInfo.Length > plus + 8) buildInfo = buildInfo.Substring(0, plus + 8); // keep a short commit id
        return version;
    }
}
