// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-224 (docs/WO-224-findings.md): grabs and combos in step -- the agent's half.
//
// A (w224_clinch). A player's clinch goes out as AvatarClinch (WO-197's poll in the DLL): enter, each move with both rows, leave, and
// the body he clinched. The receiver starts the same clinch the engine's way between his figure and that body (the DLL's op 222: the
// engine's own start with its action type for a new clinch), plays each move's two rows on the pair, and ends the clinch on both bodies
// when he has left it and they have not (op 223). The DLL's detours count every clinch start and the engine's clinch test
// (mp_w224_status).
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w224On = new(StringComparer.Ordinal);
    private bool W224On(string name) => _w224On.TryGetValue(name, out bool v) ? v : !W224Switch.TwinOff(name);
    private int _w224Pushing;
    private long _w224EngineRagdolls, _w224RawRagdolls, _w224LeftForSweep;   // D: dead copies down without the script's ragdoll / thrown by it 2.5 s after the death (the switch off) / left standing for the sweep
    private long _w224ClinchIn, _w224ClinchNoBody, _w224Starts, _w224Started, _w224Already, _w224Refused, _w224Moves, _w224MovesPlayed, _w224Leaves, _w224Ended,
        _w224NamedByHash, _w224EndedSilent, _w224CouldNotEnterIn;
    /// <summary>The partner figures in a clinch here, by figure entity: the body it clinched, when its player's machine last said so.</summary>
    private readonly ConcurrentDictionary<uint, (uint Opp, long AtMs, string Ghost)> _w224Clinches = new();

    private async Task Wo224PushNativeAsync()
    {
        if (Interlocked.CompareExchange(ref _w224Pushing, 1, 0) != 0) return;
        try
        {
            var body = new byte[5];
            BitConverter.GetBytes(W224Switch.NativeBits(W224On)).CopyTo(body, 0);
            body[4] = Wo218Role();
            await _combat.Wo163Async(Protocol.Wo224OpConfig, body);
        }
        catch (Exception ex) { Console.WriteLine($"MP-W224 the native word was not sent: {ex.Message}"); }
        finally { Interlocked.Exchange(ref _w224Pushing, 0); }
    }

    private async Task Wo224NativeStatusAsync()
    {
        try
        {
            for (byte page = 0; page < 30; page++)
            {
                var r = await _combat.Wo163Async(Protocol.Wo224OpStatus, [page]);
                if (r is not { Ok: true } rr || rr.Payload.Length == 0) break;
                Console.WriteLine($"MP-W224-NATIVE {Encoding.ASCII.GetString(rr.Payload)}");
            }
        }
        catch { }
    }

    private async Task Wo224TellModAsync()
    {
        await ExecLuaAsync($"if KCD2MP_W224Cfg then KCD2MP_W224Cfg(\"{string.Join(',', W224Switch.Names.Select(n => n + "=" + (W224On(n) ? "on" : "off")))}\") end");
        // C: the mod menu's line. The switch off = not checked, and a build that is not checked reads unofficial.
        var (ok, version, why) = OfficialBuild.ConsoleWord(W224On(W224Switch.Official), ReleaseVersionInfo.Official);
        await ExecLuaAsync($"if KCD2MP_W224Official then KCD2MP_W224Official({(ok ? 1 : 0)}, \"{version}\", \"{why}\") end");
    }


    private string Wo224StatsLine() =>
        $"MP-W224-STATS {string.Join(' ', W224Switch.Names.Select(n => $"{n}={(W224On(n) ? "on" : "off")}"))} build=\"{(W224On(W224Switch.Official) ? ReleaseVersionInfo.Official.Text : OfficialBuild.UnofficialText)}\"" +
        $" dead_down_without_script_ragdoll={Interlocked.Read(ref _w224EngineRagdolls)} script_ragdoll_at_2500ms={Interlocked.Read(ref _w224RawRagdolls)} left_for_the_sweep={Interlocked.Read(ref _w224LeftForSweep)}" +
        $" clinch_in={Interlocked.Read(ref _w224ClinchIn)} no_body={Interlocked.Read(ref _w224ClinchNoBody)} named_by_hash={Interlocked.Read(ref _w224NamedByHash)}" +
        $" starts={Interlocked.Read(ref _w224Starts)} started={Interlocked.Read(ref _w224Started)} already={Interlocked.Read(ref _w224Already)} refused={Interlocked.Read(ref _w224Refused)}" +
        $" moves={Interlocked.Read(ref _w224Moves)} moves_played={Interlocked.Read(ref _w224MovesPlayed)} leaves={Interlocked.Read(ref _w224Leaves)}" +
        $" ended_here={Interlocked.Read(ref _w224Ended)} ended_silent={Interlocked.Read(ref _w224EndedSilent)} he_could_not_enter={Interlocked.Read(ref _w224CouldNotEnterIn)} open={_w224Clinches.Count}" +
        $" | own figure in the host's world: told_out={Interlocked.Read(ref _w224FigOut)} told_in={Interlocked.Read(ref _w224FigIn)} no_copy={Interlocked.Read(ref _w224FigNoBody)}" +
        $" enters_told_to_the_dll={Interlocked.Read(ref _w224FigStarted)} leaves_told={Interlocked.Read(ref _w224FigEnded)} not_taken={Interlocked.Read(ref _w224FigRefused)}" +
        $" refusals_out={Interlocked.Read(ref _w224FigRefusalsOut)} refusals_in={Interlocked.Read(ref _w224FigRefusalsIn)}" +
                $" | copies' weapon classes: asked={Interlocked.Read(ref _w224WeaponAsks)} refreshed={Interlocked.Read(ref _w224WeaponRefreshed)}" +
        $" | chain inputs: host seen={Interlocked.Read(ref _w224ChainSeen)} sent={Interlocked.Read(ref _w224ChainOut)} not_sent={Interlocked.Read(ref _w224ChainNotSent)}" +
        $" ; in={Interlocked.Read(ref _w224ChainIn)} queued={Interlocked.Read(ref _w224ChainQueued)} not_taken={Interlocked.Read(ref _w224ChainNotTaken)} dropped={Interlocked.Read(ref _w224ChainDropped)}";

    /// <summary>The 4 Hz session tick: the native word every 2 s, the status and the stats every 30 s, the mod told every minute; a
    /// clinch nobody has spoken of for a while is ended.</summary>
    private void Wo224Tick(long n)
    {
        if (n % 8 == 3) _ = Wo224PushNativeAsync();
        if (n % 120 == 83 && (W137Host || W137Joiner)) { _ = Wo224NativeStatusAsync(); Console.WriteLine(Wo224StatsLine()); }
        if (n % 240 == 103) _ = Wo224TellModAsync();
        if (n % 4 == 2 && !_w224Clinches.IsEmpty) _ = Wo224SilentTickAsync();
    }

    private void Wo224OnConnect(CancellationToken ct)
    {
        _w224Clinches.Clear();
        _ = Wo224TellModAsync(); _ = Wo224PushNativeAsync();
    }

    private void Wo224OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w224_cfg":
            {
                int eq = (arg ?? "").IndexOf('=');
                if (eq <= 0 || arg![(eq + 1)..] is not ("on" or "off") || !W224Switch.Names.Contains(arg[..eq])) { Console.WriteLine($"MP-W224 cfg refused: '{arg}'"); return; }
                _w224On[arg[..eq]] = arg[(eq + 1)..] == "on";
                Console.WriteLine($"MP-W224 cfg {arg}");
                _ = Wo224PushNativeAsync();
                _ = Wo224TellModAsync();
                return;
            }
            case "w224_weapon":   // the mod drew (or checked) a copy's weapon: its combat weapon classes are refreshed the engine's way
                if (W224On("w224_clinch") && arg is { Length: > 0 } wn && _npcEntityIds.TryGetValue(wn, out uint we) && we != 0) _ = Wo224WeaponRefreshAsync(wn, we);
                return;
            case "w224_status":
                _ = Wo224NativeStatusAsync();
                Console.WriteLine(Wo224StatsLine());
                return;
        }
    }

    // ---------------------------------------------------------------- B: an NPC's chain input
    private long _w224ChainSeen, _w224ChainOut, _w224ChainNotSent, _w224ChainIn, _w224ChainQueued, _w224ChainNotTaken, _w224ChainDropped;

    /// <summary>B (the host): one of its NPCs, fighting a joiner's figure, made its combo input with its swing's chain slot open (the
    /// DLL's watch on the engine's combo input). Sent at once, before the follow-up's own row; with the switch off it is counted
    /// and said, not sent (the bench's twin measures the lead with it).</summary>
    private async Task Wo224ChainOutAsync(LocalActionFrame f, uint ms, Stream s)
    {
        Interlocked.Increment(ref _w224ChainSeen);
        int input = BinaryPrimitives.ReadInt32LittleEndian(f.Row.ToByteArray());
        string zone = f.Flags == 0xFF ? "-" : f.Flags.ToString(CultureInfo.InvariantCulture);
        string? why = !W137Host ? "not the host of a shared world" : !W224On("w224_chains") ? "w224_chains off" : !_npcRows ? "npc rows off"
            : !NpcNamePattern.IsMatch(f.Name) || Protocol.IsNeverSyncedNpcName(f.Name) ? "not a synced name" : null;
        if (why is not null)
        {
            Interlocked.Increment(ref _w224ChainNotSent);
            Console.WriteLine($"MP-ACTION section=outbound kind=NpcChain npc={f.Name} zone={zone} input={input} dispatch=not-sent ({why})");
            return;
        }
        byte[] pkt = _actionOut.Build(ActionKind.NpcChain, ActionPhase.Commit, new RowEvent(ms, f.Flags, f.Row, f.Name).ToBytes());
        await WritePacketAsync(s, pkt, _wo121Ct);
        _w121EvOut++;
        Interlocked.Increment(ref _w224ChainOut);
        Console.WriteLine($"MP-ACTION section=outbound kind=NpcChain gen={_actionOut.Gen} npc={f.Name} zone={zone} input={input}");
    }

    /// <summary>B (the joiner): the host's NPC made its chain input -- its copy here makes the same one when its own chain slot opens
    /// (the DLL's op 228; wo209.cpp's chain_tick). A copy that fights someone else is not taken: it plays the owner's animation.</summary>
    private async Task Wo224OnChainInAsync(InboundAction a, CancellationToken ct)
    {
        if (!RowEvent.TryFromBytes(a.Payload, out var re) || re.Name.Length == 0) { Interlocked.Increment(ref _w224ChainDropped); Console.WriteLine($"MP-ACTION section=inbound kind=NpcChain dispatch=dropped-malformed len={a.Payload.Length}"); return; }
        Interlocked.Increment(ref _w224ChainIn);
        string head = $"MP-ACTION section=inbound kind=NpcChain npc={re.Name} zone={(re.Flags == 0xFF ? "-" : re.Flags.ToString(CultureInfo.InvariantCulture))}";
        uint eid = 0;
        string? why = !W224On("w224_chains") ? "w224_chains off" : !W137Joiner ? "not a joiner in the host's world" : !_npcRows ? "npc rows off" : Wo172RowOnDead(re.Name) ? "the copy is dead"
            : !_npcEntityIds.TryGetValue(re.Name, out eid) || eid == 0 ? "no copy of it here" : null;
        if (why is not null) { Interlocked.Increment(ref _w224ChainDropped); Console.WriteLine($"{head} dispatch=dropped ({why}; its follow-up's row is played as before)"); return; }
        var b = new byte[9];
        BinaryPrimitives.WriteUInt32LittleEndian(b, eid);
        b[4] = re.Flags;
        re.Row.ToByteArray().AsSpan(0, 4).CopyTo(b.AsSpan(5));   // the engine's input class, as the host's NPC passed it
        try
        {
            var r = await _combat.Wo163Async(Protocol.Wo224OpChainInput, b, ct);
            byte res = r is { Ok: true, Payload.Length: >= 1 } x ? x.Payload[0] : (byte)255;
            if (res == 1) Interlocked.Increment(ref _w224ChainQueued); else Interlocked.Increment(ref _w224ChainNotTaken);
            Console.WriteLine($"{head} dispatch={(res == 1 ? "queued (the copy makes the same input when its own chain slot opens)" : res == 0 ? "not-taken (its attacks are not its own here: it fights someone else)" : "native-failed (its follow-up's row is played as before)")}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Interlocked.Increment(ref _w224ChainNotTaken); Console.WriteLine($"{head} dispatch=native-failed ({ex.GetType().Name}; its follow-up's row is played as before)"); }
    }

    private long _w224WeaponAsks, _w224WeaponRefreshed;

    /// <summary>The host NPC's weapon, as its hand's last word said (the item's class; "-" = it holds nothing), by NPC name.</summary>
    private readonly ConcurrentDictionary<string, string> _w224HostWeapon = new(StringComparer.Ordinal);

    /// <summary>A: the host NPC's hand changed (WO-167's word): its weapon class is told to the DLL with the refresh.</summary>
    private void Wo224OnHostWeapon(string name, string itemClass)
    {
        if (!W224On("w224_clinch")) return;
        _w224HostWeapon[name] = itemClass;
        if (_npcEntityIds.TryGetValue(name, out uint eid) && eid != 0) _ = Wo224WeaponRefreshAsync(name, eid);
    }

    private byte Wo224WeaponHint(string name)
    {
        if (!_w224HostWeapon.TryGetValue(name, out string? w)) return Wo224Rules.WeaponHintNone;
        if (w == "-") return Wo224Rules.WeaponHintNothing;
        var catalog = _swingCatalog.IsCompletedSuccessfully ? _swingCatalog.Result : null;
        return Wo224Rules.WeaponHint(catalog is not null && Guid.TryParse(w, out var g) ? catalog.WeaponClassOfItem(g) : null, false);
    }

    private async Task Wo224WeaponRefreshAsync(string name, uint eid)
    {
        try
        {
            var b = new byte[5];
            BinaryPrimitives.WriteUInt32LittleEndian(b, eid);
            b[4] = Wo224WeaponHint(name);
            Interlocked.Increment(ref _w224WeaponAsks);
            var r = await _combat.Wo163Async(Protocol.Wo224OpWeaponRefresh, b);
            if (r is { Ok: true, Payload.Length: >= 3 } x && x.Payload[0] == 1)
            {
                Interlocked.Increment(ref _w224WeaponRefreshed);
                Console.WriteLine($"MP-W224 weapon npc={name}: its combat weapon class read {(sbyte)x.Payload[1]}, its hands another -- refreshed the engine's way: now {(sbyte)x.Payload[2]}");
            }
        }
        catch { }
    }

    private long _w224CopyRows;

    /// <summary>B (the instrument, a joiner): the row a copy's own attack plays here (the DLL's capture of its attack action), one line a
    /// swing: W224-COPYROW npc=.. row=.. table=.. spec="..".</summary>
    private void Wo224NoteCopyRow(LocalActionFrame f)
    {
        if (!W137Joiner || !W224On("w224_clinch_log")) return;
        long k = Interlocked.Increment(ref _w224CopyRows);
        string what = _rowCatalog.IsCompletedSuccessfully && _rowCatalog.Result is { } cat && cat.TryGet(f.Row, out var known) ? $"table={known.Table} spec=\"{known.Spec}\"" : "table=UNKNOWN";
        Console.WriteLine($"W224-COPYROW npc={f.Name} row={f.Row} {what} [n={k}]");
    }

    // ---------------------------------------------------------------- A: a partner's clinch here

    private static byte[] Wo224Pair(uint a, uint b)
    {
        var x = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(x, a);
        BinaryPrimitives.WriteUInt32LittleEndian(x.AsSpan(4), b);
        return x;
    }

    /// <summary>The body a partner's clinch event names (its name's hash among the bodies known here), or 0.</summary>
    private uint Wo224NamedBody(RowEvent re, out string? name)
    {
        name = null;
        uint h = Wo224Rules.ClinchOpponentHash(re.Name);
        if (h == 0) return 0;
        name = Wo224Rules.NameOfHash(h, _npcEntityIds.Keys);
        return name is not null && _npcEntityIds.TryGetValue(name, out uint e) ? e : 0;
    }

    /// <summary>The partner's clinch event with w224_clinch on (Wo197OnClinchInAsync hands it over once his figure and the body are
    /// known): enter -> the engine's own clinch start on the pair; a move -> both rows on the pair (WO-197's pair op); leave -> the
    /// pair's clinch ended here when it has not ended by itself.</summary>
    private async Task Wo224OnClinchAsync(string head, string gid, uint feid, uint opp, bool master, byte phase, RowEvent re, ActionRowCatalog catalog, CancellationToken ct)
    {
        Interlocked.Increment(ref _w224ClinchIn);
        uint mEid = master ? feid : opp, sEid = master ? opp : feid;
        long now = Environment.TickCount64;
        if (phase == Wo197Rules.ClinchEnter)
        {
            Interlocked.Increment(ref _w224Starts);
            _w224Clinches[feid] = (opp, now, gid);
            // the engine refuses a start while the two bodies are not at the row's distance yet (his figure trails him by the link):
            // asked again for a moment, as the engine's own auto-trigger asks every frame
            byte res = 255;
            int tries = 0;
            for (; tries < Wo224Rules.ClinchStartTries; tries++)
            {
                if (tries > 0) await Task.Delay(Wo224Rules.ClinchStartRetryMs, ct);
                if (!_w224Clinches.TryGetValue(feid, out var cur) || cur.AtMs != now) break;   // he left it, or entered another, meanwhile
                var r = await _combat.Wo163Async(Protocol.Wo224OpClinchStart, Wo224Pair(mEid, sEid), ct);
                res = r is { Ok: true, Payload.Length: >= 1 } x ? x.Payload[0] : (byte)255;
                if (res != 0) break;
            }
            if (res == 1) Interlocked.Increment(ref _w224Started); else if (res == 2) Interlocked.Increment(ref _w224Already); else Interlocked.Increment(ref _w224Refused);
            if (res is 1 or 2) Interlocked.Increment(ref _w197ClinchPlayed);
            Console.WriteLine($"{head} master=0x{mEid:X} slave=0x{sEid:X} dispatch=clinch-start result={(res == 1 ? "started the engine's way" : res == 2 ? "in a clinch with each other already" : res == 0 ? "refused by the engine" : "not possible here")} tries={tries + (res != 0 && tries < Wo224Rules.ClinchStartTries ? 1 : 0)}");
            // the host's world is the one that counts: a clinch a joiner's own game started that the host's engine will not start is
            // told back to him as over, and his game ends it (never one body in a clinch on one screen only)
            if (res is not (1 or 2) && W137Host && byte.TryParse(gid, NumberStyles.None, CultureInfo.InvariantCulture, out byte refusedGhost))
                await Wo224FigureRefusalAsync(refusedGhost, Wo224Rules.ClinchOpponentHash(re.Name));
            return;
        }
        if (phase == Wo197Rules.ClinchMove)
        {
            Interlocked.Increment(ref _w224Moves);
            _w224Clinches[feid] = (opp, now, gid);
            // the event's row is his own half (the master's row when he leads the move, his slave row when the other body leads it)
            if ((re.Flags & Wo197Rules.ClinchRows) == 0 || !catalog.TryGet(re.Row, out var own))
            { Console.WriteLine($"{head} dispatch=skipped (the move came without his row)"); return; }
            // he leads and the other body's row came too: both halves in one step where the other body takes a row here (the host's own
            // NPC; a copy's half is its host NPC's own, shown by the copy's owner)
            if (master && catalog.TryGet(Wo197Rules.ClinchSlaveRow(re), out var srow))
            {
                var pr = await Wo197PairOpAsync(mEid, sEid, Wo197PairVictimFirst, own.Spec, srow.Spec, ct);
                if (pr is { Ok: true, Payload.Length: >= 7 } pp)
                {
                    bool both = pp.Payload[4] == 0 && pp.Payload[5] == 0;
                    if (both) { Interlocked.Increment(ref _w224MovesPlayed); Interlocked.Increment(ref _w197ClinchPlayed); }
                    Console.WriteLine($"{head} master_row=\"{own.Spec}\" slave_row=\"{srow.Spec}\" dispatch=pair result=slave={pp.Payload[4]} master={pp.Payload[5]} targets={pp.Payload[6]}");
                    return;
                }
            }
            var gr = await _combat.GhostSwingForResultAsync(feid, own.Spec, ct);
            if (gr.Ok) { Interlocked.Increment(ref _w224MovesPlayed); Interlocked.Increment(ref _w197ClinchPlayed); }
            Console.WriteLine($"{head} his_row=\"{own.Spec}\" dispatch=figure-row result={gr.ReasonTag} (the other body's half is its own machine's)");
            return;
        }
        // leave: his clinch is over on his machine. Here the same move usually ended it too; when the pair is still in it after the
        // grace, it is ended the engine's way (the DLL: both bodies back to the plain guard).
        Interlocked.Increment(ref _w224Leaves);
        _w224Clinches.TryRemove(feid, out _);
        await Task.Delay(Wo224Rules.ClinchEndGraceMs, ct);
        if (_w224Clinches.ContainsKey(feid)) { Console.WriteLine($"{head} dispatch=none (he is in a clinch again)"); return; }
        var er = await _combat.Wo163Async(Protocol.Wo224OpClinchEnd, Wo224Pair(feid, opp), ct);
        int ended = er is { Ok: true, Payload.Length: >= 1 } e ? e.Payload[0] : -1;
        if (ended > 0) Interlocked.Increment(ref _w224Ended);
        Console.WriteLine($"{head} dispatch=clinch-end bodies_still_in_it={(ended < 0 ? "?" : ended.ToString(CultureInfo.InvariantCulture))}{(ended > 0 ? " -- ended here (his machine had left it)" : "")}");
    }

    // ---------------------------------------------------------------- A: this player's own figure, clinched in the host's world

    private long _w224FigOut, _w224FigIn, _w224FigStarted, _w224FigRefused, _w224FigEnded, _w224FigNoBody, _w224FigEnterAtMs, _w224FigLeaveAtMs,
        _w224FigRefusalsOut, _w224FigRefusalsIn;

    /// <summary>The host: its DLL saw a partner's figure enter or leave a clinch in the host's world (a LocalAction frame of the
    /// figure: kind AvatarClinch, the other body's name hash in the row's first four bytes). Sent on as FigureClinch, naming him.</summary>
    private async Task Wo224FigureClinchOutAsync(LocalActionFrame f, uint ms, Stream s)
    {
        if (!W137Host || !W224On("w224_clinch") || Wo197FigureGhost(f.Name) is not byte ghost) return;
        uint hash = BinaryPrimitives.ReadUInt32LittleEndian(f.Row.ToByteArray());
        byte flags = (byte)(f.Flags & (Wo197Rules.ClinchPhaseMask | Wo197Rules.ClinchMaster));
        string name = ghost.ToString(CultureInfo.InvariantCulture) + "_" + hash.ToString("x8", CultureInfo.InvariantCulture);
        byte[] pkt = _actionOut.Build(ActionKind.FigureClinch, ActionPhase.Commit, new RowEvent(ms, flags, Guid.Empty, name).ToBytes());
        await WritePacketAsync(s, pkt, _wo121Ct);
        _w121EvOut++;
        Interlocked.Increment(ref _w224FigOut);
        Console.WriteLine($"MP-ACTION section=outbound kind=FigureClinch gen={_actionOut.Gen} ghost={ghost} clinch={Wo197Rules.ClinchPhaseName(flags)} figure_role={((flags & Wo197Rules.ClinchMaster) != 0 ? "master" : "slave")} body_hash={hash:x8}");
    }

    /// <summary>The host: the clinch a joiner's own game started could not be started in the host's world (the engine refused it for
    /// the whole retry window). Told to him as a FigureClinch leave with <see cref="Wo224Rules.FlagRefused"/>: his game ends its own.</summary>
    private async Task Wo224FigureRefusalAsync(byte ghost, uint bodyHash)
    {
        if (_wo121Stream is not Stream s || bodyHash == 0) return;
        string name = ghost.ToString(CultureInfo.InvariantCulture) + "_" + bodyHash.ToString("x8", CultureInfo.InvariantCulture);
        byte flags = (byte)(Wo197Rules.ClinchLeave | Wo224Rules.FlagRefused);
        try
        {
            await WritePacketAsync(s, _actionOut.Build(ActionKind.FigureClinch, ActionPhase.Commit, new RowEvent(SenderMsNow(), flags, Guid.Empty, name).ToBytes()), _wo121Ct);
            Interlocked.Increment(ref _w224FigRefusalsOut);
            Console.WriteLine($"MP-ACTION section=outbound kind=FigureClinch gen={_actionOut.Gen} ghost={ghost} clinch=leave (refused: the host's engine did not start the clinch his game started) body_hash={bodyHash:x8}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { Console.WriteLine($"MP-W224 the refusal was not sent: {ex.GetType().Name}"); }
    }

    /// <summary>The joiner: the host's world put his figure into a clinch with a body, or took it out. Enter: the same clinch here, the
    /// engine's way, between the local player and that body's copy (the DLL's op 222). Leave: when he is still in it after the grace,
    /// it is ended here (op 223).</summary>
    private async Task Wo224OnFigureClinchInAsync(InboundAction a, CancellationToken ct)
    {
        if (!RowEvent.TryFromBytes(a.Payload, out var re)) return;
        int bar = re.Name.IndexOf('_');
        if (bar <= 0 || !byte.TryParse(re.Name.AsSpan(0, bar), NumberStyles.None, CultureInfo.InvariantCulture, out byte ghost) || ghost != _myGhostId) return;
        Interlocked.Increment(ref _w224FigIn);
        byte phase = (byte)(re.Flags & Wo197Rules.ClinchPhaseMask);
        bool iAmMaster = (re.Flags & Wo197Rules.ClinchMaster) != 0;
        string head = $"MP-ACTION section=inbound ghost={a.SourceGhostId} kind=FigureClinch clinch={Wo197Rules.ClinchPhaseName(re.Flags)} my_role={(iAmMaster ? "master" : "slave")}";
        if (!W224On("w224_clinch") || !W137Joiner) { Console.WriteLine($"{head} dispatch=dropped (w224_clinch off, or not a joiner in the host's world)"); return; }
        string? name = Wo224Rules.NameOfHash(Wo224Rules.ClinchOpponentHash(re.Name), _npcEntityIds.Keys);
        if (name is null || !_npcEntityIds.TryGetValue(name, out uint copy) || copy == 0)
        { Interlocked.Increment(ref _w224FigNoBody); Console.WriteLine($"{head} dispatch=skipped (no copy here of the body the host's world names)"); return; }
        if (phase != Wo197Rules.ClinchEnter && phase != Wo197Rules.ClinchLeave) return;
        // the host's word goes to the DLL as a state (op 225); its tick brings the local player's own clinch to it every frame --
        // the start asked again while the engine refuses, the roles, the end (wo224.cpp, reconcile_tick)
        bool enter = phase == Wo197Rules.ClinchEnter;
        bool refused = !enter && (re.Flags & Wo224Rules.FlagRefused) != 0;
        if (enter) Interlocked.Exchange(ref _w224FigEnterAtMs, Environment.TickCount64); else Interlocked.Exchange(ref _w224FigLeaveAtMs, Environment.TickCount64);
        if (refused) Interlocked.Increment(ref _w224FigRefusalsIn);
        var body = new byte[7];
        body[0] = (byte)(enter ? 1 : 0); body[1] = (byte)(iAmMaster ? 1 : 0); body[2] = (byte)(refused ? 1 : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(3), copy);
        var r = await _combat.Wo163Async(Protocol.Wo224OpClinchHost, body, ct);
        bool told = r is { Ok: true, Payload.Length: >= 1 };
        if (told) { if (enter) Interlocked.Increment(ref _w224FigStarted); else Interlocked.Increment(ref _w224FigEnded); } else Interlocked.Increment(ref _w224FigRefused);
        Console.WriteLine($"{head} with={name} dispatch={(told ? "host-word (the DLL brings his own clinch to it)" : "not-taken")}{(refused ? " -- the host's engine refused the clinch his game started" : "")}");
    }

    /// <summary>His machine could not enter the clinch this world has his figure in (its engine refused the start for the whole
    /// window): the figure's clinch is ended here at once, with whoever it is with.</summary>
    private async Task Wo224EndFigureClinchAsync(string head, uint feid, CancellationToken ct)
    {
        _w224Clinches.TryRemove(feid, out _);
        var er = await _combat.Wo163Async(Protocol.Wo224OpClinchEnd, Wo224Pair(feid, 0xFFFFFFFFu), ct);
        int ended = er is { Ok: true, Payload.Length: >= 1 } e ? e.Payload[0] : -1;
        Interlocked.Increment(ref _w224CouldNotEnterIn);
        if (ended > 0) Interlocked.Increment(ref _w224Ended);
        Console.WriteLine($"{head} dispatch=clinch-end (his machine could not enter it) bodies_ended={(ended < 0 ? "?" : ended.ToString(CultureInfo.InvariantCulture))}");
    }

    /// <summary>A.3: a figure's clinch its player's machine has not spoken of for <see cref="Wo224Rules.ClinchSilentMs"/> is ended
    /// here (its leave was lost, or he is gone).</summary>
    private async Task Wo224SilentTickAsync()
    {
        long now = Environment.TickCount64;
        foreach (var (feid, c) in _w224Clinches)
        {
            if (now - c.AtMs < Wo224Rules.ClinchSilentMs || !_w224Clinches.TryRemove(feid, out _)) continue;
            try
            {
                var er = await _combat.Wo163Async(Protocol.Wo224OpClinchEnd, Wo224Pair(feid, c.Opp));
                int ended = er is { Ok: true, Payload.Length: >= 1 } e ? e.Payload[0] : -1;
                if (ended > 0) Interlocked.Increment(ref _w224EndedSilent);
                Console.WriteLine($"MP-W224 clinch of ghost {c.Ghost}: nothing heard of it for {Wo224Rules.ClinchSilentMs / 1000} s -- bodies still in it here: {ended}{(ended > 0 ? " (ended)" : "")}");
            }
            catch { }
        }
    }
}
