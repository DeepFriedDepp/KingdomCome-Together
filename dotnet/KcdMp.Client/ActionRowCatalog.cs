// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.IO.Compression;
using System.Xml.Linq;

namespace KcdMp.Client;

/// <summary>
/// WO-121: authored combat rows by their <c>mn_fragment_guid</c> -- the key a
/// v8 Attack / BlockImpulse / Dodge / NpcAttack event carries.
///
/// Read from the installed game's own Data\Tables.pak (the same pak
/// <see cref="WeaponSwingCatalog"/> reads): combat_action_attack,
/// combat_action_block, combat_action_perfect_block and combat_action_dodge.
/// Each row's own fragment and tags are the spec the receiver hands the
/// WO-46 cosmetic route (<c>"FragmentId, tag+tag"</c>), so the avatar plays
/// the exact row the sender's engine committed -- overhead is overhead, a
/// thrust is a thrust -- instead of the one rotating FreeAttack row.
///
/// Live finding (WO-121 session 1): a committed attack action's descriptor
/// (action +0x60) holds this GUID at +0x84 in Windows GUID byte order; five
/// captured rows all matched this table.
/// </summary>
public sealed class ActionRowCatalog
{
    /// <summary>
    /// One authored row. WO-163 adds, as optional trailing fields: the table's action type, the row's own timings (a blow lands
    /// <c>attack_time_to_start + attack_time_to_hit</c> after the row starts -- the pairing rule of A3), the actor class hash
    /// (an animal's rows carry another one than a man's) and the two weapon classes (the failed-attack choice of C3).
    /// </summary>
    public readonly record struct Row(string Table, string Fragment, string Tags, int Zone, int InputClass, int AttackType,
                                      int ActionType = -1, float TimeToStart = 0f, float TimeToHit = 0f, long ActorClass = 0,
                                      int WeaponR = -1, int WeaponL = -1)
    {
        public string Spec => $"{Fragment}, {Tags}";

        /// <summary>The lag from the row's start to the blow it causes, in ms (WO-162 Q3.2: 33 of 35 field hits fit this +-0.35 s); -1 = the row says none.</summary>
        public int HitLagMs => TimeToStart <= 0f && TimeToHit <= 0f ? -1 : (int)MathF.Round((TimeToStart + TimeToHit) * 1000f);
    }

    /// <summary>combat_action_perfect_block action types 55 / 62: the blocker's counter (a master strike; 62 is the one that kills). WO-162 Q3.3.</summary>
    public static bool IsMasterStrike(int actionType) => actionType == 55 || actionType == 62;

    /// <summary>A table's action type for the attacker-side answer to a block (WO-162 Q4): 27 failedAttackOnBlock, 15 failedAttackOnPB.</summary>
    public const int ActionFailedAttackOnBlock = 27, ActionFailedAttackOnPerfectBlock = 15;

    private readonly Dictionary<Guid, Row> _rows;
    private ActionRowCatalog(Dictionary<Guid, Row> rows) { _rows = rows; }

    public int Count => _rows.Count;
    public bool TryGet(Guid g, out Row r) => _rows.TryGetValue(g, out r);

    public static readonly string[] Tables =
    {
        "Libs/Tables/combat/combat_action_attack.xml",
        "Libs/Tables/combat/combat_action_block.xml",
        "Libs/Tables/combat/combat_action_perfect_block.xml",
        "Libs/Tables/combat/combat_action_dodge.xml",
        // WO-151: an NPC's hit reaction (NpcHit) and an animal's paired bite (a sync attack, sent as NpcAttack)
        "Libs/Tables/combat/combat_action_hit.xml",
        "Libs/Tables/combat/combat_action_sync_attack.xml",
        // WO-163 (C3, appended at the end): the attacker-side answer to a block / perfect block (WO-162 Q4) and the paired
        // hit half of a perfect block's riposte / master strike
        "Libs/Tables/combat/combat_action_failed_attack.xml",
        "Libs/Tables/combat/combat_action_sync_perfect_block_hit.xml",
    };

    public static ActionRowCatalog LoadFrom(string tablesPakPath)
    {
        var rows = new Dictionary<Guid, Row>();
        using var zip = ZipFile.OpenRead(tablesPakPath);
        foreach (var entry in Tables)
        {
            var e = zip.GetEntry(entry);
            if (e is null) continue;
            using var s = e.Open();
            var doc = XDocument.Load(s);
            string table = Path.GetFileNameWithoutExtension(entry);
            foreach (var el in doc.Descendants())
            {
                var g = (string?)el.Attribute("mn_fragment_guid");
                var frag = (string?)el.Attribute("mn_fragment_id");
                if (g is null || frag is null || !Guid.TryParse(g, out var guid)) continue;
                rows[guid] = new Row(table, frag, (string?)el.Attribute("mn_tags") ?? "",
                    Int((string?)el.Attribute("attack_zone_id") ?? (string?)el.Attribute("block_zone_id")),
                    Int((string?)el.Attribute("input_class_id")), Int((string?)el.Attribute("attack_type_id")),
                    Int((string?)el.Attribute("action_type_id")), Flt((string?)el.Attribute("attack_time_to_start")),
                    Flt((string?)el.Attribute("attack_time_to_hit")), Long((string?)el.Attribute("actor_class_hash")),
                    Int((string?)el.Attribute("r_weapon_class_id")), Int((string?)el.Attribute("l_weapon_class_id")));
            }
        }
        return new ActionRowCatalog(rows);
    }

    private static int Int(string? s) => int.TryParse(s, out int v) ? v : -1;
    private static float Flt(string? s) => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
    private static long Long(string? s) => long.TryParse(s, out long v) ? v : 0L;

    /// <summary>
    /// WO-163 (A4): one existing row to show a blow that came with no swing of its own -- an animal's bite (an anim-collision
    /// started from a procedural clip, no row exists for it in any table: WO-162 Q3.1/Q3.3) or a man's blow nothing captured.
    /// An animal: the plain bite row of the attack table (attack type 8, no lying / sitting condition); a man: the unarmed
    /// punch (the FreeAttack punch row of two empty hands). Chosen by content, the lowest tag then GUID, so the choice is
    /// the same on every machine; null when the table holds none (the caller then shows nothing and says so).
    /// </summary>
    public Row? GenericRow(bool animal)
    {
        Row? best = null; Guid bestGuid = default;
        foreach (var (g, r) in _rows)
        {
            if (r.Table != "combat_action_attack") continue;
            bool fits = animal
                ? r.AttackType == 8 && !r.Tags.Contains("oppLying", StringComparison.Ordinal) && !r.Tags.Contains("oppSitting", StringComparison.Ordinal)
                : r.AttackType == 5 && r.ActionType == 26 && r.Tags.Contains("l_noweapon+r_noweapon", StringComparison.Ordinal);
            if (!fits) continue;
            if (best is null || string.CompareOrdinal(r.Tags, best.Value.Tags) < 0 || (r.Tags == best.Value.Tags && g.CompareTo(bestGuid) < 0)) { best = r; bestGuid = g; }
        }
        return best;
    }

    /// <summary>
    /// WO-165 (C3): the attacker-side answer to a blocked (or perfectly blocked) swing -- a row of <c>combat_action_failed_attack</c>
    /// (action type 27 on a block, 15 on a perfect block; WO-162 Q4: four of each) whose weapon tags (l_* / r_*) all appear in the swing's
    /// own row tags; of several the one naming the most weapon tags, then the lowest GUID. Null = no row fits: an unarmed or other-weapon
    /// attacker has no recoil in the game's tables at all (the caller logs recoil=none and plays nothing).
    /// </summary>
    public Row? FailedAttackRow(Row swing, bool perfect)
    {
        int want = perfect ? ActionFailedAttackOnPerfectBlock : ActionFailedAttackOnBlock;
        var have = new HashSet<string>(swing.Tags.Split('+', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        Row? best = null; Guid bestGuid = default; int bestN = -1;
        foreach (var (g, r) in _rows)
        {
            if (r.Table != "combat_action_failed_attack" || r.ActionType != want) continue;
            var wt = Wo165Rules.WeaponTags(r.Tags);
            if (wt.Count == 0 || !wt.All(have.Contains)) continue;
            if (wt.Count > bestN || (wt.Count == bestN && g.CompareTo(bestGuid) < 0)) { best = r; bestGuid = g; bestN = wt.Count; }
        }
        return best;
    }

    /// <summary>WO-163 (A1): the row behind a GUID, for both reads of a sync attack's descriptor -- both hitting the catalog is the one thing that must never happen.</summary>
    public bool BothResolve(Guid a, Guid b) => a != Guid.Empty && b != Guid.Empty && _rows.ContainsKey(a) && _rows.ContainsKey(b);

    public static ActionRowCatalog? TryLoad(Action<string> log)
    {
        try
        {
            string? pak = WeaponSwingCatalog.FindTablesPak();
            if (pak is null) { log("[rowcatalog] Tables.pak not found -- v8 attack events fall back to the weapon swing rows"); return null; }
            var c = LoadFrom(pak);
            log($"[rowcatalog] loaded {c.Count} combat rows (attack/block/perfect_block/dodge/hit/sync_attack/failed_attack/sync_perfect_block_hit) from {pak}");
            return c;
        }
        catch (Exception ex)
        {
            log($"[rowcatalog] load failed ({ex.GetType().Name}: {ex.Message}) -- v8 attack events fall back to the weapon swing rows");
            return null;
        }
    }
}
