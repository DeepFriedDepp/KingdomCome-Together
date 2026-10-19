// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using KcdMp.Wire;

namespace KcdMp.Client;

// WO-201 B23 copy_hands_from_host (WO-200's switch, built now) and B11 figure_ranged: what a body holds is a value.
//   * A host copy's hands (WO-143's rows: the item classes the host's NPC holds) are shown as visual-only props of the same model in its
//     hands -- the game's own Human.AttachEntityToHand with a never-saved, unphysicalized prop (WO-148's sack) -- never an inventory
//     item, never the planner (copy_neutral_always keeps the planner out). Weapons stay WO-167's own draw.
//   * A partner's figure shows a weapon the engine will not put on it (a quest item such as the field's crossbow: "cannot be created
//     manually") the same way (B11).
// The item class -> model map is the item tables' own Model attribute (Wo201ItemModels).
public partial class GameBridge
{
    private Dictionary<string, (string Model, string Kind)>? _w201Models;
    private bool _w201ModelsTried;
    private readonly ConcurrentDictionary<string, (string L, string R)> _w201HandsShown = new(StringComparer.Ordinal);
    private long _w201HandRows, _w201HandShows, _w201HandNoModel;

    private Dictionary<string, (string Model, string Kind)>? Wo201Models()
    {
        if (_w201ModelsTried) return _w201Models;
        _w201ModelsTried = true;
        try
        {
            string? pak = TablesPakPath();
            if (pak is null) { Console.WriteLine("MP-W201 Tables.pak not found -- no item models (hands and quest weapons stay unseen)"); return null; }
            _w201Models = Wo201ItemModels.Read(pak);
            Console.WriteLine($"MP-W201 item models: {_w201Models.Count} classes with a model");
        }
        catch (Exception ex) { Console.WriteLine($"MP-W201 item models not read ({ex.GetType().Name}: {ex.Message})"); }
        return _w201Models;
    }

    /// <summary>The model to show for this class in a hand, or "" (nothing, a weapon WO-167 draws, or no model known).</summary>
    private string Wo201HandModel(string cls, bool allowWeapons)
    {
        if (string.IsNullOrEmpty(cls) || cls == "-") return "";
        var m = Wo201Models();
        if (m is null || !m.TryGetValue(cls.ToLowerInvariant(), out var v)) { Interlocked.Increment(ref _w201HandNoModel); return ""; }
        if (!allowWeapons && v.Kind is "MeleeWeapon" or "MissileWeapon") return "";
        return "Objects/" + v.Model;
    }

    /// <summary>B23: a host hand row for a copy (WO-143 kind Hands).</summary>
    private async Task Wo201OnHostHandsAsync(ExtraRow r)
    {
        if (!W137Joiner || !W200("copy_hands_from_host")) return;
        Interlocked.Increment(ref _w201HandRows);
        bool fight = W151InFight(r.Name) || _w164FleeSince.ContainsKey(r.Name);
        string l = fight ? "" : Wo201HandModel(ExtraRow.ClassText(r.Left), false);
        string rt = fight ? "" : Wo201HandModel(ExtraRow.ClassText(r.Right), false);
        if (_w201HandsShown.TryGetValue(r.Name, out var was) && was.L == l && was.R == rt) return;
        _w201HandsShown[r.Name] = (l, rt);
        Interlocked.Increment(ref _w201HandShows);
        await ExecLuaAsync($"if KCD2MP_W201Hands then KCD2MP_W201Hands(\"{EscapeLua(r.Name)}\", \"{l}\", \"{rt}\") end");
    }

    // ---- B11: a weapon the figure cannot wear ----
    private readonly ConcurrentDictionary<byte, Guid> _w201FigureProp = new();
    private long _w201FigureProps;

    /// <summary>The engine refused to put this class on the partner's figure: a weapon is shown in its right hand as a prop of its model.</summary>
    private async Task Wo201FigurePropAsync(byte ghostId, Guid cls)
    {
        if (!W201("figure_ranged")) return;
        var m = Wo201Models();
        if (m is null || !m.TryGetValue(cls.ToString("D"), out var v) || v.Kind is not ("MissileWeapon" or "MeleeWeapon")) return;
        if (_w201FigureProp.TryGetValue(ghostId, out var cur) && cur == cls) return;
        _w201FigureProp[ghostId] = cls;
        Interlocked.Increment(ref _w201FigureProps);
        Console.WriteLine($"WO201-RANGED figure kcd2mp_{ghostId}: {v.Kind} {cls:D} cannot be worn here (the engine refuses it) -- shown as a prop of its own model in the right hand");
        await ExecLuaAsync($"if KCD2MP_W201FigureProp then KCD2MP_W201FigureProp(\"kcd2mp_{ghostId}\", \"Objects/{v.Model}\") end");
    }

    /// <summary>The partner's outfit changed: a prop of a weapon he no longer has goes.</summary>
    private async Task Wo201FigurePropCheckAsync(byte ghostId, IReadOnlySet<Guid> target)
    {
        if (!_w201FigureProp.TryGetValue(ghostId, out var cls) || target.Contains(cls)) return;
        _w201FigureProp.TryRemove(ghostId, out _);
        await ExecLuaAsync($"if KCD2MP_W201FigureProp then KCD2MP_W201FigureProp(\"kcd2mp_{ghostId}\", \"\") end");
    }

    private void Wo201HandsReset() { _w201HandsShown.Clear(); _w201FigureProp.Clear(); }

    private string Wo201HandsStats() => FormattableString.Invariant(
        $"hand_rows={Interlocked.Read(ref _w201HandRows)} hand_shows={Interlocked.Read(ref _w201HandShows)} hand_no_model={Interlocked.Read(ref _w201HandNoModel)}");
}
