// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>WO-211 (docs/WO-211-findings.md): the 0.63.9 beta patch's pure rules -- the switches, the link hold (A), the seat release (C.1),
/// the over-full queue line (C.3), the joint crime's notice (D).</summary>
public static class Wo211Rules
{
    /// <summary>The console switches the agent knows (the mod's mp_* commands send "name=on|off" as w211_cfg). New mechanisms ship on;
    /// allow_fly (E) is a tester's permission and ships off.</summary>
    public static readonly string[] SwitchNames = ["link_hold", "ui_lua_main_only", "overflow_reseat", "joint_crime_told", "allow_fly"];
    public static readonly string[] DefaultOff = ["allow_fly"];
    /// <summary>Fixes with no switch of their own, each with its bench twin (KCDMP_TEST_W211_OFF=name).</summary>
    public static readonly string[] FixNames = ["snapshot_after_drop", "drop_henry_home", "seat_same_object", "handover_queue_cap"];

    public static bool Default(string name, string? twinOff) =>
        !Wo207Rules.TwinOff(twinOff, name) && !DefaultOff.Contains(name, StringComparer.Ordinal);

    /// <summary>The DLL's switch word (op 143), the same bits as wo211_rules.h kBit*.</summary>
    public static uint ConfigBits(Func<string, bool> on) => (on("ui_lua_main_only") ? 0x01u : 0u) | (on("overflow_reseat") ? 0x02u : 0u)
                                                             | (on("handover_queue_cap") ? 0x04u : 0u);

    /// <summary>"name=on|off" from the mod: false when malformed or not one of <see cref="SwitchNames"/>.</summary>
    public static bool TryParseCfg(string? arg, out string name, out bool on)
    {
        name = ""; on = false;
        if (string.IsNullOrEmpty(arg)) return false;
        int eq = arg.IndexOf('=');
        if (eq <= 0) return false;
        string n = arg[..eq], v = arg[(eq + 1)..];
        if (v is not ("on" or "off") || !SwitchNames.Contains(n, StringComparer.Ordinal)) return false;
        name = n; on = v == "on";
        return true;
    }

    // ---------------------------------------------------------------- A: a dropped link

    /// <summary>A.1: how long the joiner holds in the host's world for the link to come back, and the host keeps the partner's figure.</summary>
    public const int HoldWindowS = 60;

    /// <summary>A.1: the gap before reconnect attempt <paramref name="attempt"/> (0-based) while a link hold runs: growing, never past the
    /// window's end. Outside a hold the agent's own 3 s.</summary>
    public static int ReconnectDelayMs(int attempt, bool holding) =>
        !holding ? 3000 : attempt switch { 0 => 1000, 1 => 2000, 2 => 3000, 3 => 5000, 4 => 8000, _ => 10000 };

    /// <summary>A.1: does a lost link start a hold rather than a leave? Only a joiner in the host's world (not mid-join, not leaving, not
    /// quitting the game), with the switch on.</summary>
    public static bool HoldOnLoss(bool on, bool joinedWorld, bool joining, bool leaving, bool quitting) =>
        on && joinedWorld && !joining && !leaving && !quitting;

    /// <summary>A.1: the hold's line ("Connection to your host lost -- reconnecting... (n s)"), with the way out.</summary>
    public static string HoldText(int leftS) =>
        $"Connection to your host lost -- reconnecting... ({Math.Max(0, leftS)} s). Leave now: the co-op menu (Insert), 'Leave the host's world'.";

    /// <summary>A.1 step 4: after the link is back, the host's world epoch says whether its world is the one this game holds (no load, no
    /// jump) or the host reloaded meanwhile (the in-world rejoin: its newer world, the Henry from the matching snapshot).</summary>
    public static bool ResumeInPlace(uint epochBefore, uint epochNow) => epochBefore != 0 && epochNow == epochBefore;

    /// <summary>A.2: what the player is told when the final snapshot could not be written (never silent).</summary>
    public static string SnapshotLostText(string reason) =>
        $"Your last progress in your partner's world couldn't be saved ({(string.IsNullOrWhiteSpace(reason) ? "unknown" : reason)}).";

    // ---------------------------------------------------------------- C.1: the seat, bed or workstation the copy holds

    /// <summary>
    /// C.1: the host NPC actually left the posture the copy holds -- none of the objects that posture holds (a bed or seat by its stance, a
    /// workstation by its unstance) is held by the host's new state. An unstance change on the same seat (a seated gesture, a dialogue pose:
    /// the field's ttkc_man_10, sitting@0D02... unstance - -> 119 -> -) is not a leave; neither is the same object under another stance.
    /// </summary>
    public static bool LeftPosture(ActivityState prev, ActivityState next)
    {
        var p = prev.Normalised(); var n = next.Normalised();
        if (!p.OwnsPosition) return false;
        ulong ps = ActivityState.ObjectStance(p.Stance) ? p.StanceObj : 0, pu = p.Unstance != ActivityState.NoUnstance ? p.UnstanceObj : 0;
        ulong ns = ActivityState.ObjectStance(n.Stance) ? n.StanceObj : 0, nu = n.Unstance != ActivityState.NoUnstance ? n.UnstanceObj : 0;
        bool Held(ulong o) => o != 0 && (o == ns || o == nu);
        return !(Held(ps) || Held(pu));
    }

    /// <summary>C.1: a placement into the very object the copy's posture was released from within this long is held back (the hand-over
    /// and WO-160's placement never fight over one copy).</summary>
    public const long ReseatAfterReleaseMs = 2000;

    /// <summary>C.1's measure: a release and a re-seat of the same copy within this long is a seat loop.</summary>
    public const long SeatLoopMs = 5000;

    /// <summary>The objects a posture holds (for the re-seat guard): its seat or bed, its workstation.</summary>
    public static (ulong Seat, ulong Work) HeldObjects(ActivityState a)
    {
        var n = a.Normalised();
        return (ActivityState.ObjectStance(n.Stance) ? n.StanceObj : 0, n.Unstance != ActivityState.NoUnstance ? n.UnstanceObj : 0);
    }

    // ---------------------------------------------------------------- C.3: the engine's over-full queue line

    public const string OverflowMarker = "attempting to queue too many actions on entity '";

    /// <summary>C.3: the entity the engine's over-full line names ("--- Mannequin Error, attempting to queue too many actions on entity
    /// 'ttkc_man_10' (queue size = 11, but only up to 10 allowed) ---"), or null.</summary>
    public static string? OverflowEntity(string line)
    {
        int i = line.IndexOf(OverflowMarker, StringComparison.Ordinal);
        if (i < 0) return null;
        int a = i + OverflowMarker.Length, b = line.IndexOf('\'', a);
        if (b <= a || b - a > 120) return null;
        string n = line[a..b];
        foreach (char c in n) if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.')) return null;
        return n;
    }

    /// <summary>C.3: the agent asks the DLL at most this often per copy (the engine prints the line on every queue attempt).</summary>
    public const long OverflowAskEveryMs = 2000;

    // ---------------------------------------------------------------- D: a stop for the partner's crime (joint)

    /// <summary>D: the crime kinds of a stop this player never reported himself -- under the joint crime mode, his partner's.</summary>
    public static IReadOnlyList<string> PartnerKinds(IEnumerable<string> stopKinds, IReadOnlySet<string> ownKinds) =>
        stopKinds.Where(k => !ownKinds.Contains(k)).Distinct(StringComparer.Ordinal).ToList();

    public static string CrimeWord(string kind) => kind switch
    {
        "theft" => "theft", "lockpick" => "lockpicking", "trespass" => "trespassing", "horsetheft" => "horse theft", "robbody" => "robbing a body",
        "assault" => "assault", "murder" => "killing", _ => kind,
    };

    /// <summary>D: the stopped player's notice when the stop is for his partner's crime (the field: a 60 groschen fine the joiner took
    /// for "hitting the host" was the host's own lockpicking, charged to both by the shared crime setting).</summary>
    public static string JointStopText(IReadOnlyList<string> kinds) =>
        $"Shared crime is on: the guard holds you to account for your partner's {string.Join(" and ", kinds.Select(CrimeWord))}.";
}
