using WinterMP.Core.Protocol;

namespace WinterMP.Server;

/// <summary>Command-line options. Deliberately tiny: flags in, a settings object out, errors in plain words.</summary>
internal sealed class ServerArgs
{
    public int Port { get; private set; } = ProtocolInfo.DefaultPort;
    public string Name { get; private set; } = "WinterMP Server";
    public int MaxPlayers { get; private set; } = 8;
    public string? GameBuild { get; private set; }
    public bool ShowHelp { get; private set; }

    public static string Usage =>
        "Usage: WinterMP.Server [options]\n" +
        "  --port <n>          UDP port to listen on (default " + ProtocolInfo.DefaultPort + ")\n" +
        "  --name <text>       Server name shown to players\n" +
        "  --max-players <n>   Player cap, 1 to 64 (default 8)\n" +
        "  --game-build <ver>  Require this exact game build (default: the first player sets it)\n" +
        "  --help              Show this help\n" +
        "Console commands while running: status, kick <id>, quit";

    public static ServerArgs Parse(string[] args, out string? error)
    {
        var parsed = new ServerArgs();
        error = null;
        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];
            string Next()
            {
                if (i + 1 >= args.Length) throw new ArgumentException(flag + " needs a value.");
                return args[++i];
            }

            try
            {
                switch (flag)
                {
                    case "--port":
                        parsed.Port = ParseInt(Next(), 1, 65535, flag);
                        break;
                    case "--name":
                        parsed.Name = Next();
                        break;
                    case "--max-players":
                        parsed.MaxPlayers = ParseInt(Next(), 1, 64, flag);
                        break;
                    case "--game-build":
                        parsed.GameBuild = Next();
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        parsed.ShowHelp = true;
                        break;
                    default:
                        throw new ArgumentException("Unknown option: " + flag);
                }
            }
            catch (ArgumentException e)
            {
                error = e.Message;
                return parsed;
            }
        }

        return parsed;
    }

    private static int ParseInt(string text, int min, int max, string flag)
    {
        if (!int.TryParse(text, out var value) || value < min || value > max)
        {
            throw new ArgumentException(flag + " must be a number from " + min + " to " + max + ".");
        }

        return value;
    }
}
