using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using WinterMP.Core.Diagnostics;

namespace WinterMP.Shim;

/// <summary>What game build we are running in, and which build of ourselves is loaded.</summary>
internal static class GameInfo
{
    /// <summary>The game's version string, e.g. "v.260917-03". "unknown" if it cannot be read.</summary>
    public static string Build { get; private set; } = "unknown";

    /// <summary>The folder holding mywintercar.exe.</summary>
    public static string GameRoot => Path.GetDirectoryName(Application.dataPath) ?? string.Empty;

    /// <summary>
    /// The game writes its version as the first line of changelog.txt beside the exe. It is the
    /// same string players see, which makes it the right thing to compare between players.
    /// </summary>
    public static void Detect(ILog log)
    {
        try
        {
            var path = Path.Combine(GameRoot, "changelog.txt");
            using (var reader = new StreamReader(path))
            {
                var first = (reader.ReadLine() ?? string.Empty).Trim();
                if (first.StartsWith("v.", StringComparison.Ordinal) && first.Length <= 32)
                {
                    Build = first;
                    return;
                }
            }

            log.Warn("The game's changelog.txt does not start with a version line; treating the build as unknown.");
        }
        catch (Exception e)
        {
            log.Warn("Could not read the game version from changelog.txt: " + e.Message);
        }
    }

    /// <summary>
    /// Short content hash of a loaded assembly file. Printed in the banner so a stale copy (an old
    /// DLL left behind by a mod manager, say) is obvious from the log alone.
    /// </summary>
    public static string Fingerprint(Type fromAssembly)
    {
        try
        {
            using (var sha = new SHA256Managed())
            {
                var hash = sha.ComputeHash(File.ReadAllBytes(fromAssembly.Assembly.Location));
                return BitConverter.ToString(hash, 0, 4).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
        catch (Exception)
        {
            return "????????";
        }
    }
}
