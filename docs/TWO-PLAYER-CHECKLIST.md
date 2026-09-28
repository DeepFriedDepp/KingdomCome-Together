# The next two-player session: the combined checklist

One list for the host and the partner, built up by WO-136 to WO-140 (WO-141
adds its own section and turns the whole thing into `docs/TEST-0.41.7.md`).
Tick what you saw, write the time next to anything odd, and send the logs
listed at the end.

**Markers.** Each item has a one-word marker. When you start an item, open the
console (`~`) and type it, for example `mark_fight`. It writes one line
(`MP-MARK fight`) into your game's log and the agent's log, so we can find the
moment afterwards. If something odd happens and no item fits, type `mark_odd`.
Both of you can type the same marker; that is fine.

## Setup (both of you)

1. **Install** the same build on both machines (the Setup the maintainer sends).
   After installing, run `tools\Verify-Install.ps1` if you have it; it must end
   with "all present". Marker: `mark_setup`.
2. **The host** starts the game from the launcher, loads their save, and clicks
   CONNECT once they can move.
3. **The partner** starts the game from the launcher and **stays at the main
   menu. Don't load a save.** The launcher says so. Click CONNECT at the menu;
   the partner joins the host's world by itself. Marker: `mark_join`.
   * If the partner loads their own save by mistake, both games say so ("You
     loaded your own save…"), in the game and in the launcher. Quit, start
     again, and wait at the main menu (item 140.6 below checks this on purpose).
4. **Both**, in the console: `mp_leash_trace on`.
5. **Around fights** (the fight items below), both: `mp_npc_trace on` before the
   fight and `mp_npc_trace off` after it.

Use a **throwaway copy** of the host's save for the whole session: crimes,
fines, deaths and sleeping are real.

## Most wanted first

Do these four at the start of the session, while everyone is fresh.

### Fights with both of you

Play near a road away from towns, or at the "Find Mutt!" ambush, with enemies
about (bandits, or wolves at night).

1. **Both of you fight the same enemies.** Marker: `mark_fightboth`.
   * Host: enemies attack both of you, not only the host; some go for each.
   * Partner: the partner's hits land and count (the enemy staggers and loses
     health on both screens); an enemy the partner hits turns to the partner.
2. **A knockout.** Knock an enemy out (either of you). Marker: `mark_ko`.
   * Both screens: it goes down and stays down.
3. **The host goes down mid-fight** (throwaway save). Marker: `mark_hostdown`.
   * Partner: the fight goes on, the enemies keep attacking the partner; watch
     for enemies that stand still or pose oddly (a "T-pose").
   * Host: the host wakes nearby afterwards, not at a grave far away.
4. **The partner fights a guard** after hitting a villager in front of one.
   Marker: `mark_guardfight`.
   * Both screens: the guard attacks the partner; the host sees the same fight.

### Horses

5. **The partner rides the host's horse.** Marker: `mark_horse`.
   * Partner: the prompt says **"Mount"** (not "Mount and steal"); the horse
     can be ridden and does not vanish or freak out.
   * Host: nobody calls it a theft; after the partner gets off, the horse stays
     where it was left, on both screens.
6. **A townsperson's horse** is still a theft. Marker: `mark_horsetheft`.
   * Partner: "Mount and steal"; a guard who sees it reacts.

### Talking

7. **The partner talks to someone.** In "Find Mutt!": the herbwoman; also an
   ordinary villager. Marker: `mark_talk`.
   * Partner: the conversation plays normally.
   * Host: the person stands still meanwhile; the host can't start a second
     conversation with them. After it, both journals show the step.

### "Find Mutt!"

8. **Play several steps together** — some by the host (examine the dead deer at
   the ambush site), some by the partner (examine the bandit's body, pick up a
   quest item). Marker: `mark_mutt` at the start, `mark_muttstep` at each step.
   * The one who did the step: the journal updates at once.
   * The other: the same journal update within a few seconds, without doing
     anything.
9. **Dead stays dead.** At the ambush site the bandit lies dead on both screens.
   Marker: `mark_dead`.

## WO-136 — world presence

10. **Joining with enemies nearby.** The partner joins while the host stands
    near NPCs or animals. Marker: `mark_join`.
    * Partner: the loading screen ends normally — no red "Loading screen
      timeouted", no long wait after the world appears; note the load time.
11. **Animals.** Wolves (or dogs, boars) attack the host. Marker: `mark_animals`.
    * Partner: sees them too, running with the host's; say whether they look
      frozen while biting (their bite animation is not shown yet).
12. **Outfits.** Each of you changes clothes. Marker: `mark_outfit`.
    * The other screen shows the same clothes, no guard armour; say what is
      missing, if anything.
13. **Torch at night.** Each of you lights a torch and puts it away. Marker:
    `mark_torch`. The other screen: in the hand, then gone.
14. **Crouching.** Each of you crouches with the key. Marker: `mark_crouch`.
    The other screen shows it.
15. **Looting a body.** Both of you loot the same body and a few others.
    Marker: `mark_loot`. "Someone already took that" only when the other one
    really took it first.

Lines worth a look: `WO136-HOLD`, `MP-W136`, `MP-WO136-STATS`, `WO136-STANDIN`,
`WO136-RIDE`, `WO136-TARGET`, `WO136-HANDOVER`, `WO136-FORCED`, `WO136-TORCH`,
`WO136-CROUCH`, `WO136-OUTFIT`, `WO134-BODY not-a-put`.

## WO-137 — questing together

Use a save where "Find Mutt!" is not yet started or just started (the host's
world decides), with the main quest active. Items 7–9 above are this section's
most-wanted part.

16. **Rewards per player.** A step gives money or an item. Marker: `mark_reward`.
    * Each of you gets your own, on your own character; note who got what.
17. **No step twice.** Both of you examine the same thing, one after the other.
    Marker: `mark_nodouble`. The journal updates once; nothing is given twice.
18. **A main-quest step** together (either of you). Marker: `mark_mainquest`.
    Both journals move.
19. **The off switch (host).** Host: `mp_quest_sync off`, make a step; then
    `mp_quest_sync on`. Marker: `mark_questoff`.
    * Partner: the journal does not follow while it is off, and catches up at
      once when it is back on.

Lines worth a look: `MP-W137`, `MP-WO137-STATS`, `WO137-TALK`, `WO137-HOLD`,
`WO137-DEAD`, `WO137-CHANGE`, `WO137-APPLY`, `WO137-TIMESET`,
`MP-PAUSE … a-corpse-is-never-paused`.

## WO-138 — nobody's menu stops the other's world

In a village with people walking about. In a session a menu no longer stops
your own world either: your Henry stands there while you read, so do this
somewhere quiet.

20. **The host opens the inventory (Tab) for about 30 s.** Marker:
    `mark_inventory`.
    * Partner: the people around **stay visible and keep walking**.
    * Host: they keep walking behind the inventory screen too.
21. **The host opens the map, then the journal.** Marker: `mark_map`. Same check.
22. **The host opens the ESC menu for about 30 s**, then saves from it once.
    Marker: `mark_esc`. Same check; the save works as before.
23. **The partner opens their inventory, then the ESC menu.** Marker:
    `mark_partnermenu`.
    * Host: nothing changes.
    * Partner: after closing, the people are where the host sees them at once.
24. **The host talks to someone** (a conversation with choices) for a while.
    Marker: `mark_dialogue`.
    * Partner: the world does not freeze: people walk, the partner can move.
      Only the time of day stands still until the conversation ends.
25. **A cutscene**, if one comes up. Marker: `mark_cutscene`. The partner's
    world keeps running; nobody vanishes.
26. **Alone** (before the partner joins), the host opens the ESC menu.
    Marker: `mark_solo`. The game pauses as it always did.

Lines worth a look — host: `MP-WO138`, `WO138-LEVERS`, `WO138-PAUSE`,
`WO138-WORLD`, `[pause] local state`; partner: `MP-WO138 HOLD`, `WO138-HOLD`,
`NPC-SYNC release`. `mp_w138_status` prints the state.

## WO-139 — crime and guards

In a village with a guard walking about, in daylight. Keep some money on the
partner (the fine).

27. **The partner steals in view of a guard** (hold the key on "Steal").
    Marker: `mark_steal`.
    * Partner: "A guard saw that."; the guard comes, calls out; the "Reply"
      prompt appears.
    * Host: the guard stands still next to the partner's figure meanwhile.
28. **The partner pays the fine** (Reply, surrender, "I'll pay the fine").
    Marker: `mark_fine`.
    * Both screens: the money leaves the partner, the item is taken, the guard
      walks on. The time of day must not jump.
29. **Only townsfolk see it** ("Someone saw that. The guards will hear of it.").
    Marker: `mark_townsfolk`. About 20 s later the next guard who sees the
    partner up close stops them.
30. **Jail / the stocks don't move the clock** ("I accept the punishment").
    Marker: `mark_jail`.
    * Both screens: the time of day must not jump (before, the stocks skipped
      2 to 10 hours). Say what the partner saw.
31. **Execution respawns outside the town** (only if it comes to that).
    Marker: `mark_execution`. The partner wakes outside the town, near the
    host; the crime is gone.
32. **No robbing each other.** Each of you tries to loot or pickpocket the
    other's figure. Marker: `mark_norob`. Refused with "You can't steal from
    each other in co-op."; a friendly-fire hit between you is no crime.
33. **The host's own crimes work as before.** Marker: `mark_hostcrime`. (In a
    session the punishment does not move the clock for the host either.)

Lines worth a look — host: `MP-W139 host:`, `WO139-JUDGE`, `WO139-PURSUE`,
`WO139-PLACE`, `WO137-HOLD … w139-stop`, `WO139-ROB`; partner: `MP-W139
joiner:`, `WO139-TRESPASS`, `WO139-CRIME`, `WO139-STOP start|planted|result|end`,
`WO139-HORSE`, `WO139-SKIPTIME`; both: `MP-WO139-STATS`, `mp_crime_status`.

## WO-140 — sleeping together, and the "own world" trap

One clock: the host's. You sleep together, or not at all. Stand somewhere
safe (not in someone's house: guards react to trespass, and a sleep there can
be woken by the owner). Note the time of day on both screens before and after
each sleep.

34. **The host asks to sleep; the partner says yes.** The host picks "Sleep"
    at a bed. Marker: `mark_sleep` (both).
    * Host: nothing happens yet but "Waiting for other players..." — the host
      does **not** lie down.
    * Partner: "<host> wants to sleep. Sleep too?" — press **F11** (yes).
    * Host: now lies down and chooses how long (the game's own clock wheel).
    * Partner: when the host confirms the length, the partner's screen shows
      **the same sleep screen** — the black screen with the clock wheel,
      "Sleeping", the same hours — wherever the partner stands, no bed needed.
    * Both: you wake at about the same moment; the time of day is the same on
      both screens; each of you is rested (the stamina bar / tiredness and
      health recover as after a sleep).
35. **The partner asks; the host says yes.** Same as 34 the other way round:
    the partner sleeps in a bed, the host gets the question and the sleep
    screen. Marker: `mark_sleepjoiner`.
    * Both: the same time of day afterwards. If the partner's bed is one that
      saves ("Sleep and save"), the host's game says "Game saved" after the
      sleep.
36. **Saying no.** One asks, the other presses **F12**. Marker: `mark_sleepno`.
    * The one who asked: never lies down; one line, "Other players are not
      ready to sleep yet!"; no time passes, nothing changes.
37. **No answer.** One asks, the other does nothing for 30 s. Marker:
    `mark_sleeptimeout`. Same as 36 after 30 s (the question shows the seconds
    left).
38. **Waiting instead of sleeping** (the Wait key, or "Wait" on a bench), with
    the other saying yes. Marker: `mark_wait`. The same as 34: the other one's
    screen shows the game's own waiting screen for the same hours.
39. **The clock stays one.** After any sleep, travel or quest skip, compare the
    time of day on both screens. Marker: `mark_clock`. They must match (within a
    minute or two). Before this, the partner's own sleep moved only the
    partner's clock, and it stayed ahead until the next join.
40. **The own-world trap, on purpose.** The partner quits, starts again, and this
    time **loads their own save**, then clicks CONNECT. Marker: `mark_ownworld`.
    * Partner: a big message in the game, repeated: "You loaded your own save.
      To play in your host's world, quit the game, start it again and wait at
      the main menu."; the launcher shows the same in a window. The host's
      figure may still walk about ("You and your host are in separate worlds
      right now"), but none of the host's people, fights, loot or quests
      appear in the partner's world, and nobody pulls the partner back.
    * Then the partner quits, starts again, waits at the main menu, and joins
      normally.

Lines worth a look — both: `MP-W140`, `MP-WO140-STATS`, `WO140-HOLD`,
`WO140-PROMPT`, `WO140-ANSWER`, `WO140-GO`, `WO140-DROP`, `WO140-START`,
`WO140-STATE`, `WO140-PULL`; the trap: `MP-JOIN joiner: connected from its own
world -- NOT joined (separate)`, `WO140-SEPARATE`, `MP-LEASH host: joiner … is
in its own world`. `mp_sleep_status` prints the state; `mp_sleep_vote off`
switches the vote off (then each of you sleeps alone, and the partner's clock
still follows the host's).

## Logs to send afterwards

Both machines, before starting the game again (the game keeps only one old
log): `kcd.log` and `kcdmp-native.mirror.log` (the Modding Tools folder), the
agent log, and the host's relay log.
