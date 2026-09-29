# WinterMP - Multiplayer for My Winter Car

**WinterMP** is a free, open source (MIT) multiplayer mod for [My Winter Car](https://store.steampowered.com/app/4164420/My_Winter_Car/), built around dedicated servers.

> **Early days.** Nothing here is playable yet. What exists today is the foundation: the networking core, a dedicated server, a headless test player and the tools we use to study the game. Watch or star the repo if you want to know when that changes.

## What it is

WinterMP is designed so you and your mates can share one household, one winter and one project car, properly. Not just seeing each other drive around, but the story, the jobs, the money, the world and every bolt on the Corris, kept in sync.

- **Dedicated servers first.** A small server program looks after your world, so it keeps going after whoever started it logs off. It runs on Windows or Linux and does not need the game installed.
- **Host from the game, too.** For a quick session with a friend, one player can host straight from their game.
- **Free and open.** MIT licence, no paywalls, no accounts, no launcher.

## For players

There is nothing to install yet. When there is, you will need:

- a LEGAL copy of My Winter Car on Steam (pirated or cracked copies WILL NOT work)
- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) for Windows x64

Proper install steps will land here with the first release.

## For contributors

You will need the [.NET 10 SDK](https://dotnet.microsoft.com/download). The core, the dedicated server, the test player and the test suite all build and run without the game:

```sh
dotnet test WinterMP.NoGame.slnf
```

Run a server on your machine:

```sh
dotnet run --project src/WinterMP.Server -- --name "My Server"
```

Then point a test player at it from a second terminal:

```sh
dotnet run --project tools/WinterMP.Bot -- --connect 127.0.0.1 --stay 10
```

In-game parts build against your own copy of the game: copy `Directory.Build.targets.EXAMPLE` to `Directory.Build.targets` and point it at your install. [CONTRIBUTING.md](CONTRIBUTING.md) covers the rest, including the ground rules.

### Layout

| Folder | What lives there |
|---|---|
| `src/WinterMP.Core` | Networking and session logic. No Unity and no game code; one source builds for both the game's 2015-era runtime and modern .NET. |
| `src/WinterMP.Server` | The dedicated server. |
| `tools/WinterMP.Bot` | A headless test player, handy for checking a server is reachable. |
| `tests` | The test suite. Needs no game install. |

## About

WinterMP is made by DSMedia.

- **Not affiliated.** WinterMP is an independent, fan-made project. It is not affiliated with, authorised or endorsed by Amistech Games, the developers of My Winter Car.
- **AI-assisted development.** Parts of WinterMP are developed with AI assistance (Anthropic's Claude), under human direction and review. Disclosing that is DSMedia policy for our own work.
- **No game files here.** WinterMP references the game's assemblies by name and resolves them from your local install at build time. No game code or assets are ever committed to this repository or shipped in a release.

Licensed under the [MIT licence](LICENSE).

Questions, ideas or something broken? [Open an issue](https://github.com/DSMediaDev/wintermp/issues) and we will get back to you as soon as we can.
