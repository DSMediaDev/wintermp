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
    private bool _autoNewGame;
    private float _newGameAt = -1f;
    private float _fillAt = -1f;
    private float _beginAt = -1f;
    private bool _skipIntro;
    private float _skipIntroAt = -1f;
    private float _exitAfterSeconds = -1f;
    private float _exitAt = -1f;
    private float _saveGameAfterSeconds = -1f;
    private float _saveGameAt = -1f;
    private string? _watch;
    private string? _saveAuditFolder;
    private bool _saveAuditRestore;
    private SaveAuditProbe? _saveAudit;
    private SaveCapture? _saveCapture;

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
            if (args[i] == "-wintermp-auto-new-game") _autoNewGame = true;
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
        _saveCapture = SaveCapture.Create(_harmony, _log);
        if (_saveAuditFolder != null) _saveAudit = SaveAuditProbe.Create(_harmony, _log, _saveAuditFolder, _saveAuditRestore ? _saveCapture : null);
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
        _saveCapture?.Tick();
        _saveAudit?.Tick(now);
        if (_autoAt >= 0f && now >= _autoAt)
        {
            _autoAt = -1f;
            if (_autoConnect != null) _session.Toggle();
            if (_autoSteamProbe) _steam.Run();
        }

        if ((_autoContinue || _autoNewGame) && Application.loadedLevelName == "SplashScreen") AcceptDisclaimer();
        if (_continueAt >= 0f && now >= _continueAt)
        {
            _continueAt = -1f;
            PressContinue();
        }

        if (_newGameAt >= 0f && now >= _newGameAt)
        {
            _newGameAt = -1f;
            if (PressNewGame()) _fillAt = now + 2f;
        }

        if (_fillAt >= 0f && now >= _fillAt)
        {
            _fillAt = -1f;
            FillLicence();
            _beginAt = now + 2f;
        }

        if (_beginAt >= 0f && now >= _beginAt)
        {
            _beginAt = -1f;
            PressBegin();
        }

        if (_skipIntroAt >= 0f && now >= _skipIntroAt)
        {
            _skipIntroAt = -1f;
            if (EnterState("Auto new game", "Button", "SkipIntro", "State 4")) Logger.LogInfo("Auto new game: skipping the intro.");
        }

        if (_saveGameAt >= 0f && now >= _saveGameAt)
        {
            // What a toilet does, minus the trip back to the main menu that follows it.
            _saveGameAt = -1f;
            Logger.LogInfo("Saving in session (level " + Application.loadedLevelName + ").");
            if (_saveCapture != null) _saveCapture.Capture();
            else PlayMakerFSM.BroadcastEvent("SAVEGAME");
        }

        if (_exitAt >= 0f && now >= _exitAt)
        {
            // Killed, not quit: nothing in the game gets a chance to save on the way out.
            SaveTraceProbe.LogSummary();
            Logger.LogInfo("Exit timer reached; ending the process without saving.");
            Process.GetCurrentProcess().Kill();
        }
    }

    // Scripted runs only: the splash screen's early-access disclaimer waits for a click or Enter. Enter
    // sends SKIP to the button's FSM, which loads the main menu; this sends the same event once the
    // disclaimer is showing.
    private void AcceptDisclaimer()
    {
        var button = PlayMakerFSM.FsmList.FirstOrDefault(fsm => fsm.gameObject.name == "Button" && fsm.FsmName == "Button"
            && fsm.transform.parent != null && fsm.transform.parent.name == "Disclaimer");
        if (button == null || !button.gameObject.activeInHierarchy || button.Fsm.ActiveStateName == "State 1") return;
        Logger.LogInfo("Auto-continue: accepting the splash screen disclaimer.");
        button.SendEvent("SKIP");
    }

    // The main menu's Continue button is an FSM; entering its load state is what a click does.
    private void PressContinue()
    {
        if (EnterState("Auto-continue", "ButtonContinue", "SetSize", "Reset globals 2")) Logger.LogInfo("Auto-continue: loading the saved game.");
    }

    // Scripted runs only: New game opens the licence card, whose name fields clear themselves as it opens.
    private bool PressNewGame()
    {
        if (!EnterState("Auto new game", "ButtonNewgame", "SetSize", "State 1")) return false;
        Logger.LogInfo("Auto new game: opened the licence card.");
        return true;
    }

    // Fills in the card the way typing would. The card only shows its Begin button once a last name is
    // in, so Begin is pressed on a later frame.
    private void FillLicence()
    {
        var globals = FsmVariables.GlobalVariables;
        globals.GetFsmString("PlayerFirstName").Value = "Test";
        globals.GetFsmString("PlayerLastName").Value = "Driver";
        globals.GetFsmBool("PlayerPermaDeath").Value = false;
    }

    // Begin: the game deletes the old world, writes the new player's details and loads the intro.
    private void PressBegin()
    {
        if (!EnterState("Auto new game", "ButtonBegin", "SetSize", "State 2")) return;
        _skipIntro = true;
        Logger.LogInfo("Auto new game: starting a new game as "
            + FsmVariables.GlobalVariables.GetFsmString("PlayerName").Value + ", permadeath off.");
    }

    private bool EnterState(string purpose, string objectName, string fsmName, string stateName)
    {
        var fsm = PlayMakerFSM.FsmList.FirstOrDefault(f => f.gameObject.name == objectName && f.FsmName == fsmName);
        if (fsm == null)
        {
            Logger.LogWarning(purpose + ": " + objectName + "/" + fsmName + " not found.");
            return false;
        }

        var state = fsm.Fsm.GetState(stateName);
        var switchState = AccessTools.Method(typeof(Fsm), "SwitchState");
        if (state == null || switchState == null)
        {
            Logger.LogWarning(purpose + ": " + objectName + "/" + fsmName + " has no state '" + stateName + "' in this game build.");
            return false;
        }

        switchState.Invoke(fsm.Fsm, new object[] { state });
        return true;
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

        if (_autoNewGame && Application.loadedLevelName == "MainMenu")
        {
            _autoNewGame = false;
            _newGameAt = Time.realtimeSinceStartup + 15f;
        }

        if (_skipIntro && Application.loadedLevelName == "Intro")
        {
            _skipIntro = false;
            _skipIntroAt = Time.realtimeSinceStartup + 3f;
        }

        if (_exitAfterSeconds > 0f && Application.loadedLevelName == "GAME" && _exitAt < 0f) _exitAt = Time.realtimeSinceStartup + _exitAfterSeconds;
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
