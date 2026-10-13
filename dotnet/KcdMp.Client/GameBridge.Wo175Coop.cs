// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

// WO-175 S1 -- the co-op playline, the agent's live half (the rules and the launcher's helpers: Wo175Coop.cs).
//
//   the switch   mp_coop_playline on|off (kdcmp.lua) -> w175_coop on|off|status -> CoopPlayline in mod-settings.json
//                beside this exe (SettingsJson, key by key); the answer goes back to the mod (KCD2MP_W175CoopState).
//                The launcher reads the same file through --w175coop, and writes it itself while the game sits at
//                its main menu (a host has no agent there).
//   the joiner   the spliced world (WO-124 mpworld file) goes into the playline that already holds the HOST world's tag (WO-180),
//                else the lowest free one, never into a playline holding a save of another playthrough; none of the five: the
//                join is not even requested (Wo180SlotAvailableOrTell), the player is told.
//   WO175-COOP join to=playlineN/mpworld... seed=<tag> sha256=<12> / refused reason=no-free-playline / off -- ...
//   The host's side (WO-175's copy of the picked world) is gone: the launcher loads the save he picked, in its own playline.
public partial class GameBridge
{
    private static string W175ModSettings => Wo175Coop.ModSettingsBesideAgent();

    /// <summary><c>w175_coop on|off</c> (the console command's change) or <c>status</c>: remembered, said back.</summary>
    private void Wo175CoopOnEvent(string? arg)
    {
        string a = (arg ?? "").Trim();
        if (Wo175Coop.TryParseSwitch(a, out bool on))
        {
            var r = Wo175Coop.SetSwitch(W175ModSettings, on);
            Console.WriteLine($"WO175-COOP switch mp_coop_playline={On(on)} -> {Wo154MenuRules.ModSettingsFile} {r.Outcome.ToString().ToLowerInvariant()}"
                              + (r.Outcome == KcdMp.Wire.SettingsJson.Outcome.Refused ? $" ({Wo154MenuRules.Scrub(r.Why, AppContext.BaseDirectory)})" : ""));
        }
        else if (a == "status") Console.WriteLine($"WO175-COOP switch mp_coop_playline={On(Wo175Coop.SwitchOn(W175ModSettings))} (asked)");
        else { Console.WriteLine($"WO175-COOP malformed w175_coop '{W154mTrunc(arg)}'"); return; }
        bool now = Wo175Coop.SwitchOn(W175ModSettings);
        _ = ExecLuaAsync($"if KCD2MP_W175CoopState then KCD2MP_W175CoopState(\"{On(now)}\") end");
    }

    /// <summary>
    /// The playline for the joiner's spliced world: -1 = the switch is off (the WO-124 rule: this player's own
    /// playline), null = refused (no slot; said and logged), else the playline that already holds this host's world, or the lowest free one (WO-180).
    /// </summary>
    private int? Wo175JoinPlayline(string saves, string hostTag, uint joinId)
    {
        if (!Wo175Coop.SwitchOn(W175ModSettings))
        {
            Console.WriteLine($"WO175-COOP off -- plays in the original (join 0x{joinId:x8}: the host's world goes into this player's own playline, WO-124)");
            return -1;
        }
        if (hostTag.Length == 0)
        {
            Console.WriteLine($"WO175-COOP refused reason=no-seed (join 0x{joinId:x8}: the host's world has no playthrough seed)");
            return null;
        }
        var pick = Wo175Coop.ChooseForJoin(saves, hostTag);
        if (!pick.Ok)
        {
            Console.WriteLine($"WO175-COOP refused reason=no-free-playline (join 0x{joinId:x8}, world {hostTag})");
            return null;
        }
        Console.WriteLine($"WO175-COOP join 0x{joinId:x8}: world {hostTag} -> co-op playline{pick.Playline} ({(pick.Existing ? "it holds this world already" : "the lowest free")})");
        return pick.Playline;
    }

    /// <summary>The joiner's placed file, logged once it is placed and read back.</summary>
    private void Wo175JoinPlaced(uint joinId, string hostTag, string path, byte[] sha) =>
        Console.WriteLine($"WO175-COOP join to={Wo175Coop.Display(path)} seed={hostTag} sha256={Convert.ToHexString(sha).ToLowerInvariant()[..12]}");

    private string? _w180SlotToldTag;
    private (string Tag, bool Ok, long Until)? _w180SlotCache;

    /// <summary>
    /// WO-180 (fix 3): before any join request goes out -- the host is never paused for a join that cannot land -- there must be a slot for
    /// the host's world: a playline that already holds it, else a free one. None: nothing is sent, the player is told once in the game
    /// (toast) and in the launcher, and the answer is looked at again every few seconds (a freed slot lets the join go by itself).
    /// </summary>
    private bool Wo180SlotAvailableOrTell()
    {
        if (!Wo175Coop.SwitchOn(W175ModSettings) || !_peerSeedKnown) return true;   // switch off: the player's own playline (WO-124); no seed yet: the request checks again
        string? saves = ResolveSavesDirForJoin();
        string tag = WhsSave.SeedTag(_peerSeed);
        bool ok;
        if (saves is null) ok = true;
        else if (_w180SlotCache is { } c && c.Tag == tag && Environment.TickCount64 < c.Until) ok = c.Ok;   // the saves are not rescanned every 2 s
        else
        {
            ok = Wo175Coop.SlotAvailable(saves, tag);
            _w180SlotCache = (tag, ok, Environment.TickCount64 + 5_000);
        }
        if (ok)
        {
            _w180SlotToldTag = null;
            return true;
        }
        if (_w180SlotToldTag != tag)
        {
            _w180SlotToldTag = tag;
            Console.WriteLine($"WO175-COOP refused reason=no-free-playline (no join asked: five playlines in use, none holds world {tag}; nothing is sent, the host is not paused)");
            SetJoinUi("failed", Wo175Coop.JoinerNoFreeText);
            _ = ExecLuaAsync($"if KCD2MP_Wo124Msg then KCD2MP_Wo124Msg(\"{EscapeLua(Wo175Coop.JoinerNoFreeText)}\") end");
        }
        return false;
    }
}
