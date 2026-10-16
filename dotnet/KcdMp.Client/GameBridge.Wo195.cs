// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-195: three field fixes from a 0.60.5 joiner's evening (Wo195Rules has the rules; docs/WO-195-findings.md the evidence).
//   1  mp_dead_release (joiner): at every death apply of a copy ([npcdeath]: owner-death, 0x31 FATAL, 0x27 transition / resync, this
//      player's own fatal blow) every fight hold goes at once -- WO-132's engagement (logged now: 0.60.5 removed it quietly), WO-147's
//      own engagement, WO-187's guard hold and opponent wish (and its pin), the DLL's combat mode on the body (op 56). A copy marked dead
//      is never engaged, held or wished again. One second later the body is checked (WO195-FALL: combat mode off, physics a ragdoll),
//      two seconds later this player's own combat state (WO195-PLAYER, the mod's Lua).
//   2  mp_stealth_witness: the joiner's mod sends its stealth snapshot (w195_stealth, once a second on a change); every crime report
//      to the host carries the latest (the 8th field). The host feeds its NPCs' sleep from WO-141's own activity read
//      (KCD2MP_W195Asleep) and its witness rule uses both (the mod's Lua, WO195-WITNESS lines).
//   3  mp_body_loot_host: a body take marked "screen" (the loot screen's own key) goes to the host's take as such (the host's Lua grants it
//      once per body and class, and removes the copy its own loot screen would make).
// Log tags: WO195-RELEASE, WO195-FALL, WO195-DEAD, WO195-CFG, WO195-STATS (the Lua: WO195-WITNESS, WO195-LOOT, WO195-PLAYER).
public partial class GameBridge
{
    private volatile bool _w195DeadRelease = Wo195Rules.DefaultDeadRelease, _w195StealthWitness = Wo195Rules.DefaultStealthWitness,
                          _w195BodyLootHost = Wo195Rules.DefaultBodyLootHost;
    private readonly ConcurrentDictionary<string, long> _w195Dead = new(StringComparer.Ordinal);       // copy -> when its release ran
    private readonly ConcurrentDictionary<string, byte> _w195Refused = new(StringComparer.Ordinal);    // copy -> what was refused (said once)
    private readonly ConcurrentDictionary<string, bool> _w195HostAsleep = new(StringComparer.Ordinal); // host: NPC -> asleep (its activity)
    private volatile string _w195Stealth = "";   // joiner: the mod's latest stealth snapshot
    private long _w195TickN, _w195Releases, _w195ReleasedEngaged, _w195Refusals, _w195FallDown, _w195FallStanding, _w195AsleepSent,
                 _w195StealthIn, _w195ScreenTakes, _w195Ragdolled;

    // ---------------------------------------------------------------- switches

    private void Wo195OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w195_cfg":
                foreach (var kv in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    bool on = kv[(eq + 1)..] == "on";
                    switch (kv[..eq])
                    {
                        case "dead": _w195DeadRelease = on; break;
                        case "stealth": _w195StealthWitness = on; break;
                        case "bodyloot": _w195BodyLootHost = on; break;
                    }
                }
                Console.WriteLine($"WO195-CFG dead_release={On(_w195DeadRelease)} stealth_witness={On(_w195StealthWitness)} body_loot_host={On(_w195BodyLootHost)}");
                _ = Wo195PushConfigAsync();
                return;
            case "w195_stealth":   // the joiner's mod: its stealth snapshot (Wo195Rules.Stealth's text)
                if (Wo195Rules.TryParseStealth(arg, out _)) { _w195Stealth = arg!; Interlocked.Increment(ref _w195StealthIn); }
                return;
            case "w195_status":
                _ = Wo195StatusAsync();
                return;
            case "w195_check":     // KCD2MP_W195Check("<npc>"): the DLL's read of a copy now (WO195-CHECK)
                if (arg is { Length: > 0 } cn && NpcNamePattern.IsMatch(cn)) _ = Wo195CheckNowAsync(cn);
                return;
        }
    }

    private void Wo195OnConnect() => _ = ExecLuaAsync("if KCD2MP_W195Cfg then KCD2MP_W195Cfg() end");

    private async Task Wo195PushConfigAsync()
    {
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo195OpConfig, [(byte)(_w195DeadRelease ? 1 : 0)]);
            if (r is { Ok: true, Payload.Length: >= 1 } x && x.Payload[0] != (_w195DeadRelease ? 1 : 0))
                Console.WriteLine($"WO195-CFG the DLL holds dead_release={x.Payload[0]}");
        }
        catch (Exception ex) { Console.WriteLine($"WO195-CFG config not pushed ({ex.GetType().Name})"); }
    }

    private async Task Wo195StatusAsync()
    {
        string? n = null;
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo195OpStatus, []);
            if (r is { Ok: true } x) n = Encoding.ASCII.GetString(x.Payload);
        }
        catch { }
        Console.WriteLine(FormattableString.Invariant(
            $"WO195-STATS dead_release={On(_w195DeadRelease)} stealth_witness={On(_w195StealthWitness)} body_loot_host={On(_w195BodyLootHost)} releases={Interlocked.Read(ref _w195Releases)} released_engaged={Interlocked.Read(ref _w195ReleasedEngaged)} refusals={Interlocked.Read(ref _w195Refusals)} fall_down={Interlocked.Read(ref _w195FallDown)} fall_standing={Interlocked.Read(ref _w195FallStanding)} ragdolled={Interlocked.Read(ref _w195Ragdolled)} marked_dead={_w195Dead.Count} asleep_sent={Interlocked.Read(ref _w195AsleepSent)} host_asleep={_w195HostAsleep.Count(kv => kv.Value)} stealth_in={Interlocked.Read(ref _w195StealthIn)} screen_takes={Interlocked.Read(ref _w195ScreenTakes)} | native: {n ?? "no answer"}"));
    }

    /// <summary>Once a second (the WO-151 loop): the switch every 10 s, the stats every minute.</summary>
    private async Task Wo195TickAsync(CancellationToken ct)
    {
        long n = ++_w195TickN;
        if (n % 10 == 3) await Wo195PushConfigAsync();
        if (n % 60 == 40) await Wo195StatusAsync();
        _ = ct;
    }

    // ---------------------------------------------------------------- 1: dead copies

    /// <summary>A copy marked dead here (its release ran) is never engaged, guard-held or wished again.</summary>
    private bool Wo195IsDead(string npc) => Wo195Rules.Blocks(_w195DeadRelease, _w195Dead.ContainsKey(npc));

    /// <summary>The host's stream says it lives again (a reload on their side): the mark goes.</summary>
    private void Wo195NoteAlive(string npc)
    {
        if (_w195Dead.TryRemove(npc, out _)) { _w195Refused.TryRemove(npc, out _); Console.WriteLine($"WO195-DEAD npc={npc} alive again on the host's stream (a reload) -- it may fight again"); }
    }

    /// <summary>A refusal of a dead copy's engagement / hold (said once per copy).</summary>
    private void Wo195NoteRefused(string npc, string what)
    {
        Interlocked.Increment(ref _w195Refusals);
        if (_w195Refused.TryAdd(npc, 1)) Console.WriteLine($"WO195-DEAD npc={npc} refused={what} -- a dead copy is never engaged, held or wished again (said once per copy)");
    }

    /// <summary>
    /// Every death apply of a copy (ApplyRemoteNpcDeathAsync, before the apply itself; this player's own fatal blow): every fight hold goes now.
    ///   WO195-RELEASE npc=&lt;n&gt; path=&lt;owner-death|0x31-fatal|0x27-transition|0x27-resync|local-fatal&gt; engaged=yes|no local=yes|no wish=yes|no
    ///                 guard=yes|no native_held=&lt;engage+guard+wish+pin+combat|none&gt; combat_mode=on|off|? living=yes|no|?
    /// </summary>
    private async Task Wo195OnCopyDeathAsync(string npc, string via)
    {
        if (!_w195DeadRelease || string.IsNullOrEmpty(npc)) return;
        long now = Environment.TickCount64;
        long last = _w195Dead.TryGetValue(npc, out long at) ? at : 0;
        _w195Dead[npc] = now;
        if (!Wo195Rules.ReleaseDue(true, last, now)) return;
        Interlocked.Increment(ref _w195Releases);
        string path = Wo195Rules.PathTag(via);
        bool engaged = _w132Engaged.TryRemove(npc, out var eg);
        uint eid = eg.Eid != 0 ? eg.Eid : _w131Guarded.TryGetValue(npc, out uint ge) ? ge : _npcEntityIds.TryGetValue(npc, out uint pe) ? pe : 0;
        if (engaged)
        {
            Interlocked.Increment(ref _w132EngageOff);
            Interlocked.Increment(ref _w195ReleasedEngaged);
            var r = await _combat.Wo132EngageAsync(false, eg.Eid, default);
            Console.WriteLine($"MP-W132 engage off {npc}: the copy is dead ({path}) -> released, its skirmish left ({(r.Ok ? "ok" : "reason " + r.Reason)}) (WO-195)");
        }
        bool local = _w147Local.ContainsKey(npc);
        if (local) await Wo147ReleaseAsync(npc, "the copy is dead (WO-195)");
        _w183Left.Forget(npc);
        Wo190ForgetTurn(npc);
        bool wish = _w187OppLast.TryRemove(npc, out _);
        bool guard = _w187GuardLast.TryRemove(npc, out _);
        _w187Fights.TryRemove(npc, out _);
        byte held = 0, cm = 255, living = 255;
        if (eid != 0)
        {
            try
            {
                await Wo187SendGuardAsync(eid, false, default);                       // op 39 off
                var clear = new byte[10];
                BinaryPrimitives.WriteUInt32LittleEndian(clear, eid);                 // op 36, kind 0: the wish (and its pin) cleared
                await _combat.Wo163Async(Protocol.Wo187OpOpponent, clear);
                var a = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
                var rr = await _combat.Wo163Async(Protocol.Wo195OpRelease, a);         // op 56: everything the DLL holds, at once
                if (rr is { Ok: true, Payload.Length: >= 3 } x) { held = x.Payload[0]; cm = x.Payload[1]; living = x.Payload[2]; }
            }
            catch { }
        }
        Console.WriteLine($"WO195-RELEASE npc={npc} path={path} eid=0x{eid:X} engaged={(engaged ? "yes" : "no")} local={(local ? "yes" : "no")} wish={(wish ? "yes" : "no")} guard={(guard ? "yes" : "no")} native_held={Wo195Rules.HeldText(held)} combat_mode={Wo195Rules.Tri(cm, "on", "off")} living={Wo195Rules.Tri(living, "yes", "no")} -- never engaged, held or wished again; the game's own death takes the body");
        if (eid != 0) _ = Wo195CheckLaterAsync(npc, eid, now);
    }

    /// <summary>The release's proof: the body 1 s later (combat mode off, a ragdoll), this player's combat state 2 s later (the Lua).</summary>
    private async Task Wo195CheckLaterAsync(string npc, uint eid, long releasedMs)
    {
        try
        {
            await Task.Delay(Wo195Rules.FallCheckMs);
            var a = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
            var r = await _combat.Wo163Async(Protocol.Wo195OpCheck, a);
            if (r is { Ok: true, Payload.Length: >= 4 } x)
            {
                // the mod's ApplyDeath kills the soul with no death reaction: the copy stays upright, frozen in the dead action's first pose
                // (live: head 1.4 m up, physics still a living entity; a vanilla kill lies as a ragdoll). The game's own ragdoll then lays it down.
                bool upright = Wo195Rules.Upright(x.Payload[0], x.Payload[2]);
                if (upright)
                {
                    await ExecLuaAsync($"if KCD2MP_W195Ragdoll then KCD2MP_W195Ragdoll(\"{EscapeLua(npc)}\") end");
                    await Task.Delay(Wo195Rules.RagdollSettleMs);
                    var r2 = await _combat.Wo163Async(Protocol.Wo195OpCheck, a);
                    if (r2 is { Ok: true, Payload.Length: >= 4 } y) x = y;
                }
                bool down = x.Payload[1] != 1 && x.Payload[3] == 0 && x.Payload[2] == 0;
                Interlocked.Increment(ref down ? ref _w195FallDown : ref _w195FallStanding);
                if (upright) Interlocked.Increment(ref _w195Ragdolled);
                Console.WriteLine($"WO195-FALL npc={npc} after_ms={Environment.TickCount64 - releasedMs} dead={(x.Payload[0] == 1 ? "yes" : "no")} combat_mode={Wo195Rules.Tri(x.Payload[1], "on", "off")} living={Wo195Rules.Tri(x.Payload[2], "yes", "no(ragdoll)")} fight_held={(x.Payload[3] == 1 ? "YES" : "no")} ragdolled={(upright ? "yes (it stood upright)" : "no (not needed)")}");
            }
            else Console.WriteLine($"WO195-FALL npc={npc} no answer (the body is gone, or an older DLL)");
            await Task.Delay(Wo195Rules.PlayerCheckMs - Wo195Rules.FallCheckMs);
            int others = _w132Engaged.Count + _w147Local.Count;
            await ExecLuaAsync($"if KCD2MP_W195PlayerCheck then KCD2MP_W195PlayerCheck(\"{EscapeLua(npc)}\", {others}) end");
        }
        catch (Exception ex) { Console.WriteLine($"WO195-FALL npc={npc} check failed ({ex.GetType().Name})"); }
    }

    /// <summary>KCD2MP_W195Check: one read of a copy by the DLL (op 57) -- dead, combat mode, a ragdoll, a fight hold.</summary>
    private async Task Wo195CheckNowAsync(string npc)
    {
        uint eid = _w131Guarded.TryGetValue(npc, out uint ge) ? ge : _npcEntityIds.TryGetValue(npc, out uint pe) ? pe : 0;
        byte[] a;
        if (eid != 0) { a = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(a, eid); }
        else { var nb = Encoding.ASCII.GetBytes(npc); a = new byte[5 + nb.Length]; a[4] = (byte)nb.Length; nb.CopyTo(a, 5); }   // by name: any body
        var r = await _combat.Wo163Async(Protocol.Wo195OpCheck, a);
        Console.WriteLine(r is { Ok: true, Payload.Length: >= 4 } x
            ? $"WO195-CHECK npc={npc} eid=0x{eid:X} dead={(x.Payload[0] == 1 ? "yes" : "no")} combat_mode={Wo195Rules.Tri(x.Payload[1], "on", "off")} living={Wo195Rules.Tri(x.Payload[2], "yes", "no(ragdoll)")} fight_held={(x.Payload[3] == 1 ? "YES" : "no")} marked_dead={(_w195Dead.ContainsKey(npc) ? "yes" : "no")} engaged={(_w132Engaged.ContainsKey(npc) ? "YES" : "no")}"
            : $"WO195-CHECK npc={npc} eid=0x{eid:X} no answer");
    }

    /// <summary>This player's own fatal blow on a copy (the DLL's LocalHit, died): its name by its soul, then the same release.</summary>
    private void Wo195OnLocalFatal(Guid soul)
    {
        if (!_w195DeadRelease) return;
        foreach (var (name, v) in _copySoulGuid)
            if (v.Guid == soul) { _ = Wo195OnCopyDeathAsync(name, "local-fatal"); return; }
    }

    // ---------------------------------------------------------------- 2: stealth

    /// <summary>Joiner: the stealth snapshot every crime report carries (empty with the switch off or none read yet).</summary>
    private string Wo195StealthForReport() => _w195StealthWitness ? _w195Stealth : "";

    /// <summary>Host: WO-141's capture of its NPCs -- an NPC that lies on its bed is asleep for the witness rule (sent on a change only).</summary>
    private void Wo195NoteHostActivity(string npc, ActivityState a)
    {
        if (!_isDamageAuthority || string.IsNullOrEmpty(npc) || !NpcNamePattern.IsMatch(npc)) return;
        bool asleep = Wo195Rules.AsleepByActivity(a);
        bool was = _w195HostAsleep.TryGetValue(npc, out bool w) && w;
        if (asleep == was) return;
        _w195HostAsleep[npc] = asleep;
        Interlocked.Increment(ref _w195AsleepSent);
        _ = ExecLuaAsync($"if KCD2MP_W195Asleep then KCD2MP_W195Asleep(\"{EscapeLua(npc)}\", {(asleep ? 1 : 0)}) end");
    }

    // ---------------------------------------------------------------- 3: the loot screen's key

    private void Wo195NoteScreenTake() => Interlocked.Increment(ref _w195ScreenTakes);
}
