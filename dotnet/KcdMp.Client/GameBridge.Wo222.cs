// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-222 (docs/WO-222-findings.md): hits that land like single-player. Every switch ships on (W222Switch; `mp_<name> on|off` in the
// game's console, `mp_w222_status` lists them); the bench's twin is KCDMP_TEST_W222_OFF=<names|all> in both agents' and both games'
// environment. Log tags: MP-W222, MP-W222-NATIVE, MP-W222-STATS, W222-REACT (B.1: every host reaction row on a copy), W222-SEND (E: a
// touch of this player's blade on a copy, sent or not and why); the DLL's W222-BLOW (A.0) and W222-SWING (E).
public partial class GameBridge
{
    private readonly ConcurrentDictionary<string, bool> _w222On = new(StringComparer.Ordinal);
    private bool W222On(string name) => _w222On.TryGetValue(name, out bool v) ? v : !W222Switch.TwinOff(name);
    private int _w222Pushing;
    private long _w222ReactIn, _w222ReactPlayed, _w222ReactShown, _w222ReactLate, _w222ReactRefused, _w222ReactNoBody, _w222SendSent, _w222SendDropped;
    private readonly ConcurrentDictionary<string, long> _w222SendWhy = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _w222FleeEngaged = new(StringComparer.Ordinal);   // D (joiner): copies held as fleeing enemies
    private long _w222FleeOn, _w222FleeHitsForwarded;
    private readonly ConcurrentDictionary<string, long> _w222FightWatchAt = new(StringComparer.Ordinal);   // D (host): last time a body was watched for its fight

    private async Task Wo222PushNativeAsync()
    {
        if (Interlocked.CompareExchange(ref _w222Pushing, 1, 0) != 0) return;
        try
        {
            var body = new byte[5];
            BitConverter.GetBytes(W222Switch.NativeBits(W222On)).CopyTo(body, 0);
            body[4] = Wo218Role();
            await _combat.Wo163Async(Protocol.Wo222OpConfig, body);
        }
        catch (Exception ex) { Console.WriteLine($"MP-W222 the native word was not sent: {ex.Message}"); }
        finally { Interlocked.Exchange(ref _w222Pushing, 0); }
    }

    private async Task Wo222NativeStatusAsync()
    {
        try
        {
            for (byte page = 0; page < 30; page++)
            {
                var r = await _combat.Wo163Async(Protocol.Wo222OpStatus, [page]);
                if (r is not { Ok: true } rr || rr.Payload.Length == 0) break;
                Console.WriteLine($"MP-W222-NATIVE {Encoding.ASCII.GetString(rr.Payload)}");
            }
        }
        catch { }
    }

    private Task Wo222TellModAsync() =>
        ExecLuaAsync($"if KCD2MP_W222Cfg then KCD2MP_W222Cfg(\"{string.Join(',', W222Switch.Names.Select(n => n + "=" + (W222On(n) ? "on" : "off")))}\") end");

    private string Wo222StatsLine() =>
        $"MP-W222-STATS {string.Join(' ', W222Switch.Names.Select(n => $"{n}={(W222On(n) ? "on" : "off")}"))}" +
        $" reactions_in={Interlocked.Read(ref _w222ReactIn)} played={Interlocked.Read(ref _w222ReactPlayed)} shown_150ms={Interlocked.Read(ref _w222ReactShown)}" +
        $" late={Interlocked.Read(ref _w222ReactLate)} refused={Interlocked.Read(ref _w222ReactRefused)} no_body={Interlocked.Read(ref _w222ReactNoBody)}" +
        $" flee_held={Interlocked.Read(ref _w222FleeOn)} flee_now={_w222FleeEngaged.Count} flee_hits_forwarded={Interlocked.Read(ref _w222FleeHitsForwarded)}" +
        $" touches_sent={Interlocked.Read(ref _w222SendSent)} touches_dropped={Interlocked.Read(ref _w222SendDropped)}" +
        (_w222SendWhy.IsEmpty ? "" : " dropped_by:" + string.Join(',', _w222SendWhy.Select(kv => $"{kv.Key}={kv.Value}"))) + Wo222DefStats() + Wo222LayStats();

    /// <summary>The 4 Hz session tick: the native word every 2 s, the status and the stats every 30 s, the mod told every minute.</summary>
    private void Wo222Tick(long n)
    {
        if (n % 8 == 5) _ = Wo222PushNativeAsync();
        if (n % 120 == 77 && (W137Host || W137Joiner)) { _ = Wo222NativeStatusAsync(); Console.WriteLine(Wo222StatsLine()); }
        if (n % 240 == 97) _ = Wo222TellModAsync();
        if (n % 8 == 1) Wo222DeadLayTick();   // F.1: every 2 s
    }

    private void Wo222OnConnect(CancellationToken ct) { _ = Wo222TellModAsync(); _ = Wo222PushNativeAsync(); }

    private void Wo222OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w222_cfg":
            {
                int eq = (arg ?? "").IndexOf('=');
                if (eq <= 0 || arg![(eq + 1)..] is not ("on" or "off") || !W222Switch.Names.Contains(arg[..eq])) { Console.WriteLine($"MP-W222 cfg refused: '{arg}'"); return; }
                _w222On[arg[..eq]] = arg[(eq + 1)..] == "on";
                Console.WriteLine($"MP-W222 cfg {arg}");
                _ = Wo222PushNativeAsync();
                _ = Wo222TellModAsync();
                return;
            }
            case "w222_status":
                _ = Wo222NativeStatusAsync();
                Console.WriteLine(Wo222StatsLine());
                return;
        }
    }

    // ---------------------------------------------------------------- B.1 (joiner): every host reaction row on a copy, measured

    /// <summary>The joiner: the host's reaction row for <paramref name="npc"/> arrived at <paramref name="arrivedMs"/> and was dispatched
    /// with <paramref name="result"/>; 150 ms after its arrival the DLL is asked whether the copy shows it (WO-187's move-shown op).</summary>
    private void Wo222NoteReaction(string npc, uint eid, string spec, long arrivedMs, bool ok, string result)
    {
        Interlocked.Increment(ref _w222ReactIn);
        if (ok) Interlocked.Increment(ref _w222ReactPlayed); else Interlocked.Increment(ref _w222ReactRefused);
        long dispatchMs = Environment.TickCount64 - arrivedMs;
        _ = Task.Run(async () =>
        {
            byte st = 255; uint nowFrag = 0, ours = 0; int nowPrio = 0;
            try
            {
                long wait = 150 - (Environment.TickCount64 - arrivedMs);
                if (wait > 0) await Task.Delay((int)wait);
                var a = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
                var r = eid != 0 && ok ? await _combat.Wo163Async(Protocol.Wo222OpShown, a) : null;
                if (r is { Ok: true, Payload.Length: >= 17 } x)
                {
                    st = x.Payload[0];
                    ours = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(x.Payload.AsSpan(1));
                    nowFrag = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(x.Payload.AsSpan(5));
                    nowPrio = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(x.Payload.AsSpan(9));
                }
            }
            catch { }
            if (st == 1) Interlocked.Increment(ref _w222ReactShown);
            if (dispatchMs > 150) Interlocked.Increment(ref _w222ReactLate);
            string sh = st switch { 1 => "shown", 2 => "other", 3 => "empty", 4 => "unreadable", 0 => "none-queued", _ => "unknown" };
            Console.WriteLine(FormattableString.Invariant($"W222-REACT npc={npc} spec=\"{spec}\" dispatch_ms={dispatchMs} result={result} at150={sh} ours={ours} fullbody={nowFrag}/p{nowPrio}"));
        });
    }

    private void Wo222NoteReactionNoBody(string npc, string why)
    {
        Interlocked.Increment(ref _w222ReactIn);
        Interlocked.Increment(ref _w222ReactNoBody);
        Console.WriteLine($"W222-REACT npc={npc} dispatch_ms=-1 result={why} at150=none");
    }

    // ---------------------------------------------------------------- E (joiner): a touch of this player's blade, sent or not

    private void Wo222NoteSend(string? npc, bool sent, string why, float hp, float st)
    {
        if (!W137Joiner) return;
        if (sent) Interlocked.Increment(ref _w222SendSent);
        else { Interlocked.Increment(ref _w222SendDropped); _w222SendWhy.AddOrUpdate(why, 1, (_, v) => v + 1); }
        Console.WriteLine(FormattableString.Invariant($"W222-SEND npc={npc ?? "?"} {(sent ? "sent" : "dropped")} why={why} hp={hp:F2} st={st:F2}"));
    }
}
