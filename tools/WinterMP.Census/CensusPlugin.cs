using System;
using System.Diagnostics;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace WinterMP.Census;

/// <summary>
/// Writes a census of each level a few seconds after it loads (and on demand with F9).
/// Launch flag "-wintermp-census-exit-after N" ends the process right after censusing level N,
/// so a whole run can be scripted. The process is killed rather than quit on purpose: nothing
/// in the game gets a chance to save on the way out.
/// </summary>
[BepInPlugin("gg.dsm.wintermp.census", "WinterMP Census", "0.0.1")]
public sealed class CensusPlugin : BaseUnityPlugin
{
    private ConfigEntry<float> _delay = null!;
    private ConfigEntry<KeyCode> _key = null!;
    private int _exitAfterLevel = -1;
    private float _dueAt = -1f;
    private int _dueLevel = -1;
    private string _runFolder = string.Empty;

    private void Awake()
    {
        _delay = Config.Bind("Census", "DelaySeconds", 12f, "Seconds to wait after a level loads before the census, so FSMs can start.");
        _key = Config.Bind("Census", "Hotkey", KeyCode.F9, "Take a census of the current level now.");
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-wintermp-census-exit-after") int.TryParse(args[i + 1], out _exitAfterLevel);
        }

        _runFolder = Path.Combine(Path.Combine(Paths.BepInExRootPath, "census"), DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Logger.LogInfo("Census ready: output under " + _runFolder + (_exitAfterLevel >= 0 ? ", exiting after level " + _exitAfterLevel : string.Empty) + ".");
    }

    private void OnLevelWasLoaded(int level)
    {
        _dueLevel = level;
        _dueAt = Time.realtimeSinceStartup + _delay.Value;
    }

    private void Update()
    {
        if (Input.GetKeyDown(_key.Value)) Take(Application.loadedLevel);
        if (_dueAt < 0f || Time.realtimeSinceStartup < _dueAt) return;
        _dueAt = -1f;
        Take(_dueLevel);
        if (_dueLevel == _exitAfterLevel)
        {
            Logger.LogInfo("Census: exit-after level reached; ending the process without saving.");
            Process.GetCurrentProcess().Kill();
        }
    }

    private void Take(int level)
    {
        var folder = Path.Combine(_runFolder, "level" + level + "-" + Application.loadedLevelName);
        var clock = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(folder);
            var summary = new CensusWriter(folder).WriteAll();
            Logger.LogInfo("Census of level " + level + " (" + Application.loadedLevelName + ") written in " + clock.ElapsedMilliseconds + " ms: " + summary + " -> " + folder);
        }
        catch (Exception e)
        {
            Logger.LogError("Census failed: " + e);
        }
    }
}
