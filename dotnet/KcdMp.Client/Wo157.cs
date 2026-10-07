// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>
/// WO-157: the first public-beta patch, the agent's pure rules (docs/WO-157-findings.md).
/// </summary>
public static class Wo157Rules
{
    // ------------------------------------------------------------ 2.4 a start save several groups host

    /// <summary>
    /// The joiner's world tag for the host's world: the plain seed tag, unless this machine already keeps the world of
    /// ANOTHER host with that seed (two hosts of one start save) -- then a tag of the seed and this host's install key.
    /// <paramref name="storedHostKey"/>: the host the plain-tag folder belongs to (null: unknown, it is claimed by the
    /// first host that comes). A host without WO-157 sends no key (<paramref name="hostKey"/> null): the plain tag.
    /// </summary>
    public static string WorldTag(uint seed, uint? hostKey, uint? storedHostKey)
    {
        string plain = WhsSave.SeedTag(seed);
        if (hostKey is not uint k || storedHostKey is not uint stored || stored == k) return plain;
        return WhsSave.HostWorldTag(seed, k);
    }

    /// <summary>
    /// "Join with a new character": the saves a new game's first Henry may come from, in the order they are tried.
    /// First this player's own saves of the host's build (quest saves first, oldest first: a new game writes
    /// permanent002 right after the prologue), then -- WO-157 -- saves with the host's seed (a copy of the same start
    /// save). The pristine check (a Henry with no stat and no skill XP) decides each one: a Henry that has done
    /// nothing yet is nobody's character.
    /// </summary>
    public static List<GameBridge.OwnSave> FreshCandidates(List<GameBridge.OwnSave> ownSameBuild, List<GameBridge.OwnSave> all, string? build, uint? hostSeed)
    {
        static IEnumerable<GameBridge.OwnSave> Order(IEnumerable<GameBridge.OwnSave> s) =>
            s.OrderBy(x => x.Save.File.StartsWith("permanent", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ThenBy(x => x.Save.SaveTime).Take(80);
        var o = Order(ownSameBuild).ToList();
        if (hostSeed is uint hs)
        {
            var seen = o.Select(x => x.Save.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var copies = all.Where(x => x.Seed == hs && !seen.Contains(x.Save.FullPath));
            if (build is not null) copies = GameBridge.SameBuildSaves(copies.ToList(), build);
            o.AddRange(Order(copies));
        }
        return o;
    }
}
