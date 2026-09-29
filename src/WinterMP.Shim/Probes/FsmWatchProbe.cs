using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;

namespace WinterMP.Shim.Probes;

/// <summary>
/// Traces chosen FSMs step by step: every state they enter (with the int and string variables that
/// changed while the state's actions ran) and every event they receive (with the FSM and state that
/// sent it). An FSM is watched when its scene path contains one of the filter strings. Development
/// instrument for working out exactly what a piece of game logic does on load.
/// </summary>
internal static class FsmWatchProbe
{
    private const int MaxLines = 20000;

    private static readonly Dictionary<Fsm, string?> Paths = new Dictionary<Fsm, string?>();
    private static readonly Dictionary<Fsm, Dictionary<string, string>> LastValues = new Dictionary<Fsm, Dictionary<string, string>>();
    private static string[] _filters = new string[0];
    private static ILog? _log;
    private static int _lines;

    public static void Install(Harmony harmony, ILog log, string filterList)
    {
        _log = log;
        _filters = filterList.Split(',').Select(f => f.Trim()).Where(f => f.Length > 0).ToArray();
        if (_filters.Length == 0) return;
        var enter = AccessTools.Method(typeof(Fsm), "EnterState", new[] { typeof(FsmState) });
        var process = AccessTools.Method(typeof(Fsm), "ProcessEvent", new[] { typeof(FsmEvent), typeof(FsmEventData) });
        if (enter == null || process == null)
        {
            log.Warn("FSM watch: Fsm.EnterState or Fsm.ProcessEvent not found; this game build changed PlayMaker's API.");
            return;
        }

        harmony.Patch(enter, prefix: new HarmonyMethod(typeof(FsmWatchProbe), nameof(EnterPrefix)), postfix: new HarmonyMethod(typeof(FsmWatchProbe), nameof(EnterPostfix)));
        harmony.Patch(process, prefix: new HarmonyMethod(typeof(FsmWatchProbe), nameof(ProcessPrefix)));
        log.Info("FSM watch: tracing FSMs whose path contains " + string.Join(" | ", _filters) + ".");
    }

    private static void EnterPrefix(Fsm __instance, FsmState state)
    {
        try
        {
            var path = WatchedPath(__instance);
            if (path == null || state == null) return;
            Write(path + " :: " + __instance.Name + " > " + state.Name);
        }
        catch (Exception)
        {
            // Never let a diagnostic break the game's own state machine.
        }
    }

    private static void EnterPostfix(Fsm __instance, FsmState state)
    {
        try
        {
            var path = WatchedPath(__instance);
            if (path == null || state == null) return;
            var changes = Changes(__instance);
            if (changes.Length > 0) Write(path + " :: " + __instance.Name + " [" + state.Name + "] " + changes);
        }
        catch (Exception)
        {
        }
    }

    private static void ProcessPrefix(Fsm __instance, FsmEvent fsmEvent, FsmEventData eventData)
    {
        try
        {
            if (fsmEvent == null) return;
            var path = WatchedPath(__instance);
            if (path == null) return;
            var data = eventData ?? Fsm.EventData;
            var from = data?.SentByFsm == null ? "?" : data.SentByFsm.GameObjectName + "/" + data.SentByFsm.Name + "/" + (data.SentByState == null ? "?" : data.SentByState.Name);
            Write(path + " :: " + __instance.Name + " <- " + fsmEvent.Name + " from " + from);
        }
        catch (Exception)
        {
        }
    }

    private static string? WatchedPath(Fsm fsm)
    {
        string? path;
        if (Paths.TryGetValue(fsm, out path)) return path;
        var owner = fsm.GameObject;
        var full = owner == null ? fsm.GameObjectName : PathOf(owner.transform);
        path = _filters.Any(f => full.IndexOf(f, StringComparison.Ordinal) >= 0) ? full : null;
        Paths[fsm] = path;
        return path;
    }

    private static string PathOf(Transform transform)
    {
        var path = transform.name;
        for (var parent = transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
        return path;
    }

    // Ints and strings are where this game keeps ids, counters and save tags.
    private static string Changes(Fsm fsm)
    {
        Dictionary<string, string>? last;
        if (!LastValues.TryGetValue(fsm, out last))
        {
            last = new Dictionary<string, string>();
            LastValues[fsm] = last;
        }

        var text = new StringBuilder();
        foreach (var variable in fsm.Variables.IntVariables) Note(last, variable.Name, variable.Value.ToString(), text);
        foreach (var variable in fsm.Variables.StringVariables) Note(last, variable.Name, "\"" + variable.Value + "\"", text);
        return text.ToString();
    }

    private static void Note(Dictionary<string, string> last, string name, string value, StringBuilder text)
    {
        string? previous;
        if (last.TryGetValue(name, out previous) && previous == value) return;
        last[name] = value;
        if (text.Length > 0) text.Append(", ");
        text.Append(name).Append('=').Append(value);
    }

    private static void Write(string line)
    {
        if (_log == null || _lines >= MaxLines) return;
        _lines++;
        _log.Info("[watch " + Time.realtimeSinceStartup.ToString("0.00") + "] " + line + (_lines == MaxLines ? " (watch line limit reached)" : string.Empty));
    }
}
