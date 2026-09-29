using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using WinterMP.Core.Protocol;
using WinterMP.Shim.Probes;

namespace WinterMP.Shim;

[BepInPlugin(Guid, "WinterMP", PluginInfo.Version)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "gg.dsm.wintermp";

    private BepInExLog _log = null!;
    private Harmony? _harmony;
    private WorldProbe? _world;
    private SteamProbe _steam = null!;
    private SessionPanel _session = null!;
    private ConfigEntry<bool> _overlay = null!;
    private ConfigEntry<KeyCode> _connectKey = null!;
    private ConfigEntry<KeyCode> _steamKey = null!;
    private ConfigEntry<KeyCode> _eventsKey = null!;
    private string? _autoConnect;
    private bool _autoSteamProbe;
    private float _autoAt = -1f;
    private bool _autoDone;

    private void Awake()
    {
        _log = new BepInExLog(Logger);
        GameInfo.Detect(_log);
        Logger.LogInfo("WinterMP " + PluginInfo.Version + " (protocol " + ProtocolInfo.Version + ", shim " + GameInfo.Fingerprint(typeof(Plugin))
            + ", core " + GameInfo.Fingerprint(typeof(ProtocolInfo)) + ") on My Winter Car " + GameInfo.Build + ", Unity " + Application.unityVersion + ".");
        var args = Environment.GetCommandLineArgs();
        Logger.LogInfo("Launched with: " + string.Join(" ", args));
        // Scriptable test runs: join a server and/or run the Steam probe once a level has loaded.
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "-wintermp-connect" && i + 1 < args.Length) _autoConnect = args[i + 1];
            if (args[i] == "-wintermp-steam-probe") _autoSteamProbe = true;
        }

        var address = Config.Bind("Session", "ServerAddress", "127.0.0.1", "Dedicated server to join (host or host:port).");
        var name = Config.Bind("Session", "PlayerName", string.Empty, "Your name in sessions. Empty uses your Steam name.");
        _connectKey = Config.Bind("Session", "ConnectKey", KeyCode.F8, "Connect to or leave the server.");
        _overlay = Config.Bind("Debug", "ShowOverlay", true, "Show the session status line.");
        var eventProbe = Config.Bind("Debug", "FsmEventProbe", true, "Count PlayMaker FSM events (development).");
        var worldProbe = Config.Bind("Debug", "WorldProbe", true, "Log FSM, global and physics counts (development).");
        _eventsKey = Config.Bind("Debug", "FsmEventReportKey", KeyCode.F7, "Log the busiest FSM events so far.");
        _steamKey = Config.Bind("Debug", "SteamProbeKey", KeyCode.F10, "Run the Steam lobby and rich presence probe.");

        _harmony = new Harmony(Guid);
        if (eventProbe.Value) FsmEventProbe.Install(_harmony, _log);
        if (worldProbe.Value) _world = new WorldProbe(_log);
        _steam = new SteamProbe(_log);
        _steam.Listen();
        _session = new SessionPanel(_log, () => _autoConnect ?? address.Value, () => name.Value);
    }

    private void Update()
    {
        var now = Time.realtimeSinceStartup;
        if (Input.GetKeyDown(_connectKey.Value)) _session.Toggle();
        if (Input.GetKeyDown(_steamKey.Value)) _steam.Run();
        if (Input.GetKeyDown(_eventsKey.Value)) FsmEventProbe.LogTop(25);
        _session.Tick();
        _steam.Tick(now);
        _world?.Tick(now);
        FsmEventProbe.Tick(now);
        if (_autoAt >= 0f && now >= _autoAt)
        {
            _autoAt = -1f;
            if (_autoConnect != null) _session.Toggle();
            if (_autoSteamProbe) _steam.Run();
        }
    }

    private void OnGUI()
    {
        if (_overlay.Value) _session.OnGUI();
    }

    private void OnLevelWasLoaded(int level)
    {
        _world?.OnLevelLoaded(level);
        // One shot per process: a later level load must not toggle the session off again.
        if ((_autoConnect != null || _autoSteamProbe) && !_autoDone)
        {
            _autoDone = true;
            _autoAt = Time.realtimeSinceStartup + 5f;
        }
    }

    private void OnApplicationQuit() => _session.Close();
}
