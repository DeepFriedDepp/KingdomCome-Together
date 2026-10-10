// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-166 (docs/WO-166-findings.md): 0.48.2 -- the bridge's halves. The pure rules are Wo166Rules (Wo166.cs); the game-side halves
// are kdcmp.lua's WO-166 section; the native half wo166.cpp.
//
//   L1 the loot screen's close: the engine's "PlayAudio: ui_inv_screen_out..." line, relayed to the Lua (a second signal beside the
//      game's own ItemTransfer OnClosed event)
//   T3 a talk that never started: the engine's dialogue state for that copy (its last "Dialog interrupted/ending ... state: X", or
//      none -- the request never became a dialogue), whether the dialogue controller's NPC pause request timed out, and the Lua's
//      facts about the copy (paused, in a dialogue, the stream held or driving it) -- one line, WO166-TALK timeout ...
//   T4 a talk refused because the host's NPC is busy (counted; the Lua told the player)
//
//   WO166-TALK timeout npc=<n> state=<engine state|request-only> pause_requests=<timed-out|-> waited_s=<s> placed=<host row> <lua facts>
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, (string State, double At)> _w166DlgState = new(StringComparer.Ordinal);
    private double _w166PauseReqTimeoutAt = -1e9;
    private long _w166LootCloseLines, _w166TalkTimeouts, _w166TalkBusy;

    private void Wo166OnDisconnect() { _w166DlgState.Clear(); }

    /// <summary>The engine lines LogTailGameTransport.Wo144Line routes (Wo144OnEngineLine calls this first). True = handled.</summary>
    private bool Wo166OnEngineLine(string line)
    {
        if (line.StartsWith(Wo166Rules.LootScreenOutPrefix, StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _w166LootCloseLines);
            if (W137JoinerSession) _ = ExecLuaAsync("if KCD2MP_W166LootClosed then KCD2MP_W166LootClosed(\"audio\") end");
            return true;
        }
        if (line.Contains(Wo166Rules.PauseRequestsTimedOut, StringComparison.Ordinal))
        {
            _w166PauseReqTimeoutAt = W164Now();
            return false;
        }
        if (Wo166Rules.TryParseDialogState(line, out var souls, out string state))
        {
            double now = W164Now();
            foreach (var s in souls) if (s != "Dude") _w166DlgState[s] = (state, now);
        }
        return false;
    }

    private void Wo166OnEvent(string name, string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (name)
        {
            case "w166_talkstate":   // <npc> paused=.. copy_dialog=.. dead=.. resumed_for_talk=.. stream=.. via=.. retried=..
            {
                if (f.Length < 1 || !Wo137Text.IsNpc(f[0])) return;
                string npc = f[0];
                double now = W164Now();
                _w164Talks.TryGetValue(npc, out var t);
                double asked = t?.AskedAt ?? now;
                string st = _w166DlgState.TryGetValue(npc, out var ds) && ds.At >= asked - 1 ? ds.State : "request-only";
                bool pauseTimedOut = _w166PauseReqTimeoutAt >= asked - 1;
                Interlocked.Increment(ref _w166TalkTimeouts);
                Console.WriteLine(FormattableString.Invariant(
                    $"WO166-TALK timeout npc={npc} state={st} pause_requests={(pauseTimedOut ? "timed-out" : "-")} waited_s={now - asked:F1} placed={(t?.Placed ?? "?").Replace(' ', '_')} {string.Join(' ', f.Skip(1))}"));
                return;
            }
            case "w166_talk":        // busy <npc> <why>
                if (f.Length >= 3 && f[0] == "busy" && Wo137Text.IsNpc(f[1]))
                {
                    Interlocked.Increment(ref _w166TalkBusy);
                    Console.WriteLine($"WO166-TALK busy npc={f[1]} why={f[2]} -- not asked: the host's world has this person occupied (the player was told)");
                }
                return;
        }
    }

    private string Wo166StatsText() =>
        $"loot_close_lines={Interlocked.Read(ref _w166LootCloseLines)} talk_timeouts={Interlocked.Read(ref _w166TalkTimeouts)} talk_busy={Interlocked.Read(ref _w166TalkBusy)}";
}
