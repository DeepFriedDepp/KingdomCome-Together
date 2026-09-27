# Testing 0.30.2 (both players)

One page for the host and the partner. About an hour. Play normally apart
from the few things in section 4. If something goes wrong, note the time and
carry on or stop, then send what section 7 lists.

0.30.2 keeps the two of you together: a warning at 600 m, the partner brought
back at 650 m, and only the host fast-travels. Section 4 is the part to watch.

## 1. Install

* Both of you: run `KCDMP-Setup-0.30.2.exe` (the maintainer sends it; it is
  not on GitHub). **Both** computers need it: 0.30.0 and 0.30.2 refuse each
  other.
* Open the launcher. The bottom bar must say **v 0.30.2** on both computers.

## 2. Connect

Exactly as for 0.30.0 (`docs/TEST-0.30.0.md`, section 3): the host **HOST
GAME** → code → **START GAME**, load, **CONNECT**; the partner **JOIN THROUGH
STEAM** (or by address) and waits at the main menu. The first time in a
world, the partner's launcher shows **Bring my character** / **Start fresh**.

## 3. Both: turn the recorder on

In the game console (`~`), on **both** computers, once you are in the world:

```
mp_leash_trace on
```

You should read `WO127-LEASH trace=on`. Last time only the host's file came
back; this time the partner's matters as much.

## 4. What to watch: staying together

* **Ride away.** Partner: ride away from the host and keep going.
  * At about **600 m** you see "You're getting far from your host. Head back,
    or you'll be brought back." The host sees "<partner> is getting far
    away."
  * Past **650 m**: "Bringing you back to your host in 10..." counts down.
    Turn back before it ends: it stops ("You're back near your host.").
  * Go out again and let it run out: you are put next to the host, off your
    horse ("You were brought back to your host."). Your horse stays where it
    was; whistle for it.
* **No pull at a bad moment.** While the countdown runs, the partner opens the
  map or the inventory, or talks to someone: the countdown waits and carries
  on afterwards. Nobody is pulled while dead, loading or talking.
* **Host fast-travels** somewhere from the map. When the host arrives, the
  partner is brought along within a few seconds ("Your host fast-travelled."),
  and the time of day matches on both screens.
* **Partner tries to fast travel** from the map: refused, "Only the host can
  fast travel in co-op."
* **Die near each other** (a fall is fine; no townsfolk): you wake up near
  the other player, not at the same spot every time.

Note the time of anything that does not match.

## 5. Still to check from 0.30.0

* Your **swings** show on the other player's figure (friendly fire is on):
  swing at each other and once at an NPC away from town.
* The other player's legs move when walking, running and sprinting.
* A partner's death leaves a grave with their things.

## 5b. Fights together (0.30.5, not in 0.30.2)

Not in 0.30.2: from 0.30.5 (`docs/TEST-0.30.5.md`). What changed, in plain
words:

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
* **A death ends the fight.** After you die and wake up, the people you were
  fighting leave you alone (a crime stays a crime).
* **No more sinking into the ground.** Someone knocked flat on the partner's
  screen stands back up where the host's world has them standing.

Still not working, do not report these as new:

* On the partner's screen, people's swings should now show; robbers from a
  road ambush (the look-alikes) may still stand with their arms out instead.
* Your own swings showing on the other player's figure: still unproven.
* How hard people hit the partner compared with the partner's own armour:
  not measured yet.
* Animals are separate in each game.

**What to try (away from towns, never townsfolk or town guards):**

1. The partner walks through a village: the villagers you both see are in the
   same places; nobody appears twice.
2. Fight bandits or wild animals together. Partner: note whether they attack
   you, whether you can see every one that hits you, and whether they stand
   up again after a hard blow.
3. Kill one: the body lies in the same place on both screens. Partner: try to
   loot it (you should get the message above); host: loot it normally.
4. Get knocked down or die in a fight: after waking up, the fight is over.

## 6. Play normally

Ride, go into towns and houses, trade, talk. Please do **not** attack
townsfolk. The distances (`mp_leash_warn_m`, `mp_leash_pull_m`) stay as they
are; `mp_leash off` exists but please leave it on for this session.

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
