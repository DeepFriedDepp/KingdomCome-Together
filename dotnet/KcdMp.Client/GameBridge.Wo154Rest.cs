// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

// WO-154 Phase 6: the rest of the session list (docs/WO-154-findings.md).
//
// 6.3 A joiner's own wait or sleep, set back. In a shared world only the host's clock moves time (WO-133): a
// joiner's held picker becomes a vote (WO-140; a no says "Other players are not ready to sleep yet!"), and a skip
// that runs on his copy anyway (the vote switched off, a way in the gate does not hold) is set back to the host's
// clock the moment it ends (Wo140CheckClock) -- until now without a word. The set-back that follows this player's
// own skip is now told on screen once (Wo154Rules.SkipUndoneText); one after a cutscene's own time jump or the
// host's reload is not his doing and stays quiet. mp_skip_tell on|off (default on).
public partial class GameBridge
{
    private volatile bool _w154SkipTell = true;
    private long _w154SkipEndedMs, _w154SkipToldMs, _w154SkipTold;
    private volatile bool _w154SkipEndedShared;
    private string _w154SkipEndedKind = "skip";
    private string? _w154SkipBeganKind;   // the DLL's began edge carries the skip's id; its ended edge reads -1 (live L6)

    /// <summary>This game's own skip ended (the DLL's edge or the log's marker): a set-back right after it is his.</summary>
    private void Wo154NoteLocalSkipEnded(string kind)
    {
        _w154SkipEndedShared = W140SharedSkip;
        _w154SkipEndedKind = kind is "sleep" or "wait" ? kind : "skip";
        Interlocked.Exchange(ref _w154SkipEndedMs, Environment.TickCount64);
    }

    /// <summary>The joiner's clock was just set back <paramref name="aheadS"/> game-seconds to the host's.</summary>
    private void Wo154AfterPull(uint aheadS)
    {
        long now = Environment.TickCount64, ended = Interlocked.Read(ref _w154SkipEndedMs);
        if (!Wo154Rules.TellSkipUndone(_w154SkipTell, W140SharedSkip, now, ended, _w154SkipEndedShared, Interlocked.Read(ref _w154SkipToldMs)))
        {
            if (ended > 0 && now - ended <= Wo154Rules.SkipTellWindowMs)
                Console.WriteLine(FormattableString.Invariant(
                    $"MP-W154 skip: set back {aheadS} s after this player's {_w154SkipEndedKind} -- not told ({(!_w154SkipTell ? "mp_skip_tell off" : _w154SkipEndedShared || W140SharedSkip ? "a shared skip" : "told a minute ago")})"));
            return;
        }
        Interlocked.Exchange(ref _w154SkipToldMs, now);
        Interlocked.Increment(ref _w154SkipTold);
        Console.WriteLine(FormattableString.Invariant(
            $"MP-W154 skip: this player's own {_w154SkipEndedKind} ended {(now - ended) / 1000.0:F1} s ago and was set back {aheadS} s to the host's clock -- told on screen"));
        _ = Wo140SayAsync(Wo154Rules.SkipUndoneText);
    }

    private string Wo154RestStatsText() => FormattableString.Invariant(
        $"skip_tell={(_w154SkipTell ? "on" : "off")} skip_told={Interlocked.Read(ref _w154SkipTold)} minigame_outcome={(Wo137Rules.MinigameOutcomeShared ? "shared" : "per-machine")}");
}
