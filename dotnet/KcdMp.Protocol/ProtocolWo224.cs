// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Wire;

// ---------------------------------------------------------------------------
// WO-224 -- grabs and combos in step (docs/WO-224-findings.md).
//
// The clinch event (ActionKind.AvatarClinch, a RowEvent) names the body its player clinched. Since WO-202 that name rode behind a '|'
// in the RowEvent's name, which the RowEvent's own parser refuses (letters, digits and '_' only): every clinch event that named its
// body was dropped at the receiver, and a move with both rows and a name did not fit the name's 42 bytes at all. With w224_clinch on
// the name's place is taken by its 32-bit hash: "<slave row, 32 hex digits or nothing>_<hash, 8 hex digits>" -- at most 41 characters
// of the RowEvent's own alphabet. The receiver finds the body among the names it knows.
// ---------------------------------------------------------------------------

public static partial class Protocol
{
    /// <summary>The DLL's ops (native/KCDMP/wo224.h): 220 Config [bits:4][role:1] -> [bits:4], 221 Status [page:1] -> text.</summary>
    public const byte Wo224OpConfig = 220, Wo224OpStatus = 221;
    /// <summary>A: 222 ClinchStart [master eid:4][slave eid:4] (0 = the local player) -> [result:1][master flag:1][slave flag:1]
    /// (1 started, 0 refused by the engine, 2 in a clinch with each other already, 255 not possible); 223 ClinchEnd [a:4][b:4]
    /// (b = 0xFFFFFFFF: a's own clinch partner) -> [bodies ended:1]; 224 ClinchRead [eid:4] -> [in:1][master:1][guard type:4][opponent:4][scope0].</summary>
    public const byte Wo224OpClinchStart = 222, Wo224OpClinchEnd = 223, Wo224OpClinchRead = 224;
    /// <summary>A: 225 ClinchHost [in:1][this player's figure is the master:1][refused:1][copy eid:4] -> [1] -- the joiner: the host's word for
    /// his own figure, kept by his DLL and reconciled with the local player's own clinch every frame.</summary>
    public const byte Wo224OpClinchHost = 225;
    /// <summary>A: 226 ResolveNear [near eid:4][name hash:4] -> [eid:4] -- the one NPC within 8 m of that entity whose name has this
    /// hash (0 = none, or more than one).</summary>
    public const byte Wo224OpResolveNear = 226;
    /// <summary>A: 227 WeaponRefresh [eid:4]([hint:1]) -> [result:1][right hand's class before:1][after:1] -- a copy's combat weapon
    /// classes checked against its hands and refreshed the engine's own way when they differ (1 refreshed, 0 the same, 255 not
    /// possible). hint = the host NPC's weapon class by the weapon catalogue (<see cref="Wo224Rules.WeaponHintNothing"/> = it holds
    /// nothing, <see cref="Wo224Rules.WeaponHintNone"/> = no word): the copy's class while its hand holds no item object yet.</summary>
    public const byte Wo224OpWeaponRefresh = 227;
    /// <summary>B: 228 ChainInput [eid:4][requested zone:1 (0xFF none)][input class:4] -> [result:1] -- the joiner: the host's NPC made
    /// its combo input; its copy makes the same input, with the same requested attack zone, when its own chain slot opens (1 queued,
    /// 0 not a copy whose attacks are its own here).</summary>
    public const byte Wo224OpChainInput = 228;
}

/// <summary>
/// WO-224's switches (`mp_&lt;name&gt; on|off` in the game's console; `mp_w224_status` lists them); the bench's twin turns one off for a
/// run through <see cref="EnvVar"/> -- read by both agents and both DLLs.
/// </summary>
public static class W224Switch
{
    public const string EnvVar = "KCDMP_TEST_W224_OFF";

    /// <summary>The native word's bits (wo224.h), in order: w224_clinch (A: a grab plays on both bodies on both screens), w224_chains
    /// (B: an enemy's chain of swings plays on its copy as a chain), w224_death (D: an action the mod queued is stopped or given back only while its block is still that action),
    /// w224_clinch_log (A.0: the clinch instrument, read-only).</summary>
    public static readonly string[] NativeNames = ["w224_clinch", "w224_chains", "w224_death", "w224_clinch_log"];

    /// <summary>C: w224_official -- the agent's own (no native bit): the build's signature is checked and the mod menu says "Official
    /// build" or "Unofficial build". Off = not checked, and a build that is not checked reads unofficial (the text is never hidden).</summary>
    public const string Official = "w224_official";

    public static readonly string[] Names = [.. NativeNames, Official];

    public static bool TwinOff(string name) => TwinOff(Environment.GetEnvironmentVariable(EnvVar), name);

    public static bool TwinOff(string? env, string name) =>
        !string.IsNullOrWhiteSpace(env) && env.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => x == name || (x == "all" && name != "w224_clinch_log"));   // the instrument: every run keeps it

    public static uint NativeBits(Func<string, bool> on)
    {
        uint b = 0;
        for (int i = 0; i < NativeNames.Length; i++) if (on(NativeNames[i])) b |= 1u << i;
        return b;
    }
}

/// <summary>WO-224's pure rules (the agent's half).</summary>
public static class Wo224Rules
{
    /// <summary>A body's name as 32 bits (FNV-1a over its UTF-8 bytes); never 0 (0 = no name).</summary>
    public static uint NameHash(string? name)
    {
        if (string.IsNullOrEmpty(name)) return 0;
        uint h = 2166136261;
        foreach (byte b in System.Text.Encoding.UTF8.GetBytes(name)) { h ^= b; h *= 16777619; }
        return h == 0 ? 1 : h;
    }

    /// <summary>The clinch event's name field: the slave row (32 hex digits, or nothing) and, behind a '_', the clinched body's name
    /// hash (8 hex digits) when there is one.</summary>
    public static string ClinchName(Guid slaveRow, string? opponent)
    {
        string s = slaveRow == Guid.Empty ? "" : slaveRow.ToString("N");
        uint h = NameHash(opponent);
        return h == 0 ? s : s + "_" + h.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The slave row of a clinch event's name field in either form (WO-197's bare 32 digits, WO-202's "row|name", WO-224's
    /// "row_hash"): its first 32 characters when they are a row.</summary>
    public static Guid ClinchSlaveRow(string name)
    {
        int cut = name.IndexOfAny(['_', '|']);
        string s = cut >= 0 ? name[..cut] : name;
        return s.Length == 32 && Guid.TryParseExact(s, "N", out var g) ? g : Guid.Empty;
    }

    /// <summary>The clinched body's name hash, or 0 when the name field carries none.</summary>
    public static uint ClinchOpponentHash(string name)
    {
        int bar = name.IndexOf('_');
        if (bar < 0 || name.Length - bar - 1 != 8) return 0;
        return uint.TryParse(name.AsSpan(bar + 1), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out uint h) ? h : 0;
    }

    /// <summary>The one known name with this hash; null when none or more than one has it (a clash is never guessed).</summary>
    public static string? NameOfHash(uint hash, IEnumerable<string> known)
    {
        if (hash == 0) return null;
        string? found = null;
        foreach (string n in known)
        {
            if (NameHash(n) != hash) continue;
            if (found is not null && found != n) return null;
            found = n;
        }
        return found;
    }

    /// <summary>A FigureClinch leave with this flag: the host's engine never started the clinch the joiner's own game started (its
    /// start was refused for the whole retry window) -- his game ends its own at once.</summary>
    public const byte FlagRefused = 0x10;

    /// <summary>The engine refuses a clinch start while the two bodies are not at the row's distance (a figure trails its player by
    /// the link's delay): the start is asked again this many times, this far apart -- about as long as the engine's own auto-trigger
    /// keeps asking while a player walks in.</summary>
    public const int ClinchStartTries = 6, ClinchStartRetryMs = 100;

    /// <summary>A: op 227's hint byte -- the host NPC holds nothing; no word.</summary>
    public const byte WeaponHintNothing = 0xFE, WeaponHintNone = 0xFF;
    /// <summary>A: the hint byte for a weapon class of the catalogue (a class this byte cannot carry: no word).</summary>
    public static byte WeaponHint(int? weaponClass, bool holdsNothing) =>
        holdsNothing ? WeaponHintNothing : weaponClass is int c && c >= 0 && c < 0xFE ? (byte)c : WeaponHintNone;

    /// <summary>A.3: a clinch the partner's machine left is ended here when this body is still in it this long after the leave arrived
    /// (the engine's own end usually comes first: the same move ends it on both).</summary>
    public const int ClinchEndGraceMs = 100;   // (250 until a13: with a 0.74 s stall of the link one end came 1.06 s late)

    /// <summary>A.3: a clinch whose partner's machine has said nothing of it for this long (no enter, no move) while this body is
    /// still in it is ended here.</summary>
    public const int ClinchSilentMs = 6000;
}
