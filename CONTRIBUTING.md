# Contributing to WinterMP

Thanks for wanting to help. WinterMP is small and early, so the most useful thing you can do right now is try things, read the code and tell us what breaks. This guide covers the ground rules, how to build and how we like changes to arrive.

## Ground rules

These are not negotiable. They keep the project legal, portable and fixable.

1. **Never copy code from other multiplayer mods.** That goes for any multiplayer mod for My Winter Car or My Summer Car, public or private. Learning from how a mod behaves is fine; copying its code is not. If a mod's licence does not allow reuse, do not read its source at all, even when it is sitting on GitHub. When in doubt, ask in an issue first.
2. **Never commit game files.** No game DLLs, assets or saves, ever. The build references the game's assemblies by name from your own install, and releases ship only our own code.
3. **Keep the layers clean.** `WinterMP.Core` and `WinterMP.Server` never reference Unity, PlayMaker or anything from the game; they build and test on any machine. Only the in-game plugin (and a couple of development tools) touch the game.
4. **Respect the game's runtime.** My Winter Car runs a 2015-era Mono (the .NET 3.5 profile). Anything that loads in-game targets `net35` and can only use `mscorlib`, `System` and `System.Core`, plus BepInEx and HarmonyX. No modern NuGet packages in-game. Core builds for both `net35` and `net10.0` from the same source; if it only builds on one, it is not done.
5. **Handle save data with care.** WinterMP never writes the game's save format itself. Any change to how a session starts, ends or recovers from a crash needs a test.

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
# Everything that does not need the game: core, server, test player, tests
dotnet test WinterMP.NoGame.slnf
```

For the in-game parts you also need a legal copy of My Winter Car with [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) (Windows x64) installed into it. Copy `Directory.Build.targets.EXAMPLE` to `Directory.Build.targets` (it is git-ignored), set `MwcInstallDir` to your install, then build the full solution:

```sh
dotnet build WinterMP.sln
```

Set `WinterMpDeployToGame` to `true` in that file if you want each build copied straight into the game's `BepInEx\plugins` folder.

## Making changes

- **Small, focused pull requests.** One idea per PR is much easier to review.
- **Tests with behaviour.** New logic in Core comes with tests; bug fixes come with a test that fails without the fix.
- **Plain commit messages.** We use the `type: summary` style (`feat:`, `fix:`, `docs:`, `test:`, `chore:`). Say what changed and why, in plain words.
- **Sign off every commit.** We use the [Developer Certificate of Origin](https://developercertificate.org/): commit with `git commit -s`. CI checks it.
- **Changelog.** Anything a player or server operator would notice gets a line under `## [Unreleased]` in `CHANGELOG.md`.

## Questions

Open an issue, or start a draft PR early if you want a second pair of eyes. We are happy to help you find your feet.
