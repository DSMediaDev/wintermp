using System;
using System.Diagnostics;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using HutongGames.PlayMaker;
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
    private SaveRedirect? _saveRedirect;
    private ConfigEntry<KeyCode> _saveTraceKey = null!;
    private bool _autoContinue;
    private float _continueAt = -1f;
    private float _exitAfterSeconds = -1f;
    private float _exitAt = -1f;
    private float _saveGameAfterSeconds = -1f;
    private float _saveGameAt = -1f;
    private string? _watch;
    private string? _saveAuditFolder;
    private bool _saveAuditRestore;
    private SaveAuditProbe? _saveAudit;

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
            if (args[i] == "-wintermp-save-folder" && i + 1 < args.Length) _saveRedirect = SaveRedirect.Create(_log, args[i + 1]);
            if (args[i] == "-wintermp-auto-continue") _autoContinue = true;
            if (args[i] == "-wintermp-savegame-after-seconds" && i + 1 < args.Length) float.TryParse(args[i + 1], out _saveGameAfterSeconds);
            if (args[i] == "-wintermp-exit-after-seconds" && i + 1 < args.Length) float.TryParse(args[i + 1], out _exitAfterSeconds);
            if (args[i] == "-wintermp-watch" && i + 1 < args.Length) _watch = args[i + 1];
            if (args[i] == "-wintermp-save-audit" && i + 1 < args.Length) _saveAuditFolder = args[i + 1];
            if (args[i] == "-wintermp-save-restore") _saveAuditRestore = true;
        }

        var address = Config.Bind("Session", "ServerAddress", "127.0.0.1", "Dedicated server to join (host or host:port).");
        var name = Config.Bind("Session", "PlayerName", string.Empty, "Your name in sessions. Empty uses your Steam name.");
        _connectKey = Config.Bind("Session", "ConnectKey", KeyCode.F8, "Connect to or leave the server.");
        _overlay = Config.Bind("Debug", "ShowOverlay", true, "Show the session status line.");
        var eventProbe = Config.Bind("Debug", "FsmEventProbe", true, "Count PlayMaker FSM events (development).");
        var worldProbe = Config.Bind("Debug", "WorldProbe", true, "Log FSM, global and physics counts (development).");
        _eventsKey = Config.Bind("Debug", "FsmEventReportKey", KeyCode.F7, "Log the busiest FSM events so far.");
        _steamKey = Config.Bind("Debug", "SteamProbeKey", KeyCode.F10, "Run the Steam lobby and rich presence probe.");
        var saveTrace = Config.Bind("Debug", "SaveTrace", true, "Log every save file the game reads or writes (development).");
        _saveTraceKey = Config.Bind("Debug", "SaveTraceReportKey", KeyCode.F6, "Log a summary of save file operations so far.");

        _harmony = new Harmony(Guid);
        if (eventProbe.Value) FsmEventProbe.Install(_harmony, _log);
        if (saveTrace.Value) SaveTraceProbe.Install(_harmony, _log);
        if (_watch != null) FsmWatchProbe.Install(_harmony, _log, _watch);
        if (_saveAuditFolder != null) _saveAudit = SaveAuditProbe.Create(_harmony, _log, _saveAuditFolder, _saveAuditRestore);
        _saveRedirect?.InstallFixedNames(_harmony);
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
        if (Input.GetKeyDown(_saveTraceKey.Value)) SaveTraceProbe.LogSummary();
        _saveRedirect?.Enforce();
        _session.Tick();
        _steam.Tick(now);
        _world?.Tick(now);
        FsmEventProbe.Tick(now);
        _saveAudit?.Tick(now);
        if (_autoAt >= 0f && now >= _autoAt)
        {
            _autoAt = -1f;
            if (_autoConnect != null) _session.Toggle();
            if (_autoSteamProbe) _steam.Run();
        }

        if (_continueAt >= 0f && now >= _continueAt)
        {
            _continueAt = -1f;
            PressContinue();
        }

        if (_saveGameAt >= 0f && now >= _saveGameAt)
        {
            // What a toilet does, minus the trip back to the main menu that follows it.
            _saveGameAt = -1f;
            Logger.LogInfo("Broadcasting SAVEGAME in session (level " + Application.loadedLevelName + ").");
            PlayMakerFSM.BroadcastEvent("SAVEGAME");
            Logger.LogInfo("SAVEGAME broadcast returned.");
        }

        if (_exitAt >= 0f && now >= _exitAt)
        {
            // Killed, not quit: nothing in the game gets a chance to save on the way out.
            SaveTraceProbe.LogSummary();
            Logger.LogInfo("Exit timer reached; ending the process without saving.");
            Process.GetCurrentProcess().Kill();
        }
    }

    // The main menu's Continue button is an FSM; entering its load state is what a click does.
    private void PressContinue()
    {
        var button = PlayMakerFSM.FsmList.FirstOrDefault(fsm => fsm.gameObject.name == "ButtonContinue" && fsm.FsmName == "SetSize");
        if (button == null)
        {
            Logger.LogWarning("Auto-continue: ButtonContinue/SetSize not found (is there a save to continue?).");
            return;
        }

        var state = button.Fsm.GetState("Reset globals 2");
        var switchState = AccessTools.Method(typeof(Fsm), "SwitchState");
        if (state == null || switchState == null)
        {
            Logger.LogWarning("Auto-continue: the Continue button's load state is missing in this game build.");
            return;
        }

        Logger.LogInfo("Auto-continue: loading the saved game.");
        switchState.Invoke(button.Fsm, new object[] { state });
    }

    private void OnGUI()
    {
        if (_overlay.Value) _session.OnGUI();
    }

    private void OnLevelWasLoaded(int level)
    {
        _world?.OnLevelLoaded(level);
        _saveRedirect?.Enforce();
        if (_autoContinue && Application.loadedLevelName == "MainMenu")
        {
            _autoContinue = false;
            _continueAt = Time.realtimeSinceStartup + 15f;
        }

        if (_exitAfterSeconds > 0f && Application.loadedLevelName == "GAME") _exitAt = Time.realtimeSinceStartup + _exitAfterSeconds;
        if (_saveGameAfterSeconds > 0f && Application.loadedLevelName == "GAME") _saveGameAt = Time.realtimeSinceStartup + _saveGameAfterSeconds;
        if (Application.loadedLevelName == "GAME") _saveAudit?.Start(Time.realtimeSinceStartup);
        if (level != 1 && level != 3) Logger.LogInfo("Level " + level + " (" + Application.loadedLevelName + ") loaded.");
        // One shot per process: a later level load must not toggle the session off again.
        if ((_autoConnect != null || _autoSteamProbe) && !_autoDone)
        {
            _autoDone = true;
            _autoAt = Time.realtimeSinceStartup + 5f;
        }
    }

    private void OnApplicationQuit() => _session.Close();
}
