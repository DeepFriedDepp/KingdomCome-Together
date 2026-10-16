// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// WO-190: the pure rules of 0.60.9's agent half -- animation parity: both players see the same thing. The bridge is GameBridge.Wo190.cs;
/// the native half is wo190.cpp (rules wo190_rules.h), with A6 in wo166.cpp and A3/A4 in motion.cpp.
/// </summary>
public static class Wo190Rules
{
    // ------------------------------------------------------------------ switches (each ships on; mp_anim_trace is a research trace)

    /// <summary>mp_anim_trace (0.1, research): every animation change of the local player, the figures and the near copies, logged (WO190-ANIM).</summary>
    public const bool DefaultAnimTrace = false;
    /// <summary>mp_anim_scope_log (A9): off, auto (the default: the bodies in a fight only, rate-limited -- a field bundle shows a bad pose's cause),
    /// on (auto plus WO-187's lines around every move).</summary>
    public const byte ScopeLogOff = 0, ScopeLogAuto = 1, ScopeLogOn = 2;
    public const byte DefaultScopeLog = ScopeLogAuto;
    /// <summary>mp_anim_idle_watch (A1): a fighting figure or copy whose full body stands empty 0.25 s gets its guard idle back, any time.</summary>
    public const bool DefaultIdleWatch = true;
    /// <summary>mp_anim_standin_weapon (A1): a reaction stand-in is chosen for the weapon the body really holds (the DLL reads its hands);
    /// an armed body never gets a bare-handed move.</summary>
    public const bool DefaultStandinWeapon = true;
    /// <summary>mp_copy_weapon_hold (A2): a fighting copy is told the host NPC's weapon at every engage and swing; a light (the night
    /// routine's torch) in its hand is put away (the Lua half).</summary>
    public const bool DefaultCopyWeaponHold = true;
    /// <summary>mp_anim_requeue_gate (A3): a standing copy stands (no flipping move vector), its gait class changes at most 4 times a second,
    /// and a paused copy has no look IK of its own.</summary>
    public const bool DefaultRequeueGate = true;
    /// <summary>mp_anim_gait_hold (A4): through a short stream gap (&lt;= 0.5 s) a body keeps its last gait; a body that visibly moves never idles.</summary>
    public const bool DefaultGaitHold = true;
    /// <summary>mp_copy_swing_takeover (A6): the host's next swing for a copy still in its last one (0x20 / 0x40) ends the leftover the engine's way.</summary>
    public const bool DefaultSwingTakeover = true;
    /// <summary>mp_engage_hold (A7): a copy whose host NPC turns to the other player stays engaged and animating 3 s (its opponent switched).</summary>
    public const bool DefaultEngageHold = true;

    public static readonly string[] SwitchNames =
        { "anim_trace", "anim_idle_watch", "anim_standin_weapon", "copy_weapon_hold", "anim_requeue_gate", "anim_gait_hold", "copy_swing_takeover", "engage_hold" };

    /// <summary>"name=on|off" from the Lua (w190_cfg).</summary>
    public static bool TryParseCfg(string? arg, out string name, out bool on)
    {
        name = ""; on = false;
        var f = (arg ?? "").Split('=', 2);
        if (f.Length != 2 || Array.IndexOf(SwitchNames, f[0]) < 0 || f[1] is not ("on" or "off")) return false;
        name = f[0]; on = f[1] == "on";
        return true;
    }

    /// <summary>mp_anim_scope_log's word -> its mode (null = not a scope-log word).</summary>
    public static byte? ScopeLogMode(string? value) => value switch { "off" => ScopeLogOff, "auto" => ScopeLogAuto, "on" => ScopeLogOn, _ => null };
    public static string ScopeLogName(byte m) => m switch { ScopeLogOff => "off", ScopeLogOn => "on", _ => "auto" };

    /// <summary>The DLL's trace mode (op 51 byte 0): mp_anim_trace on -> every body (2); else the scope log's auto / on -> the bodies in a fight (1);
    /// off -> none (0).</summary>
    public static byte TraceMode(bool trace, byte scopeLog) => trace ? (byte)2 : scopeLog == ScopeLogOff ? (byte)0 : (byte)1;

    // ------------------------------------------------------------------ A1: the stand-in's weapon

    /// <summary>The DLL's word on a body's hands (op 52): unknown, armed, bare hands.</summary>
    public const byte HandsUnknown = 0, HandsArmed = 1, HandsBare = 2;

    /// <summary>A1: a stand-in candidate is allowed on this body -- never a bare-handed reaction on a body that holds a weapon (the field's T-pose:
    /// a bare-handed move on an armed figure), never an armed one on bare hands (it dies at once, WO-187).</summary>
    public static bool StandinAllowed(byte hands, string candTags)
    {
        bool bare = (candTags ?? "").Split('+').Contains("r_noweapon");
        return hands switch { HandsArmed => !bare, HandsBare => bare, _ => true };
    }

    /// <summary>A1: the body row the stand-in is scored against: the DLL's hand tags when it read them, else the agent's own guess (the last weapon
    /// row this body played, bare hands when none). The body's actor class is kept from the row it last played (<paramref name="actorClass"/>): a
    /// row without one would take the FAILED row's class -- a dog's reaction on a man then searched the dog's rows (live: not shown either).</summary>
    public static ActionRowCatalog.Row? BodyFromHands(byte hands, string? tags, long actorClass = 0) =>
        hands is HandsArmed or HandsBare && !string.IsNullOrEmpty(tags) ? new ActionRowCatalog.Row("held", "", tags!, -1, -1, -1, ActorClass: actorClass) : null;

    // ------------------------------------------------------------------ A2: never a torch in a fighting copy's hand

    /// <summary>The game's lights an NPC may hold (KCD2MP.w144.LIGHTS: two torch weapons, the NPCs' torch tool, the lamp tool).</summary>
    public static readonly HashSet<string> Lights = new(StringComparer.OrdinalIgnoreCase)
    {
        "4cea28a0-0814-405a-bf24-4fd711f7eb63", "cfec1446-ce8d-4c9c-aa9a-56fc8b10bc0e", "e95bb3ae-38ce-41fd-948a-e471673b47e5", "bdf14d9c-7264-434c-96af-748ff2779c1b",
    };

    /// <summary>A2: the weapon class to tell a fighting copy (its host NPC's last known weapon), never a light; "?" = the copy's own weapon
    /// (the engine's draw), "-" = bare hands.</summary>
    public static string WeaponToHold(string? hostWeapon) =>
        string.IsNullOrEmpty(hostWeapon) || Lights.Contains(hostWeapon) ? "?" : hostWeapon;

    /// <summary>A2: the weapon is told again at most this often per copy (every engage event and every swing would be a Lua call each).</summary>
    public const long WeaponRefreshMs = 1500;

    // ------------------------------------------------------------------ A7: a copy turning between the two players

    /// <summary>A7: an engaged copy whose host NPC turns to the host stays engaged (and animating) this long; its opponent becomes the host's figure
    /// (op 36) at once. Before, it was released and paused at once and engaged again a second later (the 0.60.7 field: three of its blows on
    /// the joiner showed no animation).</summary>
    public const long EngageHoldMs = 3000;

    /// <summary>A7's verdict on an engaged copy whose host NPC now fights the host: hold it (true) while the turn is younger than EngageHoldMs.</summary>
    public static bool HoldEngaged(bool on, bool engagedNow, NpcCombatTarget target, long turnedAtMs, long nowMs) =>
        on && engagedNow && target == NpcCombatTarget.Host && turnedAtMs > 0 && nowMs - turnedAtMs < EngageHoldMs;

    // ------------------------------------------------------------------ A8: a paired attack the copy cannot take its half of

    /// <summary>A8: a sync (paired) row of a copy is refused its half by the DLL's clinch gate (WO-178: the copy's body plays the host's rows) when
    /// the engine starts the pair inside its sync distance -- 96 of 102 asks in the 0.60.7 field: the blow was invisible. After a sync row was
    /// played, the DLL's clinch refusals are read this late, and a refusal this recent belongs to it: the row's plain attack plays at once.
    /// (Live, WO-190: farther off the engine never asks the gate and the copy plays its own half: a sync row is not replaced up front.)</summary>
    public const int ClinchLookMs = 300;
    public const uint ClinchWithinMs = 1500;
}
