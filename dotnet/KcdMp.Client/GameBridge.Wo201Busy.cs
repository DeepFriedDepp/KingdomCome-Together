// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-201 B15 host_load_toast: while the host's game loads, reloads or saves, its pings stall for seconds (the field: 1-15 s pings during the
// host's load, and "Your connection to the other player is struggling ... It's the connection, not the game."). The host says so the
// moment its engine starts one (W164 kind 20 HostBusy "load 1" / "save 1", and "... 0" when it is done); the joiner's game then shows
// "Your host is loading - please wait" instead of blaming the connection. A reload also comes as WO-125's JoinStatus (rewinding).
public partial class GameBridge
{
    private long _w201BusyOut, _w201BusyIn;

    /// <summary>The host's own engine lines: a save begins / ends; a load begins (the load's end is its gameplay start).</summary>
    private void Wo201BusyOnEngineLine(string line)
    {
        if (!W137Host || !W201("host_load_toast")) return;
        string? text = null;
        if (line.StartsWith("[SAVE GAME] Quick-saving to", StringComparison.Ordinal) || line.StartsWith("[SAVE GAME] Saving", StringComparison.Ordinal)) text = "save 1";
        else if (line.StartsWith("SaveGame: '", StringComparison.Ordinal) && line.Contains("' done.", StringComparison.Ordinal)) text = "save 0";
        if (text is null) return;
        _ = Wo201SendBusyAsync(text);
    }

    /// <summary>The host's load begins (WO-125's own edge).</summary>
    private void Wo201HostLoadStart()
    {
        if (_w201Dude2Eid != 0) _ = Wo201Dude2SetAsync(0, false, "the host's load");   // V.3: the bit never rides into a load
        if (W137Host && W201("host_load_toast")) _ = Wo201SendBusyAsync("load 1");
    }

    /// <summary>The host's gameplay starts again after a load (WO-125's own edge).</summary>
    private void Wo201HostLoadDone() { if (W137Host && W201("host_load_toast")) _ = Wo201SendBusyAsync("load 0"); }

    private async Task Wo201SendBusyAsync(string text)
    {
        var peers = Wo134Peers();
        foreach (byte g in peers) await W164SendAsync(g, Protocol.W164HostBusy, text);
        if (peers.Count > 0) Interlocked.Increment(ref _w201BusyOut);
    }

    private async Task Wo201OnHostBusyAsync(string text)
    {
        Interlocked.Increment(ref _w201BusyIn);
        var f = text.Split(' ');
        if (f.Length != 2 || f[0] is not ("load" or "save") || f[1] is not ("0" or "1")) return;
        Console.WriteLine($"WO201-BUSY the host's game is {(f[1] == "1" ? "busy: " + (f[0] == "load" ? "loading" : "saving") : "done " + (f[0] == "load" ? "loading" : "saving"))}");
        await ExecLuaAsync($"if KCD2MP_W201HostBusy then KCD2MP_W201HostBusy(\"{f[0]}\", {f[1]}) end");
    }

    /// <summary>B21 (joiner): the engine removed a script save lock -- if it is the host-only one, the Lua adds it again next tick.</summary>
    private void Wo201LockOnEngineLine(string line)
    {
        if (!W137Joiner) return;
        var (op, name) = Wo192Rules.ParseLockLine(line);
        if (op == -1 && Wo201Rules.IsEntityName(name)) _ = ExecLuaAsync($"if KCD2MP_W201LockRemoved then KCD2MP_W201LockRemoved(\"{name}\") end");
    }

    private string Wo201BusyStats() => FormattableString.Invariant($"busy_out={Interlocked.Read(ref _w201BusyOut)} busy_in={Interlocked.Read(ref _w201BusyIn)}");
}
