// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-208 B (mp_defence_local, on; docs/WO-208-findings.md): the joiner's own defence. A copy of the host's NPC swings at the joiner inside
// his fight (WO-132's engage, WO-166's strike window); his own engine judges the blow against his real guard -- hit, blocked, perfect
// block, guard broken, or missed (his dodge) -- and keeps what it applied (native hits.cpp's defence watch, wo166's missed window). The
// outcome goes to the host (W203 DefenceOutcome); the host, whose engine judged the same blow against the figure, holds its own verdict
// until the joiner's outcome arrives (the swing's window plus one round trip, at most 450 ms) and sends none when it does. With no
// outcome in time the host's verdict goes as before (WO208-DEF fallback), and the joiner applies no host verdict on an attacker it judged
// itself a moment ago (one blow, one judge). The rules are Wo208Rules (Wo208.cs).
//
//   WO208-DEF out npc=<n> outcome=<o> hp=<f> st=<f> seq=<n>                    (joiner: judged here, sent)
//   WO208-DEF in npc=<n> joiner=<g> outcome=<o> hp st seq                      (host)
//   WO208-DEF outcome npc=<n> joiner=<g> engine=<o> dmg=<hp>/<st> host_said=<v> waited_ms=<n>   (host: the joiner's engine judged it)
//   WO208-DEF fallback npc=<n> joiner=<g> host_said=<v> waited_ms=<n> [joiner=none] (host: no outcome in time, or the joiner's window
//                                                                                 closed with nothing landed -- the host's verdict)
//   WO208-DEF skipped npc=<n> host verdict hid=<n> -- judged here <ms> ago       (joiner)
public partial class GameBridge
{
    private sealed class W208Outcome { public long At; public byte Outcome; public float Hp, St; public uint Seq; public bool Used; }
    private readonly ConcurrentDictionary<string, long> _w208JudgedAt = new(StringComparer.Ordinal);       // joiner: attacker -> judged here (ms)
    private readonly ConcurrentDictionary<(byte, string), List<W208Outcome>> _w208Outcomes = new();       // host: (joiner, attacker) -> outcomes in
    private uint _w208DefSeq;
    private long _w208DefOut, _w208DefIn, _w208DefJudged, _w208DefFallback, _w208DefSkipped, _w208DefNoName, _w208DefBad, _w208DefMissed, _w208DefUnanswered, _w208DefNoneIn;

    partial void Wo203DefenceOnConnect(CancellationToken ct)
    {
        _combat.OnDefence = Wo208OnDefenceAsync;
        _ = Wo208StreamLoopAsync(ct);   // A.2: the host's NPCs as state (GameBridge.Wo208Stream.cs)
    }

    partial void Wo203DefenceOnDisconnect()
    {
        _combat.OnDefence = null;
        _w208JudgedAt.Clear();
        _w208Outcomes.Clear();
    }

    // ---------------------------------------------------------------- the joiner

    /// <summary>The joiner: this machine's engine judged an engaged copy's blow (native 0xAE) -- to the host.</summary>
    private async Task Wo208OnDefenceAsync(uint eid, byte outcome, float st, float hp)
    {
        if (!W137Joiner) return;
        string? npc = null;
        foreach (var (n, e) in _npcEntityIds) if (e == eid) { npc = n; break; }
        if (npc is null)
        {
            Interlocked.Increment(ref _w208DefNoName);
            Console.WriteLine(FormattableString.Invariant($"WO208-DEF out eid=0x{eid:X} outcome={Wo208Rules.DefName(outcome)} -- no copy known by that id here: not sent"));
            return;
        }
        if (Wo208Rules.IsJudgement(outcome)) _w208JudgedAt[npc] = Environment.TickCount64;   // none: the host's verdict is applied here as before
        uint seq = Interlocked.Increment(ref _w208DefSeq);
        Interlocked.Increment(ref _w208DefOut);
        if (outcome == Wo208Rules.DefMissed) Interlocked.Increment(ref _w208DefMissed);
        await Wo203SendSideAsync(Protocol.JoinTargetHost, Protocol.W203DefenceOutcome, Wo208Rules.DefenceText(npc, outcome, Math.Max(0, hp), Math.Max(0, st), seq));
        Console.WriteLine(FormattableString.Invariant($"WO208-DEF out npc={npc} outcome={Wo208Rules.DefName(outcome)} hp={hp:F1} st={st:F1} seq={seq}"));
    }

    /// <summary>The joiner (Wo161JudgeApplyAsync): the host's verdict on this attacker is not applied -- its blow was judged here.</summary>
    private bool Wo208SkipHostVerdict(string attacker, uint hitId)
    {
        if (attacker.Length == 0 || !_w208JudgedAt.TryGetValue(attacker, out long at)) return false;
        long now = Environment.TickCount64;
        if (!Wo208Rules.JudgedHere(at, now, W208("defence_local"))) return false;
        Interlocked.Increment(ref _w208DefSkipped);
        Console.WriteLine(FormattableString.Invariant($"WO208-DEF skipped npc={attacker} host verdict hid={hitId} -- this machine's engine judged its blow {now - at} ms ago"));
        return true;
    }

    // ---------------------------------------------------------------- the host

    /// <summary>The host: a joiner's own judgement of a copy's blow arrived (W203 DefenceOutcome).</summary>
    partial void Wo203DefenceOnFrame(byte src, LootMsg m)
    {
        if (m.Kind != Protocol.W203DefenceOutcome) return;
        if (!Wo208Rules.TryParseDefence(m.Text, out string npc, out byte outcome, out float hp, out float st, out uint seq))
        {
            Interlocked.Increment(ref _w208DefBad);
            Console.WriteLine($"WO208-DEF in from {src}: malformed '{m.Text}'");
            return;
        }
        if (!W137Host) return;
        Interlocked.Increment(ref _w208DefIn);
        long now = Environment.TickCount64;
        var list = _w208Outcomes.GetOrAdd((src, npc), _ => new List<W208Outcome>());
        lock (list)
        {
            list.RemoveAll(o => now - o.At > 5000);
            list.Add(new W208Outcome { At = now, Outcome = outcome, Hp = hp, St = st, Seq = seq });
        }
        Console.WriteLine(FormattableString.Invariant($"WO208-DEF in npc={npc} joiner={src} outcome={Wo208Rules.DefName(outcome)} hp={hp:F1} st={st:F1} seq={seq}"));
    }

    /// <summary>
    /// The host (Wo161SendHitAsync): its NPC's blow landed on joiner <paramref name="ghost"/>'s figure. Wait for that joiner's own
    /// judgement of the copy's blow (at most Wo208Rules.HoldMs); judged = the host sends no verdict.
    /// </summary>
    private async Task<(bool Judged, byte Outcome, float Hp, float St, long WaitedMs)> Wo208AwaitOutcomeAsync(byte ghost, string attacker)
    {
        if (!W208("defence_local") || attacker.Length == 0) return (false, 0, 0, 0, 0);
        long blow = Environment.TickCount64;
        int rtt = _clockRttMedianMs is double r ? (int)Math.Min(1000.0, Math.Max(0.0, r)) : 0;
        int hold = Wo208Rules.HoldMs(Wo208Rules.HoldCapMs, rtt);
        while (true)
        {
            if (_w208Outcomes.TryGetValue((ghost, attacker), out var list))
            {
                lock (list)
                {
                    // a judgement first; none (the joiner's window closed with nothing landed) only when no judgement answers: the host's
                    // own verdict then, at once (no hold to the cap)
                    var o = list.FirstOrDefault(x => !x.Used && Wo208Rules.IsJudgement(x.Outcome) && Wo208Rules.Answers(x.At, blow, hold))
                            ?? list.FirstOrDefault(x => !x.Used && Wo208Rules.Answers(x.At, blow, hold));
                    if (o is not null)
                    {
                        o.Used = true;
                        return (Wo208Rules.IsJudgement(o.Outcome), o.Outcome, o.Hp, o.St, Environment.TickCount64 - blow);
                    }
                }
            }
            long waited = Environment.TickCount64 - blow;
            if (waited >= hold) return (false, 0, 0, 0, waited);
            await Task.Delay(15);
        }
    }

    /// <summary>The host: the minute line -- outcomes that answered no blow of the host's (the joiner's engine landed one the host's missed).</summary>
    private void Wo208DefenceTick()
    {
        long now = Environment.TickCount64;
        foreach (var (key, list) in _w208Outcomes)
        {
            lock (list)
            {
                foreach (var o in list.Where(o => !o.Used && now - o.At > Wo208Rules.HoldCapMs + 200).ToList())
                {
                    list.Remove(o);
                    Interlocked.Increment(ref _w208DefUnanswered);
                    if (o.Outcome != Wo208Rules.DefMissed)
                        Console.WriteLine(FormattableString.Invariant(
                            $"WO208-DEF unanswered npc={key.Item2} joiner={key.Item1} outcome={Wo208Rules.DefName(o.Outcome)} hp={o.Hp:F1} -- no blow of the host's on the figure matched it (the joiner's engine is the judge: it stands)"));
                }
            }
        }
    }

    private long _w208RecoilPlayed, _w208RecoilNone;

    /// <summary>
    /// B.4 (host, mp_defence_recoil): the joiner's engine blocked or perfectly blocked this NPC's stab that the host's engine had as a hit --
    /// the game's own answer, the attacker's failed-attack recoil (WO-165's picker: the row for the swing's weapon, a perfect block's own),
    /// plays on the host's NPC through its own controller, so its recoil and the riposte window after it are the same on both screens (its
    /// copy follows the host's NPC). A weapon with no recoil row (blunt, unarmed) plays nothing, as in the game.
    /// </summary>
    private async Task Wo208AttackerRecoilAsync(string attacker, byte outcome, Wo161SwingLedger.Swing? sw, HitVerdict hostSaid)
    {
        ActionRowCatalog? cat = _rowCatalog.IsCompletedSuccessfully ? _rowCatalog.Result : null;
        string why;
        ActionRowCatalog.Row? fr = null;
        if (cat is null) why = "no-catalog";
        else if (sw is not { } s || !cat.TryGet(s.Row, out var swing)) why = "no-swing-row";
        else if ((fr = cat.FailedAttackRow(swing, outcome == Wo208Rules.DefPerfect)) is null) why = "no-row-for-this-weapon";
        else if (await _combat.Wo132WatchAsync(true, attacker) is not uint eid) { why = "no-entity"; fr = null; }
        else
        {
            var r = await _combat.GhostSwingForResultAsync(eid, fr.Value.Spec);
            why = r.ReasonTag;
            if (!r.Ok) fr = null;
        }
        if (fr is null) Interlocked.Increment(ref _w208RecoilNone); else Interlocked.Increment(ref _w208RecoilPlayed);
        Console.WriteLine($"WO208-DEF recoil npc={attacker} engine={Wo208Rules.DefName(outcome)} host_said={HitVerdictMsg.VerdictName(hostSaid)} row={(fr is { } x ? $"\"{x.Spec}\"" : "none")} why={why}");
    }

    private void Wo208NoteJudged(byte ghost, string attacker, byte outcome, float hp, float st, HitVerdict hostSaid, long waited)
    {
        Interlocked.Increment(ref _w208DefJudged);
        Console.WriteLine(FormattableString.Invariant(
            $"WO208-DEF outcome npc={attacker} joiner={ghost} engine={Wo208Rules.DefName(outcome)} dmg={hp:F1}/{st:F1} host_said={HitVerdictMsg.VerdictName(hostSaid)} waited_ms={waited} -- the joiner's own engine judged it: no verdict sent"));
    }

    private void Wo208NoteFallback(byte ghost, string attacker, HitVerdict hostSaid, long waited, byte joinerSaid)
    {
        Interlocked.Increment(ref _w208DefFallback);
        bool none = joinerSaid == Wo208Rules.DefNone;
        if (none) Interlocked.Increment(ref _w208DefNoneIn);
        Console.WriteLine(FormattableString.Invariant(
            $"WO208-DEF fallback npc={attacker} joiner={ghost} host_said={HitVerdictMsg.VerdictName(hostSaid)} waited_ms={waited}{(none ? " joiner=none -- the joiner's window closed with nothing landed: the host's verdict at once" : " -- no outcome from the joiner in time: the host's verdict")}"));
    }

    partial void Wo203DefenceStats(ref string line) =>
        line += $" | defence_local={(W208("defence_local") ? "on" : "off")} out={Interlocked.Read(ref _w208DefOut)} missed={Interlocked.Read(ref _w208DefMissed)} in={Interlocked.Read(ref _w208DefIn)}"
              + $" judged={Interlocked.Read(ref _w208DefJudged)} fallback={Interlocked.Read(ref _w208DefFallback)} none_in={Interlocked.Read(ref _w208DefNoneIn)} skipped={Interlocked.Read(ref _w208DefSkipped)}"
              + $" unanswered={Interlocked.Read(ref _w208DefUnanswered)} no_name={Interlocked.Read(ref _w208DefNoName)} bad={Interlocked.Read(ref _w208DefBad)}"
              + $" recoil={(W208("defence_recoil") ? "on" : "off")} recoils={Interlocked.Read(ref _w208RecoilPlayed)} recoil_none={Interlocked.Read(ref _w208RecoilNone)}";
}
