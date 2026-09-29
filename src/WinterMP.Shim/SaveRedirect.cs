using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using HutongGames.PlayMaker;
using UnityEngine;
using WinterMP.Core.Diagnostics;

namespace WinterMP.Shim;

/// <summary>
/// Points the game's world save files at another folder by rewriting the PlayMaker globals that hold
/// their filenames. The game's save library accepts absolute paths, so the files land wherever we
/// say, with the game's own bytes untouched. The folder must sit outside the game's save folder:
/// Steam cloud-syncs that one, and a multiplayer world must never overwrite a single-player save.
/// </summary>
internal sealed class SaveRedirect
{
    // The world files whose names the game keeps in globals.
    private static readonly string[] GlobalNames = { "SavePlayerData", "SaveCarparts", "SaveItems" };

    // World files the game names with fixed strings inside individual FSMs. These are caught where
    // the save library parses a path. Deliberately absent: options.txt (this machine's settings) and
    // graveyard.txt / trophies*.txt (records that outlive any one world). items2.txt is normally
    // reached through its global, but the main menu's new-game wipe deletes it by fixed name.
    // items.txt holds loose items (bottles and the like: position, fluid, used up) and appears in
    // saves only once the player has had such items.
    private static readonly string[] FixedWorldFiles = { "notepad.txt", "speedcam.txt", "hockeyleague.txt", "meshsave.txt", "defaultES2File.txt", "items2.txt", "items.txt" };

    // Fixed names we know about and leave alone on purpose.
    private static readonly string[] KnownLocalFiles = { "options.txt", "graveyard.txt", "trophies.txt", "trophies1999.txt" };

    private static readonly char[] PathMarks = { '/', Path.DirectorySeparatorChar, ':' };
    private static readonly HashSet<string> Unlisted = new(StringComparer.OrdinalIgnoreCase);
    private static string? _fixedFolder;
    private static ILog? _fixedLog;

    private readonly ILog _log;
    private readonly string _folder;
    private readonly string[] _originals = new string[GlobalNames.Length];
    private readonly string[] _targets = new string[GlobalNames.Length];
    private bool _applied;

    private SaveRedirect(ILog log, string folder)
    {
        _log = log;
        _folder = folder;
    }

    public string Folder => _folder;

    /// <summary>Returns a redirect for the given folder, or null (with the reason logged) if it is unsafe.</summary>
    public static SaveRedirect? Create(ILog log, string folder)
    {
        var full = Normalise(Path.GetFullPath(folder));
        var native = Normalise(Path.GetFullPath(Application.persistentDataPath));
        if (full.StartsWith(native + "/", StringComparison.OrdinalIgnoreCase) || string.Equals(full, native, StringComparison.OrdinalIgnoreCase))
        {
            log.Error("Save redirect refused: " + full + " is inside the game's own save folder, which Steam cloud-syncs.");
            return null;
        }

        Directory.CreateDirectory(full);
        return new SaveRedirect(log, full);
    }

    /// <summary>Also redirects the world files the game names with fixed strings. Call once, before any save loads.</summary>
    public void InstallFixedNames(Harmony harmony)
    {
        var parser = AccessTools.TypeByName("ES2FilenameData");
        var init = parser == null ? null : AccessTools.Method(parser, "Init");
        if (init == null)
        {
            _log.Warn("Save redirect: ES2FilenameData.Init not found; fixed-name world files stay in the game's folder.");
            return;
        }

        _fixedFolder = _folder;
        _fixedLog = _log;
        harmony.Patch(init, prefix: new HarmonyMethod(typeof(SaveRedirect), nameof(InitPrefix)));
        _log.Info("Save redirect: also covering " + string.Join(", ", FixedWorldFiles) + ".");
    }

    // Harmony binds by parameter name. Only bare relative names on the list are touched; anything
    // with a folder, an absolute path or a URL passes through unchanged.
    private static void InitPrefix(ref string path)
    {
        if (_fixedFolder == null || string.IsNullOrEmpty(path)) return;
        var query = path.IndexOf('?');
        var name = query < 0 ? path : path.Substring(0, query);
        // No name (just "?tag=..."): the library is parsing settings, not naming a file.
        if (name.Length == 0 || name.IndexOfAny(PathMarks) >= 0) return;
        foreach (var world in FixedWorldFiles)
        {
            if (string.Equals(name, world, StringComparison.OrdinalIgnoreCase))
            {
                path = _fixedFolder + "/" + path;
                return;
            }
        }

        // A save file nobody has classified yet (a game update, most likely) lands in the game's own
        // folder. Say so once per name so it gets a decision instead of going unnoticed.
        if (Array.IndexOf(KnownLocalFiles, name.ToLowerInvariant()) < 0 && Unlisted.Add(name))
        {
            _fixedLog?.Warn("Save redirect: '" + name + "' is not a known save file; it stays in the game's own save folder.");
        }
    }

    /// <summary>Applies the redirect, and re-applies it if the game has reset a global. Cheap; call every frame.</summary>
    public void Enforce()
    {
        var globals = FsmVariables.GlobalVariables;
        if (globals == null) return;
        for (var i = 0; i < GlobalNames.Length; i++)
        {
            var variable = globals.FindFsmString(GlobalNames[i]);
            if (variable == null)
            {
                if (!_applied) _log.Warn("Save redirect: global " + GlobalNames[i] + " not found in this game build.");
                continue;
            }

            if (!_applied)
            {
                _originals[i] = variable.Value;
                _targets[i] = _folder + "/" + variable.Value;
            }
            else if (variable.Value == _targets[i])
            {
                continue;
            }
            else
            {
                _log.Warn("Save redirect: " + GlobalNames[i] + " was reset to '" + variable.Value + "' (level " + Application.loadedLevel + "); re-applying.");
            }

            variable.Value = _targets[i];
        }

        if (!_applied)
        {
            _applied = true;
            _log.Info("Save redirect: world files now read and written under " + _folder + " (" + string.Join(", ", _originals) + ").");
        }
    }

    private static string Normalise(string path) => path.Replace('\\', '/').TrimEnd('/');
}
