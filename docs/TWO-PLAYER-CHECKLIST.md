# The next two-player session: the combined checklist

One list for the host and the partner, built up by WO-136 to WO-140. Each WO
adds its own section; the installer (WO-140) carries all of them. Tick what you
saw, write the time next to anything odd, and send the logs listed at the end.

Before you start, both of you, in the console (`~`): `mp_leash_trace on`.

## WO-136 — world presence

Play together near a road with wild animals around (wolves at night are
ideal), away from towns.

1. **Joining with enemies nearby.** The partner joins while the host stands
   near NPCs or animals. The loading screen should end normally — no red
   "Loading screen timeouted" text, no long wait after the world appears.
   Partner: note how long the load took.
2. **Animals.** When wolves (or dogs, boars) attack the host, the partner sees
   them too, running with the host's. The partner's screen does not show their
   bite animation yet — say whether they look frozen while biting.
3. **Riding the host's horse.** The partner mounts one of the host's horses.
   It must not vanish or freak out; it can be ridden; after getting off it stays
   where it was left, on both screens. **Tell us what the mount prompt said**
   ("Mount" or "Mount and steal") and whether anyone reacted to a theft.
4. **A knockout in a fight.** Knock an enemy out in a fight (host or partner).
   It goes down on both screens and stays down.
5. **The partner fights too.** In a fight, the partner hits an enemy that was
   on the host: it should turn to the partner. With two or more enemies, some
   go for each of you.
6. **The host goes down mid-fight.** Let the host die in a fight while the
   partner is beside the enemies (a throwaway save!). The fight goes on: the
   enemies keep attacking the partner. Partner: watch whether they stand still
   or pose oddly (a "T-pose").
7. **Outfits.** Each of you changes clothes; the other's screen shows the same
   clothes, no guard armour. If something is missing, say what.
8. **Torch at night.** Each of you lights a torch at night and puts it away
   again; the other's screen shows it in the hand, then gone.
9. **Crouching.** Each of you crouches with the key; the other's screen shows it.
10. **Looting a body.** Both of you loot the same body and a few others. "Someone
    already took that" must only appear when the other one really took it first.

Logs to send afterwards (both machines, before relaunching the game):
`kcd.log`, `kcdmp-native.mirror.log` (Modding Tools folder), the agent log, the
host's relay log. Lines worth a look: `WO136-HOLD`, `MP-W136`, `MP-WO136-STATS`,
`WO136-STANDIN`, `WO136-RIDE`, `WO136-TARGET`, `WO136-HANDOVER`,
`WO136-FORCED`, `WO136-TORCH`, `WO136-CROUCH`, `WO136-OUTFIT`,
`WO134-BODY not-a-put`.

## WO-137 — questing together

Use a save where both of you have **"Find Mutt!"** not yet started or just
started (the host's world decides), and the main quest active. A throwaway
copy of the host's save is best. Do the WO-136 list above in the same session.

1. **The host advances, the partner's journal follows.** The host makes a step
   (examines the dead deer at the ambush site). The partner's journal shows
   the same update within a few seconds, without doing anything.
2. **The partner advances, the host's journal follows.** The partner makes the
   next step (examines the bandit's body, or picks up a quest item). The host's
   journal updates; the partner's too.
3. **The partner talks, it counts for both.** The partner talks to the quest's
   person (in Find Mutt: the herbwoman). The conversation plays on the
   partner's screen; the person stands still in the host's world meanwhile
   (the host can't start a second conversation with them). After it, both
   journals show the step. Also try an ordinary villager.
4. **Rewards per player.** When a step gives money or an item, each of you gets
   your own on your own character. Note who got what.
5. **No step twice.** Both of you examine the same thing, one after the other.
   The journal updates once; nothing is given twice.
6. **A side quest: "Find Mutt!"** — play several of its steps together, some
   by the host, some by the partner.
7. **A main-quest step** — make one step of the main quest together (either of
   you); both journals move.
8. **Dead stays dead.** At the Find Mutt ambush site the bandit lies dead on
   both screens (it stood up on the partner's screen before). A person the
   story kills lies dead on both.
9. **The off switch (host).** Host: console `mp_quest_sync off`, make a step —
   the partner's journal does not follow. Then `mp_quest_sync on`: the
   partner's journal catches up at once.

Say for each whether it worked, and the time of anything odd.

Lines worth a look (both machines): `MP-W137`, `MP-WO137-STATS`,
`WO137-TALK`, `WO137-HOLD`, `WO137-DEAD`, `WO137-CHANGE`, `WO137-APPLY`,
`WO137-TIMESET`, `MP-PAUSE … a-corpse-is-never-paused`.

## WO-138 — nobody's menu stops the other's world

Play together in a village, with people walking about near both of you. In a
session, opening a menu no longer stops your own world either: your Henry
stands there while you read, as in an online game, so do this somewhere quiet.

1. **The host opens the inventory (Tab) for about 30 s.** The partner watches
   the people around them: they **stay visible and keep walking**. Before this
   fix they vanished until the host closed the inventory. The host sees them
   keep walking too, behind the inventory screen.
2. **The host opens the map, then the journal**, same check: nobody vanishes,
   people keep moving.
3. **The host opens the ESC menu for about 30 s.** Same check on both screens.
   Then the host saves from the ESC menu once: the save works as before.
4. **The partner opens their inventory, then the ESC menu.** Nothing changes
   for the host. When the partner closes it, the people around them are where
   the host sees them at once (no slow slide into place).
5. **The host talks to someone** (a real conversation with choices) for a
   while. The partner's world does not freeze: people walk, the partner can
   move. Only the time of day stands still until the conversation ends.
6. **A cutscene** (a rendered story video), if one comes up. The partner's
   world keeps running; nobody vanishes.
7. **Alone (no partner joined yet), the host opens the ESC menu:** the game
   pauses as it always did.

Say for each whether it worked, and the time of anything odd (a menu that
behaves strangely with the world running behind it, people frozen or
vanishing, a stutter when a menu opens).

Lines worth a look:
* host: `MP-WO138`, `WO138-LEVERS`, `WO138-PAUSE`, `WO138-WORLD`,
  `[pause] local state`;
* partner: `MP-WO138 HOLD`, `WO138-HOLD`, `NPC-SYNC release`.

`mp_w138_status` on either machine prints the state.

## WO-139 — crime and guards

Play together in a village with a guard walking about (Zelejov, or any town
with guards in the street), in daylight. **Use a throwaway copy of the host's
save:** crimes, fines and punishments are real. Keep some money on the partner
(the fine).

1. **The partner steals in view of a guard.** The partner takes an item that
   is not theirs ("Steal", hold the key) while a guard can see them. The
   partner gets "A guard saw that." Within seconds the guard comes to the
   partner and calls out; the partner gets the "Reply" prompt. On the host's
   screen the guard stands still next to the partner's figure meanwhile.
2. **The partner pays the fine.** The partner presses Reply, surrenders, and
   picks "I'll pay the fine" in the dialogue. The money leaves the partner, the
   stolen item is taken, and the guard walks on — on both screens. Note the
   time of day before and after: it must not jump.
3. **Stealing where only townsfolk see it.** Steal again with no guard around
   but a villager watching ("Someone saw that. The guards will hear of it.").
   About 20 s later the next guard who sees the partner up close stops them.
4. **Jail / the stocks don't move the clock.** Commit a crime, let a guard stop
   the partner, and choose "I accept the punishment" (with no money, the only
   choice). The partner is taken to the punishment; the time of day on both
   screens must not jump (before this, the stocks skipped 2 to 10 hours).
   Say what the partner saw (stocks, a beating, branding) and whether anything
   played oddly.
5. **Fighting the guard, and being arrested after.** The partner hits a
   villager in front of a guard. The guard attacks the partner (on the host's
   screen too). Let the partner be knocked out, or run far away and come back
   a few minutes later: the next guard arrests the partner instead of fighting
   (the crime still stands); pay or accept the punishment.
6. **Execution respawns outside the town** (only if the partner gets there —
   a murder with a branding already given). The partner wakes outside the
   town, not far from the host; the crime is gone.
7. **No robbing each other.** Each of you tries to loot or pickpocket the
   other's figure (knocked out, or from behind): refused with "You can't steal
   from each other in co-op." Also: a friendly-fire hit between you is no crime
   (nobody reacts).
8. **The host's horses.** The partner mounts the host's own horse: the prompt
   says "Mount" (not "Mount and steal") and nobody calls it a theft. A
   townsperson's horse is still a theft.
9. **The host's own crimes work as before.** The host steals in view of a guard
   and deals with it as usual (in a session the punishment does not move the
   clock for the host either).

Say for each whether it worked, and the time of anything odd.

Lines worth a look:
* host: `MP-W139 host:`, `WO139-JUDGE`, `WO139-PURSUE`, `WO139-PLACE`,
  `WO137-HOLD … w139-stop`, `WO139-ROB`;
* partner: `MP-W139 joiner:`, `WO139-TRESPASS`, `WO139-CRIME`,
  `WO139-STOP start|planted|result|end`, `WO139-HORSE`, `WO139-SKIPTIME`;
* both: `MP-WO139-STATS`; `mp_crime_status` prints the state.
