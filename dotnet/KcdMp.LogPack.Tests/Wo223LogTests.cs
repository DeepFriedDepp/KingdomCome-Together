// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.LogPack;

namespace KcdMp.LogPack.Tests;

/// <summary>WO-223 F: the briefing's lines on who was noticed first and on copies fighting the wrong player.</summary>
public class Wo223LogTests
{
    [Fact]
    public void Who_was_noticed_first_is_counted_by_side_with_the_mean_distance()
    {
        var log = new[]
        {
            "[KCD2-MP] WO223-NOTICE npc=tkop_vrahBandit_3 noticed=joiner:1 at=12.0 pace=3.1 crouch=0 in_view=1 other=host other_at=190.4 t=100.0",
            "[KCD2-MP] WO223-NOTICE npc=tkop_vrahBandit_6 noticed=joiner:1 at=8.0 pace=3.0 crouch=0 in_view=1 other=host other_at=188.0 t=101.0",
            "[KCD2-MP] WO223-NOTICE npc=tapo_kasparBandit_2 noticed=host at=14.8 pace=1.4 crouch=1 in_view=0 other=joiner:1 other_at=30.2 t=400.0",
            "[KCD2-MP] WO223-GATE figure=kcd2mp_1 npc=tkop_vrahBandit_3 named: seen at 21.1 m (sight 22.4 m, noise 1.4 m, vib 1.69, crouch 0)",
        };
        Assert.Equal("the host first: 1 (at 15 m on average); the joiner first: 2 (at 10 m on average)", Wo223Briefing.NoticeValue(log));
    }

    [Fact]
    public void A_session_without_an_alert_enemy_has_no_notice_line()
    {
        Assert.Null(Wo223Briefing.NoticeValue(new[] { "[KCD2-MP] WO223-CFG w223_perceive=on", "Skirmish event: Attack on x (target Dude)" }));
    }

    [Fact]
    public void The_newest_summary_says_how_long_copies_fought_the_wrong_player()
    {
        var native = new[]
        {
            "[12:00:00.000] t001234 Laaaaaa WO223-COPYSUM me_samples=40 aimed=20 (50.0 %) in_my_fight=30 (75.0 %) both=20 (50.0 %) wrong_s=5.0 of 10.0 wrong_spells=2",
            "[12:00:30.000] t001234 Laaaaaa WO223-COPYSUM me_samples=400 aimed=396 (99.0 %) in_my_fight=398 (99.5 %) both=394 (98.5 %) wrong_s=1.8 of 100.0 wrong_spells=3",
        };
        Assert.Equal("2 s of the 100 s enemies fought this player (in his fight and aimed at him in 98.5 % of 400 samples)", Wo223Briefing.CopyValue(native));
        Assert.Null(Wo223Briefing.CopyValue(new[] { "[12:00:00.000] t001234 WO209-OPP status w209 bits=0x7" }));
    }
}
