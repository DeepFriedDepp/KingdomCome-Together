// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// Portions from the original project, marczukmichal/kcd2-multiplayer; its author keeps their copyright (AUTHORS).
using KCDMP_launcher.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace KCDMP_launcher.Components
{
    public class ModalBase : Base
    {
        [Parameter] public bool IsVisible { get; set; }
        [Parameter] public EventCallback<bool> IsVisibleChanged { get; set; }
        [Parameter] public EventCallback OnClose { get; set; }

        protected bool isClosing { get; set; }

        protected async Task CloseModalAsync()
        {
            if (isClosing) return;

            isClosing = true;
            StateHasChanged();

            await Task.Delay(200);

            isClosing = false;
            IsVisible = false;

            await IsVisibleChanged.InvokeAsync(false);
            await OnClose.InvokeAsync();

            StateHasChanged();
        }
    }
}