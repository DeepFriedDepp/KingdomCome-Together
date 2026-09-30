<p align="center"><img src="branding/banner-1280.jpg" alt="Kingdom Come: Together" width="640"></p>

# Testing 0.42.2 (both players)

**Kingdom Come: Together.** Unofficial. Not affiliated with or endorsed by
Warhorse Studios.

0.42.2 fixes what your first evenings together on 0.42.0 found. One page for
the host and the partner: install, then the checks below. Tick what you saw,
write the time next to anything odd, and send the logs at the end.

## Install

Both of you: run `KingdomComeTogether-Setup-0.42.2.exe` (the maintainer sends
it; it is not on GitHub). It installs over 0.42.0. Both computers need it:
0.42.2 refuses every other version. Afterwards, `tools\Verify-Install.ps1` (if
you have it) must end with "all present".

## What's fixed in 0.42.2

- **No more "invisible second partner."** When the partner restarted the game,
  the host kept waiting for the old one: every sleep question timed out. Now a
  restart simply replaces the old connection.
- **Loading a save no longer crashes the host.** You can load in the middle of
  a session again.
- **The other player is dressed.** You no longer see your partner in their
  underwear: the figure wears what its player wears, and puts it back on by
  itself if the game takes it off.
- **Crouching stays crouched**, and sneaking looks like sneaking.
- **No lantern out of nowhere.** At night the other player's figure holds a
  light only when its player does, and nothing is left lying on the ground.
- **Horses show up** on the partner's screen where they used to be missing.
- **Talking is calmer.** The host's villager waits for the partner only while
  the partner really talks to them. If someone won't talk, the partner is told.
- **Dice with a villager** start on the partner's screen after the
  conversation.
- **One clock.** The partner's clock stops when the host's does (the host
  talking to someone) and never runs more than about a minute ahead.
- **Joining with an early save** (from the start of the game) works; if a join
  cannot go through, you are told why.
- **Lying down.** When the other player sits on a bed, their figure lies on it.
- **Logs are kept.** Report a bug now also sends the logs of the six launches
  before, so a restart after a crash no longer loses them.

> **If something looks wrong, turn it off with…** (the console, `~`; `on`
> switches it back; nothing else changes)
>
> | what looks wrong | type |
> |---|---|
> | the other player's clothes | `mp_avatar_dress off` |
> | a torch or a lamp in the other player's hand | `mp_avatar_lights off` |
> | a horse or an animal that should not be there | `mp_show_animals off` |
>
> Say which one you switched off, and when (`mark_odd` first).

**Still known**

- A wolf, a dog or a boar from a random encounter on the host's side may be
  missing on the partner's screen.
- A red icon at the top of the partner's screen after a crime can stay on
  (check 12 below).
- The partner does not hear the host whistle, and the other way round.
- At the grindstone the blade is not in the other player's figure's hands.

## Setup (both of you)

1. **Install** the same build on both machines. Marker: `mark_setup`.
2. **The host** starts the game from the launcher, loads their save, and clicks
   CONNECT once they can move.
3. **The partner** starts the game from the launcher and **stays at the main
   menu. Don't load a save.** Click CONNECT at the menu; the partner joins the
   host's world by itself. Marker: `mark_join`.

**Markers.** Each check has a one-word marker. When you start it, open the
console (`~`) and type it, for example `mark_clothes`. It writes one line into
the logs so we can find the moment. Anything odd with no check to fit:
`mark_odd`.

## The checks

1. **A restart in the middle.** The partner quits the game, starts it again and
   joins again. Then the host sleeps in a bed. Marker: `mark_rejoin`.
   * The question goes to the partner once, he says yes (F11), you both sleep.
2. **Load a save** (host). Save once, walk a bit, load it; twice more.
   Marker: `mark_load`.
   * The game loads every time; the partner's figure comes back.
3. **Talk to someone** (partner), by day and at night. Marker: `mark_talk`.
   * The conversation plays, or you are told "This person can't talk to you
     right now." Host: that person stands still only while the partner talks.
4. **Clothes.** Both change clothes a few times (a hat, armour), then crouch
   and sneak a few steps. Marker: `mark_clothes`.
   * The other player's figure wears what they wear, within a few seconds,
     every time, also while sneaking. Nobody in their underwear.
5. **Crouch** for a minute and sneak about. Marker: `mark_crouch`.
   * The figure stays crouched; it never stands up by itself.
6. **Night**, first without a torch, then with one. Marker: `mark_torch`.
   * No light in the figure's hand until its player holds a torch; when the
     torch goes away, nothing is left on the ground.
7. **Horses.** The host rides past the partner; then the partner past the host.
   Marker: `mark_horse`.
   * The rider sits on a horse. Horses standing about are there on both screens.
8. **Wolves or dogs** attack the partner. Marker: `mark_animals`.
   * The partner sees them, can hit them, and they die when they should.
9. **Dice** with someone in a tavern (partner). Marker: `mark_dice`.
   * After the conversation the dice game starts and you can play.
10. **The time.** Now and then the host says the time; the partner compares.
    Also while the host talks to someone. Marker: `mark_time`.
    * They never differ by more than about a minute.
11. **Sitting on a bed** (partner), then getting up. Marker: `mark_bed`.
    * Host: the partner's figure lies on that bed, then gets up.
12. **A red icon** at the top of the partner's screen that stays: write down
    when it came and send a screenshot. Marker: `mark_icon`.

## Logs to send afterwards

Both of you: **Report a bug** in the launcher, and send the zip it puts on your
desktop. It holds this evening's logs and those of the six launches before.
