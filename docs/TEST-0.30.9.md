<p align="center"><img src="branding/banner-1280.jpg" alt="Kingdom Come: Together" width="640"></p>

# Testing 0.30.9 (both players)

**Kingdom Come: Together** (the new name; it was "KCD2 Multiplayer").
Unofficial. Not affiliated with or endorsed by Warhorse Studios.

One page for the host and the partner. About an hour and a half. Play normally
apart from the checks in section 4. If something goes wrong, note the time and
carry on or stop, then send what section 7 lists.

0.30.9 carries three pieces of work at once:

* **WO-132** (was 0.30.7): joins that load, safe damage, combat mode together;
* **WO-133**: the old quest catch-up is off in a shared world, and only the
  host's clock moves the world;
* **WO-134**: **looting together** (bodies, things lying about, chests), and
  the new name and logo.

## 1. Install

* Both of you: run `KingdomComeTogether-Setup-0.30.9.exe` (the maintainer
  sends it; it is not on GitHub). It installs over your old version: afterwards
  Windows' apps list shows **one** entry, **Kingdom Come: Together**, and your
  settings are still there. **Both** computers need it: 0.30.9 refuses every
  other version.
* Open the launcher (the Start menu and desktop shortcut are now called
  **Kingdom Come Together**). The window title says **Kingdom Come: Together**,
  the banner is at the top, the bottom bar shows the logo and
  **Kingdom Come: Together v 0.30.9** on both computers.

## 2. Connect

As before (`docs/TEST-0.30.0.md`, section 3): the host **HOST GAME** → code →
**START GAME**, load, **CONNECT**; the partner **JOIN THROUGH STEAM** (or by
address) and waits at the main menu. The first time in a world the partner's
launcher asks **Bring my character** / **Start fresh**.

## 3. Both: turn the recorder on

In the game console (`~`), on **both** computers, once you are in the world:

```
mp_leash_trace on
```

You should read `WO127-LEASH trace=on`.

## 4. What to try

Away from towns, never townsfolk or town guards. Each item says what each of
you looks at. Note the time of anything that does not match.

### A. Looting together (WO-134) -- **do A1 and A2 first**

What changed, in plain words: things you drop for each other work exactly as
before. Bodies are the host's: the partner loots the host's body, and the first
to take an item has it. Things lying about exist once. Chests are each your own,
and remembered per world.

1. **Drops, host → partner.** Host: drop an apple in front of the partner.
   Partner: it appears where it fell; pick it up. Host: it vanishes from the
   ground. Partner: you have it.
2. **Drops, partner → host.** The same the other way round. Both: nobody gets
   a second apple, and nobody's apple comes back.
3. **A body, the host loots.** Kill a bandit together. Host: take its clothes.
   Partner: watch the body; it is stripped on your screen too.
4. **A body, the partner loots.** Kill another. Partner: loot it; the loot
   screen shows what the host's body holds (the same items the host would see);
   take something. Host: open the same body; that item is not there any more.
5. **Both grab the same thing.** On a third body, both open the loot at once and
   take the same item. One of you keeps it; the other sees "Someone already
   took that." and it is not in your pack.
6. **Pickpocketing** a living person on the partner's side is still blocked
   (the game's own message).
7. **Loose items.** Partner: pick up something lying about (a bowl on a table,
   a tool, food) while the host watches it. Host: it is gone on your screen too.
   Then the host picks one up; partner: gone for you.
8. **Chests, per player.** Host: take the dice (or anything) from a chest.
   Partner: open the same chest; the dice are still there for you; take them.
   Both: you each have your own dice.
9. **Chests across a rejoin.** Partner: quit to the desktop, restart and join
   again (the host saves first, or waits for an autosave). Partner: the chest
   the host emptied is still full for you; the chest you emptied is still empty.
   Host: your own chests are as you left them.
10. **Herbs:** each of you gathers your own (expected, not a bug).

### B. Fights together (WO-132, from 0.30.7)

1. **The join works from any host save.** Host: load an old save and a new one
   in turn, and one early in the story. Partner: join each time; you always
   reach the world (never "Savegame loading failed"). Host: nothing to see.
2. **Combat mode for both of you.** Fight bandits together, weapons out.
   Partner: the direction indicator on the one in front of you, the combat icon
   under the compass, and your block works. Host: the same on your screen for
   the one you fight.
3. **Enemies swing on the partner's screen.** Partner: the one facing you raises
   its weapon and swings, and changes guard. Host: that bandit is really
   attacking in your world at that moment.
4. **The guard engages both.** The partner hits a bandit first, then the host
   joins in. Host: it turns to you when you hit it, and you get combat mode.
   Partner: it keeps fighting you too.
5. **No hits after a respawn.** Partner: die in a fight. After waking, no damage
   for the first seconds, and none from the fight you left. Host: nothing to do
   (the agent log shows `NOT forwarded: its player is down|waking`).
6. **No second death from bleeding.** Partner: get cut, then get away. Your
   health drops only from your own bleeding, never a second death after the
   fight. Host: nothing to see.
7. **The guard doesn't vanish when the partner dies.** Host: the enemy you were
   both fighting keeps fighting you and stays where it was. Partner: after
   waking, it no longer comes for you.
8. **Fair damage.** Same armour, the same enemy, one hit each. Partner: your
   health lost (kcd.log `[playerhit] took N damage`). Host: your own bar. Note
   both numbers.
9. **Your swings on each other.** Swing at each other once (friendly fire is on).
   Each screen: the other player's figure swings.

### C. The story and the clock (WO-133)

1. **F11 / F12 do nothing** outside a dice match, on both computers: no prompt,
   nothing jumps; typing `mp_quest_yes` says "Quest catch-up is off in a shared
   world." Both screens.
2. **No quest-gap notices.** Play past a quest step on the host's side. Partner:
   no "you are behind" or divergence message. Host: none either.
3. **Only the host moves the clock.** Partner: sleep or wait an hour. Host: your
   clock does not jump (the partner's own clock may run ahead until the next
   join; expected). Host: sleep; partner: your clock follows.

## 5. Still to check from before

* Staying together (0.30.2): the warning at 600 m, the pull at 650 m, only the
  host fast-travels.
* One world (0.30.5): nobody appears twice; bodies lie in one place.

## 6. Play normally

Ride, go into towns and houses, trade, talk. Please do **not** attack or rob
townsfolk (crime is still separate for each of you).

When you finish: `mp_leash_trace off` on both (or just quit; the file is kept).

## 7. What to send afterwards (both of you, before starting the game again)

* The launcher's **REPORT BUG** button makes one zip with the logs and the
  recorder files. Or collect by hand:
  * `kcd.log` from the Modding Tools folder (the next launch overwrites it);
  * `kcdmp-native.log` from the same folder;
  * `agent.log`, and the `leash` folder, beside `KcdMpClient.exe`;
  * the launcher's `app*.log`;
  * the host: the relay's `relay*.log`.
* The times of anything odd, and what you saw.
* **Never send save files**, and not the `KCDMP\henry` or `KCDMP\chests`
  folders either.
