# Testing 0.30.7 (both players)

One page for the host and the partner. About an hour. Play normally apart
from the few things in section 4. If something goes wrong, note the time and
carry on or stop, then send what section 7 lists.

0.30.7 is about three things: joining no longer fails on the loading screen,
nobody gets hit while dead or waking up, and **fighting feels like fighting on
both screens** (the partner gets the game's own combat stance, the direction
indicator and block, against the people in front of them).

## 1. Install

* Both of you: run `KCDMP-Setup-0.30.7.exe` (the maintainer sends it; it is
  not on GitHub). **Both** computers need it: 0.30.7 refuses every other
  version at the handshake.
* Open the launcher. The bottom bar must say **v 0.30.7** on both computers.

## 2. Connect

Exactly as before (`docs/TEST-0.30.0.md`, section 3): the host **HOST GAME** →
code → **START GAME**, load, **CONNECT**; the partner **JOIN THROUGH STEAM**
(or by address) and waits at the main menu. The first time in a world, the
partner's launcher shows **Bring my character** / **Start fresh**.

## 3. Both: turn the recorder on

In the game console (`~`), on **both** computers, once you are in the world:

```
mp_leash_trace on
```

You should read `WO127-LEASH trace=on`.

## 4. What to watch: fights together

What changed, in plain words:

* **Joining works from any save.** Some host worlds used to stop the partner
  on the loading screen ("Savegame loading failed"). That was our fault, in
  how we rebuilt the world file; it is fixed.
* **Combat mode on the partner's screen.** When one of the host's people is
  fighting near the partner, the partner's game now treats them as a real
  opponent: the direction indicator shows on them, the partner can block, and
  they face the partner in their guard, change guard and swing. The host's
  world still decides every hit, every wound and every death.
* **Nobody is hit while dead or waking up.** From the moment a player goes
  down until five seconds after they wake up, nothing that happens in the
  host's world hurts them.
* **Only real hits count.** Bleeding in the host's world no longer drips onto
  the partner (the last session had a second death from it). If a real hit
  should make the partner bleed, the partner's own game does that.
* **Each hit lands once.** (Every hit used to arrive twice on the partner's
  side, one of them doing nothing.)
* **When the partner dies, only the partner leaves the fight.** The people
  they were fighting drop them and go on with the host. (Before, the host's
  fight was reset too: the guard "vanished".)
* **The host keeps their own fight.** When the partner hits someone who is
  already fighting the host, that person stays on the host.

Still not working, do not report these as new:

* How hard people hit the partner compared with the partner's own armour:
  still not measured.
* Blocking **by the people** (not you) may look just like their guard,
  especially with clubs.
* Your own swings showing on the other player's figure: still unproven.
* Animals are separate in each game.

**What to try (away from towns, never townsfolk or town guards):**

1. **The join works from any host save.** Host: load an old save and a new one
   in turn, and one early in the story. Partner: join each time; you always
   reach the world.
2. **Combat mode for both of you.** Fight bandits together, weapons out.
   Partner: the direction indicator on the one in front of you, and your block
   works. Host: the same on your screen for the one you fight.
3. **They swing on the partner's screen.** Partner: the one facing you raises
   their weapon and swings, and changes guard when they do in the host's world.
4. **The fight includes both of you.** The partner hits a bandit first, then
   the host joins in. Host: it turns to you, and you get combat mode. Partner:
   it keeps fighting you too.
5. **No hits after waking up.** Partner: die in a fight. After waking, no
   damage for the first seconds, and none from the fight you left.
6. **No second death from bleeding.** Partner: get cut, then get away. Your
   health drops only from your own bleeding, never twice.
7. **Nobody vanishes when the partner dies.** Host: the bandit you were both
   fighting keeps fighting you and stays where it was.
8. **Fair damage.** Similar armour, the same enemy, one hit each: note how
   much health each of you lost.
9. **Your swings on each other.** Swing at each other once (friendly fire is
   on): each of you sees the other's figure swing.

Note the time of anything that does not match.

## 5. Still to check from before

* Staying together (0.30.2): the warning at 600 m, the pull at 650 m, only the
  host fast-travels.
* One world (0.30.5): nobody appears twice; bodies lie in one place; the
  partner can't loot yet.

## 6. Play normally

Ride, go into towns and houses, trade, talk. Please do **not** attack
townsfolk.

**Don't press F11 or F12** outside a dice match, and don't type any
`mp_quest_…` command. In 0.30.7 the old quest catch-up behind those keys can
still jump the story forward in the host's world, and that goes into the
host's saves. The next installer switches it off in a shared world.

When you finish: `mp_leash_trace off` on both (or just quit; the file is
kept).

## 7. What to send afterwards (both of you, before starting the game again)

* The launcher's **REPORT BUG** button makes one zip with the logs and the
  recorder files. Or collect by hand:
  * `kcd.log` from the Modding Tools folder (the next launch overwrites it);
  * `kcdmp-native.log` from the same folder;
  * `agent.log`, and the `leash` folder, beside `KcdMpClient.exe`;
  * the launcher's `app*.log`;
  * the host: the relay's `relay*.log`.
* The times of anything odd, and what you saw.
* **Never send save files**, and not the `KCDMP\henry` folder either.
