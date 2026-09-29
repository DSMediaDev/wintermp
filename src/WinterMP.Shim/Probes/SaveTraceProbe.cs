using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using WinterMP.Core.Diagnostics;

namespace WinterMP.Shim.Probes;

/// <summary>
/// Logs every file operation the game's save library (Easy Save 2) performs: the first time each
/// operation touches each path, then a count summary on request. Development instrument: it shows
/// which save files a play session reads and writes, and whether any of them escape a redirect.
/// Hooked by type name so the Shim needs no compile-time reference to the save library.
/// </summary>
internal static class SaveTraceProbe
{
    private static readonly string[] FileOps = { "Delete", "Exists", "Move", "ReadAllBytes", "CreateFileStream" };
    private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
    private static ILog? _log;
    private static string _nativeFolder = string.Empty;

    public static void Install(Harmony harmony, ILog log)
    {
        _log = log;
        _nativeFolder = Application.persistentDataPath.Replace('\\', '/');
        var utility = AccessTools.TypeByName("ES2FileUtility");
        if (utility == null)
        {
            log.Warn("Save trace: ES2FileUtility not found; the save library changed.");
            return;
        }

        var hooked = new List<string>();
        var prefix = new HarmonyMethod(typeof(SaveTraceProbe), nameof(Prefix));
        foreach (var method in utility.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (Array.IndexOf(FileOps, method.Name) < 0) continue;
            harmony.Patch(method, prefix: prefix);
            hooked.Add(method.Name);
        }

        log.Info("Save trace: hooked ES2FileUtility." + string.Join(", ", hooked.ToArray()) + "; native save folder is " + _nativeFolder + ".");
    }

    // Harmony passes the patched method and its arguments; every hooked op takes the path first.
    private static void Prefix(MethodBase __originalMethod, object[] __args)
    {
        if (__args.Length == 0 || !(__args[0] is string path)) return;
        try
        {
            var op = __originalMethod.Name;
            if (op == "CreateFileStream" && __args.Length > 1) op += "(" + __args[1] + ")";
            if (op == "Move" && __args.Length > 1) path += " -> " + __args[1];
            var key = op + " " + path.Replace('\\', '/');
            Counts.TryGetValue(key, out var count);
            Counts[key] = count + 1;
            if (count == 0)
            {
                var where = key.IndexOf(_nativeFolder, StringComparison.OrdinalIgnoreCase) >= 0 ? " [NATIVE FOLDER]" : string.Empty;
                _log?.Info("Save trace: " + key + where + " (level " + Application.loadedLevel + ", frame " + Time.frameCount + ")");
            }
        }
        catch (Exception e)
        {
            _log?.Warn("Save trace failed: " + e.Message);
        }
    }

    public static void LogSummary()
    {
        if (_log == null) return;
        var keys = new List<string>(Counts.Keys);
        keys.Sort(StringComparer.Ordinal);
        _log.Info("Save trace summary: " + keys.Count + " distinct operations.");
        foreach (var key in keys) _log.Info("  " + Counts[key] + " x " + key);
    }
}
