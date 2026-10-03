<p align="center"><img src="branding/banner-1280.jpg" alt="Kingdom Come: Together" width="640"></p>

# Testing 0.45.0 (both players)

**Kingdom Come: Together.** Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial and free; not
affiliated with or endorsed by Warhorse Studios or PLAION.

0.45.0 is the public beta: quests that go both ways, fighting together, joins that
look like they work, smooth riding, and an in-game menu instead of console commands.
One page for the host and the partner: install, then the checks below. Tick what you
saw, write the time next to anything odd, and send the logs at the end.

**Status:** everything on this page ran in the game solo, against a scripted partner or a synthetic host
(`docs/WO-154-findings.md`); these checks are its first run with two people. If a join's load ever freezes the game,
the launcher says so: restart the game and join again, and note the time.

## Install

Both of you: run `KingdomComeTogether-Setup-0.45.0.exe` (the maintainer sends it).
It installs over 0.43.0 or 0.44.0 and keeps every launcher setting you have. Both
computers need it: 0.45.0 refuses every other version. Afterwards,
`tools\Verify-Install.ps1` (if you have it) must end with "all present".

## The mod menu

Press **Insert** in the game. **PgUp/PgDn** choose, **End** changes, **Insert** or
**Esc** closes. Every switch named below is in it (the console commands still work,
see `docs/MOD-MENU.md`). The host's settings show on the partner's screen as
"[set by the host]".

## What's new in 0.45.0

- **Quests go both ways**: the host's steps reach the partner's game, and the
  partner's own steps (a duel, a brawl, a battle) reach the host's world.
- **Your figure never turns on your partner**, and a knocked-down player's figure
  falls on the other screen too, lies there and gets up with them.
- **The host's blows count**: an enemy fighting the partner turns on the host the
  way it would in the game. A partner who falls or respawns is left alone.
- **Joins**: the host sees the partner's progress through the whole join; a slow
  load is waited out; plain messages when a join can't work; **Join with a new
  character**.
- **Riding** is smooth on both screens.
- **Fast travel is off** during a session unless the host turns it on; **voice chat
  is off** unless you turn it on.

**Still known:** cutscenes are not shared yet (each game plays its own); the tutorial
is the roughest part; no lip sync (the Modding Tools build lacks the data); the
partner's herb picking shows them standing; conversations aren't heard by the other
player.

## Setup (both of you)

Install on both machines (`mark_setup`), the host loads their save and clicks
CONNECT, the partner waits at the main menu and clicks CONNECT there
(`mark_join`). **Use throwaway saves only.**

**Markers.** Each check has a one-word marker. When you start it, open the console
(`~`) and type it, for example `mark_quest`. It writes one line into the log so we
can find the moment.

## The checks (`docs/TWO-PLAYER-CHECKLIST.md`, WO-154, items 111–126)

1. **A quest step each** (`mark_quest`). The host moves a quest on (a conversation, an
   item); the partner wins a duel or brawl step or picks up a quest item. Both
   journals follow.
2. **Friendly fire** (`mark_ff`). Hit each other once with a fist and once with a
   weapon. The other's figure never barks, draws or attacks.
3. **Knocked down** (`mark_knock`). Knock each other down once. The figure falls on
   the other screen, lies there, and gets up with its player.
4. **The host turns an enemy** (`mark_turn`). An enemy fights the partner; the host
   hits it from behind. It turns to the host.
5. **The partner falls** (`mark_partnerdown`). The partner goes down in a fight with
   two enemies. Every enemy stops fighting the partner; after the respawn nobody goes
   for them for two minutes.
6. **A fight that won't end** (`mark_endfight`). The partner uses "I'm stuck" in the
   menu: every fight against them ends.
7. **The join bar** (`mark_joinbar`). The host watches the panel during the partner's
   join: it shows from start to end.
8. **Riding together** (`mark_ride`). Ride side by side at a trot and a gallop. The
   other's horse moves smoothly.
9. **Fast travel** (`mark_fasttravel`). Off: the map refuses with a plain line. The
   host turns it on in the menu: the host travels, the partner is brought along.
10. **The menu** (`mark_menu`). Both open it. On the partner's screen the host's
    settings are locked; name badges off and on; clean screen on and off.
11. **The partner's own wait** (`mark_skip`). With "Sleep and wait together" off on
    the partner, the partner waits an hour: the time goes back to the host's and the
    game says so.
12. **Voice** (`mark_voice`). Off after the install; turn it on in the menu on both:
    you hear each other.
13. **A caravan** (`mark_caravan`). Walk past people sitting on a cart: they stay
    seated and move with the cart.
14. **Talking** (`mark_talk`). The partner talks to a few villagers, men and women.
15. **If a join's load ever freezes** (`mark_joinslow`): the launcher says to restart
    the game; do so, join again, and note the time.

Anything stuck: "I'm stuck" in the menu (or `mp_unstuck`, then `mp_unstuck hard`),
marker `mark_stuck`. Anything odd: "Something's wrong here" in the menu.

## Logs to send afterwards

Both machines: Report a bug in the launcher. It collects the logs of the six
launches before as well, so a restart does not lose the logs of a crash.
