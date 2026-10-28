// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

// WO-201: the dispatch to each part's own file (GameBridge.Wo201*.cs).
public partial class GameBridge
{
    private long _w201RecvKept;

    /// <summary>V.1 (joiner, in the host's world): a root step his own quest logic made by itself is kept here, never asked for.</summary>
    private bool Wo201ReceiveOnlyKeeps(QuestChange c)
    {
        if (!W137Joiner || !W201("joiner_receive_only")) return false;
        if (Wo201Rules.ReceiveOnlyAsks(Wo147InOwnConversation(), c.Path)) return false;
        long n = Interlocked.Increment(ref _w201RecvKept);
        if (n <= 30 || n % 50 == 0)
            Console.WriteLine(FormattableString.Invariant($"WO201-RECV kept {c.Path} {(c.Port.Length > 0 ? c.Port : "-")} {c.Old}->{c.New} -- this game's own quest logic decided it (not this player's conversation or interaction): the host's world decides; not asked [n={n}]"));
        return true;
    }

    private void Wo201OnCfg(string name, bool on)
    {
        if (name is "joiner_scene_noposition" or "walk_from_host" or "oneshot_tick_cap" or "copy_no_idle_turn") _ = Wo201PushConfigAsync();
        if (name == "joiner_no_story_barks") _ = Wo200PushConfigAsync();   // B7 rides WO-200's gate byte (bit 8)
    }

    /// <summary>The session loop's 4 Hz tick (n counts the ticks).</summary>
    private void Wo201Tick(long n)
    {
        if (n % 4 == 1) _ = Wo201MountReassertAsync();   // B3: 1 Hz
        Wo201CombatTick();                                // B4: 4 Hz
        Wo201Dude2Tick(n);                                // V.3: 1 Hz set, 1 Hz watchdog
        Wo201NearTick(n);                                 // V.2: 1 Hz
        Wo201SpeechTick();                                // Act V F1's capture: 4 Hz
        Wo201RangedTick();                                // B11: 4 Hz (the DLL queues every step)
        Wo204Tick(n);                                     // WO-204: 4 Hz
        Wo206Tick(n);                                     // WO-206 E: the host's epochs every 10 s, the stats line every minute
        Wo206DigestTick(n);                               // WO-206 C/D: the checkpoint digests and the probe, 4 Hz
        Wo207Tick(n);                                     // WO-207 B.1: the inbound queue, every second
        Wo208Tick(n);                                     // WO-208: the DLL's switch word every 2 s
        Wo209Tick(n);                                     // WO-209: its switch word every 2 s, the mirror's numbers every 30 s
        Wo210Tick(n);                                     // WO-210: its switch word every 2 s, its numbers every 30 s, the DLL's asks
    }

    private void Wo201OnEventMore(string name, string? arg)
    {
        if (name == "w201_dude2_census") _ = Wo201Dude2CensusAsync();
    }

    private void Wo201OnEngineLineMore(string line)
    {
        Wo201ScenesOnEngineLine(line);
        Wo201ValuesOnEngineLine(line);
        Wo201BusyOnEngineLine(line);
        Wo201LockOnEngineLine(line);
    }

    private string Wo201StatsMore() => Wo201ScenesStats() + " " + Wo201ValuesStats() + " " + Wo201CombatStats() + " " + Wo201BusyStats() + " " + Wo201HandsStats() + " " + Wo201Dude2Stats() + " " + Wo201SpeechStats() + " " + Wo201RangedStats() + $" recv_kept={Interlocked.Read(ref _w201RecvKept)}";
}
