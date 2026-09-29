# AGENTS.md

Guidance for AI coding agents (and humans in a hurry) working in this repository. The full ground rules are in [CONTRIBUTING.md](CONTRIBUTING.md); this is the short version plus the commands.

## Rules

- **Never copy code from other multiplayer mods** for My Winter Car or My Summer Car. If a mod's licence does not allow reuse, do not read its source.
- **Never add game files** (DLLs, assets, saves) to the repository.
- **Layering:** `src/WinterMP.Core` and `src/WinterMP.Server` must not reference UnityEngine, PlayMaker or game assemblies. Only `src/WinterMP.Shim` and development tools under `tools/` may.
- **Runtime ceiling:** in-game code targets `net35` and may use only `mscorlib`, `System`, `System.Core`, BepInEx and HarmonyX. Core multi-targets `net35;net10.0`; both must build with zero warnings (warnings are errors).
- **No exception filters (`catch ... when`) in code that runs in-game**; the game's old Mono runtime is unreliable with them.
- **Wire format:** hand-rolled, little-endian, bounds-checked. The datagram magic, the `Hello` prefix and the `Reject` layout are frozen forever; tests lock them byte for byte. Bump `ProtocolInfo.Version` for any other wire change.
- **Save data:** never write the game's save format.
- **Commits:** `type: summary` style, plain language, signed off (`git commit -s`).

## Commands

```sh
dotnet test WinterMP.NoGame.slnf                              # game-free build + tests
dotnet run --project src/WinterMP.Server -- --help            # dedicated server
dotnet run --project tools/WinterMP.Bot -- --connect 127.0.0.1 # headless test player
dotnet build WinterMP.sln                                     # full build (needs Directory.Build.targets)
```
