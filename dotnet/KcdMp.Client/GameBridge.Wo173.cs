// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;

namespace KcdMp.Client;

// WO-173 (0.49.0) -- the agent's half (docs/WO-173-findings.md; the pure rules are Wo173.cs, the native half wo173.h).
//   C1  host: the joiner's Attack row puts his figure into Striking for the swing's window (WO-166's op 9 on the figure), and the joiner's
//       attributed hit goes to the DLL's real-hit op (18): the engine's hit processor with that row's fields, the reaction tail, or the
//       capped raw path. mp_real_hits (w173_cfg) is pushed to the DLL (op 20).
//   C2  host: the outcome of each of his blows goes to that joiner (W164 RealHit); the NPCs a figure fought or a joiner hit are polled for
//       down / up (op 21, 4 Hz) and every change goes to the joiners (W164 NpcDown). Joiner: the outcome is logged and counted, a down / up
//       is applied to the copy at once (WO-135's native knock-out / wake, the stand-up within 0.3 s).
//   MP-W173 ... / WO173-OUTCOME ... / WO173-DOWN ... / MP-WO173-STATS ...
public partial class GameBridge
{
    private volatile bool _w173RealHits = Wo173Rules.DefaultRealHits, _w173Threat = true, _w173React = true;
    private volatile bool _w173NativeMissing;   // an older DLL answered "bad request" to op 18: WO-121's path, said once
    private readonly ConcurrentDictionary<string, (ActionRowCatalog.Row Row, long At)> _w173LastRow = new(StringComparer.Ordinal);   // ghost -> its last Attack row
    private readonly ConcurrentDictionary<string, long> _w173Touched = new(StringComparer.Ordinal);    // host NPC -> last fight with a figure / hit by a joiner
    private readonly ConcurrentDictionary<string, Wo173Rules.DownTrack> _w173Down = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _w173CopyDown = new(StringComparer.Ordinal);  // joiner: the host's last down state per copy
    private long _w173Strikes, _w173StrikeRefused, _w173Engine, _w173Raw, _w173Failed, _w173OutSent, _w173OutIn, _w173DownSent, _w173DownIn, _w173DownApplied;
    private readonly long[] _w173Verdicts = new long[7];
    private int _w173DownLoop, _w173StrikeSaid;

    private void Wo173OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w173_cfg":
                foreach (var (k, v) in Wo173Rules.ParseCfg(arg))
                {
                    if (k == "real_hits") _w173RealHits = v == "on";
                    else if (k == "threat") _w173Threat = v == "on";
                    else if (k == "react") _w173React = v == "on";
                }
                Console.WriteLine($"MP-W173 cfg real_hits={(_w173RealHits ? "on" : "off")} threat={(_w173Threat ? "on" : "off")} react={(_w173React ? "on" : "off")}");
                _ = Wo173PushConfigAsync();
                return;
            case "w173_status":
                _ = Wo173StatusAsync();
                return;
        }
    }

    private async Task Wo173PushConfigAsync()
    {
        try
        {
            var r = await _combat.Wo173ConfigAsync((byte)(_w173RealHits ? 1 : 0), (byte)(_w173Threat ? 1 : 0), (byte)(_w173React ? 1 : 0));
            if (r is { } c && (c.RealHits != _w173RealHits || c.Threat != _w173Threat || c.React != _w173React))
                Console.WriteLine($"MP-W173 the DLL holds real_hits={(c.RealHits ? "on" : "off")} threat={(c.Threat ? "on" : "off")} react={(c.React ? "on" : "off")}");
        }
        catch (Exception ex) { Console.WriteLine($"MP-W173 config not pushed ({ex.GetType().Name})"); }
    }

    private async Task Wo173StatusAsync()
    {
        string? n = null;
        try { n = await _combat.Wo173StatusAsync(); } catch { }
        Console.WriteLine("MP-WO173-STATS " + Wo173StatsText() + " native: " + (n ?? "?"));
    }

    private string Wo173StatsText() => FormattableString.Invariant(
        $"real_hits={(_w173RealHits ? "on" : "off")} figure_strikes={Interlocked.Read(ref _w173Strikes)} strike_refused={Interlocked.Read(ref _w173StrikeRefused)} engine={Interlocked.Read(ref _w173Engine)} raw={Interlocked.Read(ref _w173Raw)} failed={Interlocked.Read(ref _w173Failed)} hit={_w173Verdicts[1]} blocked={_w173Verdicts[2]} pb={_w173Verdicts[5]} broken={_w173Verdicts[6]} outcomes_sent={Interlocked.Read(ref _w173OutSent)} outcomes_in={Interlocked.Read(ref _w173OutIn)} down_sent={Interlocked.Read(ref _w173DownSent)} down_in={Interlocked.Read(ref _w173DownIn)} down_applied={Interlocked.Read(ref _w173DownApplied)}");

    /// <summary>Once a second (the WO-151 loop): the config every 10 s, the down poll started, the stats once a minute.</summary>
    private long _w173TickN;
    private async Task Wo173TickAsync(CancellationToken ct)
    {
        long n = ++_w173TickN;
        if (n % 10 == 1) await Wo173PushConfigAsync();
        if (_isDamageAuthority && !_w173Touched.IsEmpty && Interlocked.CompareExchange(ref _w173DownLoop, 1, 0) == 0) _ = Wo173DownLoopAsync(ct);
        if (n % 60 == 0 && Interlocked.Read(ref _w173Engine) + Interlocked.Read(ref _w173Raw) + Interlocked.Read(ref _w173OutIn) + Interlocked.Read(ref _w173DownIn) > 0)
            await Wo173StatusAsync();
    }

    // ================================================================ C1 (host)

    /// <summary>The joiner's Attack: its row is kept for his next hit, and his figure strikes for the swing's window.</summary>
    private void Wo173OnAvatarAttack(byte ghost, Guid rowGuid, ActionRowCatalog? catalog, CancellationToken ct)
    {
        if (catalog is null || !catalog.TryGet(rowGuid, out var row)) return;
        string gid = ghost.ToString(CultureInfo.InvariantCulture);
        _w173LastRow[gid] = (row, Environment.TickCount64);
        if (!_w173RealHits || !_isDamageAuthority) return;
        var f = Wo166Rules.StrikeFor(row);
        if (f is null) return;
        _ = Wo173FigureStrikeAsync("kcd2mp_" + gid, f.Value, ct);
    }

    private async Task Wo173FigureStrikeAsync(string figure, Wo166Rules.Strike f, CancellationToken ct)
    {
        string? r;
        try { r = await _combat.Wo166StrikeAsync(figure, f.StartMs, f.HitMs, f.Type, f.Zone, f.Hand, f.Strength, ct); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { Console.WriteLine($"MP-W173 strike for {figure} not sent ({ex.GetType().Name})"); return; }
        bool ok = r is not null && (r.StartsWith("queued", StringComparison.Ordinal) || r.StartsWith("merged", StringComparison.Ordinal));
        long k = ok ? Interlocked.Increment(ref _w173Strikes) : Interlocked.Increment(ref _w173StrikeRefused);
        if (!ok && Interlocked.Increment(ref _w173StrikeSaid) <= 20) Console.WriteLine($"MP-W173 figure {figure} strike refused: {r ?? "no answer"}");
        else if (ok && k <= 10) Console.WriteLine($"MP-W173 figure {figure} strikes for the joiner's swing: {r}");
    }

    /// <summary>
    /// The joiner's attributed hit through the DLL's real-hit op. Null = the op is not there (an older DLL) or gave no answer: the caller
    /// uses WO-121's AttributedDamageAsync as before.
    /// </summary>
    private async Task<(bool Ok, byte Steps, ulong AttackerWuid, ulong VictimWuid, byte Reason)?> Wo173AttributedAsync(
        byte source, string npc, Guid soul, float stamina, float health, byte flags, uint avatarEid, CancellationToken ct)
    {
        // WO-204 IV.3: a blow the joiner's own engine judged blocked (stamina only) is applied as he judged it -- WO-121's attributed damage
        // with his values (the caller's path on null); the host's engine never re-judges it into a hit
        // WO-210 C.4: retired -- the defender's world judges; the joiner's computed values for a blow on a host NPC are never applied
        if (!W210("no_raw_blows") && W204("blow_joiner_values") && Wo204Rules.JoinerJudgedBlocked(health, stamina))
        {
            Interlocked.Increment(ref _w204BlowsJoinerValues);
            Wo204NoteBlow(source, npc, health, stamina, "joiner-values(blocked)");
            return null;
        }
        Wo204NoteBlow(source, npc, health, stamina, _w173NativeMissing ? "wo121" : "engine");
        if (_w173NativeMissing) return null;
        string gid = source.ToString(CultureInfo.InvariantCulture);
        long now = Environment.TickCount64;
        Wo166Rules.Strike? f = _w173LastRow.TryGetValue(gid, out var lr) ? Wo173Rules.FieldsFor(lr.Row, lr.At, now) : null;
        // WO-210 C.4: no fresh swing row (a kick, a combo the capture missed) -- the engine's hit all the same, with the joiner's own last
        // attack (his weapon's row) or a plain slash at the engine's zone pick: the host's engine computes the damage and the stamina itself,
        // never the raw write (session E: 9 raw blows, every one "fallback=no-swing-row")
        if (f is null && W210("no_raw_blows")) f = Wo210Rules.FallbackStrike(_w173LastRow.TryGetValue(gid, out var lr2) ? lr2.Row : null);
        Wo173Hit? h;
        try
        {
            h = await _combat.Wo173RealHitAsync(f?.Type ?? -1, f?.Zone ?? -1, f?.Hand ?? 0, f?.Strength ?? 0f, f is not null, soul, stamina, health,
                                                flags, avatarEid, npc, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { Console.WriteLine($"MP-W173 real hit not asked ({ex.GetType().Name}) -- WO-121's path"); return null; }
        if (h is null) return null;
        if (!h.Value.Ok)
        {
            if (!_w173NativeMissing && h.Value.Reason != 0)
            {
                _w173NativeMissing = true;
                Console.WriteLine($"MP-W173 the DLL has no real-hit op (reason {h.Value.Reason}): the joiner's blows go WO-121's way this session");
            }
            return null;
        }
        var x = h.Value;
        if (x.Applied == Wo178Rules.AppliedHeld)
        {
            // WO-178 C4 (1): the DLL holds the blow up to 0.8 s for the figure's strike window, then applies it once (the engine, or the
            // engine-like fallback). It counts as applied here (never twice, never dropped); its outcome reaches the joiner from the
            // background completion.
            _w173Touched[npc] = now;
            Console.WriteLine(FormattableString.Invariant(
                $"MP-W173 hit npc={npc} by_ghost={source} applied=held (waiting for the figure's strike window) joiner_said=st{stamina:F1}/hp{health:F1} hold_id={x.Seq}"));
            Wo178FinishHeld(x.Seq, source, npc, stamina, health, f);
            return (true, 1, 0, 0, x.Fallback);
        }
        if (x.Fallback == Wo176Rules.FallbackArranged)
        {
            // WO-176 A1: the victim is in a quest's arranged fight -- neither the engine nor the raw path ran; nothing else may run either
            Console.WriteLine(FormattableString.Invariant(
                $"MP-W173 hit npc={npc} by_ghost={source} applied=none verdict=- fallback={Wo173Rules.FallbackName(x.Fallback)} joiner_said=st{stamina:F1}/hp{health:F1} seq={x.Seq}"));
            await Wo176KeptAsync(source, npc, "real-hit");
            return (false, 0, 0, 0, x.Fallback);
        }
        _w173Touched[npc] = now;
        if (x.ClientHit) _w210EngineSignal[npc] = now;   // WO-210 C.1: the engine sends the brain its own hit signal for this blow
        if (x.ByEngine) Interlocked.Increment(ref _w173Engine);
        else if (x.AppliedAny) Interlocked.Increment(ref _w173Raw);
        else Interlocked.Increment(ref _w173Failed);
        var verdict = x.ByEngine ? Wo173Rules.VerdictOf(x.Outcome) : HitVerdict.None;
        if ((int)verdict is > 0 and < 7) Interlocked.Increment(ref _w173Verdicts[(int)verdict]);
        Console.WriteLine(FormattableString.Invariant(
            $"MP-W173 hit npc={npc} by_ghost={source} applied={(x.ByEngine ? "engine" : x.AppliedAny ? "raw" : "none")} verdict={(verdict == HitVerdict.None ? "-" : HitVerdictMsg.VerdictName(verdict))} reaction={Wo173Rules.ReactName(x.React)} joiner_said=st{stamina:F1}/hp{health:F1} raw_applied=st{x.StApplied:F1}/hp{x.HpApplied:F1} row={(f is null ? "none" : $"type{f.Value.Type}/zone{f.Value.Zone}")} fallback={Wo173Rules.FallbackName(x.Fallback)} seq={x.Seq}"));
        Wo202NoteJoinerBlow(npc, x.AppliedAny, x.React, x.ClientHit);   // WO-202 II.3: a killing blow without the death reaction leaves the body upright
        // C2: the outcome goes to the joiner whose blow it was
        if (W164Text.IsNpcName(npc))
        {
            Interlocked.Increment(ref _w173OutSent);
            _ = W164SendAsync(source, Protocol.W164RealHit, W164Text.RealHit(npc, verdict, x.React, x.ByEngine, health, stamina));
        }
        bool ok = x.AppliedAny;
        byte steps = x.Steps;
        if (ok) steps |= 1;   // bit 0 = the blow is applied (by the engine or the raw path)
        return (ok, steps, x.AttackerWuid, x.VictimWuid, x.Fallback);
    }

    /// <summary>C2: an NPC's blow on a figure (WO-132's 0x9A) -- the NPC is in a fight with a joiner: its down state is watched.</summary>
    private void Wo173NoteFigureFight(string npc)
    {
        if (_isDamageAuthority && W164Text.IsNpcName(npc)) _w173Touched[npc] = Environment.TickCount64;
    }

    // ================================================================ C2 (host): down / up of the NPCs in a joiner's fight

    private async Task Wo173DownLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(Wo173Rules.DownPollMs, ct); } catch { break; }
                if (!_isDamageAuthority) continue;
                long now = Environment.TickCount64;
                foreach (var k in _w173Touched.Where(kv => now - kv.Value > Wo173Rules.DownWatchMs).Select(kv => kv.Key).ToList())
                {
                    _w173Touched.TryRemove(k, out _);
                    _w173Down.TryRemove(k, out _);
                }
                var names = Wo173Rules.DownCandidates(_w173Touched, now);
                if (names.Count == 0) continue;
                byte[]? states;
                try { states = await _combat.Wo173DownPollAsync(names, ct); } catch (OperationCanceledException) { break; } catch { continue; }
                if (states is null) continue;
                var peers = Wo134Peers();
                for (int i = 0; i < names.Count; i++)
                {
                    if (states[i] > 2) continue;
                    var t = _w173Down.GetOrAdd(names[i], _ => new Wo173Rules.DownTrack());
                    bool first = !t.Known;
                    if (!t.ShouldSend(states[i], now)) continue;
                    if (first && states[i] == 0) continue;   // standing when first seen: nothing to tell
                    foreach (byte j in peers) _ = W164SendAsync(j, Protocol.W164NpcDown, W164Text.NpcDown(names[i], states[i]));
                    long c = Interlocked.Increment(ref _w173DownSent);
                    if (c <= 200) Console.WriteLine($"MP-W173 down npc={names[i]} state={(states[i] == 0 ? "up" : states[i] == 1 ? "knocked-out" : "dead")} -> {peers.Count} joiner(s)");
                }
            }
        }
        finally { Interlocked.Exchange(ref _w173DownLoop, 0); }
    }

    // ================================================================ C2 (joiner)

    private async Task Wo173OnRealHitInAsync(string text)
    {
        if (!W164Text.TryParseRealHit(text, out string npc, out var verdict, out byte react, out bool byEngine, out float hp, out float st))
        {
            Interlocked.Increment(ref _w164Malformed);
            return;
        }
        long n = Interlocked.Increment(ref _w173OutIn);
        Wo151NoteFight(npc, "the host's NPC took the joiner's blow");   // its own hit reactions stay off: the host's reaction plays on it
        var show = Wo173Rules.ShowFor(verdict);
        string applied = show switch
        {
            Wo173Rules.CopyShow.HitReaction => react == 1 ? "host-reaction-row" : "none(no host reaction)",
            Wo173Rules.CopyShow.Block or Wo173Rules.CopyShow.PerfectBlock or Wo173Rules.CopyShow.BlockBroken => react == 1 ? "host-reaction-row" : "none(no host reaction)",
            _ => "none",
        };
        bool down = _w173CopyDown.TryGetValue(npc, out byte ds) && ds == 1;
        if (!down && byEngine) await Wo176CopyBlockAsync(npc, verdict, react);   // WO-176 B3a: its own block row when the host sends no reaction
        if (n <= 400 || n % 50 == 0)
            Console.WriteLine(FormattableString.Invariant(
                $"WO173-OUTCOME npc={npc} verdict={(verdict == HitVerdict.None ? "none" : HitVerdictMsg.VerdictName(verdict))} reaction={Wo173Rules.ReactName(react)} by={(byEngine ? "engine" : "raw")} down={(down ? "yes" : "no")} applied_on_copy={applied} joiner_said=hp{hp:F1}/st{st:F1}"));
    }

    private async Task Wo173OnNpcDownInAsync(string text)
    {
        if (!W164Text.TryParseNpcDown(text, out string npc, out byte state)) { Interlocked.Increment(ref _w164Malformed); return; }
        Interlocked.Increment(ref _w173DownIn);
        byte prev = _w173CopyDown.TryGetValue(npc, out byte p) ? p : (byte)0;
        _w173CopyDown[npc] = state;
        await ExecLuaAsync($"if KCD2MP_W173Down then KCD2MP_W173Down(\"{EscapeLua(npc)}\", {state}) end");
        if (state == 2 || state == prev) return;   // deaths: WO-172's path; a repeat: the copy already follows
        Interlocked.Increment(ref _w173DownApplied);
        Console.WriteLine($"WO173-DOWN npc={npc} state={(state == 1 ? "knocked-out" : "up")} -- the copy follows now");
        await Wo135KoAsync(npc, state == 1);
    }
}
