# Changelog

All notable changes to WinterMP are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Networking core: hand-rolled wire format, UDP and in-memory transports, and the join handshake (version checks that always explain a mismatch, game build matching, player limits, keepalive and clean goodbyes).
- Dedicated server (`WinterMP.Server`): runs on Windows or Linux without the game, with `status`, `kick` and `quit` console commands and a clean shutdown on Ctrl+C or SIGTERM.
- Headless test player (`WinterMP.Bot`) for checking that a server is reachable.
- Game-free test suite, run by CI on Linux and Windows.
