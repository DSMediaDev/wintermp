using System.Linq;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;

namespace WinterMP.Shim.Probes;

/// <summary>
/// Scene-level vital signs: how many FSMs, globals and physics objects exist, and what is moving.
/// Cheap enough to leave on in development builds.
/// </summary>
internal sealed class WorldProbe
{
    private const float MoversEverySeconds = 30f;
    private readonly ILog _log;
    private float _nextMovers;

    public WorldProbe(ILog log)
    {
        _log = log;
    }

    public void OnLevelLoaded(int level)
    {
        var live = PlayMakerFSM.FsmList.Count;
        var all = Resources.FindObjectsOfTypeAll(typeof(PlayMakerFSM)).Length;
        var bodies = Object.FindObjectsOfType(typeof(Rigidbody)).Length;
        _log.Info("Level " + level + " (" + Application.loadedLevelName + ") loaded: " + live + " live FSMs (" + all + " incl. inactive), " + bodies + " active rigidbodies.");

        var globals = FsmVariables.GlobalVariables;
        if (globals != null)
        {
            _log.Info("PlayMaker globals: " + globals.FloatVariables.Length + " float, " + globals.IntVariables.Length + " int, "
                + globals.BoolVariables.Length + " bool, " + globals.StringVariables.Length + " string, "
                + globals.GameObjectVariables.Length + " GameObject, " + globals.GetAllNamedVariables().Length + " total.");
        }

        _nextMovers = Time.realtimeSinceStartup + 5f;
    }

    /// <summary>Every 30 s: which rigidbodies are actually moving. Call every frame.</summary>
    public void Tick(float now)
    {
        if (now < _nextMovers) return;
        _nextMovers = now + MoversEverySeconds;
        var awake = Object.FindObjectsOfType(typeof(Rigidbody)).Cast<Rigidbody>()
            .Where(body => !body.isKinematic && !body.IsSleeping()).ToList();
        var fastest = awake.OrderByDescending(body => body.velocity.sqrMagnitude).Take(3)
            .Select(body => body.name + " @ " + body.position.ToString("F1") + " " + body.velocity.magnitude.ToString("F1") + " m/s");
        _log.Info("Physics: " + awake.Count + " awake rigidbodies. Fastest: " + string.Join("; ", fastest.ToArray()));
    }
}
