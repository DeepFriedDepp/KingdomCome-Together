// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-136: world presence -- the testable rules (docs/WO-136-findings.md).
/// </summary>
public static class Wo136Rules
{
    // ---------------------------------------------------------------- Phase 7: the loot notice

    /// <summary>
    /// A body take's answer: ok (out of the host's body), gone (someone ELSE took
    /// it first -- the only verdict that shows "Someone already took that."),
    /// mine (a duplicate of this joiner's own earlier take), none (the host's body
    /// never held it: the joiner's copy's own item). Both of the last two are
    /// taken back off the joiner's Henry quietly.
    /// </summary>
    public static bool IsTakeVerdict(string v) => v is "ok" or "gone" or "mine" or "none";

    /// <summary>A world item's answer: ok, gone (someone else), unknown (per machine), mine (this joiner's own earlier take).</summary>
    public static bool IsItemVerdict(string v) => v is "ok" or "gone" or "unknown" or "mine";

    /// <summary>Only another player's earlier take is worth a notice.</summary>
    public static bool ShowsNotice(string verdict) => verdict == "gone";

    // ---------------------------------------------------------------- Phase 5: the torch

    /// <summary>The state block's torch bit (BodyState2Bits 0x20): the sender holds a torch.</summary>
    public const BodyState2Bits TorchBit = BodyState2Bits.TorchLit;

    public static BodyState2 WithTorch(BodyState2 s, bool torch) =>
        s with { Bits = torch ? s.Bits | TorchBit : s.Bits & ~TorchBit };

    /// <summary>A peer's torch edge from two successive state blocks (null = no change).</summary>
    public static bool? TorchEdge(BodyState2? before, BodyState2 now)
    {
        bool was = before is BodyState2 b && (b.Bits & TorchBit) != 0;
        bool on = (now.Bits & TorchBit) != 0;
        return was == on ? null : on;
    }

    // ---------------------------------------------------------------- Phase 4: knockout beats engagement

    /// <summary>NpcState flags: dead (0x01) or knocked out (0x02) on the host.</summary>
    public static bool Down(byte flags) => (flags & (Protocol.NpcStateFlagDead | Protocol.NpcStateFlagUnconscious)) != 0;

    /// <summary>
    /// Joiner: may a host NpcCombat engage this copy? Never while the host's NPC
    /// is down (a knockout or a death ends the engagement first, then WO-135's
    /// knockout mode takes the copy down); after it is up again, only a fresh
    /// "in combat" from the host re-engages it.
    /// </summary>
    public static bool MayEngage(bool hostDown) => !hostDown;

    // ---------------------------------------------------------------- Phase 3: the rider owns the horse

    /// <summary>Joiner: drop the host's samples for a horse this player rides (and for the settle after the dismount).</summary>
    public static bool DropRidden(IReadOnlyDictionary<string, DateTime> ridden, string name, DateTime nowUtc) =>
        ridden.TryGetValue(name, out var until) && (until == DateTime.MaxValue || nowUtc < until);

    public static readonly TimeSpan RideReturn = TimeSpan.FromSeconds(3);
}

/// <summary>
/// WO-136 Phase 2: the game's own soul and item tables (Tables.pak). A host NPC's
/// name is its soul's name (8,206 souls on this build, every field stand-in among
/// them), and the soul's archetype name is the entity class (NPC, NPC_Female,
/// Horse, Wolf, WildDog, Boar, ...), so a joiner's stand-in is the host's own spawn.
/// </summary>
public sealed class SoulIndex
{
    public readonly record struct Soul(Guid Id, string Class);
    public readonly record struct Item(string Name, string Kind);

    private readonly Dictionary<string, Soul> _souls;
    private readonly Dictionary<Guid, Item> _items;

    private SoulIndex(Dictionary<string, Soul> souls, Dictionary<Guid, Item> items) { _souls = souls; _items = items; }

    public int SoulCount => _souls.Count;
    public int ItemCount => _items.Count;

    public bool TryGetSoul(string name, out Soul s) => _souls.TryGetValue(name, out s);
    public bool TryGetItem(Guid cls, out Item it) => _items.TryGetValue(cls, out it);

    private static readonly Regex SoulFile = new(@"^Libs/Tables/rpg/soul(__[^/]+)?\.xml$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ItemFile = new(@"^Libs/Tables/item/item(__[^/]+)?\.xml$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static SoulIndex LoadFrom(string tablesPak)
    {
        using var zip = ZipFile.OpenRead(tablesPak);
        var arch = new Dictionary<string, string>(StringComparer.Ordinal);
        if (zip.GetEntry("Libs/Tables/rpg/soul_archetype.xml") is { } ae)
            foreach (var (_, attrs) in Elements(ae, "soul_archetype"))
                if (attrs.TryGetValue("soul_archetype_id", out var id) && attrs.TryGetValue("soul_archetype_name", out var an)) arch[id] = an;
        var souls = new Dictionary<string, Soul>(StringComparer.OrdinalIgnoreCase);
        var items = new Dictionary<Guid, Item>();
        foreach (var e in zip.Entries)
        {
            if (SoulFile.IsMatch(e.FullName))
            {
                foreach (var (_, a) in Elements(e, "soul"))
                {
                    if (!a.TryGetValue("soul_name", out var n) || !a.TryGetValue("soul_id", out var g) || !Guid.TryParse(g, out var gid)) continue;
                    string cls = a.TryGetValue("soul_archetype_id", out var aid) && arch.TryGetValue(aid, out var an) ? an : arch.GetValueOrDefault("0", "NPC");
                    souls[n] = new Soul(gid, cls);
                }
            }
            else if (ItemFile.IsMatch(e.FullName))
            {
                foreach (var (kind, a) in Elements(e, null))
                    if (a.TryGetValue("Id", out var g) && Guid.TryParse(g, out var gid) && a.TryGetValue("Name", out var n))
                        items[gid] = new Item(n, kind);
            }
        }
        return new SoulIndex(souls, items);
    }

    /// <summary>For tests: an index from literal rows.</summary>
    public static SoulIndex FromRows(IEnumerable<(string Name, Guid Id, string Class)> souls, IEnumerable<(Guid Id, string Name, string Kind)> items)
    {
        var s = new Dictionary<string, Soul>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in souls) s[r.Name] = new Soul(r.Id, r.Class);
        var i = new Dictionary<Guid, Item>();
        foreach (var r in items) i[r.Id] = new Item(r.Name, r.Kind);
        return new SoulIndex(s, i);
    }

    /// <summary>Every element (of <paramref name="only"/>, or any with attributes) with its attributes, streamed.</summary>
    private static IEnumerable<(string Name, Dictionary<string, string> Attrs)> Elements(ZipArchiveEntry e, string? only)
    {
        using var s = e.Open();
        using var r = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true, IgnoreWhitespace = true });
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || !r.HasAttributes) continue;
            if (only is not null && r.LocalName != only) continue;
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            while (r.MoveToNextAttribute()) d[r.LocalName] = r.Value;
            r.MoveToElement();
            yield return (r.LocalName, d);
        }
    }

    /// <summary>
    /// Why the game may refuse a class on an avatar, from the item table: a
    /// quick-slot belt is not armour an NPC body wears (the field's belt_2slot).
    /// null = nothing known (the kcd.log "Can't equip" line names armour reasons).
    /// </summary>
    public string? UnwearableReason(Guid cls) =>
        TryGetItem(cls, out var it) && it.Kind == "QuickSlotContainer"
            ? "a quick-slot belt: not armour an NPC body wears"
            : null;

    public string Describe(Guid cls) => TryGetItem(cls, out var it) ? $"{it.Name} ({it.Kind})" : cls.ToString();

    /// <summary>The stand-in answer for a name: (soul, class) or null (the tables do not know it).</summary>
    public (Guid Soul, string Class)? StandIn(string name) => TryGetSoul(name, out var s) ? (s.Id, s.Class) : null;
}
