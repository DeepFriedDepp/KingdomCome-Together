// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// Portions from the original project, marczukmichal/kcd2-multiplayer; its author keeps their copyright (AUTHORS).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KCDMP_launcher.Models;
using KCDMP_launcher.Services;
using Microsoft.AspNetCore.Components;

namespace KCDMP_launcher.Components.Shared
{
    public class ModsProfile : BaseProfile
    {
        private string? Id { get; } = null;
        public string Name { get; set; } = "Default Profile";
        public List<string> EnabledMods { get; set; } = new List<string>();

        public ModsProfile()
        {

        }

        public void SaveProfileToFile(ServerInfo server)
        {
            try
            {
                base.SaveProfileToFile(Globals.ModsProfilesDirectoryPath, $"{server.Name}_{server.MapName}_{Id}");
            }
            catch (Exception ex)
            {
                _uiService.LogError(ex, $"Failed to save mods profile for server {server.Name} at {server.MapName}");
            }
        }

        public void LoadProfileFromFile(ServerInfo server)
        {
            try
            {
                string filePath = Path.Combine(Globals.ModsProfilesDirectoryPath, $"{server.Name}_{server.MapName}_{Id}.json");
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    var loadedProfile = System.Text.Json.JsonSerializer.Deserialize<ModsProfile>(json);
                    if (loadedProfile != null)
                    {
                        Name = loadedProfile.Name;
                        EnabledMods = loadedProfile.EnabledMods;
                    }
                }
            }
            catch (Exception ex)
            {
                _uiService.LogError(ex, $"Failed to load mods profile for server {server.Name} at {server.MapName}");
            }
        }
    }
}
