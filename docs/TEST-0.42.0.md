<p align="center"><img src="branding/banner-1280.jpg" alt="Kingdom Come: Together" width="640"></p>

# Testing 0.42.0 (both players)

**Kingdom Come: Together.** Unofficial. Not affiliated with or endorsed by
Warhorse Studios.

One page for the host and the partner. Plan on an evening: the four "most
wanted" checks first (fights together, horses, talking, "Find Mutt!"), then as
much of the rest as you like. Tick what you saw, write the time next to
anything odd, and send what the last section lists.

0.42.0 carries WO-136 to WO-143. New since 0.41.7 (WO-143): what people hold
and do shows on the other screen — the tool in a villager's hand, a farmer
hoeing, a guest's drink, a dice player's reaction — and so do the other
player's grindstone, reading, alchemy, herbs, lockpicking, digging and
smithing.

## Install

Both of you: run `KingdomComeTogether-Setup-0.42.0.exe` (the maintainer sends
it; it is not on GitHub). It installs over 0.41.7 or 0.40.0. Both computers
need it: 0.42.0 refuses every other version, 0.41.7 included. Afterwards,
`tools\Verify-Install.ps1` (if you have it) must end with "all present".

## What's new in 0.42.0

- **Tools in hands.** A villager carrying a saw, a bucket, a broom or a hoe
  carries it on the partner's screen too, in the same hand.
- **Farmers hoe.** A farmer working a field hoes along the row, bent over the
  hoe, as on the host's screen, and holds it still at the row's end.
- **Tavern life.** Seated guests drink and dice players react on the partner's
  screen, and stay in their seats.
- **The other player's minigames.** At a grindstone the other player's figure
  sits on the seat and grinds; reading, alchemy, picking herbs, picking a lock,
  digging and smithing show as themselves. When the minigame ends, the figure
  gets up.
- **Quieter logs.** Something the game refuses to show is logged three times,
  then once a minute.

> **If something looks wrong, turn it off with…** (the console, `~`; `on`
> switches it back; nothing else changes)
>
> | what looks wrong | type |
> |---|---|
> | a tool in someone's hand (floating, jumping, the wrong one) | `mp_hand_items off` |
> | a farmer's hoeing walk, or a drunk or injured walk | `mp_activity_gaits off` |
> | a guest's drink, a dice reaction, someone's short gesture | `mp_oneshots off` |
> | the other player's figure at a grindstone, a book, an anvil… | `mp_player_minigames off` |
> | heads turning oddly | `mp_idles off` |
> | anyone sitting, lying, leaning or working (0.41.7's part) | `mp_activities off` |
>
> Say which one you switched off, and when (`mark_odd` first).

## Also new since 0.40.0 (in 0.41.7 already)

**Joining**

- **The partner waits at the main menu.** Start the game from the launcher and
  **don't load a save**: click CONNECT at the menu and you join your host's
  world by yourself. Only the host loads a save first.
- **If you load your own save by mistake,** the game and the launcher tell you
  plainly: "You loaded your own save. To play in your host's world, quit the
  game, start it again and wait at the main menu." Until you do, nothing of the
  host's world happens in yours.

**Playing together**

- **What people do shows on both screens.** A villager asleep in bed lies in the
  same bed on the partner's screen, one on a bench sits on it, a woman leaning
  on a wall leans on it, a guard stands at his post — instead of standing next
  to it. Someone who is doing something gets up first when a fight, a
  conversation or a knockout comes.
- **The same for the two of you.** Sit on a bench or lie in a bed and your
  figure does the same on the other screen, with your name above it. Washing
  your face at a water trough shows as a wash at that trough.
- **Wolves bite on both screens.** An animal's attack on the host plays on the
  partner's screen too, instead of a frozen animal.
- **Sleeping together.** When one of you picks "Sleep" at a bed (or waits), the
  other is asked first: "<name> wants to sleep. Sleep too?" — F11 yes, F12 no.
  Until the answer comes, nothing happens but "Waiting for other players...".
  On a yes, both of you see the sleep screen for the same hours, wherever the
  other one stands, and you wake together, rested, at the same time of day. On a
  no (or nothing in 30 s): "Other players are not ready to sleep yet!".
- **One clock for both.** The host's clock is the world's; the partner's can no
  longer run ahead of it.
- **Nobody's menu stops the other's world.** The inventory, the map, the
  journal, the ESC menu, a conversation or a cutscene of one player no longer
  freezes the other's world. (Alone, the game pauses as it always did.)
- **Quests are shared.** Main quests and side quests like "Find Mutt!" live in
  the host's world: when either of you makes a step, both journals update. The
  partner can talk to people. A step counts once, rewards are your own, and
  dead stays dead.
- **Crimes count in the host's world.** If the host's people see the partner
  steal, pick a lock, take a horse or trespass, the host's guards deal with the
  partner — the game's own arrest, fine, punishment or fight. Punishment no
  longer skips hours. You can't rob each other; the host's horses are free to
  ride for the partner.
- **The world is there for both of you.** The host's animals appear for the
  partner; enemies fight both of you (and keep fighting the partner if the host
  goes down); knocked-out enemies go down on both screens; whoever rides a horse
  owns it; clothes, torches and crouching show on the other screen; "Someone
  already took that" only when the other really took it. Joining is calmer.

**Off switches** (the console, `~`): the five above, and `mp_activities off`
(bodies stand where they are, as before 0.41.7), `mp_animal_attacks off`
(bites not animated on the partner's screen), `mp_sleep_vote off` (each of you
sleeps alone), `mp_quest_sync off` (host: quests not shared), `mp_crime_shared
off` (host: crimes not shared). `on` switches each back.

**Still known**

- Work at a bench or a desk that needs a tool (a carpenter debarking, a sawyer,
  a scribe) is not shown: on the partner's screen the person stands at the spot.
- A seated guest's tankard is not in the hand (the drink plays).
- A barmaid serving or wiping a table, or someone busy on the way somewhere,
  may not show that gesture on the partner's screen.
- A cart's horses can walk ahead of the cart on the partner's screen (the cart
  itself is not sent yet).
- At the grindstone the blade is not in the other player's figure's hands.
- Stone throwing and archery are not shown: the figure stands.
- A surrendering enemy does not kneel on the partner's screen yet.
- A sleep in a bed that isn't yours (a stranger's house, a barn) can be woken
  early by the owner or a guard, as in the game.
- If you are already well rested, the game cuts a sleep short (or refuses it),
  also for the one who said yes.
- Waiting (instead of sleeping) together has not been tried by two players yet.

# The checklist

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
    * Partner: sees them too, running with the host's, and **biting** (a lunge
      and a bite; WO-141, item 50).
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

## WO-141 — sitting, sleeping, working, and animals' bites

What people do with things now shows on the other screen: a sleeper lies in the
same bed, a sitter sits on the same bench, a leaner leans on the same wall, a
guard stands at his post. The same for the two of you. Both switches are on by
default (`mp_activities`, `mp_animal_attacks`). Stand where you are allowed
(guards react to trespass in houses).

41. **People in bed.** In the evening, go where people sleep (an inn, a house
    whose door is open to you). Marker: `mark_npcsleep`.
    * Partner: each sleeper lies in the same bed as on the host's screen — not
      standing next to it or inside it.
42. **People sitting, leaning, on guard.** A village in daylight. Marker:
    `mark_npcsit`.
    * Partner: the same people sit on the same benches, lean on the same walls,
      and the guards stand at the same posts as on the host's screen.
43. **People at work.** A woodworker, a smith, someone sweeping, a farmer in a
    field. Marker: `mark_workstation` (at a workbench), `mark_garden` (a field
    or a garden).
    * Partner: say what each one does. Expected for now: work at a bench that
      needs a tool in the hand (the woodworker's) shows them standing at the
      spot; the farmer hoes with the hoe (0.42.0, item 52).
44. **A fight next to someone sitting.** A fight starts near someone who sits
    or lies (bandits at a camp, or the host knocks someone out). Marker:
    `mark_npcfight`.
    * Partner: the sitter gets up first, then fights or runs, and sits down
      again afterwards.
45. **The host sits on a bench.** Marker: `mark_bench`.
    * Partner: the host's figure sits on the same bench, the name above its
      head (not above an empty spot); when the host gets up, it gets up.
46. **The partner sits, then lies down** (a bed you may use, a bench).
    Markers: `mark_bench`, then `mark_bed`.
    * Host: the partner's figure sits on the same bench, then lies in the same
      bed, the name above it.
47. **Getting up.** Each of you gets up and walks away. Marker: `mark_standup`.
    * The other screen: the figure gets up and walks on from there — no jump,
      no sliding.
48. **The partner washes at a water trough** (when dirty or bloody enough for
    the game to offer "Wash"). Marker: `mark_trough`.
    * Host: the partner's figure goes to the same trough and washes its face
      for a few seconds, then stands again.
49. **The partner sharpens at a grindstone.** Marker: `mark_grindstone`.
    * Host: since 0.42.0 the figure sits on the grindstone's seat and grinds
      (item 55); say if anything odd happens.
50. **Wolves bite.** Wolves (or dogs, boars) attack the host, at night away
    from town. Marker: `mark_wolfbite`.
    * Partner: the animals lunge and bite, they don't stand frozen; the host's
      health drops on the host's screen.
51. **The off switch** (partner): `mp_activities off`, look at a sleeper and
    a sitter, then `mp_activities on`. Marker: `mark_npcsit`.
    * Off: they stand where they are (as before 0.41.7). On: back in the bed and
      on the bench within a second.

Lines worth a look — both: `MP-W141`, `MP-W141 stats`, `WO141-CONFIG`,
`WO141-CAPTURE`, `WO141-APPLY`, `WO141-LEAVE`, `MP-NPCWRITE … activity-hold`;
the trough: `WO141-ANIM`, `WO141-SHOW`, `MP-W141 anim`; the bites: `SWING:
entity=… is not a human`. `mp_activity_status` prints the state.

## WO-143 — tools in hands, hoeing, one-shots, and the players' minigames

What people hold and do now shows on the other screen too: the tool in a
villager's hand, a farmer hoeing a field, a guest's drink, a dice player's
reaction; and the other player's grindstone, reading, alchemy, herbs,
lockpicking, digging and smithing. All five switches are on by default. If
something looks wrong, turn that piece off in the console and say which:
`mp_hand_items off`, `mp_activity_gaits off`, `mp_oneshots off`,
`mp_player_minigames off`, `mp_idles off` (`on` switches each back).
`mp_activity2_status` prints the state.

52. **A farmer hoeing a field.** Daytime, a field by a village (the host
    nearby). Marker: `mark_hoe`.
    * Partner: she holds the hoe and hoes along the row, bent over it, as on the
      host's screen. At the row's end she straightens and holds the hoe still.
    * Say at once if the hoe jitters or jumps between frames (`mark_odd`, the
      time): that was the bug before this build.
53. **People carrying tools.** Someone walking with a saw, a bucket, a broom.
    Marker: `mark_tool`.
    * Partner: the same tool in the same hand. Nothing floats in the air next
      to a hand (`mark_floating` if something does).
    * Expected for now: a carpenter at his bench, a sawyer, a scribe at his desk
      stand at the spot, as in 0.41.7.
54. **A tavern with guests.** Marker: `mark_oneshot`.
    * Partner: seated guests drink (the arm to the mouth) and dice players react
      to a throw, and they stay seated — nobody stands up and sits down again.
    * Expected for now: the tankard is not in the guest's hand; a barmaid
      serving or wiping a table may not show it on the partner's screen.
55. **The partner sharpens at a grindstone.** Marker: `mark_minigame`.
    * Host: the partner's figure sits on the grindstone's seat, a foot on the
      pedal, the hands on the wheel, and grinds. (The blade is not in its hands
      yet.) When the partner stops, it gets up.
56. **The partner reads a book, makes a potion, picks herbs, picks a lock,
    digs, works at an anvil.** Marker: `mark_minigame`, once for each.
    * Host: the figure does the same (reading, stirring at the alchemy table,
      bending to the herbs, crouching at the lock, digging, hammering).
    * Stone throwing and archery are not shown: the figure stands.
57. **The host does the same** (a grindstone, a book). Marker: `mark_minigame`.
    * Partner: the host's figure does it.
58. **Walk close past people standing about.** Marker: `mark_look`.
    * Partner: some turn their heads to you by themselves, as in the game.
      Nobody's head swings back and forth, and no weapon flashes in and out of
      its sheath (`mark_odd` if one does).
59. **A cart or a caravan passes** near the host. Marker: `mark_cart`.
    * Partner: say what you see. Expected for now: its horses can walk ahead of
      the cart, which stays behind (the cart itself is not sent yet).
60. **The switches** (partner, in the console): `mp_hand_items off` (the tools
    go away), `mp_activity_gaits off` (the farmer walks upright), then both
    `on`; the partner at a grindstone while the host types
    `mp_player_minigames off` (the figure stands), then `on`. Marker:
    `mark_tool`.
    * Each piece stops within a second, nothing else changes, and `on` brings
      it back.
61. **Any time:** a body in a T-pose (arms straight out) — `mark_tpose`; an
    item floating — `mark_floating`.

Lines worth a look — both: `MP-W143`, `MP-W143 stats`, `WO143-CONFIG`,
`WO143-HANDS`, `WO143-GAIT`, `WO143-SHOT`, `WO143-TEMP`, and WO-141's
`WO141-APPLY` (a refused apply: three lines, then one a minute with the count).

## Logs to send afterwards

Both machines, before starting the game again (the game keeps only one old
log): `kcd.log` and `kcdmp-native.mirror.log` (the Modding Tools folder), the
agent log, and the host's relay log.
