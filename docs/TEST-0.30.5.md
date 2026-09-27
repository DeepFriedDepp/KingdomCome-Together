# Testing 0.30.5 (both players)

One page for the host and the partner. About an hour. Play normally apart
from the few things in section 4. If something goes wrong, note the time and
carry on or stop, then send what section 7 lists.

0.30.5 is about fighting together: one world (the host's), hits and bodies in
one place, the partner seen by the host's people, and no more figures sinking
into the ground. Section 4 is the part to watch.

## 1. Install

* Both of you: run `KCDMP-Setup-0.30.5.exe` (the maintainer sends it; it is
  not on GitHub). **Both** computers need it: 0.30.5 refuses every other
  version at the handshake.
* Open the launcher. The bottom bar must say **v 0.30.5** on both computers.

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

You should read `WO127-LEASH trace=on`. The partner's file matters most this
time: it shows whether any person on the partner's side ran on their own.

## 4. What to watch: fighting together

What changed, in plain words:

* **One world.** On the partner's screen you only see the host's people.
  Someone the host's world has somewhere else is not shown to the partner,
  instead of standing around as a second copy. The people near either of you
  stand where the host's world has them.
* **Your hits count in the host's world**, and only on a person who is really
  there: a hit on anyone the host's world has elsewhere does nothing.
* **Deaths and bodies are the host's.** A person dies when they die in the
  host's world, and the body lies where they fell there. On the partner's side
  nobody dies from their own blows alone.
* **The partner cannot loot bodies or pickpocket yet.** The game says: "Only
  the host can loot bodies in co-op for now." Everything is still on the body
  for the host.
* **Ambushes on the road** now show up for the partner (the robbers may look a
  little different from the host's).
* **The host's world sees the partner.** People always notice the partner's
  figure now, not only right after a hit, and it counts as on the host's side
  in a fight.
* **People swing on the partner's screen** when they attack in the host's
  world (they only stood there before).
* **A death ends the fight.** After you die and wake up, the people you were
  fighting leave you alone (a crime stays a crime).
* **No more sinking into the ground.** Someone knocked flat on the partner's
  screen stands back up where the host's world has them standing.
* **Leash:** the countdown only stops once you are back under 630 m (it used
  to start and stop over and over at 650 m), and chatter near you while riding
  no longer pauses it.

Still not working, do not report these as new:

* Robbers from a road ambush (the look-alikes) may stand with their arms out
  instead of swinging.
* Your own swings showing on the other player's figure: still unproven.
* How hard people hit the partner compared with the partner's own armour:
  not measured yet.
* A person the partner hits turns to the partner's figure but may not fight
  back on the host's side.
* Animals are separate in each game.

**What to try (away from towns, never townsfolk or town guards):**

1. **One world.** The partner walks through a village: the villagers you both
   see are in the same places; nobody appears twice.
2. **They come for the partner.** Fight bandits or wild animals together; the
   host stays back a little. Partner: do they attack you? Can you see every
   one that hits you?
3. **They swing.** Partner: when someone attacks, do you see the swing on
   your screen?
4. **Nobody sinks into the ground.** Hit someone hard: they never sink into
   the ground; they only lie down if they lie down on the host's screen too.
5. **One body, one place.** Kill one: the body lies in the same place on both
   screens. Partner: try to loot it (you should get the message above); host:
   loot it normally.
6. **Fair damage.** Both of you in similar armour take a hit from the same
   enemy: note how much health each of you lost.
7. **A death ends the fight.** Get knocked down or die in a fight: after
   waking up, the fight is over.

Note the time of anything that does not match.

## 5. Still to check from before

* Your **swings** show on the other player's figure (friendly fire is on):
  swing at each other once.
* Staying together (0.30.2): the warning at 600 m, the pull at 650 m, only the
  host fast-travels.

## 6. Play normally

Ride, go into towns and houses, trade, talk. Please do **not** attack
townsfolk.

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
