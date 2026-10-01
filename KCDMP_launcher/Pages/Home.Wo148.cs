// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System;
using System.IO;
using System.Threading.Tasks;
using KCDMP_launcher.Models;
using Serilog;

namespace KCDMP_launcher.Pages
{
    /// <summary>
    /// WO-148: the dice keys are built on this machine, from the game's own files, before every
    /// game start (the agent's --keys-pak verb; dotnet/KcdMp.Client/KeybindPak.cs). The game reads
    /// its Mods folder only at startup, and a game update can change the files our lines go into,
    /// so the launcher refreshes the pak here; it is rewritten only when its content would change.
    /// A failure costs the keys only and never stops the launch. Logged without paths.
    /// </summary>
    public partial class Home
    {
        private sealed class KeysPakResult
        {
            public bool Ok { get; set; }
            public string Action { get; set; } = "";
            public string Detail { get; set; } = "";
            public string? Source { get; set; }
        }

        private static async Task RefreshKeysPakAsync(string agentPath, string gameRoot)
        {
            try
            {
                string modDir = Path.Combine(gameRoot, "Mods", "kdcmp");
                if (!Directory.Exists(modDir))
                {
                    Log.Warning("keys pak: no Mods\\kdcmp folder in the game root; not built");
                    return;
                }
                var r = await AgentHelper.RunAsync<KeysPakResult>(
                    agentPath, $"--keys-pak --game-root \"{gameRoot}\" --mod-dir \"{modDir}\"", "KEYS-PAK", TimeSpan.FromSeconds(20));
                if (r is null) Log.Warning("keys pak: the agent gave no answer; the dice keys may be missing");
                else if (r.Ok) Log.Information("keys pak: {Action} from {Source} ({Detail})", r.Action, r.Source, r.Detail);
                else Log.Warning("keys pak: not built -- {Detail}; the dice keys may be missing", r.Detail);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "keys pak: failed; the dice keys may be missing");
            }
        }
    }
}
