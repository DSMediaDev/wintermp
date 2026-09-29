namespace WinterMP.Core.Diagnostics;

/// <summary>
/// The logging seam. Core cannot depend on BepInEx or Microsoft.Extensions.Logging, so each host
/// (the in-game plugin, the dedicated server, tests) adapts this to its own sink.
/// </summary>
public interface ILog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

/// <summary>Discards everything. Handy for tests that do not care about log output.</summary>
public sealed class NullLog : ILog
{
    public static readonly NullLog Instance = new NullLog();

    private NullLog() { }

    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message) { }
}
