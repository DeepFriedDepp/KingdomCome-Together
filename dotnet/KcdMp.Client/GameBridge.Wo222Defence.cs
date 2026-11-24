// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-222 C (def_wait, on; docs/WO-222-findings.md): a blow on the joiner is judged on the joiner's screen. The host's wait for his own
// verdict follows the link (GameBridge.Wo208Defence.cs: the 95th percentile of how long his verdicts took, a silent link keeps it open);
// when a host guess was applied here all the same and this machine's engine then judges the same blow, the guess is given back -- the
// health and stamina it took, through the soul's own state (the game's scriptbind) -- and counted. The rules: Wo222Rules.
//   W222-DEF fallback npc=<n> joiner=<g> hold_ms=<n> waited_ms=<n> silent_seen=<0|1> samples=<n>      (host)
//   W222-DEF corrected npc=<n> hid=<n> host_said=<v> own=<o> gave_back=<hp>/<st> after_ms=<n> contradicted=<0|1>   (joiner)
public partial class GameBridge
{
    private readonly ConcurrentDictionary<byte, Wo222Rules.DelayRing> _w222DefDelays = new();
    private readonly List<Wo222Rules.Applied> _w222Applied = new();   // joiner: the host's guesses applied here (lock: itself)
    private long _w222DefFallbacks, _w222DefApplied, _w222DefCorrected, _w222DefContradicted;

    /// <summary>The joiner: a host verdict was applied here (the host's guess for a blow on this player).</summary>
    private void Wo222NoteApplied(string attacker, uint hitId, float hp, float st, HitVerdict verdict)
    {
        if (!W137Joiner) return;
        Interlocked.Increment(ref _w222DefApplied);
        lock (_w222Applied)
        {
            long now = Environment.TickCount64;
            _w222Applied.RemoveAll(a => now - a.AtMs > Wo222Rules.CorrectWithinMs * 2);
            _w222Applied.Add(new Wo222Rules.Applied(attacker, hitId, hp, st, now, (byte)verdict));
        }
    }

    /// <summary>The joiner: this machine's engine judged a blow of <paramref name="npc"/> -- a host guess for the same blow applied a moment
    /// ago is given back (one blow, one judge: the joiner's engine).</summary>
    private void Wo222OwnJudgement(string npc, byte outcome)
    {
        if (!W137Joiner || !W222On("def_wait")) return;
        Wo222Rules.Applied? a;
        long now = Environment.TickCount64;
        lock (_w222Applied)
        {
            a = Wo222Rules.Overruled(_w222Applied, npc, now);
            if (a is not null) a.Settled = true;
        }
        if (a is null) return;
        bool contra = Wo222Rules.Contradicts(outcome, (HitVerdict)a.HostVerdict);
        Interlocked.Increment(ref _w222DefCorrected);
        if (contra) Interlocked.Increment(ref _w222DefContradicted);
        _ = ExecLuaAsync(string.Create(CultureInfo.InvariantCulture, $"if KCD2MP_W222GiveBack then KCD2MP_W222GiveBack({a.Hp:F3}, {a.St:F3}, \"{EscapeLua(npc)}\") end"));
        Console.WriteLine(FormattableString.Invariant(
            $"W222-DEF corrected npc={npc} hid={a.HitId} host_said={HitVerdictMsg.VerdictName((HitVerdict)a.HostVerdict)} own={Wo208Rules.DefName(outcome)} gave_back={a.Hp:F1}/{a.St:F1} after_ms={now - a.AtMs} contradicted={(contra ? 1 : 0)} -- this machine's engine judged the same blow: the host's guess is given back"));
    }

    private readonly ConcurrentDictionary<(byte, string), long> _w222LastFallback = new();
    private long _w222DefLate;

    /// <summary>The host: its guess for this joiner's blow by <paramref name="attacker"/> (landed at <paramref name="blowMs"/>) went (the wait ran out).</summary>
    private void Wo222NoteFallback(byte ghost, string attacker, long blowMs) => _w222LastFallback[(ghost, attacker)] = blowMs;

    /// <summary>The host: a verdict of the joiner's own engine arrived after the host's guess for the same attacker went -- the joiner applies
    /// his own (his game skips the host's verdict or gives it back); counted, and its delay joins the sample (the next waits cover it).</summary>
    private void Wo222NoteLateAnswer(byte ghost, string attacker, byte outcome, long nowMs)
    {
        if (!Wo208Rules.IsJudgement(outcome) || !_w222LastFallback.TryGetValue((ghost, attacker), out long blow) || nowMs - blow > Wo222Rules.CorrectWithinMs) return;
        _w222LastFallback.TryRemove((ghost, attacker), out _);
        Interlocked.Increment(ref _w222DefLate);
        if (_w223Answers.TryGetValue(ghost, out var tally)) tally.LateAnswer();   // WO-223 C: answered after all
        if (Wo222Rules.DelaySample(nowMs, blow)) _w222DefDelays.GetOrAdd(ghost, _ => new Wo222Rules.DelayRing()).Add((int)(nowMs - blow));
        Console.WriteLine(FormattableString.Invariant($"W222-DEF late npc={attacker} joiner={ghost} own={Wo208Rules.DefName(outcome)} after_blow_ms={nowMs - blow} -- his engine's verdict came after the host's guess: his game keeps his own"));
    }

    private string Wo222DefStats()
    {
        var holds = string.Join(',', _w222DefDelays.Select(kv => $"{kv.Key}:{Wo222Rules.DefHoldMs(kv.Value.Snapshot())}"));
        return $" def: fallbacks={Interlocked.Read(ref _w222DefFallbacks)} late_answers={Interlocked.Read(ref _w222DefLate)} holds_ms={(holds.Length == 0 ? "-" : holds)}"
             + $" guesses_applied_here={Interlocked.Read(ref _w222DefApplied)} corrected={Interlocked.Read(ref _w222DefCorrected)} contradicted={Interlocked.Read(ref _w222DefContradicted)}";
    }
}

// WO-222 F.1 (dead_lay, on; joiner): a dead copy left standing is laid down. Every 2 s the copies this joiner has as dead (WO-195's
// release ran) within the last two minutes are read (op 57: dead, living physics); one dead and still on its living physics 4 s after its
// death handling began is finished the engine's way again (op 142: its ragdoll and its dead state), then -- still standing -- given the
// game's ragdoll (WO-195's), at most three tries a body. W222-DEADLAY npc=<n> try=<k> how=<finish|ragdoll> after=<down|still-upright>.
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, int> _w222LayTries = new(StringComparer.Ordinal);
    private int _w222LayBusy;
    private long _w222LayChecks, _w222LayDown, _w222LayStill;

    private void Wo222DeadLayTick()
    {
        if (!W137Joiner || !W222On("dead_lay") || Interlocked.CompareExchange(ref _w222LayBusy, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                long now = Environment.TickCount64;
                foreach (var (npc, at) in _w195Dead.ToArray())
                {
                    if (now - at < 4000 || now - at > 120_000) continue;
                    int tries = _w222LayTries.TryGetValue(npc, out int t) ? t : 0;
                    if (tries >= 3) continue;
                    var nb = System.Text.Encoding.ASCII.GetBytes(npc);
                    if (nb.Length == 0 || nb.Length > 63) continue;
                    uint eid = _w131Guarded.TryGetValue(npc, out uint ge) ? ge : _npcEntityIds.TryGetValue(npc, out uint pe) ? pe : 0;
                    byte[] a;
                    if (eid != 0) { a = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(a, eid); }
                    else { a = new byte[5 + nb.Length]; a[4] = (byte)nb.Length; nb.CopyTo(a, 5); }
                    var r = await _combat.Wo163Async(Protocol.Wo195OpCheck, a);
                    Interlocked.Increment(ref _w222LayChecks);
                    if (r is not { Ok: true, Payload.Length: >= 4 } x || !Wo195Rules.Upright(x.Payload[0], x.Payload[2])) continue;
                    _w222LayTries[npc] = ++tries;
                    string how = "finish";
                    var f = await Wo210FinishBodyAsync(npc, eid);
                    await Task.Delay(Wo195Rules.RagdollSettleMs);
                    var r2 = await _combat.Wo163Async(Protocol.Wo195OpCheck, a);
                    bool still = r2 is { Ok: true, Payload.Length: >= 4 } y && Wo195Rules.Upright(y.Payload[0], y.Payload[2]);
                    if (still)
                    {
                        how = f is null ? "ragdoll" : "finish+ragdoll";
                        await ExecLuaAsync($"if KCD2MP_W195Ragdoll then KCD2MP_W195Ragdoll(\"{EscapeLua(npc)}\") end");
                        await Task.Delay(Wo195Rules.RagdollSettleMs);
                        var r3 = await _combat.Wo163Async(Protocol.Wo195OpCheck, a);
                        still = r3 is { Ok: true, Payload.Length: >= 4 } z && Wo195Rules.Upright(z.Payload[0], z.Payload[2]);
                    }
                    Interlocked.Increment(ref still ? ref _w222LayStill : ref _w222LayDown);
                    Console.WriteLine($"W222-DEADLAY npc={npc} try={tries} how={how} finish={Wo210FinishHow(f, true)} after={(still ? "still-upright" : "down")} -- a dead copy found standing {(now - at) / 1000.0:F1} s after its death");
                }
                foreach (var k in _w222LayTries.Keys.Where(k => !_w195Dead.ContainsKey(k)).ToList()) _w222LayTries.TryRemove(k, out _);
            }
            catch (Exception ex) { Console.WriteLine($"W222-DEADLAY sweep failed ({ex.GetType().Name}: {ex.Message})"); }
            finally { Interlocked.Exchange(ref _w222LayBusy, 0); }
        });
    }

    private string Wo222LayStats() => $" dead_lay: checks={Interlocked.Read(ref _w222LayChecks)} laid={Interlocked.Read(ref _w222LayDown)} still={Interlocked.Read(ref _w222LayStill)}";
}
