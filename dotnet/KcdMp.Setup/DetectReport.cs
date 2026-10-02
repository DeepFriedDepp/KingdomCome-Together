// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;

namespace KcdMp.Setup;

/// <summary>
/// What Setup asks the shared detection before it installs, as key=value
/// lines (Inno's Pascal Script reads a text file far more easily than JSON).
/// The installer acts on <c>place_mod</c> and nothing else decides it.
///
/// <c>place_mod=1</c> only when the Modding Tools are installed AND their
/// workspace is linked: installed is not set up. Otherwise Setup installs the
/// launcher and holds the mod back, and the launcher's checklist places it
/// after linking (the agent builds the keys pak from the linked game files,
/// which an unlinked Modding Tools does not have).
/// </summary>
public static class DetectReport
{
    public static string Build(SetupSnapshot s)
    {
        var sb = new StringBuilder();
        void Kv(string k, object? v) => sb.Append(k).Append('=').Append(v switch { bool b => b ? "1" : "0", null => "", _ => v.ToString() }).Append("\r\n");

        bool placeMod = s.ModdingToolsInstalled && s.WorkspaceLinked;
        Kv("helper", 1);
        Kv("steam_found", s.Steam.Found);
        Kv("steam_root", s.Steam.Root);
        Kv("libraries", s.Steam.Libraries.Count);
        Kv("game_found", s.GameInstalled);
        Kv("mt_registered", s.ModdingToolsManifest is not null);
        Kv("mt_found", s.ModdingToolsInstalled);
        Kv("mt_exe", s.ModdingToolsExe);
        Kv("mt_root", s.ModdingToolsRoot);
        Kv("mods_dir", s.ModsTarget);
        Kv("workspace", s.Workspace is null ? "unknown" : s.WorkspaceLinked ? "linked" : "unlinked");
        Kv("workspace_expected", s.Workspace?.Entries.Count ?? 0);
        Kv("workspace_ok", s.Workspace?.OkCount ?? 0);
        Kv("place_mod", placeMod);
        Kv("reason", placeMod ? "" : Reason(s));
        return sb.ToString();
    }

    /// <summary>One plain sentence for the installer's page, saying what the launcher will finish.</summary>
    public static string Reason(SetupSnapshot s) =>
        !s.Steam.Found ? "Steam was not found on this PC. The launcher will walk you through installing the game and the Modding Tools." :
        !s.ModdingToolsInstalled ? "The KCD2 Modding Tools are not installed yet. The launcher will install them through Steam, link the game's files and then place the mod." :
        !s.GameInstalled ? "Kingdom Come: Deliverance II itself was not found. The launcher will install it through Steam, link its files and then place the mod." :
        "The Modding Tools are installed but the game's files are not linked into them yet. The launcher will link them and then place the mod.";

    public static Dictionary<string, string> Parse(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            int eq = line.IndexOf('=');
            if (eq > 0) d[line[..eq]] = line[(eq + 1)..];
        }
        return d;
    }
}
