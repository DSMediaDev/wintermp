using System;
using Steamworks;
using UnityEngine;
using WinterMP.Core.Diagnostics;
using WinterMP.Core.Net;
using WinterMP.Core.Protocol;
using WinterMP.Core.Session;

namespace WinterMP.Shim;

/// <summary>
/// The in-game end of a session: connect to a dedicated server over UDP with the same Core the
/// server runs, pumped from the game's main thread, and show the state in a small overlay.
/// </summary>
internal sealed class SessionPanel
{
    private readonly ILog _log;
    private readonly Func<string> _address;
    private readonly Func<string> _playerName;
    private UdpTransport? _transport;
    private NetClient? _client;
    private GUIStyle? _style;

    public SessionPanel(ILog log, Func<string> address, Func<string> playerName)
    {
        _log = log;
        _address = address;
        _playerName = playerName;
    }

    private bool Active => _client != null && (_client.State == ClientState.Connecting || _client.State == ClientState.Connected);

    public void Toggle()
    {
        if (Active)
        {
            Close();
            return;
        }

        Close();
        NetEndPoint? server;
        string error;
        if (!UdpTransport.TryResolve(_address(), ProtocolInfo.DefaultPort, out server, out error))
        {
            _log.Warn("Cannot connect: " + error);
            return;
        }

        _transport = UdpTransport.Bind(0);
        _client = new NetClient(_transport, _log);
        _client.Connect(server, new ClientIdentity
        {
            ModVersion = PluginInfo.Version,
            GameBuild = GameInfo.Build,
            PlayerName = ResolveName(),
        }, MonotonicClock.NowMs);
    }

    public void Tick() => _client?.Update(MonotonicClock.NowMs);

    public void OnGUI()
    {
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleLeft, fontSize = 12 };
            _style.normal.textColor = new Color(0.85f, 0.9f, 1f);
        }

        var status = _client == null ? "F8 to connect to " + _address() : _client.StatusText;
        if (_client != null && _client.State == ClientState.Connected && _client.RttMs.HasValue) status += "  |  " + _client.RttMs.Value + " ms";
        GUI.Box(new Rect(8, 8, 620, 24), " WinterMP " + PluginInfo.Version + "  |  " + status, _style);
    }

    public void Close()
    {
        if (_client != null) _client.Disconnect();
        if (_transport != null) _transport.Dispose();
        _client = null;
        _transport = null;
    }

    private string ResolveName()
    {
        var configured = _playerName().Trim();
        if (configured.Length > 0) return configured;
        try
        {
            if (SteamAPI.IsSteamRunning()) return SteamFriends.GetPersonaName();
        }
        catch (Exception)
        {
            // Steam not ready: fall through to the default.
        }

        return PlayerNames.Fallback;
    }
}
