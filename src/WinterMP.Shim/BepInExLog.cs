using BepInEx.Logging;
using WinterMP.Core.Diagnostics;

namespace WinterMP.Shim;

/// <summary>Routes Core's logging into BepInEx's log (console + LogOutput.log).</summary>
internal sealed class BepInExLog : ILog
{
    private readonly ManualLogSource _source;

    public BepInExLog(ManualLogSource source)
    {
        _source = source;
    }

    public void Info(string message) => _source.LogInfo(message);
    public void Warn(string message) => _source.LogWarning(message);
    public void Error(string message) => _source.LogError(message);
}
