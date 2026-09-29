using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using HutongGames.PlayMaker;
using WinterMP.Core.Diagnostics;

namespace WinterMP.Shim.Probes;

/// <summary>
/// Counts every event any PlayMaker FSM processes, via one Harmony prefix on Fsm.ProcessEvent (the
/// funnel all event delivery passes through). Development instrument: it shows which parts of the
/// game are chatty, which is what sizes the job of keeping FSMs in sync between players.
/// </summary>
internal static class FsmEventProbe
{
    private const int MaxTrackedPairs = 8000;
    private const float ReportEverySeconds = 60f;

    private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
    private static ILog? _log;
    private static long _total;
    private static long _totalAtLastReport;
    private static float _nextReport;

    public static bool Installed { get; private set; }

    public static void Install(Harmony harmony, ILog log)
    {
        _log = log;
        var target = AccessTools.Method(typeof(Fsm), "ProcessEvent", new[] { typeof(FsmEvent), typeof(FsmEventData) });
        if (target == null)
        {
            log.Warn("FSM event probe: Fsm.ProcessEvent not found; this game build changed PlayMaker's API.");
            return;
        }

        harmony.Patch(target, prefix: new HarmonyMethod(typeof(FsmEventProbe), nameof(Prefix)));
        Installed = true;
        log.Info("FSM event probe: hooked Fsm.ProcessEvent.");
    }

    // Harmony binds arguments by name: __instance is the receiving Fsm, fsmEvent the event.
    private static void Prefix(Fsm __instance, FsmEvent fsmEvent)
    {
        if (fsmEvent == null) return;
        _total++;
        try
        {
            var key = __instance.GameObjectName + "/" + __instance.Name + " <- " + fsmEvent.Name;
            int count;
            if (Counts.TryGetValue(key, out count)) Counts[key] = count + 1;
            else if (Counts.Count < MaxTrackedPairs) Counts[key] = 1;
        }
        catch (Exception)
        {
            // Never let a diagnostic break the game's own event delivery.
        }
    }

    /// <summary>Logs a traffic summary at most once a minute. Call every frame.</summary>
    public static void Tick(float now)
    {
        if (!Installed || _log == null || now < _nextReport) return;
        var fresh = _total - _totalAtLastReport;
        if (_nextReport > 0f) _log.Info("FSM events: " + fresh + " in the last " + ReportEverySeconds + " s (" + _total + " total, " + Counts.Count + " distinct object/fsm/event triples).");
        _totalAtLastReport = _total;
        _nextReport = now + ReportEverySeconds;
    }

    /// <summary>The busiest triples since the game started, for the log or a census file.</summary>
    public static IEnumerable<string> Top(int count) =>
        Counts.OrderByDescending(pair => pair.Value).Take(count).Select(pair => pair.Value + "\t" + pair.Key);

    public static void LogTop(int count)
    {
        if (_log == null) return;
        _log.Info("Busiest FSM events so far (count, object/fsm <- event):");
        foreach (var line in Top(count)) _log.Info("  " + line);
    }
}
