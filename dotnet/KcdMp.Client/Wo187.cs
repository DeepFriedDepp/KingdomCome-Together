// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-187: the pure rules of 0.60.5's agent half -- every fight move visible to the watcher, the joiner's shield, fight positions, weapon
/// timing. The bridge is GameBridge.Wo187.cs; the native half is wo187.cpp (rules wo187_rules.h).
/// </summary>
public static class Wo187Rules
{
    // ------------------------------------------------------------------ switches (each ships on; mp_anim_scope_log is a trace)

    /// <summary>mp_opp_logical (A2): a copy's opponent is set by the engine's own C_CombatActor::SetLogicalTarget, re-checked every wind-up.</summary>
    public const bool DefaultOppLogical = true;
    /// <summary>mp_opp_pin (A2): a wish overwritten twice is pinned with the game's own combat_forcedTarget relation.</summary>
    public const bool DefaultOppPin = true;
    /// <summary>mp_engage_intent (A1): the joiner engages a copy the moment the host's NPC targets him, judged on the host's position too.</summary>
    public const bool DefaultEngageIntent = true;
    /// <summary>mp_anim_guard (C2): a copy holds its guard for the host NPC's whole fight (any target); a figure keeps it through a quiet stream.</summary>
    public const bool DefaultAnimGuard = true;
    /// <summary>mp_anim_idle (C2): a body whose full-body scope stays empty after a move gets its idle back.</summary>
    public const bool DefaultAnimIdle = true;
    /// <summary>mp_anim_defence (C2): the host NPC's own block, perfect block and dodge play on its copy.</summary>
    public const bool DefaultAnimDefence = true;
    /// <summary>mp_anim_combo (C2): a player's combo steps and throws (sync attacks) play on their figure (the attacker's half).</summary>
    public const bool DefaultAnimCombo = true;
    /// <summary>mp_anim_scope_log (C3, research): the full-body scope's action around every move, logged. Off: a trace, not a mechanism.</summary>
    public const bool DefaultAnimScopeLog = false;
    /// <summary>mp_fight_pos (B1): once a second per NPC in a fight with a player, both machines' positions on the shared clock.</summary>
    public const bool DefaultFightPos = true;
    /// <summary>mp_chase_follow (B2): a copy whose host NPC runs keeps following its stream through a swing instead of being held for it
    /// (the live chase: a held copy fell 0.5-1.6 m behind at 1.6 m/s on every swing; at a run, metres).</summary>
    public const bool DefaultChaseFollow = true;

    /// <summary>mp_anim_hit_fallback (C1): a host NPC's hit reaction row that does not play on its copy (another weapon in the copy's hand here)
    /// is shown with the copy's own weapon's reaction row for the same zone, or a weaponless one.</summary>
    public const bool DefaultAnimHitFallback = true;
    /// <summary>mp_anim_avatar_hit (C1): a player's own hit reaction (the row his engine played when he took a blow) plays on his figure on the
    /// partner's screen.</summary>
    public const bool DefaultAnimAvatarHit = true;

    public static readonly string[] SwitchNames =
        { "opp_logical", "opp_pin", "engage_intent", "anim_guard", "anim_idle", "anim_defence", "anim_combo", "anim_scope_log", "fight_pos", "chase_follow",
          "anim_hit_fallback", "anim_avatar_hit" };

    /// <summary>"name=on|off" from the Lua (w187_cfg).</summary>
    public static bool TryParseCfg(string? arg, out string name, out bool on)
    {
        name = ""; on = false;
        var f = (arg ?? "").Split('=', 2);
        if (f.Length != 2 || Array.IndexOf(SwitchNames, f[0]) < 0 || f[1] is not ("on" or "off")) return false;
        name = f[0]; on = f[1] == "on";
        return true;
    }

    // ------------------------------------------------------------------ A1: engage on intent

    /// <summary>The joiner's engage range (the DLL's own: native wo132.cpp kEngageRangeM).</summary>
    public const float EngageRangeM = 15f;
    /// <summary>A host stream sample older than this does not count as the host NPC's position.</summary>
    public const long StreamFreshMs = 1500;

    /// <summary>
    /// A1: the host's NPC targets THIS player (target=Avatar:&lt;me&gt;). The DLL judges the range on the copy's own position; a copy that lags
    /// behind its host NPC (the field: a chase) was refused while the host's NPC already stood at the joiner. With mp_engage_intent the
    /// host's own position counts too: within range by either, the copy is engaged at once.
    /// </summary>
    public static bool IntentInRange(bool on, bool targetsMe, float streamDistM, long streamAgeMs) =>
        on && targetsMe && streamDistM >= 0f && streamDistM <= EngageRangeM && streamAgeMs >= 0 && streamAgeMs <= StreamFreshMs;

    // ------------------------------------------------------------------ A2: whom a copy fights on this screen

    /// <summary>The native op 36's kinds and reasons (wo187.h).</summary>
    public const byte OppNone = 0, OppLocalPlayer = 1, OppFigure = 2;
    public const byte WhyTargetsMe = 1, WhyTargetsHost = 2, WhyTargetsOther = 3;

    /// <summary>
    /// A2: the copy's wished opponent from one host npc-combat event. The host's NPC targets this player -> the local player; it targets the
    /// host -> the host's figure here; another player -> that player's figure. No target -> no change (the caller keeps the last wish until it
    /// goes stale: a fight between two targets reads "none" for a moment, WO-183 4b).
    /// </summary>
    public static (byte Kind, byte Ghost, byte Why)? OppWish(NpcCombatEvent ev, byte myGhost, byte hostGhost)
    {
        switch (ev.Target)
        {
            case NpcCombatTarget.Avatar when myGhost != 0xFF && ev.TargetGhost == myGhost: return (OppLocalPlayer, 0xFF, WhyTargetsMe);
            case NpcCombatTarget.Avatar: return (OppFigure, ev.TargetGhost, WhyTargetsOther);
            case NpcCombatTarget.Host when hostGhost != 0xFF: return (OppFigure, hostGhost, WhyTargetsHost);
            default: return null;
        }
    }

    /// <summary>A wish is sent on a change, else refreshed this often (the DLL drops one not refreshed for 4 s).</summary>
    public const long OppRefreshMs = 2000;

    /// <summary>Only copies this close to the joiner (by the host's own position) get a wish or a guard hold: each is a pipe call on the game's
    /// main thread, and a far copy is neither seen fighting nor ever engaged (a battle of twenty would cost ~30 calls a second).</summary>
    public const float NearM = 35f;
    public static bool Near(float distM) => distM < 0f || distM <= NearM;   // unknown distance: as near

    // ------------------------------------------------------------------ C2: the guard hold

    /// <summary>C2: a copy holds its guard while its host NPC is in combat or has any target (the CombatMode bit alone reads 0 against a player).</summary>
    public static bool GuardHoldWants(NpcCombatEvent ev) => ev.InCombat || ev.Target != NpcCombatTarget.None;
    /// <summary>The hold is refreshed this often (the DLL lets it go 3 s after the last word).</summary>
    public const long GuardRefreshMs = 1500;

    // ------------------------------------------------------------------ B3: weapons and timing

    /// <summary>A row's weapon class by its right-hand tag (the native mirror is wo187rules::Weapon; the byte goes with the strike, op 9).</summary>
    public enum Weapon : byte { Unknown = 0, Sword = 1, ShortSword = 2, Longsword = 3, Polearm = 4, Blunt = 5, Unarmed = 6, Other = 7 }

    public static Weapon WeaponOf(string tags)
    {
        foreach (var t in (tags ?? "").Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (t)
            {
                case "r_swords": case "r_sabre": return Weapon.Sword;
                case "r_shortSwords": return Weapon.ShortSword;
                case "r_longsword": return Weapon.Longsword;
                case "r_halberd": return Weapon.Polearm;
                case "r_bluntWeapon": case "r_mace": case "r_axe": return Weapon.Blunt;
                case "r_noweapon": return Weapon.Unarmed;
            }
            if (t.StartsWith("r_", StringComparison.Ordinal)) return Weapon.Other;
        }
        return Weapon.Unknown;
    }

    public static string WeaponName(Weapon w) => w switch
    {
        Weapon.Sword => "sword", Weapon.ShortSword => "short-sword", Weapon.Longsword => "longsword", Weapon.Polearm => "polearm",
        Weapon.Blunt => "blunt", Weapon.Unarmed => "unarmed", Weapon.Other => "other", _ => "unknown",
    };

    /// <summary>A charged row (the table's charged_attack, or its tag): its blow lands at start + hit, every other row's at hit (the vanilla trace: the
    /// countdown at wind-up equals attack_time_to_hit on 35 vanilla swings; WO-185: the charged stab's blade lands at start + hit).</summary>
    public static bool IsCharged(ActionRowCatalog.Row r) => r.Charged || (r.Tags ?? "").Contains("chargedAttack", StringComparison.Ordinal);

    /// <summary>B3: the blow's contact after the row starts, ms (-1 = the row has no timings).</summary>
    public static int ContactMs(ActionRowCatalog.Row r)
    {
        if (r.TimeToHit <= 0f) return -1;
        return (int)MathF.Round((IsCharged(r) ? r.TimeToStart + r.TimeToHit : r.TimeToHit) * 1000f);
    }

    /// <summary>
    /// B3: the striking window's "hit" the DLL adds to the start (native strike_window: contact = start + hit). For an ordinary row that is
    /// hit - start (the blow lands attack_time_to_hit after the row starts), floored at 50 ms; a charged row keeps hit (start + hit).
    /// </summary>
    public static int StrikeHitMs(ActionRowCatalog.Row r)
    {
        if (r.TimeToHit <= 0f) return -1;
        if (IsCharged(r) || r.TimeToStart <= 0f) return (int)MathF.Round(r.TimeToHit * 1000f);
        return Math.Max(50, (int)MathF.Round((r.TimeToHit - r.TimeToStart) * 1000f));
    }

    /// <summary>B3: the attacker's recoil rows exist (combat_action_failed_attack) only for these weapons; blunt and unarmed recoil is procedural.</summary>
    public static bool HasRecoilRows(Weapon w) => w is Weapon.Sword or Weapon.ShortSword or Weapon.Longsword or Weapon.Polearm;

    /// <summary>B3: the perfect-blocked (0x10) length is measured for these weapons -- the sword (WO-185's vanilla trace), the mace and bare
    /// hands (WO-187's vanilla traces with real input); short sword, longsword and polearm use the 0.42 s fallback (native wo187_rules.h pb_stage2).</summary>
    public static bool PbStage2Measured(Weapon w) => w is Weapon.Sword or Weapon.Blunt or Weapon.Unarmed;

    /// <summary>The per-weapon timing table (B3), one row per class: where contact comes from, whether 0x10 is measured, whether recoil rows exist.</summary>
    public static (string Contact, bool PbMeasured, bool Recoil) Timing(Weapon w) =>
        (w == Weapon.Unknown ? "the row's hit (default)" : "the row's attack_time_to_hit (start + hit when charged)", PbStage2Measured(w), HasRecoilRows(w));

    // ------------------------------------------------------------------ C2: the defence rows

    /// <summary>An NPC row that is its own defence (block, broken block, perfect block that is not the counter, dodge): played on the copy alone.</summary>
    public static bool IsDefenceRow(ActionRowCatalog.Row r) =>
        r.Table is "combat_action_block" or "combat_action_dodge"
        || (r.Table == "combat_action_perfect_block" && !ActionRowCatalog.IsMasterStrike(r.ActionType));

    /// <summary>The native capture's flag bits on an NPC row (motion.cpp): 0x01 perfect-block class, 0x02 block/dodge class; on a player row 0x04 a sync attack.</summary>
    public const byte NativeFlagPerfect = 0x01, NativeFlagDefence = 0x02, NativeFlagPlayerSync = 0x04;

    // ------------------------------------------------------------------ C1: hit reactions

    /// <summary>
    /// C1 (mp_anim_hit_fallback): the score of one hit-reaction row <paramref name="candTags"/> (fragment <paramref name="candFragment"/>) as the
    /// stand-in for a reaction row that was not shown on a body. Live (WO-187): a CombatHitGen row naming another weapon still plays (the game
    /// animates it with the body's own weapon), a weaponless CombatHitTorso row never shows as a full-body reaction, and another actor class's
    /// row is dropped at once. So: 16 for the body's own weapon (its last played row's l_* / r_* tags), 8 for a full-body CombatHitGen row,
    /// 4 for the blow's zone (block_zone_id, dZ), 2 for the guard side (leftGuard / rightGuard), 1 for the hit type (hitTypeNormal / Low).
    /// </summary>
    public static int HitFallbackScore(string failedTags, int failedZone, IReadOnlyList<string> bodyWeapon, string candFragment, string candTags, int candZone)
    {
        var cw = Wo165Rules.WeaponTags(candTags);
        int s = 0;
        if (cw.Count > 0 && bodyWeapon.Count == cw.Count && cw.All(bodyWeapon.Contains)) s += 16;
        if (candFragment == "CombatHitGen") s += 8;
        if (failedZone >= 0 && candZone == failedZone) s += 4;
        static string? Tag(string tags, params string[] any) => tags.Split('+').FirstOrDefault(t => Array.IndexOf(any, t) >= 0);
        string? side = Tag(failedTags, "leftGuard", "rightGuard");
        if (side is not null && Tag(candTags, "leftGuard", "rightGuard") == side) s += 2;
        string? type = Tag(failedTags, "hitTypeNormal", "hitTypeLow");
        if (type is not null && Tag(candTags, "hitTypeNormal", "hitTypeLow") == type) s += 1;
        return s;
    }

    /// <summary>C1: the DLL's answer to "was the last move on this body shown" (op 41; native wo187_rules.h move_shown): a move whose own action
    /// left the body within 0.1 s, or never appeared within 0.6 s, was not animated -- the pipe's "ok" only says the row was queued.</summary>
    public const byte MoveUnknown = 0, MovePending = 1, MoveShown = 2, MoveDropped = 3;
    /// <summary>C1: the weapon a body that played no weapon row yet is first assumed to hold (bare hands: the weapon rows the stand-in may
    /// name are dropped on a bare-handed body, live).</summary>
    public const string BareHandsTags = "r_noweapon+l_noShield";
    /// <summary>C1: the first look after a reaction row was queued, and the second when the first is still pending.</summary>
    public const int MoveFirstLookMs = 150, MoveSecondLookMs = 650;

    /// <summary>C1 (mp_anim_avatar_hit): a partner's hit reaction (ActionKind.AvatarHit) plays on his figure here only while the figure stands --
    /// known, not lying dead (WO-155: a dead figure lies until the respawn) and not fallen from a friendly-fire knockdown.</summary>
    public static bool AvatarHitPlayable(bool figureKnown, bool figureDown) => figureKnown && !figureDown;

    // ------------------------------------------------------------------ C1: what syncs (the inventory as code)

    public enum Stage { Captured, Sent, Played }

    /// <summary>
    /// C1: every fight action class and the path that carries it, after WO-187 (P = a player's move on their figure, N = a host NPC's move on its
    /// copy). A class whose path is null at a stage does not reach the watcher; Wo187InventoryTests fails if one drops out.
    /// </summary>
    public static readonly (string Class, string Side, string? Captured, string? Sent, string? Played)[] Inventory =
    {
        ("slash / stab / blunt", "P", "C_CombatActorActionAttack capture", "Attack (v8 row)", "the figure: ghost_swing of the row"),
        ("slash / stab / blunt", "N", "C_CombatActorActionAttack capture (mp_npc_rows)", "NpcAttack", "the copy: ghost_swing + its striking window"),
        ("combos, special and master strikes", "P", "attack rows; sync attacks with mp_anim_combo; master strike as a perfect-block row", "Attack (FlagCombo) / BlockImpulse", "the figure: ghost_swing (the attacker's half)"),
        ("combos, special and master strikes", "N", "sync / perfect-block class capture", "NpcAttack", "the copy: the attacker's half, or the plain fallback (mp_sync_fallback)"),
        ("unarmed and fist fights", "P", "attack rows (r_noweapon)", "Attack", "the figure: ghost_swing"),
        ("unarmed and fist fights", "N", "attack rows (r_noweapon)", "NpcAttack", "the copy: ghost_swing, unarmed"),
        ("kicks and stop moves", "P", "attack rows (type kick) / perfect-block rows", "Attack / BlockImpulse", "the figure: ghost_swing"),
        ("kicks and stop moves", "N", "attack rows (type kick) / perfect-block rows", "NpcAttack", "the copy: ghost_swing"),
        ("block, parry, perfect block, dodge", "P", "Block / PerfectBlock / Dodge capture", "BlockImpulse / Dodge", "the figure: ghost_swing (no swing-clip fallback)"),
        ("block, parry, perfect block, dodge", "N", "Block / PerfectBlock / Dodge capture (mp_anim_defence)", "NpcAttack (defence row)", "the copy: ghost_swing of its own defence"),
        ("hit reactions, knockdowns, the attacker's recoil", "P", "C_CombatActorActionHit capture of the player (mp_anim_avatar_hit); Downed bit", "AvatarHit; Downed bit", "the figure: ghost_swing of his reaction (a stand-in when not shown); falls on a death or a friendly-fire knockdown"),
        ("hit reactions, knockdowns, the attacker's recoil", "N", "C_CombatActorActionHit capture", "NpcHit", "the copy: ghost_swing (a stand-in when not shown: mp_anim_hit_fallback); recoil rows where the weapon has them"),
        ("clinch and sync attacks", "P", "sync attack capture (mp_anim_combo)", "Attack (FlagCombo)", "the figure: the attacker's half"),
        ("clinch and sync attacks", "N", "sync attack capture", "NpcAttack", "the copy: the attacker's half, or the plain fallback"),
        ("the guard stance between moves", "P", "combat mode, guard zone and stance (BodyState2)", "the v8 state block (guard=)", "the figure: combat mode held (a quiet stream lingers 3 s)"),
        ("the guard stance between moves", "N", "the host's NPC watch (npc-combat)", "NpcCombat", "the copy: combat mode held for any fight (mp_anim_guard)"),
    };

    public static int InventoryGaps() => Inventory.Count(r => r.Captured is null || r.Sent is null || r.Played is null);

    // ------------------------------------------------------------------ B1: the fight-position line

    /// <summary>The planar distance (m).</summary>
    public static float Dist2D(float ax, float ay, float bx, float by) => MathF.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    /// <summary>B1, the host's line: the NPC's position and its target's, on the shared clock.</summary>
    public static string HostPosLine(string npc, string shared, float nx, float ny, string target, float tx, float ty) => FormattableString.Invariant(
        $"WO187-FIGHTPOS role=host npc={npc} shared={shared} npc_at={nx:F2},{ny:F2} target={target} target_at={tx:F2},{ty:F2} dist_m={Dist2D(nx, ny, tx, ty):F2}");

    /// <summary>B1, the joiner's line: the copy here, the host's own position of the NPC (its stream: age, samples per second, the host's clock),
    /// this player, and the gap between the copy and the host's NPC (lag_m) -- which side is out of place.</summary>
    public static string JoinerPosLine(string npc, string shared, float cx, float cy, (float X, float Y, long AgeMs, int Pps, uint HostMs)? stream,
                                       float mx, float my, string target)
    {
        string st = stream is { } s ? FormattableString.Invariant($"{s.X:F2},{s.Y:F2} stream_age_ms={s.AgeMs} pps={s.Pps} host_ms={s.HostMs}") : "none";
        float dStream = stream is { } a ? Dist2D(a.X, a.Y, mx, my) : float.NaN, lag = stream is { } b ? Dist2D(b.X, b.Y, cx, cy) : float.NaN;
        return FormattableString.Invariant(
            $"WO187-FIGHTPOS role=joiner npc={npc} shared={shared} copy_at={cx:F2},{cy:F2} stream_at={st} me_at={mx:F2},{my:F2} dist_copy_m={Dist2D(cx, cy, mx, my):F2} dist_stream_m={dStream:F2} lag_m={lag:F2} target={target}");
    }

    /// <summary>The relay's clock (the shared one: the relay runs on the host) for a line: this machine's UTC plus the measured offset.</summary>
    public static string SharedClock(DateTime utcNow, double? offsetMs) =>
        offsetMs is double o ? utcNow.AddMilliseconds(o).ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) : "?";
}
