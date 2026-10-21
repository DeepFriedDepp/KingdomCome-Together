// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-197: the animation-sync release's rules (GameBridge.Wo197.cs is the agent's half; native/KCDMP/wo197.cpp the DLL's). Engine-free;
/// Wo197Tests pins them.
///   B1 mp_anim_recoil    a player's recoil (his blow blocked / perfect-blocked) on his figure: the engine's own row, else the failed-attack
///                        row for the weapon of the swing his figure last played (WO-165's picker), else nothing (blunt, unarmed).
///   B2 mp_anim_paired    a paired move's victim half: the sync_hit row with the attacker row's key, queued on the victim before the attacker's.
///   B3 mp_anim_clinch    a player's clinch: both halves of each clinch move on his figure and its opponent in one step; the clinch guard
///                        through the engine's StartSyncGuard on the pair.
///   B4 mp_anim_knockout  a player's brawl knockout: his figure falls with the game's own unconsciousness and stands up when he does.
///   B5 mp_copy_guard_engine  A6's takeover ends a copy's leftover with the engine's EnsureGuardAction.
///   Part C mp_anim_seats / mp_anim_work / mp_anim_routines: a changed activity of that kind applied at once (the DLL's 250 ms tick waited).
/// </summary>
public static class Wo197Rules
{
    public const bool DefaultRecoil = true, DefaultPaired = true, DefaultClinch = true, DefaultKnockout = true, DefaultGuardEngine = true;

    public static readonly string[] SwitchNames =
    {
        "anim_recoil", "anim_paired", "anim_clinch", "anim_knockout", "copy_guard_engine",
        "anim_seats", "anim_work", "anim_routines",
    };

    /// <summary>"name=on|off" from the Lua (w197_cfg).</summary>
    public static bool TryParseCfg(string? arg, out string name, out bool on)
    {
        name = ""; on = false;
        var f = (arg ?? "").Split('=', 2);
        if (f.Length != 2 || Array.IndexOf(SwitchNames, f[0]) < 0 || f[1] is not ("on" or "off")) return false;
        name = f[0]; on = f[1] == "on";
        return true;
    }

    // ---------------------------------------------------------------- B1
    public const byte RecoilPerfect = 0x01, RecoilKnown = 0x02, RecoilRow = 0x04;

    /// <summary>The kind the receiver plays: a perfect block's recoil only when the sender read one; an unread kind is a plain block's.</summary>
    public static bool RecoilIsPerfect(byte flags) => (flags & RecoilPerfect) != 0 && (flags & RecoilKnown) != 0;

    public static string RecoilKindName(byte flags) => (flags & RecoilKnown) == 0 ? "unknown" : (flags & RecoilPerfect) != 0 ? "pb" : "block";

    /// <summary>A figure's last swing is the one a recoil belongs to while it is this fresh (ms); older: no row is guessed.</summary>
    public const long RecoilSwingFreshMs = 4000;

    // ---------------------------------------------------------------- B2
    /// <summary>The victim fragments of an attacker's paired fragment (the game's tables: combat_action_sync_attack -> combat_action_sync_hit). A
    /// victim's death half (CombatHitSyncDeathGen, CombatStopDeathSlave ...) only for the attacker's own death row: the blow's outcome is the
    /// host's, and a death pose on a body that lives is worse than the plain half.</summary>
    public static IReadOnlyCollection<string> VictimFragments(string attackFragment) => attackFragment switch
    {
        "CombatAttackComboGen" => new[] { "CombatHitComboGen" },
        "CombatAttackComboDeathGen" => new[] { "CombatHitComboDeathGen" },
        "CombatStopMaster" => new[] { "CombatStopSlave" },
        "CombatStealthAttackSuccess" => new[] { "CombatStealthHitSuccess" },
        "CombatAttackSyncGen" => new[] { "CombatHitSyncGen" },   // a death half only for a death row (the live b2pair picked CombatHitSyncDeathGen for a punch)
        "CombatRiposteSyncGen" => new[] { "CombatRiposteHitSyncGen" },
        "CombatRiposteSyncDeathGen" => new[] { "CombatRiposteHitSyncDeathGen" },
        "HorseCombatAttackSync" => new[] { "HorseCombatHitSync" },
        "HorseCombatAttackSyncFail" => new[] { "HorseCombatHitSyncFail" },
        "CombatAttackSync" => new[] { "CombatHitSync" },
        "CombatAttackSyncDeath" => new[] { "CombatHitSyncDeathGen" },
        _ => Array.Empty<string>(),
    };

    private static readonly string[] AttackTypeTags = { "slash", "stab", "punch", "kick", "bite", "hook", "throw", "smash" };

    /// <summary>
    /// The attacker row's tags as the victim's row names them: the attacker's weapon (l_/r_) becomes the opponent's (ol_/or_), the opponent's
    /// weapon (ol_/or_) the victim's own (l_/r_) -- or the victim's real weapon when it is known --, the strike zone sZn becomes oppSZn, the
    /// attack type (slash, stab ...) oppSlash ..., the opponent's guard (oppRightGuard) the victim's own (rightGuard); the combo, step and
    /// variant tags and the rest stay as they are.
    /// </summary>
    public static HashSet<string> MirrorTags(string attackTags, IReadOnlyList<string>? victimWeapon)
    {
        var e = new HashSet<string>(StringComparer.Ordinal);
        bool own = victimWeapon is { Count: > 0 };
        if (own) foreach (var w in victimWeapon!) e.Add(w);
        foreach (var t in attackTags.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            if (t.StartsWith("ol_", StringComparison.Ordinal) || t.StartsWith("or_", StringComparison.Ordinal)) { if (!own) e.Add(t[1..]); }
            else if (t.StartsWith("l_", StringComparison.Ordinal) || t.StartsWith("r_", StringComparison.Ordinal)) e.Add("o" + t);
            else if (t.Length > 2 && t.StartsWith("sZ", StringComparison.Ordinal)) e.Add("oppSZ" + t[2..]);
            else if (Array.IndexOf(AttackTypeTags, t) >= 0) { e.Add("opp" + char.ToUpperInvariant(t[0]) + t[1..]); e.Add(t); }
            else if (t is "oppRightGuard" or "oppLeftGuard" or "oppTopGuard" or "oppBottomGuard") e.Add(char.ToLowerInvariant(t[3]) + t[4..]);
            else e.Add(t);
        }
        return e;
    }

    public const long ManClass = 1578932418;

    /// <summary>A victim row's score: one point per expected tag it names; +4 when its own weapon tags are exactly the victim's known
    /// weapon; -4 when it names another weapon for him; +2 when its actor class is the victim's. int.MinValue = never (another actor class).</summary>
    public static int VictimScore(HashSet<string> expected, string hitTags, IReadOnlyList<string>? victimWeapon, long victimClass, long rowClass)
    {
        if (victimClass != 0 && rowClass != 0 && rowClass != victimClass) return int.MinValue;
        var tags = hitTags.Split('+', StringSplitOptions.RemoveEmptyEntries);
        int s = 0;
        foreach (var t in tags) if (expected.Contains(t)) s++;
        if (victimWeapon is { Count: > 0 })
        {
            var own = Wo165Rules.WeaponTags(hitTags);
            if (own.Count > 0) s += own.Count == victimWeapon.Count && own.All(victimWeapon.Contains) ? 4 : -4;
        }
        if (victimClass != 0 && rowClass == victimClass) s += 2;
        return s;
    }

    // ---------------------------------------------------------------- B3
    public const byte ClinchEnter = 1, ClinchMove = 2, ClinchLeave = 3, ClinchPhaseMask = 0x03, ClinchMaster = 0x04, ClinchRows = 0x08;

    public static string ClinchPhaseName(byte flags) => (flags & ClinchPhaseMask) switch { ClinchEnter => "enter", ClinchMove => "move", ClinchLeave => "leave", _ => "?" };

    /// <summary>The clinch event's payload: the master row as the RowEvent's row, the slave row as its name (32 hex digits).</summary>
    public static RowEvent ClinchEvent(uint ms, byte flags, Guid masterRow, Guid slaveRow, string? opponent = null) =>
        new(ms, flags, masterRow, (slaveRow == Guid.Empty ? "" : slaveRow.ToString("N")) + (ClinchOpponentOk(opponent) ? "|" + opponent : ""));

    /// <summary>The slave row: the name's first 32 characters (WO-202: a "|opponent" may follow).</summary>
    public static Guid ClinchSlaveRow(RowEvent e)
    {
        string s = e.Name;
        int bar = s.IndexOf('|');
        if (bar >= 0) s = s[..bar];
        return s.Length == 32 && Guid.TryParseExact(s, "N", out var g) ? g : Guid.Empty;
    }

    /// <summary>WO-202 IV.3: the body the sender's player clinched (his engine's own opponent), or null.</summary>
    public static string? ClinchOpponent(RowEvent e)
    {
        int bar = e.Name.IndexOf('|');
        if (bar < 0) return null;
        string n = e.Name[(bar + 1)..];
        return ClinchOpponentOk(n) ? n : null;
    }

    private static bool ClinchOpponentOk(string? n) =>
        n is { Length: > 0 and <= 63 } && n.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');

    /// <summary>The clinch guard type for StartSyncGuard on a pair: the move's own row says it (guard 4 unarmed, 24 / 25 the longsword
    /// clinches); else bare hands -> 4, anything else -> 24.</summary>
    public static int ClinchGuardType(int rowGuardType, bool bareHands) => rowGuardType is 4 or 24 or 25 ? rowGuardType : bareHands ? 4 : 24;

    /// <summary>The clinch entry's rows run this long before the loop rows replace them (the game's CombatClinchStartGuard rows: about 1 s).</summary>
    public const int ClinchEntryMs = 900;

    // ---------------------------------------------------------------- B4
    public enum KnockEdge { None, Down, Up }

    /// <summary>The engine-knockout bit's edges in a partner's vitals (a death is the WO-155 collapse, never this).</summary>
    public static KnockEdge KnockoutEdge(byte was, byte now)
    {
        bool a = (was & Protocol.PlayerStateFlagEngineKnockout) != 0, b = (now & Protocol.PlayerStateFlagEngineKnockout) != 0;
        return a == b ? KnockEdge.None : b ? KnockEdge.Down : KnockEdge.Up;
    }

    /// <summary>The sender's bit: the game's own knockout (IsUnconscious) while the death guard has not downed him.</summary>
    public static bool EngineKnockout(bool? isUnconscious, bool localDowned) => isUnconscious == true && !localDowned;
}
