// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.RegularExpressions;

namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-137 -- shared quests (docs/WO-137-findings.md).
//
// Quest progress is the host's world's concept graph. The host's DLL records
// every quest State change in its own graph (the engine's setter, native
// wo137.cpp) and the host's agent sends each one to every joiner, in order;
// the joiner's DLL applies it to its copy through the State's own Set<Value>
// port (the engine's edge -- every consumer runs, journal and HUD included).
// The joiner's OWN root transitions (examine, pickup, area, a conversation's
// outcome) go to the host as requests; the host applies them to the real
// world, and they come back as ordinary changes.
//
// Two messages ride the WO-123 join channel (same header, same routing, rows
// in Protocol.JoinWire, so the relay's gate needs no code of its own):
//
//   type up/down  name        up body (after [target:1][joinId:4])       sent by
//   0x60 / 0x61   QuestHost   [kind:1][tok:4][text:1..QuestTextMax]      the host -> one joiner
//   0x62 / 0x63   QuestAsk    [kind:1][tok:4][text:1..QuestTextMax]      a joiner -> the host
//
// joinId is 0 (unused). The text is printable ASCII, space-separated, checked
// field by field on both ends (Wo137Text): paths are concept paths under
// Barbora ([A-Za-z0-9_] segments joined by '.'), ports and NPC names are engine
// names, numbers are invariant integers. Nothing a peer sends reaches the DLL
// or a Lua string without passing those checks.
//
// Host kinds (APPEND-ONLY):
//   1 Change     "<seq> <flags> <old> <new> <port|-> <questLen> <path>"
//                one State change in the host's world, in the host's order (seq)
//   2 Result     "<verdict> <hostVal> <hostPort|-> <path>"   tok = the request's
//                verdict: applied | already | refused | failed | off | held | notquest
//   3 Checkpoint "<part> <nparts> <val>:<port|->:<path> ..." the host's current values of
//                States that changed this session (the periodic compare)
//   4 Hold       "<on|off> <npc>"                            tok = the talk's: the host's NPC is held (busy)
//   5 Mode       "<on|off> <why>"                            the host's quest sync (mp_quest_sync)
// Ask kinds (APPEND-ONLY):
//   1 Request    "<flags> <old> <new> <port> <questLen> <path>"   tok = request id
//   2 Talk       "<on|off> <npc>"                                 tok = talk id
//   3 Resync     "<why>"                                          the joiner's world has just loaded:
//                                                                 the host answers with a Checkpoint now
//
// No protocol bump: two new types on the join channel. A mixed release is
// refused at the relay (WO-110 R9), so a peer that does not know them never
// shares a session with one that sends them.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    public const byte QuestHostUp = 0x60, QuestHostDown = 0x61;   // WO-137
    public const byte QuestAskUp  = 0x62, QuestAskDown  = 0x63;   // WO-137

    public const int QuestTextMax = 1400;

    // ---- host kinds (APPEND-ONLY) ----
    public const byte QuestHostChange = 1, QuestHostResult = 2, QuestHostCheckpoint = 3, QuestHostHold = 4, QuestHostMode = 5;
    // ---- ask kinds (APPEND-ONLY) ----
    public const byte QuestAskRequest = 1, QuestAskTalk = 2, QuestAskResync = 3;

    public static string QuestHostName(byte k) => k switch
    {
        QuestHostChange => "change", QuestHostResult => "result", QuestHostCheckpoint => "checkpoint",
        QuestHostHold => "hold", QuestHostMode => "mode", _ => $"unknown-{k}",
    };

    public static string QuestAskName(byte k) => k switch
    {
        QuestAskRequest => "request", QuestAskTalk => "talk", QuestAskResync => "resync", _ => $"unknown-{k}",
    };
}

/// <summary>WO-137: the field checks both ends run on every quest message.</summary>
public static partial class Wo137Text
{
    public const int PathMax = 400;

    /// <summary>A concept path under Barbora: 3..40 engine-name segments.</summary>
    [GeneratedRegex(@"^Barbora(\.[A-Za-z0-9_]{1,80}){2,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex PathRx();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRx();

    public static bool IsPath(string? s) => s is { Length: > 0 and <= PathMax } && PathRx().IsMatch(s);
    public static bool IsPort(string? s) => s is not null && NameRx().IsMatch(s);
    public static bool IsPortOrDash(string? s) => s == "-" || IsPort(s);
    public static bool IsNpc(string? s) => s is not null && NameRx().IsMatch(s);
    public static bool IsOnOff(string? s) => s is "on" or "off";

    [GeneratedRegex(@"^[a-z][a-z0-9_-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex WordRx();

    /// <summary>A short lower-case reason word ("joined", "reloaded", "mp_quest_sync-off").</summary>
    public static bool IsWord(string? s) => s is not null && WordRx().IsMatch(s);

    public static bool TryInt(string? s, out int v) =>
        int.TryParse(s, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out v);
}
