// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KCDMP_launcher.Models;
using KcdMp.Wire;
using Serilog;

namespace KCDMP_launcher.Pages
{
    /// <summary>
    /// WO-175 S1, as WO-180 left it: only the mp_coop_playline switch (a joiner's world goes into a save slot of its own, never into
    /// the player's own playline). The host's copy of the picked world is gone -- Start Game loads the save that was picked, directly.
    /// The switch lives in mod-settings.json beside the agent; while the game sits at its main menu this side remembers the
    /// console command's change itself (a host has no agent there to do it).
    /// </summary>
    public partial class Home
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(long Seq, bool On)> w175Switches = new();   // mp_coop_playline read from kcd.log

        /// <summary>mp_coop_playline typed while the game sits at its main menu: remembered in mod-settings.json beside the
        /// agent (in a world the agent does it), and said on the game's screen.</summary>
        private async Task Wo175CoopSwitchAsync(GameConsole console, bool on)
        {
            string file = Path.Combine(Path.GetDirectoryName(ResolveAgainstLauncher(settings.AgentPath)) ?? "", "mod-settings.json");
            var r = await Task.Run(() => SettingsJson.Update(file, new[] { SettingsJson.Edit.Set("CoopPlayline", JsonValue.Create(on)) }, createIfMissing: true));
            bool ok = r.Outcome != SettingsJson.Outcome.Refused;
            Log.Information("WO175-COOP switch mp_coop_playline={On} -> mod-settings.json {Outcome}", on ? "on" : "off", r.Outcome.ToString().ToLowerInvariant());
            await console.LuaAsync(MenuTakeoverRule.HudLine(!ok ? "The co-op save slot setting could not be saved. Switch to the launcher window for details."
                : on ? "A joined world goes into a save slot of its own (mp_coop_playline on)."
                : "A joined world goes into your own playline (mp_coop_playline off)."));
        }
    }
}
