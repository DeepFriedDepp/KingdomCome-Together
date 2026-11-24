// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-223 -- enemies notice the joiner, and fight him on both screens (docs/WO-223-findings.md).
//
// W203Stealth = 13 (ProtocolWo203.cs), text = WO-195's stealth snapshot "c=,l=,v=,n=,s=,k=" unchanged: the joiner's own crouch, light,
// visibility, noise, conspicuousness and stealth skill as his own game reads them, sent to the host on every change (at most once a
// second) and every ten seconds -- the host's gate for which hostile NPCs attend to his figure. The relay passes it as every W203 row.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    /// <summary>The DLL's ops (native/KCDMP/wo223.h): 210 Config [bits:4][role:1] -> [bits:4], 211 Status [page:1] -> text.</summary>
    public const byte Wo223OpConfig = 210, Wo223OpStatus = 211;
    /// <summary>B: 212 Attend [on:1][npc eid:4][figure eid:4] -> [result:1]; 213 AttendSet "figure name|npc,npc,..." (ASCII) ->
    /// [set:1][cleared:1][refused:1]: the hostile NPCs that attend to this figure now (the pairs of his not named are cleared).</summary>
    public const byte Wo223OpAttend = 212, Wo223OpAttendSet = 213;
    /// <summary>B.3: "&lt;npc name&gt;" -- the enemy the host's mod called on the host's own player; the DLL pins it to him for a few seconds.</summary>
    public const byte Wo223OpCallPin = 215;
    /// <summary>B: 214 Engine -> the engine's own recognition numbers as text ("minR=..,minVis=..,tP=..,tConPos=..,tConNeg=..,gain=..,
    /// decay=..,npcMod=..,skT=..,skR=.."), read by the DLL from the game's parameter object; empty = not read on this build.</summary>
    public const byte Wo223OpEngine = 214;
}

/// <summary>
/// WO-223's switches (`mp_&lt;name&gt; on|off` in the game's console; `mp_w223_status` lists them); the bench's twin turns one off for a
/// run through <see cref="EnvVar"/> -- read by both agents and both DLLs.
/// </summary>
public static class W223Switch
{
    public const string EnvVar = "KCDMP_TEST_W223_OFF";

    /// <summary>The native word's bits (wo223.h), in order: w223_perceive (B: hostile NPCs notice a joiner's figure as they would a
    /// player), w223_copy_target (C: on the joiner's screen a copy fights whom its host NPC fights), w223_combat_mode (D: the engine's
    /// combat mode decides a hit reaction, WO-222's offer for a blow it still reads out of it), w223_guard (E: what the old rules protect stays protected), w223_notice_log (F: who
    /// noticed whom first, whom each enemy fights on both screens).</summary>
    public static readonly string[] NativeNames = ["w223_perceive", "w223_copy_target", "w223_combat_mode", "w223_guard", "w223_notice_log"];

    /// <summary>Every switch: the native ones and the mod's own w223_split (B: with both players at an enemy and none on the host
    /// yet, a second enemy is left to the host for a moment -- the mod's naming only, no native bit).</summary>
    public static readonly string[] Names = [.. NativeNames, "w223_split"];

    public static bool TwinOff(string name) => TwinOff(Environment.GetEnvironmentVariable(EnvVar), name);

    public static bool TwinOff(string? env, string name) =>
        !string.IsNullOrWhiteSpace(env) && env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => x == name || (x == "all" && name != "w223_notice_log"));   // the notice log is an instrument: every run keeps it

    public static uint NativeBits(Func<string, bool> on)
    {
        uint b = 0;
        for (int i = 0; i < NativeNames.Length; i++) if (on(NativeNames[i])) b |= 1u << i;
        return b;
    }
}

/// <summary>WO-223's pure rules (the agent's half).</summary>
public static class Wo223Rules
{
    /// <summary>The mod's attend line "figure|npc,npc,...": a figure name of the mod's own form and NPC names the game could have
    /// given (letters, digits, '_', '-', '.', '(', ')'), at most <see cref="MaxNames"/> of them; anything else is refused whole.</summary>
    public const int MaxNames = 24, MaxText = 900;

    /// <summary>The mod's w223_call argument: one entity name (the same alphabet as an attend name).</summary>
    public static bool TryCallName(string? arg, out string name)
    {
        name = (arg ?? "").Trim();
        return name.Length is > 0 and <= 64 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '(' or ')');
    }

    public static bool TryAttendText(string? arg, out string text)
    {
        text = "";
        if (string.IsNullOrEmpty(arg) || arg.Length > MaxText) return false;
        int bar = arg.IndexOf('|');
        if (bar <= 0) return false;
        string fig = arg[..bar];
        if (!fig.StartsWith("kcd2mp_", StringComparison.Ordinal) || fig.Length > 16 || !fig.AsSpan(7).ToString().All(char.IsAsciiDigit) || fig.Length == 7) return false;
        string[] names = arg[(bar + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (names.Length > MaxNames) return false;
        foreach (string n in names)
            if (n.Length is 0 or > 64 || !n.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '(' or ')')) return false;
        text = fig + "|" + string.Join(',', names);
        return true;
    }

    /// <summary>The DLL's engine text (op 214) before it goes into a console line: "name=number" items joined by ',', nothing else
    /// (letters, digits, '.', '-', '=' and ','), at most 240 characters.</summary>
    public static bool TryEngineText(string? s, out string text)
    {
        text = "";
        if (string.IsNullOrEmpty(s) || s.Length > 240) return false;
        foreach (string kv in s.Split(','))
        {
            int eq = kv.IndexOf('=');
            if (eq <= 0 || eq == kv.Length - 1 || !kv.AsSpan(0, eq).ToString().All(char.IsAsciiLetter)) return false;
            if (!double.TryParse(kv.AsSpan(eq + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || !double.IsFinite(v)) return false;
            if (!kv.AsSpan(eq + 1).ToString().All(c => char.IsAsciiDigit(c) || c is '.' or '-')) return false;
        }
        text = s;
        return true;
    }

    // ---------------------------------------------------------------- C: the host's wait for the joiner's own verdict

    /// <summary>WO-222 capped the host's wait for the joiner's own verdict at 1.5 s, because half its waits were never answered (his
    /// copies fought the host's figure on his screen: each such blow cost the whole wait). With his copies in his fight every blow is
    /// answered, and what is left of the host's guesses are answers that came a stall late (the field's link: two stalls a minute of
    /// up to 0.9 s, each way). While at least <see cref="AnsweredPct"/> % of his last waits (at least <see cref="AnsweredMin"/> known)
    /// were answered -- in time or late -- the wait may run to <see cref="LongCapMs"/>; otherwise WO-222's cap stands.</summary>
    public const int LongCapMs = 3000, AnsweredPct = 90, AnsweredMin = 10;

    public static int DefCapMs(int answered, int total, int shortCapMs) =>
        total >= AnsweredMin && answered * 100 >= total * AnsweredPct ? LongCapMs : shortCapMs;

    /// <summary>Two blows of one attacker can wait at once (a combo, a chain of swings), and each took the first answer that fitted
    /// its own window: a later blow took the answer an earlier one was still waiting for, and the earlier one ran out to the host's
    /// guess (a third of the guesses left in the two-player camp scene). An answer that arrived at <paramref name="answerMs"/> goes to
    /// <paramref name="blowMs"/> only when no older blow still waiting (<paramref name="waitingMs"/>) could take it.</summary>
    public static bool OldestFirst(long answerMs, long blowMs, IReadOnlyList<long> waitingMs, int earlyMs)
    {
        foreach (long b in waitingMs) if (b < blowMs && answerMs >= b - earlyMs) return false;
        return true;
    }

    /// <summary>"Nothing landed and no dodge" (his copy's attack ended with no evidence) answers a blow only when it was told after that
    /// blow landed -- one told before it speaks of the attack before -- and only <see cref="NoneGraceMs"/> after it came: his engine's
    /// verdict on a blow that lands as the attack ends reaches the host a frame behind the "nothing".</summary>
    public const int NoneGraceMs = 300;

    public static bool NoneAnswers(long noneMs, long blowMs, long nowMs) => noneMs >= blowMs && nowMs - noneMs >= NoneGraceMs;

    /// <summary>A blow struck inside a grab (the game's paired clinch rows: fragment "...Sync...", a "clinch" tag). The joiner's game
    /// runs no grab between its player and a copy (a known limit), so his engine never judges such a blow: the host's own verdict
    /// goes at once instead of after the whole wait.</summary>
    public static bool GrabRow(string fragment, string tags) =>
        fragment.Contains("Sync", StringComparison.Ordinal) && tags.Contains("clinch", StringComparison.OrdinalIgnoreCase);

    /// <summary>The last waits of one joiner: answered (in time, or late) or never.</summary>
    public sealed class AnswerTally
    {
        private readonly bool[] _v;
        private int _n, _at;
        public AnswerTally(int size = 40) { _v = new bool[size]; }
        public void Add(bool answered) { lock (_v) { _v[_at] = answered; _at = (_at + 1) % _v.Length; if (_n < _v.Length) _n++; } }
        /// <summary>A late answer came for the newest wait counted as never answered.</summary>
        public void LateAnswer()
        {
            lock (_v)
            {
                for (int k = 1; k <= _n; k++)
                {
                    int i = ((_at - k) % _v.Length + _v.Length) % _v.Length;
                    if (!_v[i]) { _v[i] = true; return; }
                }
            }
        }
        public (int Answered, int Total) Read() { lock (_v) { int a = 0; for (int i = 0; i < _n; i++) if (_v[i]) a++; return (a, _n); } }
    }

    /// <summary>The joiner's stealth snapshot goes to the host when it changed and <paramref name="sinceMs"/> since the last send is at
    /// least a second, or every ten seconds unchanged (a host that joined the session late, a lost row).</summary>
    public static bool StealthDue(string now, string sent, long sinceMs) =>
        now.Length > 0 && ((now != sent && sinceMs >= 1000) || sinceMs >= 10_000);
}
