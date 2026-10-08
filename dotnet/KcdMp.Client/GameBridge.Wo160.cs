// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

// WO-160: the 2026-10-07 tester findings, agent half (docs/WO-160-findings.md). The pure rules are Wo160Rules (Wo160.cs).
public partial class GameBridge
{
    // ------------------------------------------------------------ 2 the false "You loaded your own save"

    // The joiner agent outlives its game. When a game ends without a quit line (a crash, a kill) the agent's _where kept
    // "World"; the next process was still at its main menu when the host connected, and the own-world verdict said "You
    // loaded your own save" ~20 s before the join's load even started (field, joiner 17:41:54). The verdict now needs the
    // mod's own, recent "world" answer, and is silent during and for 60 s after a join's activity.

    private long _w160ConfirmedMs;      // TickCount64 of the mod's last "world" answer (or Gameplay started); 0 = never
    private long _w160AskedMs;          // the last time the mod was asked where the game is (this feature's asks)
    private long _w160JoinActiveMs;     // TickCount64 of the last tick a join was in flight; 0 = none
    private long _w160Held;             // verdicts held back by the rule (the log counts them)

    /// <summary>The mod said a world is loaded (its own answer), or "Gameplay started" just landed.</summary>
    private void W160WorldConfirmed() => Interlocked.Exchange(ref _w160ConfirmedMs, Math.Max(1, Environment.TickCount64));

    /// <summary>The game is not in a world (menu, a load, a quit, a failed load): the proof goes.</summary>
    private void W160WorldGone() => Interlocked.Exchange(ref _w160ConfirmedMs, 0);

    private long W160ConfirmedAgeMs()
    {
        long c = Interlocked.Read(ref _w160ConfirmedMs);
        return c == 0 ? -1 : Environment.TickCount64 - c;
    }

    /// <summary>Ask the mod where the game is when the proof is missing or aging (the answer lands in Wo124OnWhere).</summary>
    private async Task W160AskWhereIfDueAsync()
    {
        long now = Environment.TickCount64;
        if (!Wo160Rules.WhereRefreshDue(W160ConfirmedAgeMs(), now - _w160AskedMs)) return;
        _w160AskedMs = now;
        try { await ExecLuaAsync("if KCD2MP_Wo124Where then KCD2MP_Wo124Where() end"); } catch { }
    }

    /// <summary>Called every second by the WO-140 loop: a join in flight keeps the verdict quiet for 60 s after it ends.</summary>
    private void W160NoteJoinActivity()
    {
        if (W140JoinBusy) _w160JoinActiveMs = Math.Max(1, Environment.TickCount64);
    }

    private long W160JoinQuietAgeMs() => _w160JoinActiveMs == 0 ? -1 : Environment.TickCount64 - _w160JoinActiveMs;

    /// <summary>The own-world verdict's gate (WO-160 2). The first hold in a row is logged.</summary>
    private async Task<bool> W160SeparateAllowedAsync()
    {
        await W160AskWhereIfDueAsync();
        bool ok = Wo160Rules.SeparateVerdictAllowed(W160ConfirmedAgeMs(), W140JoinBusy, W160JoinQuietAgeMs());
        if (!ok && Interlocked.Increment(ref _w160Held) % 30 == 1)
            Console.WriteLine($"WO160-SEPARATE held: no verdict (world confirmed by the mod {(W160ConfirmedAgeMs() < 0 ? "never" : $"{W160ConfirmedAgeMs() / 1000.0:F0} s ago")}, join busy={W140JoinBusy}, join activity {(W160JoinQuietAgeMs() < 0 ? "none" : $"{W160JoinQuietAgeMs() / 1000.0:F0} s ago")}, where={_where}) -- a game at its main menu or a join's load is not 'your own save'");
        return ok;
    }
}
