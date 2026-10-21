// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-202 V.3 keys_per_player (the maintainer's decision 2026-10-20): keys are each player's own. A key one player takes -- from a body, a
// chest, an NPC -- the other gets too (the game's keys: MiscItem Type 4 with a key icon, quest keys excluded: the quest mirror carries
// those); a chest the host's own Henry unlocks is unlocked in the joiner's world the game's own way (WO-195B already carries the joiner's
// unlock to the host; doors were WO-151's both ways). Chest contents stay shared, one taker (WO-134). One W164 kind, KeyShare:
//   "k|<class>"                  a key this player took
//   "c|<name>|<x>|<y>|<z>"       the host unlocked that chest (its name and pivot, as WO-195B's ask carries them)
public partial class GameBridge
{
    private Dictionary<string, string>? _w202KeyClasses;
    private bool _w202KeysPushed;
    private long _w202KeysOut, _w202KeysIn, _w202ChestsOut, _w202ChestsIn;

    private async Task Wo202KeysTickAsync()
    {
        if (!W202("keys_per_player") || !(W137Host || W137Joiner) || !Wo134Peers().Any()) return;
        if (!_w202KeysPushed)
        {
            _w202KeysPushed = true;
            string? tables = TablesPakPath();
            if (tables is null) { Console.WriteLine("WO202-KEY Tables.pak not found -- no key classes, the key share is idle"); return; }
            try { _w202KeyClasses ??= WhsSave.KeyClasses(tables); }
            catch (Exception ex) { Console.WriteLine($"WO202-KEY key classes not read ({ex.GetType().Name}: {ex.Message}) -- the key share is idle"); return; }
            var all = _w202KeyClasses.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            await ExecLuaAsync("if KCD2MP_W202KeyClassReset then KCD2MP_W202KeyClassReset() end");
            for (int i = 0; i < all.Count; i += Wo200Rules.QClassChunk)
                await ExecLuaAsync($"if KCD2MP_W202KeyClassAdd then KCD2MP_W202KeyClassAdd(\"{string.Join(' ', all.Skip(i).Take(Wo200Rules.QClassChunk))}\") end");
            Console.WriteLine($"WO202-KEY {all.Count} key classes (the game's keys, quest keys left to the quest mirror) given to the game's script");
        }
        await ExecLuaAsync("if KCD2MP_W202KeysTick then KCD2MP_W202KeysTick() end");
    }

    /// <summary>The mod: this player took a key (w202_key), or the host unlocked a chest (w202_chest) -- to every partner.</summary>
    private async Task Wo202KeysOnEventAsync(string name, string? arg)
    {
        if (!W202("keys_per_player")) return;
        string? text = name == "w202_key" ? Wo202Rules.KeyText(arg) : Wo202Rules.ChestText(arg);
        if (text is null) return;
        foreach (byte g in Wo134Peers()) await W164SendAsync(g, Protocol.W164KeyShare, text);
        if (name == "w202_key") Interlocked.Increment(ref _w202KeysOut); else Interlocked.Increment(ref _w202ChestsOut);
    }

    /// <summary>A partner's KeyShare: his key is made here too; the host's chest unlock is the joiner's too.</summary>
    private async Task Wo202OnKeyShareAsync(byte src, string text)
    {
        if (!W202("keys_per_player")) return;
        string who = src == 0 ? "your host" : "your partner";
        if (Wo202Rules.TryParseKey(text, out string cls))
        {
            Interlocked.Increment(ref _w202KeysIn);
            await ExecLuaAsync($"if KCD2MP_W202KeyIn then KCD2MP_W202KeyIn(\"{cls}\", \"{who}\") end");
        }
        else if (Wo202Rules.TryParseChest(text, out string chest, out float x, out float y, out float z) && W137Joiner)
        {
            Interlocked.Increment(ref _w202ChestsIn);
            await ExecLuaAsync(FormattableString.Invariant($"if KCD2MP_W202ChestIn then KCD2MP_W202ChestIn(\"{chest}\", {x:F2}, {y:F2}, {z:F2}) end"));
        }
    }

    private string Wo202KeysStats() => $"keys_out={Interlocked.Read(ref _w202KeysOut)} keys_in={Interlocked.Read(ref _w202KeysIn)} chests_out={Interlocked.Read(ref _w202ChestsOut)} chests_in={Interlocked.Read(ref _w202ChestsIn)}";
}
