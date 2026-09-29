using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;

namespace WinterMP.Shim;

/// <summary>
/// Saves the world in the middle of a session without leaving it broken. The game only ever saves
/// right before it reloads the level, so SAVEGAME sends every listening FSM into a save state and
/// many of those states have no way out. This broadcasts SAVEGAME, notes which FSMs received it and
/// the state each was in at that moment, and then puts each one back: at once when its save state is
/// a dead end, or after a short settle time when its save chain may still be running. An FSM that
/// passes back through the state it was in is only watched from then on: some save chains run through
/// it and on into a dead end (those are put back), others carry on normally (those are left alone).
/// </summary>
internal sealed class SaveCapture
{
    /// <summary>How long a save chain may keep running before its FSM is put back anyway.</summary>
    public const float SettleSeconds = 2f;

    private const string SaveEvent = "SAVEGAME";

    private static SaveCapture? _capturing;

    private readonly ILog _log;
    private readonly Action<Fsm, FsmState> _switchState;
    private readonly Dictionary<Fsm, FsmState> _pending = new Dictionary<Fsm, FsmState>();
    private readonly Dictionary<Fsm, FsmState> _watched = new Dictionary<Fsm, FsmState>();
    private bool _broadcasting;
    private float _startedAt;
    private float _settleBy;
    private SaveCaptureResult? _result;
    private Action<SaveCaptureResult>? _done;

    private SaveCapture(ILog log, Action<Fsm, FsmState> switchState)
    {
        _log = log;
        _switchState = switchState;
    }

    public bool Busy => _result != null;

    public static SaveCapture? Create(Harmony harmony, ILog log)
    {
        var process = AccessTools.Method(typeof(Fsm), "ProcessEvent", new[] { typeof(FsmEvent), typeof(FsmEventData) });
        var enter = AccessTools.Method(typeof(Fsm), "EnterState", new[] { typeof(FsmState) });
        var switchState = AccessTools.Method(typeof(Fsm), "SwitchState", new[] { typeof(FsmState) });
        if (process == null || enter == null || switchState == null)
        {
            log.Warn("Save capture: Fsm.ProcessEvent, EnterState or SwitchState not found; this game build changed PlayMaker's API. In-session saves are off.");
            return null;
        }

        harmony.Patch(process, prefix: new HarmonyMethod(typeof(SaveCapture), nameof(ProcessPrefix)));
        harmony.Patch(enter, prefix: new HarmonyMethod(typeof(SaveCapture), nameof(EnterPrefix)));
        return new SaveCapture(log, AccessTools.MethodDelegate<Action<Fsm, FsmState>>(switchState));
    }

    /// <summary>
    /// Saves the world now. The game freezes for the length of the save. <paramref name="done"/> runs
    /// once every FSM the save moved has been put back (up to <see cref="SettleSeconds"/> later).
    /// </summary>
    public bool Capture(Action<SaveCaptureResult>? done = null)
    {
        if (Busy)
        {
            _log.Warn("Save capture: a save is still settling; ignoring the new request.");
            return false;
        }

        _result = new SaveCaptureResult { Frame = Time.frameCount };
        _done = done;
        _pending.Clear();
        _watched.Clear();
        _capturing = this;
        _broadcasting = true;
        var clock = Stopwatch.StartNew();
        try
        {
            PlayMakerFSM.BroadcastEvent(SaveEvent);
        }
        finally
        {
            _broadcasting = false;
            clock.Stop();
        }

        _result.SaveMilliseconds = clock.ElapsedMilliseconds;
        // Every FSM hears the broadcast; only the ones that moved need anything from us.
        var unmoved = new List<Fsm>();
        foreach (var pair in _pending)
        {
            if (pair.Key.ActiveState == pair.Value) unmoved.Add(pair.Key);
        }

        foreach (var fsm in unmoved) _pending.Remove(fsm);
        _result.Moved = _pending.Count + _watched.Count;
        _startedAt = Time.realtimeSinceStartup;
        _settleBy = _startedAt + SettleSeconds;
        RestoreParked(_pending);
        RestoreParked(_watched);
        if (_pending.Count == 0 && _watched.Count == 0) Finish();
        return true;
    }

    /// <summary>Call every frame.</summary>
    public void Tick()
    {
        if (!Busy) return;
        if (Time.realtimeSinceStartup >= _settleBy)
        {
            // Out of the pending set first, so our own switch back is not mistaken for the FSM finding its way.
            RestoreParked(_watched);
            _result!.FoundTheirWay = _watched.Count;
            _watched.Clear();
            var late = new List<KeyValuePair<Fsm, FsmState>>(_pending);
            _pending.Clear();
            foreach (var pair in late) Restore(pair.Key, pair.Value, late: true);
        }
        else
        {
            RestoreParked(_pending);
            RestoreParked(_watched);
        }

        if (_pending.Count == 0 && _watched.Count == 0) Finish();
    }

    // Puts back every FSM in the set whose current state has nowhere to go.
    private void RestoreParked(Dictionary<Fsm, FsmState> set)
    {
        List<Fsm>? parked = null;
        foreach (var pair in set)
        {
            var state = pair.Key.ActiveState;
            if (state == null || state.Transitions == null || state.Transitions.Length == 0) (parked ??= new List<Fsm>()).Add(pair.Key);
        }

        if (parked == null) return;
        foreach (var fsm in parked)
        {
            var before = set[fsm];
            set.Remove(fsm);
            Restore(fsm, before, late: false);
        }
    }

    private void Restore(Fsm fsm, FsmState before, bool late)
    {
        try
        {
            var from = fsm.ActiveState;
            if (from == before) return;
            _result!.Restored.Add(fsm.GameObjectName + "/" + fsm.Name + ": " + (from == null ? "?" : from.Name) + " -> " + before.Name + (late ? " (after settling)" : string.Empty));
            _switchState(fsm, before);
            if (late) _result!.RestoredLate++;
            else _result!.RestoredParked++;
        }
        catch (Exception exception)
        {
            _result!.Failed++;
            if (_result.Failed <= 5) _log.Warn("Save capture: could not put " + fsm.GameObjectName + "/" + fsm.Name + " back in " + before.Name + ": " + exception.Message);
        }
    }

    private void Finish()
    {
        var result = _result!;
        _result = null;
        _capturing = null;
        result.SettleSeconds = Time.realtimeSinceStartup - _startedAt;
        _log.Info("Save capture: saved in " + result.SaveMilliseconds + " ms; " + result.Heard + " FSMs heard it, " + result.Moved + " moved, " + result.RestoredParked + " put back at once, "
            + result.RestoredLate + " after settling, " + result.FoundTheirWay + " found their own way back, " + result.Failed + " failed.");
        var done = _done;
        _done = null;
        done?.Invoke(result);
    }

    // Runs before the event is handled, so ActiveState is still the state the FSM was in before the save.
    private static void ProcessPrefix(Fsm __instance, FsmEvent fsmEvent)
    {
        var capture = _capturing;
        if (capture == null || !capture._broadcasting || fsmEvent == null) return;
        try
        {
            if (fsmEvent.Name != SaveEvent) return;
            var state = __instance.ActiveState;
            if (state == null || capture._pending.ContainsKey(__instance)) return;
            capture._pending[__instance] = state;
            capture._result!.Heard++;
        }
        catch (Exception)
        {
            // Never let bookkeeping break the save itself.
        }
    }

    // An FSM that re-enters its pre-save state may be fine, or may be passing through on its way to a
    // dead end; from here on it is only put back if it parks.
    private static void EnterPrefix(Fsm __instance, FsmState state)
    {
        var capture = _capturing;
        if (capture == null || state == null) return;
        try
        {
            FsmState before;
            if (!capture._pending.TryGetValue(__instance, out before) || before != state) return;
            capture._pending.Remove(__instance);
            capture._watched[__instance] = before;
        }
        catch (Exception)
        {
        }
    }
}

/// <summary>What one in-session save did.</summary>
internal sealed class SaveCaptureResult
{
    public int Frame;
    public long SaveMilliseconds;
    public int Heard;
    public int Moved;
    public int RestoredParked;
    public int RestoredLate;
    public int FoundTheirWay;
    public int Failed;
    public float SettleSeconds;

    /// <summary>One line per FSM put back: where, from which state, to which.</summary>
    public readonly List<string> Restored = new List<string>();
}
