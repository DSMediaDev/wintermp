using System;
using Steamworks;
using UnityEngine;
using WinterMP.Core.Diagnostics;

namespace WinterMP.Shim.Probes;

/// <summary>
/// Proves the Steam plumbing a hosted session needs, using the game's own Steam client (the game
/// initialises Steam and pumps its callbacks every frame; we never call SteamAPI.Init ourselves):
/// create a friends-only lobby, write and read lobby data, set rich presence including a "connect"
/// string, read it back, then clean up. Also listens for the overlay "Join Game" callbacks.
/// </summary>
internal sealed class SteamProbe
{
    private const float HoldSeconds = 15f;
    private readonly ILog _log;
    private CallResult<LobbyCreated_t>? _lobbyCreated;
    private Callback<GameRichPresenceJoinRequested_t>? _richJoin;
    private Callback<GameLobbyJoinRequested_t>? _lobbyJoin;
    private CSteamID _lobby;
    private float _requestedAt = -1f;
    private float _releaseAt = -1f;

    public SteamProbe(ILog log)
    {
        _log = log;
    }

    /// <summary>Registers the join listeners. Safe to call before Steam is up; they just never fire.</summary>
    public void Listen()
    {
        try
        {
            _richJoin = new Callback<GameRichPresenceJoinRequested_t>(OnRichPresenceJoin);
            _lobbyJoin = new Callback<GameLobbyJoinRequested_t>(OnLobbyJoin);
            _log.Info("Steam probe: listening for overlay join requests.");
        }
        catch (Exception e)
        {
            _log.Warn("Steam probe: could not register join callbacks: " + e.Message);
        }
    }

    public void Run()
    {
        if (_requestedAt >= 0f || _releaseAt >= 0f)
        {
            _log.Info("Steam probe: already running.");
            return;
        }

        try
        {
            if (!SteamAPI.IsSteamRunning())
            {
                _log.Warn("Steam probe: Steam is not running.");
                return;
            }

            _log.Info("Steam probe: signed in as " + SteamFriends.GetPersonaName() + " (" + SteamUser.GetSteamID().m_SteamID + "). Creating a friends-only lobby...");
            _lobbyCreated = new CallResult<LobbyCreated_t>(OnLobbyCreated);
            _lobbyCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, 4));
            _requestedAt = Time.realtimeSinceStartup;
        }
        catch (Exception e)
        {
            _log.Error("Steam probe failed to start: " + e);
        }
    }

    public void Tick(float now)
    {
        if (_requestedAt >= 0f && now - _requestedAt > 15f)
        {
            _log.Warn("Steam probe: no LobbyCreated callback after 15 s. The game may not be pumping Steam callbacks right now.");
            _requestedAt = -1f;
        }

        if (_releaseAt >= 0f && now >= _releaseAt)
        {
            _releaseAt = -1f;
            SteamMatchmaking.LeaveLobby(_lobby);
            SteamFriends.ClearRichPresence();
            _log.Info("Steam probe: left the lobby and cleared rich presence. Done.");
        }
    }

    private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
    {
        var waited = Time.realtimeSinceStartup - _requestedAt;
        _requestedAt = -1f;
        if (ioFailure || result.m_eResult != EResult.k_EResultOK)
        {
            _log.Warn("Steam probe: lobby creation failed (" + result.m_eResult + ", io failure " + ioFailure + ").");
            return;
        }

        _lobby = new CSteamID(result.m_ulSteamIDLobby);
        _log.Info("Steam probe: lobby " + result.m_ulSteamIDLobby + " created in " + waited.ToString("F2") + " s (callback delivered by the game's own pump).");

        var wrote = SteamMatchmaking.SetLobbyData(_lobby, "wintermp", PluginInfo.Version);
        _log.Info("Steam probe: lobby data write " + (wrote ? "ok" : "FAILED") + ", read back \"" + SteamMatchmaking.GetLobbyData(_lobby, "wintermp") + "\".");

        var connect = "+wintermp_lobby " + result.m_ulSteamIDLobby;
        var statusOk = SteamFriends.SetRichPresence("status", "Testing WinterMP");
        var connectOk = SteamFriends.SetRichPresence("connect", connect);
        var readBack = SteamFriends.GetFriendRichPresence(SteamUser.GetSteamID(), "connect");
        _log.Info("Steam probe: rich presence status " + (statusOk ? "ok" : "FAILED") + ", connect " + (connectOk ? "ok" : "FAILED")
            + ", read back \"" + readBack + "\" (" + (readBack == connect ? "match" : "MISMATCH") + ").");
        _log.Info("Steam probe: holding the lobby for " + HoldSeconds + " s; a friend's overlay should show Join Game now.");
        _releaseAt = Time.realtimeSinceStartup + HoldSeconds;
    }

    private void OnRichPresenceJoin(GameRichPresenceJoinRequested_t request) =>
        _log.Info("Steam: Join Game from friend " + request.m_steamIDFriend.m_SteamID + " with connect \"" + request.m_rgchConnect + "\".");

    private void OnLobbyJoin(GameLobbyJoinRequested_t request) =>
        _log.Info("Steam: lobby join request for lobby " + request.m_steamIDLobby.m_SteamID + " from friend " + request.m_steamIDFriend.m_SteamID + ".");
}
