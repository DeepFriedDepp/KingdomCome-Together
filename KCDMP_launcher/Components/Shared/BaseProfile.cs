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
using KCDMP_launcher.Services;
using Microsoft.AspNetCore.Components;

namespace KCDMP_launcher.Components.Shared
{
    public class BaseProfile
    {
        [Inject]
        protected UiService _uiService { get; set; } = default!;
        public void SaveProfileToFile(string dir, string fileName)
        {
            if (!Directory.Exists(dir))
            {
                Log.Warning($"Profiles directory does not exist. Creating: {dir}");
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(Path.Combine(dir, $"{fileName}.json"), System.Text.Json.JsonSerializer.Serialize(this));
        }
    }
}