// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-157: the first public-beta patch, agent half (docs/WO-157-findings.md). The pure rules are Wo157Rules (Wo157.cs).
public partial class GameBridge
{
    /// <summary>
    /// The launcher's LAUNCH ANYWAY (WO-157 2.2) starts a game whose mod files it could not find. When the mod really
    /// is missing (its KCD2MP global is absent in the game's Lua), the game's own HUD says so. Harmless when the mod
    /// is loaded: the statement does nothing then.
    /// </summary>
    internal const string Wo157ModMissingLua =
        "if KCD2MP == nil then pcall(function() UIAction.CallFunction('hud', -1, 'ShowInfoText', " +
        "'Kingdom Come: Together did not load in this game: its mod file (kdcmp.pak) is missing or was blocked. " +
        "Close the game and read the launcher message.', 10, 15000, true) end) end";

    // ------------------------------------------------------------ 3b.5 sleeping together

    /// <summary>A partner's "woke" that arrived while this player's own skip ran, and did not stop it.</summary>
    private long _w157WokeKept;

    // ------------------------------------------------------------ 3b.8 the joiner's character is saved with the host's save

    private long _w157SavedToldMs = -1_000_000;

    /// <summary>
    /// Testers asked whether the other player's inventory saves. It does: each host save is paired with a snapshot of the
    /// joiner's character (WO-125). One short line on his screen when that happened -- at most once in 10 minutes, so an
    /// autosave every few minutes does not repeat it.
    /// </summary>
    private void Wo157TellCharacterSaved()
    {
        long now = Environment.TickCount64;
        if (now - _w157SavedToldMs < 600_000) return;
        _w157SavedToldMs = now;
        const string msg = "Your host's game saved, and your character with it (your inventory and progress in this world).";
        Console.WriteLine("MP-W157 told the joiner: his character was saved with the host's save");
        _ = ExecLuaAsync($"if KCD2MP_ShowNativeToast then KCD2MP_ShowNativeToast(\"{EscapeLua(msg)}\") end");
    }

    // ------------------------------------------------------------ switches (w157_cfg key=on|off ...)

    /// <summary>mp_trespass_hud (joiner, default off): his own game's trespass warning is shown again (0.45.1).</summary>
    private volatile bool _w157TrespassHud;

    private void Wo157OnEvent(string name, string arg)
    {
        if (name != "w157_cfg") return;
        foreach (var kv in arg.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = kv.IndexOf('=');
            if (eq <= 0 || kv[(eq + 1)..] is not ("on" or "off")) continue;
            bool on = kv[(eq + 1)..] == "on";
            switch (kv[..eq])
            {
                case "trespass_hud":
                    if (_w157TrespassHud != on) { _w157TrespassHud = on; _w139CfgKey = -1; }   // the next 1 s tick pushes it
                    Console.WriteLine($"MP-W157 mp_trespass_hud {(on ? "on: this joiner's own game shows its trespass warning" : "off: the host's world decides a trespass; this game's own warning is hidden in a session")}");
                    break;
            }
        }
    }

    // ------------------------------------------------------------ 2.4 a start save several groups host

    private uint? _w157InstallKey;
    private volatile bool _w157PeerHostKeyKnown;
    private uint _w157PeerHostKey;

    /// <summary>This install's key, sent by a host beside its world's identity (state 10).</summary>
    private uint Wo157InstallKey() => _w157InstallKey ??= _henry.InstallKey();

    /// <summary>A world with this seed was hosted by this install (the launcher's host flow): its saves of it are its own.</summary>
    private bool Wo157HostedSeed(uint seed) => _henry.HostedHere(WhsSave.SeedTag(seed));

    /// <summary>The joiner's tag for the host's world with this seed (Wo157Rules.WorldTag).</summary>
    private string Wo157TagFor(uint seed)
    {
        string plain = WhsSave.SeedTag(seed);
        uint? key = _w157PeerHostKeyKnown ? _w157PeerHostKey : null;
        return Wo157Rules.WorldTag(seed, key, key is null ? null : _henry.WorldHostKey(plain));
    }

    /// <summary>JoinStatus state 10 from the host: its install key. Re-tags the host's world when it changes.</summary>
    private void Wo157OnHostKey(uint key)
    {
        if (key == 0) return;
        bool changed = !_w157PeerHostKeyKnown || _w157PeerHostKey != key;
        _w157PeerHostKey = key;
        _w157PeerHostKeyKnown = true;
        if (!changed || !_peerSeedKnown) return;
        string tag = Wo157TagFor(_peerSeed);
        if (tag != _peerTag)
        {
            Console.WriteLine($"MP-HENRY joiner: this host's world is kept apart from another host's world of the same start save here: {_peerTag} -> {tag}");
            _peerTag = tag;
            _chooseAsked = false;
        }
    }

    /// <summary>After a join: the world folder belongs to this host (a pre-WO-157 folder is claimed by the first host that comes).</summary>
    private void Wo157ClaimJoinedWorld(string tag)
    {
        if (!_w157PeerHostKeyKnown || !_peerSeedKnown) return;
        try { _henry.ClaimHost(tag, WhsSave.SeedTag(_peerSeed), _w157PeerHostKey); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.WriteLine($"MP-HENRY world {tag}: the host could not be recorded ({ex.Message})"); }
    }
}
