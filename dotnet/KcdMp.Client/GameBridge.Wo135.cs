using System.Collections.Concurrent;
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-135 -- the agent's half (docs/WO-135-findings.md).
//
//   * Phase 1 (host): the avatar's quiet groups ride MotionConfig byte 5 (the mod's
//     mp_avatar_quiet -> w135_cfg). The contexts themselves are set natively.
//   * Phase 2 (joiner): w135_ko <name> 1|0 -> wo131 op 2 mode 2 (knocked out) / 3
//     (woken) on the bound copy -> KCD2MP_W135KoDone. A takedown on a host-owned
//     copy (w135_takedown) goes to the host as LootAsk kind 5; the host's mod
//     performs it with the joiner's avatar and answers (w135_tdres -> LootHost
//     kind 6).
//   * Phase 3: mp_w135_check crouch on|off (the player's own crouch setter, no input).
//   * Phase 5 (host): the host announces its world's game build to every joiner
//     (LootHost kind 7, on change and every 30 s) -- the joiner picks a Henry
//     source from that build BEFORE any request, so the host is never paused
//     for a join that cannot splice.
public partial class GameBridge
{
    private volatile byte _avatarQuiet = Wo135Rules.QuietAll;
    private volatile string? _hostWorldBuild;              // host: the identifying save's BuildInfo
    private volatile string? _peerHostBuild;               // joiner: the host's announced build
    private DateTime _peerSeedKnownSinceUtc = DateTime.MinValue;
    private readonly ConcurrentDictionary<byte, (string Build, DateTime AtUtc)> _w135BuildTold = new();
    private long _w135KoDown, _w135KoUp, _w135KoFail, _w135TdOut, _w135TdIn, _w135TdResOut, _w135TdResIn;

    private void Wo135OnDisconnect()
    {
        _peerHostBuild = null;
        _w135BuildTold.Clear();
    }

    private void Wo135OnEvent(string name, string? arg)
    {
        arg ??= "";
        switch (name)
        {
            case "w135_cfg":
                if (Wo135Rules.ParseQuiet(arg) is byte q)
                {
                    _avatarQuiet = q;
                    Console.WriteLine($"MP-WO135 avatar quiet=0x{q:X} ({Wo135Rules.QuietGroups(q)}) -- pushed to the DLL");
                    _ = PushWo121ConfigAsync(_wo121Ct);
                }
                return;
            case "w135_check":
                _ = Task.Run(() => Wo135CheckAsync(arg));
                return;
            case "w135_ko":
                if (Wo135Rules.TryParseKo(arg, out string kn, out bool down)) _ = Task.Run(() => Wo135KoAsync(kn, down));
                else Console.WriteLine($"MP-WO135 malformed w135_ko '{arg}'");
                return;
            case "w135_takedown":
                if (Wo135Rules.TryParseTakedown(arg, out string tn, out string tk) && Wo134JoinerRole)
                {
                    Interlocked.Increment(ref _w135TdOut);
                    Console.WriteLine($"MP-WO135 joiner: takedown {tk} on {tn} -> asked of the host (its world performs it)");
                    _ = Wo134SendAsync(Protocol.LootAskUp, Protocol.JoinTargetHost, Protocol.LootAskTakedown, 0, $"{tn} {tk}");
                }
                return;
            case "w135_tdres":
            {
                // "<src> <tok> <ok|refused> <name> <kind>" from the host's mod.
                var f = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (f.Length == 5 && Wo134HostRole && byte.TryParse(f[0], out byte to) && uint.TryParse(f[1], out uint tok)
                    && Wo135Rules.TryParseTakedownResult($"{f[2]} {f[3]} {f[4]}", out _, out _, out _))
                {
                    Interlocked.Increment(ref _w135TdResOut);
                    Console.WriteLine($"MP-WO135 host: takedown {f[4]} on {f[3]} for ghost {to} -> {f[2]}");
                    _ = Wo134SendAsync(Protocol.LootHostUp, to, Protocol.LootHostTakedownResult, tok, $"{f[2]} {f[3]} {f[4]}");
                }
                return;
            }
        }
    }

    /// <summary>Joiner: the host's NPC is down (or up again) -- its copy follows, natively.</summary>
    private async Task Wo135KoAsync(string name, bool down)
    {
        bool ok = false;
        string why = "";
        if (!Wo131JoinerActive) why = "not a joiner under the copy guard";
        else if (!_w131Guarded.TryGetValue(name, out uint eid)) why = "no guarded copy";
        else
        {
            var r = await _combat.Wo131Async(2, Wo135CopyGuardArgs(down ? (byte)2 : (byte)3, eid));
            ok = r is { Ok: true } && r.Value.Payload.Length > 0 && r.Value.Payload[0] == 1;
            if (r is null) why = "no answer";
            else if (!r.Value.Ok) why = $"reason {r.Value.Reason}";
        }
        if (!ok) Interlocked.Increment(ref _w135KoFail);
        else if (down) Interlocked.Increment(ref _w135KoDown);
        else Interlocked.Increment(ref _w135KoUp);
        Console.WriteLine($"MP-WO135 copy {name}: {(down ? "knocked out (the host's NPC is)" : "woken (the host's NPC is up)")} -> {(ok ? "ok" : $"FAILED ({why})")}");
        await ExecLuaAsync($"if KCD2MP_W135KoDone then KCD2MP_W135KoDone(\"{name}\", {B(down)}, {B(ok)}) end");
    }

    private static byte[] Wo135CopyGuardArgs(byte mode, uint eid)
    {
        var a = new byte[5];
        a[0] = mode;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(1), eid);
        return a;
    }

    /// <summary>mp_w135_check: "crouch on|off" (the player's own SetCrouch, no input), "status".</summary>
    private async Task Wo135CheckAsync(string arg)
    {
        var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 2 && p[0] == "crouch" && p[1] is "on" or "off")
        {
            var r = await _combat.Wo131Async(8, [(byte)(p[1] == "on" ? 1 : 0)]);
            Console.WriteLine($"MP-WO135 check: the player's own crouch setter {p[1]} -> {(r is { Ok: true } ? "called (no input)" : $"FAILED (reason {r?.Reason})")}");
        }
        else if (p.Length == 1 && p[0] == "status")
        {
            Console.WriteLine(Wo135StatsLine());
            Console.WriteLine($"MP-WO135 dll motion: {await _combat.Wo121StatusAsync() ?? "no answer"}");
            Console.WriteLine($"MP-WO135 dll wo131: {await _combat.Wo131StatusAsync() ?? "no answer"}");
        }
        else Console.WriteLine($"MP-WO135 check: unknown '{arg}' (crouch on|off, status)");
    }

    private string Wo135StatsLine() =>
        $"MP-WO135-STATS quiet=0x{_avatarQuiet:X} ko_down={_w135KoDown} ko_up={_w135KoUp} ko_fail={_w135KoFail} td_out={_w135TdOut} td_in={_w135TdIn} td_res_out={_w135TdResOut} td_res_in={_w135TdResIn} host_build={_hostWorldBuild ?? "-"} peer_host_build={_peerHostBuild ?? "-"}";

    /// <summary>LootAsk kind 5 (host) and LootHost kinds 6/7 (joiner). True = handled here.</summary>
    private async Task<bool> Wo135OnLootFrameAsync(int type, byte src, LootMsg m)
    {
        if (type == Protocol.LootAskDown && m.Kind == Protocol.LootAskTakedown)
        {
            if (!Wo134HostRole || !Wo135Rules.TryParseTakedown(m.Text, out string n, out string k)) return false;
            Interlocked.Increment(ref _w135TdIn);
            Console.WriteLine($"MP-WO135 host: ghost {src} asks takedown {k} on {n} -- this world performs it");
            await ExecLuaAsync($"if KCD2MP_W135HostTakedown then KCD2MP_W135HostTakedown({src}, {m.Tok}, \"{n}\", \"{k}\") end");
            return true;
        }
        if (type != Protocol.LootHostDown) return false;
        if (m.Kind == Protocol.LootHostTakedownResult)
        {
            if (!Wo134JoinerRole || !Wo135Rules.TryParseTakedownResult(m.Text, out string res, out string n, out string k)) return false;
            Interlocked.Increment(ref _w135TdResIn);
            Console.WriteLine($"MP-WO135 joiner: the host's answer to takedown {k} on {n}: {res}");
            await ExecLuaAsync($"if KCD2MP_W135TakedownResult then KCD2MP_W135TakedownResult(\"{n}\", \"{k}\", \"{res}\") end");
            return true;
        }
        if (m.Kind == Protocol.LootHostBuild)
        {
            string b = m.Text.Trim();
            if (!Wo134JoinerRole || !Wo135Rules.BuildText.IsMatch(b)) return false;
            if (_peerHostBuild != b)
            {
                _peerHostBuild = b;
                _chooseAsked = false;
                Console.WriteLine($"MP-HENRY joiner: the host's world is from game build {b} -- a Henry is taken only from a save of that build");
            }
            return true;
        }
        return false;
    }

    /// <summary>Host, 1 s: every joiner knows this world's build (on change and every 30 s).</summary>
    private void Wo135HostTick()
    {
        if (!Wo134HostRole || _hostWorldBuild is not string b || !Wo135Rules.BuildText.IsMatch(b)) return;
        var now = DateTime.UtcNow;
        foreach (byte g in Wo134Peers())
        {
            if (_w135BuildTold.TryGetValue(g, out var t) && t.Build == b && (now - t.AtUtc).TotalSeconds < 30) continue;
            _w135BuildTold[g] = (b, now);
            _ = Wo134SendAsync(Protocol.LootHostUp, g, Protocol.LootHostBuild, 0, b);
        }
    }
}
