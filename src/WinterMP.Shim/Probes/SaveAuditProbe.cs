using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;

namespace WinterMP.Shim.Probes;

/// <summary>
/// Measures what an in-session save costs and what else it sets off. Once the game level is up it runs
/// a fixed schedule: a control window with no save, then an audited save (every event delivered and
/// every state entered while it runs and for a few seconds after, plus a before/after snapshot of every
/// FSM's state), then a second save with the recorders off to time it cleanly. Comparing the audited
/// window against the control window separates the save's side effects from the world's normal churn.
/// Results go to plain files in the chosen folder, not the log (the log is buffered and lost on a kill).
/// With a save capture given, saves go through it (so every FSM the save moved is put back), and a later
/// window shows whether the world came back to life. Without one, saves are bare broadcasts.
/// </summary>
internal sealed class SaveAuditProbe
{
    private const float ControlAt = 30f;
    private const float AuditedSaveAt = 45f;
    private const float PostAt = 55f;
    private const float PostSeconds = 20f;
    private const float CleanSaveAt = 80f;
    private const float WindowSeconds = 8f;
    private const float SlowFrameSeconds = 0.1f;

    private static readonly Dictionary<Fsm, string> Paths = new Dictionary<Fsm, string>();
    private static Dictionary<string, int>? _events;
    private static Dictionary<string, int>? _entered;

    private readonly ILog _log;
    private readonly string _folder;
    private readonly SaveCapture? _capture;
    private readonly StringBuilder _summary = new StringBuilder();
    private readonly StringBuilder _frames = new StringBuilder("time\tframe\tdelta_ms\n");
    private float _startedAt = -1f;
    private int _step;
    private string? _window;
    private float _windowEndsAt;
    private List<string>? _before;

    private SaveAuditProbe(ILog log, string folder, SaveCapture? capture)
    {
        _log = log;
        _folder = folder;
        _capture = capture;
    }

    public static SaveAuditProbe? Create(Harmony harmony, ILog log, string folder, SaveCapture? capture)
    {
        var process = AccessTools.Method(typeof(Fsm), "ProcessEvent", new[] { typeof(FsmEvent), typeof(FsmEventData) });
        var enter = AccessTools.Method(typeof(Fsm), "EnterState", new[] { typeof(FsmState) });
        if (process == null || enter == null)
        {
            log.Warn("Save audit: Fsm.ProcessEvent or Fsm.EnterState not found; this game build changed PlayMaker's API.");
            return null;
        }

        Directory.CreateDirectory(folder);
        harmony.Patch(process, prefix: new HarmonyMethod(typeof(SaveAuditProbe), nameof(ProcessPrefix)));
        harmony.Patch(enter, prefix: new HarmonyMethod(typeof(SaveAuditProbe), nameof(EnterPrefix)));
        log.Info("Save audit: results go to " + folder + (capture != null ? ", saving through save capture." : ", saving with bare broadcasts."));
        return new SaveAuditProbe(log, folder, capture);
    }

    /// <summary>Starts the schedule. Call when the game level has loaded.</summary>
    public void Start(float now)
    {
        if (_startedAt >= 0f) return;
        _startedAt = now;
        Note("Game level up at " + now.ToString("0.00") + " s.");
    }

    /// <summary>Call every frame.</summary>
    public void Tick(float now)
    {
        if (_startedAt < 0f) return;
        var delta = Time.unscaledDeltaTime;
        if (delta >= SlowFrameSeconds) _frames.Append(now.ToString("0.000")).Append('\t').Append(Time.frameCount).Append('\t').Append((delta * 1000f).ToString("0")).Append('\n');
        if (_window != null && now >= _windowEndsAt) EndWindow();
        var elapsed = now - _startedAt;
        if (_step == 0 && elapsed >= ControlAt)
        {
            _step = 1;
            BeginWindow("control", now);
        }
        else if (_step == 1 && _window == null && elapsed >= AuditedSaveAt)
        {
            _step = 2;
            BeginWindow("save", now);
            Save("audited save");
        }
        else if (_step == 2 && _window == null && _capture != null && elapsed >= PostAt)
        {
            _step = 3;
            BeginWindow("post", now);
            // The world has loops of 10 s and more; a short window misses them and reads as dead.
            _windowEndsAt = now + PostSeconds;
        }
        else if (_step == 2 && _capture == null)
        {
            _step = 3;
        }
        else if (_step == 3 && _window == null && elapsed >= CleanSaveAt)
        {
            _step = 4;
            Screenshot("clean-before");
            Save("clean save");
            Flush();
        }
        else if (_step == 4 && elapsed >= CleanSaveAt + 3f)
        {
            _step = 5;
            Screenshot("clean-after");
            Note("Schedule complete.");
            Flush();
        }
    }

    private void Save(string label)
    {
        var frame = Time.frameCount;
        if (_capture != null)
        {
            var started = _capture.Capture(result =>
            {
                Write("restored-" + result.Frame + ".tsv", result.Restored.OrderBy(line => line, StringComparer.Ordinal));
                Note(label + ": saved in " + result.SaveMilliseconds + " ms on frame " + result.Frame + "; " + result.Heard + " FSMs heard it, " + result.Moved + " moved, "
                    + result.RestoredParked + " put back at once, " + result.RestoredLate + " after settling, " + result.FoundTheirWay + " found their own way back, "
                    + result.Failed + " failed; settled in " + result.SettleSeconds.ToString("0.00") + " s.");
                Flush();
            });
            Note(label + (started ? ": saved; parked FSMs put back, the rest settling." : ": save capture was busy, nothing saved."));
            return;
        }

        var clock = Stopwatch.StartNew();
        PlayMakerFSM.BroadcastEvent("SAVEGAME");
        clock.Stop();
        Note(label + ": SAVEGAME broadcast took " + clock.ElapsedMilliseconds + " ms on frame " + frame + " (level " + Application.loadedLevelName + ").");
    }

    private void BeginWindow(string name, float now)
    {
        Screenshot(name + "-before");
        _before = Snapshot();
        _events = new Dictionary<string, int>();
        _entered = new Dictionary<string, int>();
        _window = name;
        _windowEndsAt = now + WindowSeconds;
        Note("Window " + name + " opened at frame " + Time.frameCount + ".");
    }

    private void EndWindow()
    {
        var name = _window!;
        var after = Snapshot();
        Write(name + "-states-before.tsv", _before!);
        Write(name + "-states-after.tsv", after);
        Write(name + "-events.tsv", Sorted(_events!));
        Write(name + "-entered.tsv", Sorted(_entered!));
        var changed = after.Except(_before!).Count();
        Note("Window " + name + " closed at frame " + Time.frameCount + ": " + _events!.Values.Sum() + " events, " + _entered!.Values.Sum()
            + " state entries, " + changed + " FSMs in a different state or activity than before.");
        Screenshot(name + "-after");
        _window = null;
        _events = null;
        _entered = null;
        _before = null;
        Flush();
    }

    // One line per FSM: where it lives, its name, its current state, and whether it is running at all.
    private static List<string> Snapshot()
    {
        var lines = new List<string>(PlayMakerFSM.FsmList.Count);
        foreach (var component in PlayMakerFSM.FsmList)
        {
            try
            {
                var fsm = component.Fsm;
                var live = component.enabled && component.gameObject.activeInHierarchy ? "on" : "off";
                lines.Add(PathOf(fsm) + "\t" + fsm.Name + "\t" + fsm.ActiveStateName + "\t" + live);
            }
            catch (Exception)
            {
                // An FSM torn down mid-snapshot is not worth failing the audit over.
            }
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private static void ProcessPrefix(Fsm __instance, FsmEvent fsmEvent)
    {
        if (_events == null || fsmEvent == null) return;
        try
        {
            Count(_events, PathOf(__instance) + "\t" + __instance.Name + "\t" + fsmEvent.Name);
        }
        catch (Exception)
        {
            // Never let a diagnostic break the game's own event delivery.
        }
    }

    private static void EnterPrefix(Fsm __instance, FsmState state)
    {
        if (_entered == null || state == null) return;
        try
        {
            Count(_entered, PathOf(__instance) + "\t" + __instance.Name + "\t" + state.Name);
        }
        catch (Exception)
        {
        }
    }

    private static void Count(Dictionary<string, int> counts, string key)
    {
        int count;
        counts.TryGetValue(key, out count);
        counts[key] = count + 1;
    }

    private static string PathOf(Fsm fsm)
    {
        string path;
        if (Paths.TryGetValue(fsm, out path)) return path;
        var owner = fsm.GameObject;
        if (owner == null) path = fsm.GameObjectName;
        else
        {
            path = owner.transform.name;
            for (var parent = owner.transform.parent; parent != null; parent = parent.parent) path = parent.name + "/" + path;
        }

        Paths[fsm] = path;
        return path;
    }

    private static IEnumerable<string> Sorted(Dictionary<string, int> counts) =>
        counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value + "\t" + pair.Key);

    private void Screenshot(string name) => Application.CaptureScreenshot(Path.Combine(_folder, name + ".png"));

    private void Note(string line)
    {
        _summary.Append(Time.realtimeSinceStartup.ToString("0.00")).Append(" s  ").Append(line).Append('\n');
        _log.Info("Save audit: " + line);
    }

    private void Write(string file, IEnumerable<string> lines) => File.WriteAllText(Path.Combine(_folder, file), string.Join("\n", lines.ToArray()) + "\n");

    private void Flush()
    {
        File.WriteAllText(Path.Combine(_folder, "summary.txt"), _summary.ToString());
        File.WriteAllText(Path.Combine(_folder, "slow-frames.tsv"), _frames.ToString());
    }
}
