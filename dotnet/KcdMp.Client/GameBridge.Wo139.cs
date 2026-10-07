// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-139: crime and guards -- the agent's half (docs/WO-139-findings.md).
//
// The joiner's crimes are crimes in the host's world, and his own problem:
// never pinned on the host's Henry. The engine has no culprit in a crime: every
// crime record an NPC holds counts against the LOCAL player. So nothing about
// the joiner's crime is ever written into the host's crime memory; the host
// keeps the record itself (JoinerCrimeRecord), and the stop runs on the
// joiner's machine, where the local player IS the joiner's Henry.
//
// JOINER
//   * his own game says what is a crime (Lua: a stolen item, a stash taken
//     from, a lock picked, a horse mounted and stolen, a body robbed; the DLL:
//     the trespass level the HUD shows) -> CrimeAsk Report;
//   * a guard's stop (CrimeHost Stop) -> the mod frees that guard's copy for the
//     stop and plants the crimes in its memory; the game runs its own arrest and
//     crime dialogue against this Henry (fine from his money, the punishment, a
//     fight); the dialogue's result -> CrimeAsk Outcome (with where the stop ended);
//   * an execution (WO-113's respawn, kind 2) -> Outcome "executed";
//   * horses the host's Henry may ride are legal to ride here too.
// HOST
//   * a report is judged in its world: its NPCs who can see the joiner's avatar
//     at that spot are the witnesses (Lua); nobody saw it = no crime; guards among
//     them know at once, a civilian's report reaches the settlement's guards after
//     JoinerCrimeRecord.ReportDelayMs;
//   * its own detections: the joiner's attributed hits on its NPCs (assault, a kill
//     = murder) and the takedowns his avatar performs -- whose victims are marked
//     first so the game's stealth-hit branch never blames the host's player;
//   * a guard who knows and is near the avatar: a violent crime -> the guard fights
//     the avatar (skirmish + combat_forcedTarget, native); otherwise -> a stop: the
//     guard is held (busy) and the joiner told; the outcome clears the record there
//     (paid / punished / persuaded / executed) or turns it violent (fought / fled);
//     the held guard is placed where the stop ended and released;
//   * the horses its Henry may ride go to every joiner.
// BOTH (a session with a partner): the punishment moves no clock -- its time sets
// run nothing (native, WO-137's hook) and its skip-time cutscene lasts a second (Lua).
public partial class GameBridge
{
    private volatile bool _w139SharedOn = true;        // mp_crime_shared (default ON: fail-closed pieces, the maintainer's rule)
    private volatile bool _w139HostModeOn = true;      // joiner: the host's announced mode
    private volatile bool _w139HostModeKnown;
    private volatile bool _w139Connected;
    private uint _w139Tok;
    private int _w139CfgKey = -1;
    private long _w139CfgAtMs;
    private readonly ConcurrentDictionary<byte, JoinerCrimeRecord> _w139Records = new();
    private readonly ConcurrentDictionary<byte, (string Guard, uint StopId, long AtMs, string Settlement)> _w139Stops = new();
    private readonly ConcurrentDictionary<string, long> _w139GuardCooldown = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (byte Peer, uint Avatar, string Settlement, long NearMs)> _w139Pursuits = new(StringComparer.Ordinal);   // NearMs: last seen near the avatar
    private readonly ConcurrentDictionary<string, long> _w139AssaultSeen = new(StringComparer.Ordinal);   // "<src>|<npc>" -> ms
    private readonly ConcurrentDictionary<string, bool> _w139StoppedHere = new(StringComparer.OrdinalIgnoreCase);   // joiner: guards in a stop
    private readonly List<string> _w139HorseParts = new();
    private string? _w139HorsesSent;   // null: never sent (an empty list is sent too)
    private long _w139HorsesAtMs, _w139HorsesSentAtMs, _w139ModeAtMs;
    private volatile int _w139TrespassLevel;
    private long _w139TrespassSentMs;
    private long _w139Reports, _w139ReportsIn, _w139Judged, _w139Witnessed, _w139Unseen, _w139StopsOut, _w139StopsIn, _w139Outcomes,
                 _w139Pursue, _w139Cleared, _w139Violent, _w139TakedownMarked, _w139HorseLegal, _w139Trespass, _w139TrespassAtStats;

    /// <summary>The host of a shared world with a partner connected.</summary>
    private bool W139Host => _combatRoleApplied && _isDamageAuthority && _sharedWorld && Wo134Peers().Count > 0;
    /// <summary>A joiner whose loaded world is the host's.</summary>
    private bool W139Joiner => W137Joiner;
    /// <summary>Crime sharing runs: this machine's switch, and (a joiner) the host's announced mode.</summary>
    private bool W139On => _w139SharedOn && (W139Host || (W139Joiner && (!_w139HostModeKnown || _w139HostModeOn)));

    private void Wo139OnConnect(CancellationToken ct)
    {
        _w139Connected = true;
        _w139CfgKey = -1;
        _w139HostModeKnown = false;
        _w139HostModeOn = true;
        _combat.OnTrespass = (level, prev, x, y, z) => { _ = Task.Run(() => Wo139OnTrespassAsync(level, prev, x, y, z)); };
        _ = Wo139LoopAsync(ct);
    }

    private async Task Wo139OnDisconnectAsync()
    {
        _w139Connected = false;
        _combat.OnTrespass = null;
        foreach (var (peer, st) in _w139Stops.ToArray())
            if (_w139Stops.TryRemove(peer, out _)) _ = ExecLuaAsync($"if KCD2MP_W137HostHold then KCD2MP_W137HostHold(false, \"{st.Guard}\", {peer}, \"w139-disconnect\") end");
        _w139Pursuits.Clear();      // the DLL clears the pairs it set when the pipe closes
        _w139Records.Clear();
        _w139StoppedHere.Clear();
        try { await _combat.Wo139ConfigAsync(false); } catch { }
        try { await _combat.Wo139PunishGateAsync(false); } catch { }
        _w139CfgKey = -1;
        try { await ExecLuaAsync("if KCD2MP_W139Session then KCD2MP_W139Session(false, false, false) end"); } catch { }
    }

    // ---------------------------------------------------------------- the 1 s loop

    private async Task Wo139LoopAsync(CancellationToken ct)
    {
        long lastStats = Environment.TickCount64;
        while (!ct.IsCancellationRequested && _w139Connected)
        {
            try { await Task.Delay(1000, ct); } catch { return; }
            try
            {
                bool holding = Wo136Holding;
                bool host = W139Host, joiner = W139Joiner, on = W139On && !holding;
                await Wo139PushConfigAsync(host, joiner, on);
                _ = ExecLuaAsync($"if KCD2MP_W139Session then KCD2MP_W139Session({B(host)}, {B(joiner)}, {B(on)}) end");
                long now = Environment.TickCount64;
                if (host)
                {
                    if (now - _w139ModeAtMs >= 10_000) { _w139ModeAtMs = now; await Wo139SendModeAsync("heartbeat"); }
                    if (on && now - _w139HorsesAtMs >= 10_000)
                    {
                        _w139HorsesAtMs = now;
                        _ = ExecLuaAsync("if KCD2MP_W139HostHorses then KCD2MP_W139HostHorses() end");
                    }
                    if (on) await Wo139HostTickAsync(now);
                }
                if (joiner && on && _w139TrespassLevel >= 3 && now - _w139TrespassSentMs >= 8_000)
                    await Wo139ReportTrespassAsync(_lastX, _lastY, _lastZ, "still inside");
                if (now - lastStats >= 60_000)
                {
                    lastStats = now;
                    Console.WriteLine(Wo139StatsLine());
                    // a joiner's trespass this minute: the DLL's own count of warnings it hid (quieted=) goes in the log too
                    // (live 0.45.8: "it showed he was trespassing", and nothing in the bundle said whether the HUD gate held)
                    if (joiner && _w139Trespass != _w139TrespassAtStats)
                    {
                        _w139TrespassAtStats = _w139Trespass;
                        Console.WriteLine($"MP-W139 native: {await _combat.Wo139StatusAsync() ?? "no answer"}");
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"MP-W139 tick failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private async Task Wo139PushConfigAsync(bool host, bool joiner, bool on)
    {
        bool detector = joiner && on;
        bool punish = (host || joiner) && on;
        int key = (detector ? 1 : 0) | (punish ? 2 : 0) | (_w157TrespassHud ? 4 : 0);
        long now = Environment.TickCount64;
        if (key == _w139CfgKey && now - _w139CfgAtMs < 10_000) return;
        bool changed = key != _w139CfgKey;
        var cfg = await _combat.Wo139ConfigAsync(detector, quiet: !_w157TrespassHud);
        var pg = await _combat.Wo139PunishGateAsync(punish);
        if (cfg is null) { if (changed) Console.WriteLine("MP-W139 config: the DLL did not answer (a world loading, or no plugin) -- tried again"); return; }
        _w139CfgKey = key; _w139CfgAtMs = now;
        if (changed)
            Console.WriteLine($"MP-W139 crime sharing {(on ? "ON" : "off")} -- role={(host ? "host" : joiner ? "joiner" : "none")}; trespass detector {(detector ? (cfg.Value.Armed ? "on" : "NOT ARMED (the HUD anchor did not match)") : "off")}; punishment time gate {(punish ? (pg == true ? "on" : "NOT ARMED") : "off")}");
    }

    private string Wo139StatsLine() => FormattableString.Invariant(
        $"MP-WO139-STATS role={(W139Host ? "host" : W139Joiner ? "joiner" : "none")} shared={On(_w139SharedOn)} host_mode={On(_w139HostModeOn)} reports_out={_w139Reports} reports_in={_w139ReportsIn} judged={_w139Judged} witnessed={_w139Witnessed} unseen={_w139Unseen} violent={_w139Violent} records={_w139Records.Sum(r => r.Value.Count)} stops_out={_w139StopsOut} stops_in={_w139StopsIn} outcomes={_w139Outcomes} pursuits={_w139Pursuits.Count}/{_w139Pursue} cleared={_w139Cleared} takedown_marks={_w139TakedownMarked} horses_legal={_w139HorseLegal} trespass_edges={_w139Trespass}");

    private async Task Wo139SendAsync(byte type, byte target, byte kind, uint tok, string text)
    {
        try { await WriteJoinAsync(new LootMsg(kind, tok, text).BuildUp(type, target)); }
        catch (Exception ex) { Console.WriteLine($"MP-W139 {(type == Protocol.CrimeHostUp ? Protocol.CrimeHostName(kind) : Protocol.CrimeAskName(kind))} not sent: {ex.Message}"); }
    }

    private async Task Wo139SendModeAsync(string why)
    {
        foreach (byte g in Wo134Peers()) await Wo139SendAsync(Protocol.CrimeHostUp, g, Protocol.CrimeHostMode, 0, Wo139Rules.ModeText(_w139SharedOn, why));
    }

    // ---------------------------------------------------------------- joiner: reports

    private async Task Wo139OnTrespassAsync(byte level, byte prev, float x, float y, float z)
    {
        Interlocked.Increment(ref _w139Trespass);
        _w139TrespassLevel = level;
        bool now = level >= 3, before = prev >= 3 && prev != 0xFF;
        Console.WriteLine(FormattableString.Invariant($"MP-W139 trespass level {prev} -> {level} at ({x:F1}, {y:F1}, {z:F1}){(now && !before ? " -- entered someone's property" : !now && before ? " -- left it" : "")}"));
        if (now && W139Joiner && W139On) await Wo139ReportTrespassAsync(x, y, z, "entered");
    }

    private async Task Wo139ReportTrespassAsync(float x, float y, float z, string why)
    {
        _w139TrespassSentMs = Environment.TickCount64;
        await Wo139ReportAsync(new Wo139Rules.Report("trespass", x, y, z, "", "", "area"), why);
    }

    private async Task Wo139ReportAsync(Wo139Rules.Report r, string why)
    {
        uint tok = Interlocked.Increment(ref _w139Tok);
        await Wo139SendAsync(Protocol.CrimeAskUp, Protocol.JoinTargetHost, Protocol.CrimeAskReport, tok, Wo139Rules.ReportText(r));
        Interlocked.Increment(ref _w139Reports);
        Console.WriteLine(FormattableString.Invariant($"MP-W139 joiner -> host crime #{tok}: {r.Crime} {r.Where} at ({r.X:F1}, {r.Y:F1}, {r.Z:F1}) victim={(r.Victim.Length > 0 ? r.Victim : "-")} ({why}) -- the host's world judges it"));
    }

    /// <summary>WO-113's respawn (kind 2 = execution) on this machine.</summary>
    private void Wo139OnLocalRespawned(byte reason)
    {
        if (reason != 2) return;
        if (W139Joiner && W139On)
        {
            uint tok = Interlocked.Increment(ref _w139Tok);
            _ = Wo139SendAsync(Protocol.CrimeAskUp, Protocol.JoinTargetHost, Protocol.CrimeAskOutcome, tok, Wo139Rules.OutcomeText("executed", "", 0) + " 0 0 0");
            Console.WriteLine("MP-W139 joiner: executed -- the host clears this player's record; the respawn is WO-113's (outside the town)");
        }
        _ = ExecLuaAsync("if KCD2MP_W139Executed then KCD2MP_W139Executed() end");
    }

    // ---------------------------------------------------------------- host: judging, guards

    private JoinerCrimeRecord Wo139Record(byte peer) => _w139Records.GetOrAdd(peer, _ => new JoinerCrimeRecord());

    private async Task Wo139HostTickAsync(long now)
    {
        foreach (var (peer, rec) in _w139Records.ToArray())
        {
            if (!IsLivePeer(peer)) { _w139Records.TryRemove(peer, out _); continue; }   // WO-144 (a reconnect gets it back: GameBridge.Wo144)
            foreach (var c in rec.Tick(now, 0))
                Console.WriteLine($"MP-W139 host: ghost {peer}'s {c.Kind} in {(c.Settlement.Length > 0 ? c.Settlement : "the wilds")} is known to the guards now (a witness reported it)");
            if (rec.Open.Any(c => c.Known) || _w139Pursuits.Values.Any(p => p.Peer == peer))
                await ExecLuaAsync($"if KCD2MP_W139HostGuards then KCD2MP_W139HostGuards({peer}) end");
        }
        foreach (var (peer, st) in _w139Stops.ToArray())
            if (now - st.AtMs > 300_000 && _w139Stops.TryRemove(peer, out _))
            {
                Console.WriteLine($"MP-W139 host: the stop of ghost {peer} by {st.Guard} got no outcome in 5 min -- released");
                await ExecLuaAsync($"if KCD2MP_W137HostHold then KCD2MP_W137HostHold(false, \"{st.Guard}\", {peer}, \"w139-stop-timeout\") end");
            }
    }

    /// <summary>w139_judged &lt;src&gt; &lt;id&gt; &lt;kind&gt; &lt;witnesses&gt; &lt;guards&gt; &lt;settlement|-&gt; &lt;guards|-&gt;</summary>
    private async Task Wo139OnJudgedAsync(string[] f)
    {
        if (f.Length < 7 || !byte.TryParse(f[0], out byte src) || !uint.TryParse(f[1], out uint id) || !Wo139Text.IsCrime(f[2])
            || !int.TryParse(f[3], out int wit) || !int.TryParse(f[4], out int guards) || !Wo139Text.IsSettlementOrDash(f[5])) return;
        Interlocked.Increment(ref _w139Judged);
        string settlement = f[5] == "-" ? "" : f[5];
        var c = new JoinerCrimeRecord.Crime { Id = id, Kind = f[2], Settlement = settlement, Witnesses = wit, Guards = guards, AtMs = Environment.TickCount64 };
        var rec0 = Wo139Record(src);
        // a re-reported trespass merges into the open one: told again only when a guard now knows of it
        var open = c.Kind == "trespass" ? rec0.Open.FirstOrDefault(o => o.Kind == "trespass" && o.Settlement == settlement) : null;
        bool wasKnown = open?.Known == true;
        bool kept = rec0.Add(c);
        if (open is not null && kept && (wasKnown || !open.Known))
        {
            Console.WriteLine($"MP-W139 host: ghost {src}'s trespass #{id} is the open one again ({wit} witness(es), {guards} guard(s)) -- one trespass, merged");
            return;
        }
        if (kept) Interlocked.Increment(ref _w139Witnessed); else Interlocked.Increment(ref _w139Unseen);
        Console.WriteLine($"MP-W139 host: ghost {src}'s {f[2]}{(id != 0 ? $" #{id}" : " (seen here)")}: {wit} witness(es), {guards} guard(s){(guards > 0 ? $" ({f[6]})" : "")} in {(settlement.Length > 0 ? settlement : "the wilds")} -- {(kept ? (c.Known ? "a crime in this world, known to the guards" : "a crime in this world; the guards hear of it soon") : "nobody saw it: no crime")}");
        if (id != 0) await Wo139SendAsync(Protocol.CrimeHostUp, src, Protocol.CrimeHostJudged, id, Wo139Rules.JudgedText(f[2], wit, guards, c.Known, settlement));
        else if (kept) await Wo139SendAsync(Protocol.CrimeHostUp, src, Protocol.CrimeHostJudged, 0, Wo139Rules.JudgedText(f[2], wit, guards, c.Known, settlement));
    }

    /// <summary>w139_guards &lt;src&gt; &lt;guard:settlement:dist:sees:duty,...|-&gt;: the host's guards around a joiner's avatar.</summary>
    private async Task Wo139OnGuardsAsync(string[] f)
    {
        if (f.Length < 2 || !byte.TryParse(f[0], out byte src) || !_w139Records.TryGetValue(src, out var rec)) return;
        if (!_ghostEntityIds.TryGetValue(src.ToString(CultureInfo.InvariantCulture), out uint avatar)) return;
        long now = Environment.TickCount64;
        var guards = new List<(string Name, string Settlement, int Dist, bool Sees, bool Duty)>();
        if (f[1] != "-")
            foreach (var e in f[1].Split(','))
            {
                var p = e.Split(':');
                if (p.Length == 5 && Wo139Text.IsName(p[0]) && Wo139Text.IsSettlementOrDash(p[1]) && int.TryParse(p[2], out int d))
                    guards.Add((p[0], p[1] == "-" ? "" : p[1], d, p[3] == "1", p[4] == "1"));
            }
        // WO-154 3.3: a partner who is down, or just up again, is left alone (mp_guard_respite)
        string? respite = Wo154GuardsBlocked(src);
        // hostile (a fresh violent crime, or a resist under 5 min old): every guard of that
        // settlement near the avatar attacks it -- the game's own attack interrupt
        // (crime:attackInitiatedByConcept), the target held by combat_forcedTarget
        foreach (var g in guards)
        {
            if (_w139Pursuits.TryGetValue(g.Name, out var cur)) { _w139Pursuits[g.Name] = cur with { NearMs = now }; continue; }
            if (respite is not null) continue;
            if (g.Settlement.Length == 0 || !rec.AnyHostileKnownIn(g.Settlement, now) || g.Dist > 40) continue;
            if (_w139Pursuits.Count(p => p.Value.Peer == src) >= 3) break;
            var r = await _combat.Wo139PursueAsync(true, avatar, g.Name);
            if (r == 4) { Wo154NoteGuardOnHost(g.Name, src); continue; }   // WO-154 3.1: it fights the host -- left to it
            if (r is 1 or 0)
            {
                _w139Pursuits[g.Name] = (src, avatar, g.Settlement, now);
                Interlocked.Increment(ref _w139Pursue);
                await ExecLuaAsync($"if KCD2MP_W139HostAttack then KCD2MP_W139HostAttack(\"{g.Name}\", {src}) end");
                await Wo139SendAsync(Protocol.CrimeHostUp, src, Protocol.CrimeHostPursue, 0, Wo139Rules.PursueText(true, g.Name));
                Console.WriteLine($"MP-W139 host: {g.Name} ({g.Settlement}) knows ghost {src}'s crime (fresh violent, or resisted) -- attacks his avatar ({g.Dist} m)");
            }
        }
        // pursuits that are over: the record there cleared, or that guard not near the avatar for a minute
        // (it fled, or the guard is down); the joiner going down or dying ends them too (Wo139OnPeerDownAsync)
        foreach (var (guard, p) in _w139Pursuits.ToArray())
            if (p.Peer == src && (rec.KnownIn(p.Settlement).Count == 0 || now - p.NearMs > 60_000))
                await Wo139EndPursuitAsync(guard, rec.KnownIn(p.Settlement).Count == 0 ? "the record is cleared" : "the avatar got away");
        // arrestable (everything not hostile now, violent crimes included once no longer fresh): one stop at
        // a time, by a guard of that settlement who sees the avatar up close
        if (_w139Stops.ContainsKey(src) || respite is not null) return;
        foreach (var g in guards.Where(g => g.Sees && g.Dist <= 12).OrderBy(g => g.Dist))
        {
            if (g.Settlement.Length == 0 || rec.AnyHostileKnownIn(g.Settlement, now) || _w139Pursuits.ContainsKey(g.Name)) continue;
            if (_w139GuardCooldown.TryGetValue(g.Name, out long until) && now < until) continue;
            var crimes = rec.ArrestableIn(g.Settlement, now);
            if (crimes.Count == 0) continue;
            uint stopId = Interlocked.Increment(ref _w139Tok);
            _w139Stops[src] = (g.Name, stopId, now, g.Settlement);
            _w139GuardCooldown[g.Name] = now + 60_000;
            await ExecLuaAsync($"if KCD2MP_W137HostHold then KCD2MP_W137HostHold(true, \"{g.Name}\", {src}, \"w139-stop\") end");
            await Wo139SendAsync(Protocol.CrimeHostUp, src, Protocol.CrimeHostStop, stopId, Wo139Rules.StopText(g.Name, crimes.Select(c => c.Kind), JoinerCrimeRecord.FineOf(crimes)));
            Interlocked.Increment(ref _w139StopsOut);
            Console.WriteLine($"MP-W139 host: {g.Name} ({g.Settlement}, {g.Dist} m) stops ghost {src} for {Wo139Rules.CrimesText(crimes.Select(c => c.Kind))} -- held here; the stop runs on the joiner's machine, against his own Henry");
            return;
        }
    }

    /// <summary>
    /// The joiner went down (knocked out) or died: the guards fighting his avatar stop. The fight is
    /// over and the record stands (a death is no sentence); his crimes are no longer fresh or
    /// escalated, so the next guard to see him up close arrests him (a stop, violent crimes included).
    /// </summary>
    private async Task Wo139OnPeerDownAsync(byte peer, string why)
    {
        if (!W139Host) return;
        if (_w139Records.TryGetValue(peer, out var rec)) rec.Calm(null);
        // WO-154 3.3: every pursuit of his avatar ends, whatever the record says (a pursuit left standing keeps its
        // guard's skirmish against the avatar, and the guard goes for the partner again after his respawn)
        foreach (var (g, p) in _w139Pursuits.ToArray())
            if (p.Peer == peer) await Wo139EndPursuitAsync(g, $"the joiner {why}");
    }

    private async Task Wo139EndPursuitAsync(string guard, string why)
    {
        if (!_w139Pursuits.TryRemove(guard, out var p)) return;
        await _combat.Wo139PursueAsync(false, p.Avatar, guard);
        await Wo139SendAsync(Protocol.CrimeHostUp, p.Peer, Protocol.CrimeHostPursue, 0, Wo139Rules.PursueText(false, guard));
        Console.WriteLine($"MP-W139 host: {guard} stops fighting ghost {p.Peer}'s avatar ({why})");
    }

    /// <summary>The joiner's stop ended on his machine.</summary>
    private async Task Wo139HostOutcomeAsync(byte src, uint tok, string result, string guard, int fine, float x, float y, float z)
    {
        Interlocked.Increment(ref _w139Outcomes);
        var rec = Wo139Record(src);
        string settlement = "";
        if (_w139Stops.TryGetValue(src, out var st) && (st.StopId == tok || result == "executed"))
        {
            _w139Stops.TryRemove(src, out _);
            settlement = st.Settlement;
            guard = st.Guard;
            if (x != 0 || y != 0) await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W139HostPlace then KCD2MP_W139HostPlace(\"{guard}\", {x:F2}, {y:F2}, {z:F2}) end"));
            await ExecLuaAsync($"if KCD2MP_W137HostHold then KCD2MP_W137HostHold(false, \"{guard}\", {src}, \"w139-stop-{result}\") end");
        }
        switch (Wo139Rules.EffectOf(result))
        {
            case Wo139Rules.OutcomeEffect.Clear:
                int n = result == "executed" ? rec.Clear(null) : rec.Clear(settlement.Length > 0 ? settlement : null);
                Interlocked.Increment(ref _w139Cleared);
                foreach (var (g, p) in _w139Pursuits.ToArray()) if (p.Peer == src) await Wo139EndPursuitAsync(g, "the record is cleared");
                await Wo139SendAsync(Protocol.CrimeHostUp, src, Protocol.CrimeHostCleared, tok, Wo139Rules.ClearedText(result, settlement));
                Console.WriteLine($"MP-W139 host: ghost {src}'s stop by {(guard.Length > 0 ? guard : "-")}: {result}{(fine > 0 ? $" ({fine / 10.0:F1} groschen)" : "")} -- {n} crime(s) cleared in {(settlement.Length > 0 ? settlement : "every settlement")}");
                await Wo151OnJoinerClearedAsync(src, result);   // WO-151 joint: the host's Henry is cleared with him
                break;
            case Wo139Rules.OutcomeEffect.Resist when Wo154GuardsBlocked(src) is string blocked:
                // WO-154 3.3: he went down (or is just up again): a death is no sentence, and no resist either
                Interlocked.Increment(ref _w154ResistIgnored);
                if (guard.Length > 0) _w139GuardCooldown[guard] = Environment.TickCount64 + 30_000;
                Console.WriteLine($"MP-W139 host: ghost {src} {result} at the stop by {(guard.Length > 0 ? guard : "-")}, but he is {blocked} -- no resist (mp_guard_respite); the record stands");
                break;
            case Wo139Rules.OutcomeEffect.Resist:
                if (settlement.Length > 0) rec.MarkResisted(settlement, Environment.TickCount64);
                Console.WriteLine($"MP-W139 host: ghost {src} {result} at the stop by {guard} -- resisting arrest: the guards of {(settlement.Length > 0 ? settlement : "?")} fight him now");
                break;
            default:
                if (guard.Length > 0) _w139GuardCooldown[guard] = Environment.TickCount64 + 30_000;
                Console.WriteLine($"MP-W139 host: ghost {src}'s stop by {(guard.Length > 0 ? guard : "-")}: {result} -- the record stands");
                break;
        }
    }

    // ---------------------------------------------------------------- host: its own detections

    /// <summary>WO-121's attributed hit landed on a host NPC: the joiner's assault, or his murder.</summary>
    private void Wo139OnAvatarHit(byte source, string npcName, float health)
    {
        if (!W139Host || !W139On || !Wo139Text.IsName(npcName)) return;
        long now = Environment.TickCount64;
        string key = $"{source}|{npcName}";
        string? kind = null;
        // WO-154 3.1 (mp_fair_crime): `health` is the hit's damage, not the victim's: a blocked 0-hp hit is no murder
        // (the field: 11 of 11 duel hits on a living kunes judged murders). The mod's Lua judges a murder by the
        // victim's own death, and every hit asks it ("hit" = no new assault, only the death check).
        if (!_w154FairCrime && health <= 0.01f) { kind = "murder"; _w139AssaultSeen.TryRemove(key, out _); }
        else if (!_w139AssaultSeen.TryGetValue(key, out long at) || now - at > 60_000) { kind = "assault"; _w139AssaultSeen[key] = now; }
        if (_w154FairCrime) kind ??= "hit";
        if (kind is null) return;
        if (kind != "hit") Interlocked.Increment(ref _w139Violent);
        _ = ExecLuaAsync($"if KCD2MP_W139HostViolent then KCD2MP_W139HostViolent({source}, \"{npcName}\", \"{kind}\") end");
    }

    /// <summary>
    /// Before the joiner's avatar performs a takedown on a host NPC (WO-135): the game's stealth-hit
    /// branch writes the LOCAL player as the attacker whoever hit (handleHitReaction), and a body found
    /// later is attributed to a player who was near -- both would pin the joiner's takedown on the
    /// host's Henry. The victim is marked first; the joiner's crime is judged by the mod instead.
    /// </summary>
    private async Task Wo139MarkTakedownVictimAsync(string npc, string kind)
    {
        if (!Wo139Text.IsName(npc)) return;
        var marks = new List<string> { "crime_suppressMeleeStealthHitReaction", "crime_ignoredNPCHitVolume",
                                       kind == "knockout" ? "crime_ignoredUnconsciousBody" : "crime_ignoredCorpse" };
        var res = new List<string>();
        foreach (var m in marks) res.Add($"{m}={(await _combat.Wo139ContextAsync(true, m, npc))?.ToString(CultureInfo.InvariantCulture) ?? "no-answer"}");
        Interlocked.Increment(ref _w139TakedownMarked);
        Console.WriteLine($"MP-W139 host: takedown victim {npc} marked before the avatar's {kind} ({string.Join(" ", res)}) -- never the host's crime");
    }

    /// <summary>The avatar's takedown took (WO-135 result ok): the joiner's crime, if witnessed.</summary>
    private void Wo139OnTakedownDone(byte src, string npc, string kind)
    {
        if (!W139Host || !W139On || !Wo139Text.IsName(npc)) return;
        Interlocked.Increment(ref _w139Violent);
        string crime = kind == "knockout" ? "knockout" : "murder";
        _ = ExecLuaAsync($"if KCD2MP_W139HostViolent then KCD2MP_W139HostViolent({src}, \"{npc}\", \"{crime}\") end");
    }

    // ---------------------------------------------------------------- frames

    private async Task Wo139OnFrameAsync(int type, byte src, byte[] body)
    {
        if (!LootMsg.TryDecode(body, out var m)) return;
        if (type == Protocol.CrimeAskDown)
        {
            if (!W139Host) return;
            switch (m.Kind)
            {
                case Protocol.CrimeAskReport when Wo139Rules.TryParseReport(m.Text, out var r):
                    Interlocked.Increment(ref _w139ReportsIn);
                    if (!W139On) { Console.WriteLine($"MP-W139 host: ghost {src}'s {r.Crime} not judged (mp_crime_shared off)"); return; }
                    await ExecLuaAsync(FormattableString.Invariant(
                        $"if KCD2MP_W139HostJudge then KCD2MP_W139HostJudge({src}, {m.Tok}, \"{r.Crime}\", {r.X:F2}, {r.Y:F2}, {r.Z:F2}, \"{(r.Victim.Length > 0 ? r.Victim : "-")}\", \"{(r.Item.Length > 0 ? r.Item : "-")}\", \"{r.Where}\") end"));
                    return;
                case Protocol.CrimeAskOutcome:
                {
                    var f = m.Text.Split(' ');
                    if (f.Length != 6 || !Wo139Rules.TryParseOutcome(string.Join(' ', f[..3]), out string res, out string guard, out int fine)
                        || !Wo139Text.TryCoord(f[3], out float x) || !Wo139Text.TryCoord(f[4], out float y) || !Wo139Text.TryCoord(f[5], out float z)) return;
                    await Wo139HostOutcomeAsync(src, m.Tok, res, guard, fine, x, y, z);
                    return;
                }
                case Protocol.CrimeAskEndFights when Wo154Rules.TryParseEndFights(m.Text, out string endWhy):
                    await Wo154EndFightsForPeerAsync(src, endWhy);   // WO-154 3.4: his mp_unstuck
                    return;
                case Protocol.CrimeAskResync when Wo139Rules.TryParseResync(m.Text, out string why):
                {
                    var rec = Wo139Record(src);
                    await Wo139SendAsync(Protocol.CrimeHostUp, src, Protocol.CrimeHostRecord, 0,
                        Wo139Rules.RecordText(rec.Count, rec.Open.Select(c => c.Kind), rec.Settlements));
                    Console.WriteLine($"MP-W139 host: ghost {src} asked for its record ({why}): {rec.Count} open");
                    return;
                }
            }
            return;
        }
        if (type == Protocol.CrimeHostDown)
        {
            if (!W137JoinerSession) return;
            switch (m.Kind)
            {
                case Protocol.CrimeHostJudged when Wo139Rules.TryParseJudged(m.Text, out string crime, out int wit, out int guards, out bool known, out string st):
                    Console.WriteLine($"MP-W139 joiner: the host's world on my {crime}{(m.Tok != 0 ? $" #{m.Tok}" : "")}: {wit} witness(es), {guards} guard(s) in {(st.Length > 0 ? st : "the wilds")}{(wit == 0 ? " -- nobody saw it" : "")}");
                    if (wit > 0) _ = ExecLuaAsync($"KCD2MP_ShowNativeToast(\"{(guards > 0 ? "A guard saw that." : "Someone saw that. The guards will hear of it.")}\")");
                    return;
                case Protocol.CrimeHostStop when Wo139Rules.TryParseStop(m.Text, out string guard, out var crimes, out int fine):
                    Interlocked.Increment(ref _w139StopsIn);
                    Console.WriteLine($"MP-W139 joiner: stop #{m.Tok} by {guard} for {string.Join(",", crimes.Select(c => $"{c.Crime}:{c.Count}"))} (the crime table's fine {fine / 10.0:F1} groschen) -- the game's own arrest runs here");
                    _w139StoppedHere[guard] = true;
                    await ExecLuaAsync($"if KCD2MP_W139Stop then KCD2MP_W139Stop({m.Tok}, \"{guard}\", \"{string.Join(",", crimes.Select(c => $"{c.Crime}:{c.Count}"))}\") end");
                    return;
                case Protocol.CrimeHostPursue when Wo139Rules.TryParsePursue(m.Text, out bool on, out string pg):
                    Console.WriteLine($"MP-W139 joiner: {pg} {(on ? "fights" : "no longer fights")} me in the host's world");
                    if (on) _ = ExecLuaAsync("KCD2MP_ShowNativeToast(\"The guards are after you.\")");
                    return;
                case Protocol.CrimeHostRecord when Wo139Rules.TryParseRecord(m.Text, out int open, out var rc, out var sts):
                    Console.WriteLine($"MP-W139 joiner: the host's record for me: {open} open ({string.Join(",", rc.Select(c => $"{c.Crime}:{c.Count}"))}) in {string.Join(",", sts)}");
                    return;
                case Protocol.CrimeHostHorses when Wo139Rules.TryParseHorses(m.Text, out int part, out int nparts, out var names):
                    if (part == 1) _w139HorseParts.Clear();
                    _w139HorseParts.AddRange(names);
                    if (part == nparts)
                    {
                        string csv = string.Join(",", _w139HorseParts.Distinct(StringComparer.Ordinal));
                        await ExecLuaAsync($"if KCD2MP_W139LegalHorses then KCD2MP_W139LegalHorses(\"{csv}\") end");
                    }
                    return;
                case Protocol.CrimeHostCleared when Wo139Rules.TryParseCleared(m.Text, out string why, out string cst):
                    Console.WriteLine($"MP-W139 joiner: the host cleared my record in {(cst.Length > 0 ? cst : "every settlement")} ({why})");
                    await ExecLuaAsync($"if KCD2MP_W139Cleared then KCD2MP_W139Cleared(\"{why}\") end");
                    return;
                case Protocol.CrimeHostMode when Wo139Rules.TryParseMode(m.Text, out bool mon, out string mwhy):
                    if (!_w139HostModeKnown || mon != _w139HostModeOn) Console.WriteLine($"MP-W139 joiner: the host's crime sharing is {(mon ? "ON" : "OFF")} ({mwhy})");
                    _w139HostModeKnown = true;
                    _w139HostModeOn = mon;
                    return;
            }
        }
    }

    /// <summary>Joiner, NpcStateDown: the host's stream for a guard that is stopping this player is not applied.</summary>
    private bool Wo139DropStopped(string npcName) => !_w139StoppedHere.IsEmpty && _w139StoppedHere.ContainsKey(npcName);

    // ---------------------------------------------------------------- the mod's events

    private void Wo139OnEvent(string name, string? arg)
    {
        var f = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (name)
        {
            case "w139_cfg":       // shared=on|off (mp_crime_shared)
                if (f.Length >= 1 && f[0].StartsWith("shared=", StringComparison.Ordinal))
                {
                    bool on = f[0] == "shared=on";
                    if (on != _w139SharedOn) Console.WriteLine($"MP-W139 mp_crime_shared {(on ? "ON" : "OFF")}");
                    _w139SharedOn = on;
                    _w139CfgKey = -1;
                    if (_combatRoleApplied && _isDamageAuthority) _ = Wo139SendModeAsync(on ? "mp_crime_shared-on" : "mp_crime_shared-off");
                }
                return;
            case "w139_crime":     // <kind> <x> <y> <z> <victim|-> <cls|-> <where>
                if (W139Joiner && W139On && Wo139Rules.TryParseReport(string.Join(' ', f), out var r)) _ = Wo139ReportAsync(r, "this game's own crime");
                return;
            case "w139_judged":
                if (W139Host) _ = Wo139OnJudgedAsync(f);
                return;
            case "w139_guards":
                if (W139Host) _ = Wo139OnGuardsAsync(f);
                return;
            case "w139_horses":    // <name,name|-> (host)
                if (W139Host && f.Length >= 1)
                {
                    var names = f[0] == "-" ? new List<string>() : f[0].Split(',').Where(Wo139Text.IsName).ToList();
                    string csv = string.Join(",", names);
                    // sent on a change, and again every 30 s (a joiner that joined or reloaded since)
                    if (csv == _w139HorsesSent && Environment.TickCount64 - _w139HorsesSentAtMs < 30_000) return;
                    _w139HorsesSent = csv;
                    _w139HorsesSentAtMs = Environment.TickCount64;
                    foreach (byte g in Wo134Peers())
                        foreach (var t in Wo139Rules.HorsesTexts(names)) _ = Wo139SendAsync(Protocol.CrimeHostUp, g, Protocol.CrimeHostHorses, 0, t);
                }
                return;
            case "w139_outcome":   // <stopId> <result> <guard> <paid> [x y z] (joiner)
                if (f.Length >= 4 && uint.TryParse(f[0], out uint sid) && Wo139Text.IsResult(f[1]) && Wo139Text.IsNameOrDash(f[2]) && int.TryParse(f[3], out int paid))
                {
                    string pos = f.Length >= 7 ? $"{f[4]} {f[5]} {f[6]}" : "0 0 0";
                    _w139StoppedHere.TryRemove(f[2], out _);
                    if (W137JoinerSession)
                        _ = Wo139SendAsync(Protocol.CrimeAskUp, Protocol.JoinTargetHost, Protocol.CrimeAskOutcome, sid, $"{Wo139Rules.OutcomeText(f[1], f[2], paid)} {pos}");
                    Console.WriteLine($"MP-W139 joiner: stop #{sid} by {f[2]} -> {f[1]}{(paid > 0 ? $" ({paid / 10.0:F1} groschen paid from this Henry)" : "")} -- told the host");
                }
                return;
            case "w139_stop":      // on|off <guard>
                if (f.Length >= 2 && Wo139Text.IsName(f[1]))
                {
                    if (f[0] == "on") _w139StoppedHere[f[1]] = true;
                    else _ = Task.Run(async () => { await Task.Delay(3000); _w139StoppedHere.TryRemove(f[1], out _); });
                }
                return;
            case "w139_horse":     // <name> 1|0 (joiner): the crime side of a legal horse
                if (f.Length >= 2 && Wo139Text.IsName(f[0]))
                {
                    bool on = f[1] == "1";
                    if (on) Interlocked.Increment(ref _w139HorseLegal);
                    _ = Task.Run(async () =>
                    {
                        var res = await _combat.Wo139ContextAsync(on, "crime_ignoredHorseTheft_Horse", f[0]);
                        Console.WriteLine($"MP-W139 joiner: {f[0]} {(on ? "legal to ride here (the host's Henry may ride it)" : "back to its own rules")} -- crime_ignoredHorseTheft_Horse {(res is 1 or 0 ? "ok" : $"not set ({res?.ToString(CultureInfo.InvariantCulture) ?? "no answer"})")}");
                    });
                }
                return;
            case "w139_punish":
                return;
            case "w139_status":
                Console.WriteLine(Wo139StatsLine());
                _ = Task.Run(async () => { var s = await _combat.Wo139StatusAsync(); Console.WriteLine($"MP-W139 native: {s ?? "no answer"}"); });
                return;
        }
    }
}
