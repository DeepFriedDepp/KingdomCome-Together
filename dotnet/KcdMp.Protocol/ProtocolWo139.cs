// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.RegularExpressions;

namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-139 -- crime and guards (docs/WO-139-findings.md).
//
// The joiner's crimes are crimes in the host's world, and his own problem:
// never pinned on the host's Henry. The joiner's game still knows what counts
// as a crime (its own theft, trespass, lockpicking and horse checks), but the
// host's NPCs are suspended copies there: nobody sees him. So the joiner
// reports each crime; the host judges it in its own world -- who of its NPCs
// could see the joiner's avatar at that spot, whether a guard knows -- and
// keeps the record. A guard who knows stops the joiner; the stop itself (the
// game's own crime dialogue: fine, punishment, a fight) runs on the joiner's
// machine against his own Henry, and its outcome comes back to the host.
//
// Two messages ride the WO-123 join channel (same header, same routing, rows in
// Protocol.JoinWire, so the relay's gate needs no code of its own):
//
//   type up/down  name        up body (after [target:1][joinId:4])        sent by
//   0x64 / 0x65   CrimeAsk    [kind:1][tok:4][text:1..CrimeTextMax]      a joiner -> the host
//   0x66 / 0x67   CrimeHost   [kind:1][tok:4][text:1..CrimeTextMax]      the host -> one joiner
//
// joinId is 0 (unused). The text is printable ASCII, space-separated, checked
// field by field on both ends (Wo139Text): kinds and results are fixed words,
// names are engine names ([A-Za-z0-9_]), item classes GUIDs, numbers invariant.
// Nothing a peer sends reaches the DLL or a Lua string without those checks.
//
// Ask kinds (APPEND-ONLY):
//   1 Report     "<crime> <x> <y> <z> <victim|-> <item|-> <where>"     tok = the joiner's crime id
//                crime: theft | lockpick | trespass | horsetheft | robbody
//                where: world | stash | door | lock | horse | body | area
//   2 Outcome    "<result> <guard|-> <fine> <x> <y> <z>"               tok = the host's stop id
//                result: paid | punished | fought | fled | bribed | persuaded | talked | executed | refused | nostop
//                        | attacked (WO-154: the guard attacked a player who stood: no resist)
//                        | died (WO-154: the player died during the stop: no resist)
//                fine = what left this Henry (decagroschen); x y z = where the guard's copy ended (0 0 0 = none)
//   3 Resync     "<why>"                     the joiner's world has just loaded: the host answers with its Record
//   4 EndFights  "<why>"                     WO-154: the joiner's mp_unstuck -- every fight against his avatar ends
// Host kinds (APPEND-ONLY):
//   1 Judged     "<crime> <witnesses> <guards> <known 0|1> <settlement|->"   tok = the joiner's crime id
//                the host raised it in its world (witnesses 0 = nobody saw it: no crime there)
//   2 Stop       "<guard> <crimes> <fine>"                              tok = the stop id
//                a guard who knows stops the joiner (his machine opens the game's own crime dialogue)
//                crimes = kind:count,... ; fine = the host's sum of the crime table's fines (decagroschen)
//   3 Pursue     "<on|off> <guard>"                                     a guard fights the joiner's avatar
//   4 Record     "<open> <crimes|-> <settlements|->"                    the host's open record for this joiner
//   5 Horses     "<part> <nparts> <names|->"                            horses the host's Henry may ride
//                (his own, and any the host's world says is legal): legal for the joiner too
//   6 Cleared    "<why> <settlement|->"                                 the host cleared this joiner's record there
//   7 Mode       "<on|off> <why>"                                       mp_crime_shared on the host
//
// No protocol bump: two new types on the join channel. A mixed release is
// refused at the relay (WO-110 R9), so a peer that does not know them never
// shares a session with one that sends them.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    public const byte CrimeAskUp  = 0x64, CrimeAskDown  = 0x65;   // WO-139
    public const byte CrimeHostUp = 0x66, CrimeHostDown = 0x67;   // WO-139

    public const int CrimeTextMax = 1400;

    // ---- ask kinds (APPEND-ONLY) ----
    public const byte CrimeAskReport = 1, CrimeAskOutcome = 2, CrimeAskResync = 3, CrimeAskEndFights = 4;
    // ---- host kinds (APPEND-ONLY) ----
    public const byte CrimeHostJudged = 1, CrimeHostStop = 2, CrimeHostPursue = 3, CrimeHostRecord = 4,
                      CrimeHostHorses = 5, CrimeHostCleared = 6, CrimeHostMode = 7, CrimeHostShops = 8;

    public static string CrimeAskName(byte k) => k switch
    {
        CrimeAskReport => "report", CrimeAskOutcome => "outcome", CrimeAskResync => "resync", CrimeAskEndFights => "end-fights", _ => $"unknown-{k}",
    };

    public static string CrimeHostName(byte k) => k switch
    {
        CrimeHostJudged => "judged", CrimeHostStop => "stop", CrimeHostPursue => "pursue", CrimeHostRecord => "record",
        CrimeHostHorses => "horses", CrimeHostCleared => "cleared", CrimeHostMode => "mode", CrimeHostShops => "shops", _ => $"unknown-{k}",
    };
}

/// <summary>WO-139: the field checks both ends run on every crime message.</summary>
public static partial class Wo139Text
{
    /// <summary>The crimes a joiner reports (the host raises assault/murder/takedowns from its own world).</summary>
    public static readonly string[] ReportedCrimes = { "theft", "lockpick", "trespass", "horsetheft", "robbody" };
    /// <summary>Every crime the host's record holds.</summary>
    public static readonly string[] AllCrimes = { "theft", "lockpick", "trespass", "horsetheft", "robbody", "assault", "murder", "knockout" };
    public static readonly string[] Wheres = { "world", "stash", "door", "lock", "horse", "body", "area" };
    public static readonly string[] Results = { "paid", "punished", "fought", "fled", "bribed", "persuaded", "talked", "executed", "refused", "nostop",
                                                "attacked", "died" };   // WO-154: APPEND-ONLY

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRx();

    [GeneratedRegex(@"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex GuidRx();

    [GeneratedRegex(@"^[a-z][a-z0-9_-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex WordRx();

    /// <summary>A settlement key: engine faction segments ("trosecko_settlements_zelejov").</summary>
    [GeneratedRegex(@"^[a-z][A-Za-z0-9_]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex SettlementRx();

    public static bool IsName(string? s) => s is not null && NameRx().IsMatch(s);
    public static bool IsNameOrDash(string? s) => s == "-" || IsName(s);
    public static bool IsItem(string? s) => s is not null && GuidRx().IsMatch(s);
    public static bool IsItemOrDash(string? s) => s == "-" || IsItem(s);
    public static bool IsWord(string? s) => s is not null && WordRx().IsMatch(s);
    public static bool IsSettlement(string? s) => s is not null && SettlementRx().IsMatch(s);
    public static bool IsSettlementOrDash(string? s) => s == "-" || IsSettlement(s);
    public static bool IsReportedCrime(string? s) => s is not null && Array.IndexOf(ReportedCrimes, s) >= 0;
    public static bool IsCrime(string? s) => s is not null && Array.IndexOf(AllCrimes, s) >= 0;
    public static bool IsWhere(string? s) => s is not null && Array.IndexOf(Wheres, s) >= 0;
    public static bool IsResult(string? s) => s is not null && Array.IndexOf(Results, s) >= 0;
    public static bool IsOnOff(string? s) => s is "on" or "off";

    /// <summary>A world coordinate: invariant decimal, inside the map's plausible range.</summary>
    public static bool TryCoord(string? s, out float v)
    {
        v = 0;
        if (s is null || s.Length > 12) return false;
        if (!float.TryParse(s, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
                            System.Globalization.CultureInfo.InvariantCulture, out v)) return false;
        return float.IsFinite(v) && v > -100000f && v < 100000f;
    }

    public static bool TryInt(string? s, out int v) =>
        int.TryParse(s, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out v);
}
