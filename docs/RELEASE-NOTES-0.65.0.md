# Kingdom Come: Together 0.65.0 beta

**Official repository: the project's own GitHub repository, KingdomCome-Together** — only builds made from it are this project's
releases; other repositories may carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or PLAION. Kingdom Come: Deliverance II, its assets and its
content belong to Warhorse Studios and PLAION; this project's copyright covers only its own code.

The installer is `KingdomComeTogether-Setup-0.65.0-<build id>.exe`. What to try together is in `docs/TESTER-SCRIPT-0.65.0.md`; what
we know is not right yet is in `docs/KNOWN-ISSUES.md`; the evidence is `docs/WO-218-findings.md` (and the findings before it).

**Both players must install the same build.** Builds are told apart by their **build id** (the launcher's About shows it, and so does
the installer's name); a player on a different build is told so at the connection.

## This build: the joiner keeps up

- **No more falling behind in a crowd.** In 0.64 the joiner's game handed its own requests to the mod one at a time, one each frame;
  in a packed tavern or a brawl it fell seconds — on our test PC up to a minute — behind the host's: people standing up late, a
  fight's falls late, work shown late. Now everything waiting goes in one go. On our test bench, in a packed tavern and in a brawl,
  the host's people's movements and the fight reached the joiner within a tenth of a second 99 % of the time, and what they do (sitting
  down, a job) usually within a quarter of a second — at worst about one and a half.
- **Doors are the host's.** The joiner's game no longer locks a door by itself, and a door a villager opens on the host's screen
  opens on the joiner's (each door near the players is checked once a second).
- **People keep at their work.** A villager on the joiner's screen no longer drops the work the host's villager is doing for a
  moment of their own (a stretch, a serving gesture, an idle step) as often.
- **Less of the host's frame for the bug report.** The bug report's check of the story's state now takes at most a third of a
  millisecond of a frame (it took 1.4–1.6 ms on the host). The rest of the mod's own time on the host is not smaller yet (see Known).
- **The bug report says first whether the joiner kept up**: its first section, "The queue", says in one line whether the partner's
  world reached your game on time and how late at worst.

## Since 0.63.9: what you will see

The builds between 0.63.9 and this one were test builds. Everything below is **on** by default unless it says otherwise.

### NPCs walking, working, sitting and dressed

- **People wear the host's clothes.** On the joiner's screen each of the host's villagers wears what they wear on the host's
  (armour, clothes, weapons, slot for slot), not their own outfit from the joiner's game.
- **What people are doing shows.** Most of what villagers do (working, eating, sleeping) shows on the joiner's screen within a
  second. A job that cannot be shown gets a plain idle instead of a bind pose.
- **Seated guests** jump between their seat and another spot far less often: the seat alone holds a seated guest.
- **Walking people follow steadily**, turning where the host's person turns; bodies no longer flicker between standing and walking.
- **The game's own walking is opt-in.** `mp_route on` in both consoles walks the host's people along the host's own routes. On our
  bench it stays closer to the host but pulls people back more often, and it crashed the joiner's game once: it ships **off**. Try
  it for a while and tell us which looks better (`mp_route off` to go back).
- **Only the host's people.** No one on the joiner's side acts on their own any more: people the joiner's game would add by itself
  are hidden, and road travellers the host's game keeps parked are not shown to the joiner.

### Fights

- **Smoother fights near you.** Enemies fighting within 15 m reach the joiner's screen many times a second.
- **Swings show.** The attacks of the host's people play on the joiner's screen, also in fights neither player is part of.
- **Weapons in hand.** An enemy draws on the joiner's screen when they draw on the host's.
- **Deaths count on both screens.** A person the host kills falls with their death animation and lies on the ground on the
  joiner's screen too.
- **Knockdowns.** A person knocked down falls on the joiner's screen within a fraction of a second of the host's, stays down while
  the host's lies, and gets up with them.

### Loot

- **One body, one list.** When both players have the same body's loot open, an item one of them takes leaves the other's open
  screen without closing and reopening it. A worn item taken off a body comes off it on the other screen too.
- **Nothing doubled, nothing lost.** If both take the same item at the same moment, exactly one gets it; the other sees "Already
  taken". The joiner's take goes through the host's game; with no answer in 5 seconds it is cancelled and the item stays on the body.

### Carts and riders

- **Carts follow the host's.** The host's carts and caravans drive on the joiner's screen where they drive on the host's, and the
  people riding on them sit on them. The horses in front of a cart may be missing on the joiner's screen.

### Conversations and trades

- **The joiner's conversations work again** (they hung after the first line in 0.64.3), and a trade's shop screen opens for the joiner.
- **Late lines keep their voice**, and the first line after a choice is always voiced in full.

### The world

- **The dead lie where they fell**, on both screens. What a road encounter leaves (its dead, its cart, its campfire) is made on the
  joiner's side too; our bench could not start a road encounter, so tell us what you see.

### The connection

- **Joins no longer freeze the people around** while the world crosses the link.
- **Newest first.** On a slow link a person's newest state replaces an older one still waiting; a hit, a death or a conversation line
  is never dropped.
- **Each player's figure moves closer to real time** for the other, and a player turning on the spot shows.
- **"Your connection to the host is struggling: NPCs may look jumpy"** tells the joiner when the link has been poor for ten seconds.

## Fixed

- **The joiner falling behind in crowds and fights** (above).
- **Held in a fight by someone who is not there:** the joiner's game no longer gets the host's parked road travellers, and a fight
  held by nobody ends by itself.
- **People frozen for minutes** in a pose the host's person had left (reading, lying, sitting, on guard): released within a second.
- **The host's kill not dying for the joiner**, and dead bodies standing upright.
- **Animations overflowing** on people (a body flicking between standing and walking until it jerked or bind-posed).

## Known

The short list; the full one is `docs/KNOWN-ISSUES.md`.

- **A person may still flicker between two places** — usually for seconds, now and then for minutes; most often after a fight.
- **Work tied to an object** (a smith at his forge, a cook at her pot) may show the person working without the tool or idle.
- **Doors, chests and loose items:** chests and herb patches are each player's own by design; loose items lying in the world may not be
  the same for both.
- **Stolen marks** do not travel with a looted item yet: this game build offers the mod no way to set an item's owner.
- **Paired moves** do not always show; **grabs** do not happen between the players and the host's people.
- **A short stall when something loads** (a shop screen, a conversation's scene) delays what arrives in that moment.
- **The mod's own work on the host** costs about 1.7 ms of a frame on average, and in about one frame in twenty 9–10 ms.
- **A trade with a busy shopkeeper** may end by itself on the joiner's screen a few seconds after it begins; talk again.
- **The host's memory climbs while the host waits alone:** join within a couple of minutes of the host loading.

## How to report

Press the **period key** (`.`) the moment something looks wrong, then type `mark <what you saw>` in the console (~): the note reaches
the other player's bug report too. After the session **both players** send their bug reports. Every report carries the build id; say
which build you played if you write to us.
