using WinterMP.Core.Diagnostics;

namespace WinterMP.Server;

/// <summary>Timestamped console logging. Thread-safe enough for one main loop plus the input thread.</summary>
internal sealed class ConsoleLog : ILog
{
    private readonly object _gate = new object();

    public void Info(string message) => Write("INFO", message, null);
    public void Warn(string message) => Write("WARN", message, ConsoleColor.Yellow);
    public void Error(string message) => Write("FAIL", message, ConsoleColor.Red);

    private void Write(string level, string message, ConsoleColor? colour)
    {
        lock (_gate)
        {
            var previous = Console.ForegroundColor;
            if (colour.HasValue) Console.ForegroundColor = colour.Value;
            Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + level + " " + message);
            if (colour.HasValue) Console.ForegroundColor = previous;
        }
    }
}
