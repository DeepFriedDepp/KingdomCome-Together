using System.Globalization;

namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-140 -- sleeping together (docs/WO-140-findings.md).
//
// One clock: the host's. Sleep together, or not at all. A player who picks
// "Sleep" at a bed (or presses Wait) is held before the lie-down and the time
// picker; the others are asked; only if everyone says yes does his sleep carry
// on, and when he confirms the length everyone else's game shows its own sleep
// screen for the same hours, wherever they stand.
//
// One message rides the WO-123 join channel (same header, same routing, a row
// in Protocol.JoinWire; "Either": a joiner's goes to the host (target 0xFF),
// the host's to one joiner by id -- the relay needs no code of its own):
//
//   type up/down  name        up body (after [target:1][joinId:4])
//   0x68 / 0x69   SleepVote   [kind:1][tok:4][text:1..SleepTextMax]
//
// The host coordinates: a joiner's ask goes to the host, which asks its own
// player and every other joiner, and answers the asker once everyone has.
// tok = the vote id, chosen by the asker (the asker's ghost id rides the text,
// so a vote forwarded by the host still names who asked).
//
// Kinds (APPEND-ONLY):
//   1 Ask     "<sleep|wait> <asker> <save 0|1>"          who wants to skip time, and how
//   2 Answer  "<yes|no|timeout|busy> <asker>"            one player's answer (the host's
//                                                        answer to a joiner asker is everyone's)
//   3 Begin   "<sleep|wait> <asker> <hours> <save 0|1>"  the asker confirmed the length:
//                                                        every accepter's sleep screen starts
//   4 Cancel  "<why> <asker>"                             the vote is over before a Begin
//                                                        (the asker backed out of his picker,
//                                                        left, or the vote was refused); after a
//                                                        Begin, why = woke: the host or the asker
//                                                        woke, everyone wakes (their skip stops)
//
// save = the asker's bed is one the game saves on (EntityModule.
// WillSleepingOnThisBedSave): the host makes the game's own rest save after the
// shared sleep even when the bed was on a joiner's machine (a joiner never saves).
//
// No protocol bump: one new type on the join channel. A mixed release is
// refused at the relay (WO-110 R9), so a peer that does not know it never
// shares a session with one that sends it.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    public const byte SleepVoteUp = 0x68, SleepVoteDown = 0x69;   // WO-140

    public const int SleepTextMax = 120;

    // ---- kinds (APPEND-ONLY) ----
    public const byte SleepAsk = 1, SleepAnswer = 2, SleepBegin = 3, SleepCancel = 4;

    /// <summary>How long a vote waits for answers (the prompt shows the same seconds).</summary>
    public const int SleepVoteTimeoutSeconds = 30;

    public static string SleepVoteName(byte k) => k switch
    {
        SleepAsk => "ask", SleepAnswer => "answer", SleepBegin => "begin", SleepCancel => "cancel", _ => $"unknown-{k}",
    };
}

/// <summary>WO-140: the field checks both ends run on every SleepVote text.</summary>
public static class Wo140Text
{
    public static readonly string[] Kinds = { "sleep", "wait" };
    public static readonly string[] Answers = { "yes", "no", "timeout", "busy" };
    public static readonly string[] CancelWhys = { "backed-out", "refused", "timeout", "left", "busy", "not-shown", "lost", "woke" };

    public const float MaxHours = 24f;

    public static bool IsKind(string? s) => s is not null && Array.IndexOf(Kinds, s) >= 0;
    public static bool IsAnswer(string? s) => s is not null && Array.IndexOf(Answers, s) >= 0;
    public static bool IsCancelWhy(string? s) => s is not null && Array.IndexOf(CancelWhys, s) >= 0;

    public static string Ask(string kind, byte asker, bool save) =>
        FormattableString.Invariant($"{kind} {asker} {(save ? 1 : 0)}");
    public static string Answer(string answer, byte asker) => FormattableString.Invariant($"{answer} {asker}");
    public static string Begin(string kind, byte asker, float hours, bool save) =>
        FormattableString.Invariant($"{kind} {asker} {hours:0.###} {(save ? 1 : 0)}");
    public static string Cancel(string why, byte asker) => FormattableString.Invariant($"{why} {asker}");

    public static bool TryAsk(string text, out string kind, out byte asker, out bool save)
    {
        kind = ""; asker = 0; save = false;
        var f = text.Split(' ');
        if (f.Length != 3 || !IsKind(f[0]) || !TryId(f[1], out asker) || f[2] is not ("0" or "1")) return false;
        kind = f[0]; save = f[2] == "1";
        return true;
    }

    public static bool TryAnswer(string text, out string answer, out byte asker)
    {
        answer = ""; asker = 0;
        var f = text.Split(' ');
        if (f.Length != 2 || !IsAnswer(f[0]) || !TryId(f[1], out asker)) return false;
        answer = f[0];
        return true;
    }

    public static bool TryBegin(string text, out string kind, out byte asker, out float hours, out bool save)
    {
        kind = ""; asker = 0; hours = 0; save = false;
        var f = text.Split(' ');
        if (f.Length != 4 || !IsKind(f[0]) || !TryId(f[1], out asker) || f[3] is not ("0" or "1")) return false;
        if (!float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out hours)) return false;
        if (!float.IsFinite(hours) || hours <= 0f || hours > MaxHours) return false;
        kind = f[0]; save = f[3] == "1";
        return true;
    }

    public static bool TryCancel(string text, out string why, out byte asker)
    {
        why = ""; asker = 0;
        var f = text.Split(' ');
        if (f.Length != 2 || !IsCancelWhy(f[0]) || !TryId(f[1], out asker)) return false;
        why = f[0];
        return true;
    }

    private static bool TryId(string s, out byte id) =>
        byte.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out id);
}
