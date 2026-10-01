// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// Portions from the original project, marczukmichal/kcd2-multiplayer; its author keeps their copyright (AUTHORS).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms.Design;
using KCDMP_launcher.Services;
using Microsoft.AspNetCore.Components;

namespace KCDMP_launcher.Components.Shared
{
    public class Base : ComponentBase
    {
        [Inject]
        protected UiService UiService { get; set; } = default!;

        [Inject]
        protected NetService NetService { get; set; } = default!;

    }
}
