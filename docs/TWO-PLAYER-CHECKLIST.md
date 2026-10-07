# The next two-player session: the combined checklist

One list for the host and the partner, built up by WO-136 to WO-144. The same
list, with what is new, is the tester page for 0.42.0: `docs/TEST-0.42.0.md`.
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

## WO-144 — what the first real sessions found

The fixes from the first two-player evenings on 0.42.0. Three new switches, all
on: `mp_avatar_dress`, `mp_avatar_lights`, `mp_show_animals` (`off` goes back
to 0.42.0's way for that piece).

62. **A restart in the middle.** The partner quits the game and starts it again
    (or restarts the launcher), then joins again. Marker: `mark_rejoin`.
    * Host: then sleep in a bed. The question goes to the partner once, he says
      yes, and you both sleep. (0.42.0: the question waited for a second,
      invisible partner and timed out.)
63. **Load a save in the session** (host). Save once, walk a bit, load it.
    Marker: `mark_load`.
    * Host: the game loads; the partner's figure comes back. (0.42.0 crashed
      here.) Do it twice more.
64. **Talk to someone** (partner), by day, then at night. Marker: `mark_talk`.
    * Partner: the conversation plays. If someone does not answer, you get
      "This person can't talk to you right now." and nothing else happens.
    * Host: that person stands still only while the partner really talks to
      them.
65. **Clothes.** Both change clothes a few times: a hat on and off, armour on
    and off, then crouch and sneak a few steps. Marker: `mark_clothes`.
    * Both: the other player's figure wears what they wear, within a few
      seconds, every time, and keeps it on while sneaking. Nobody is shown in
      their underwear (`mark_odd` and the time if someone is).
66. **Crouch.** Each crouches and sneaks for a minute. Marker: `mark_crouch`.
    * The other: the figure stays crouched and sneaks; it never stands up by
      itself.
67. **Night.** Walk about at night without a torch, then with one. Marker:
    `mark_torch`.
    * The other: no lantern or torch in the figure's hand until its player
      holds a torch; then the torch, and when it goes away nothing is left
      lying on the ground.
68. **Horses.** The host rides past the partner; then the partner rides past the
    host. Marker: `mark_horse`.
    * Both: the rider sits on a horse, in the riding pose. Horses standing
      about on the host's screen are there on the partner's too.
69. **Wolves or dogs** attack the partner. Marker: `mark_animals`.
    * Partner: you can see them and hit them, and they die when they should.
70. **Dice** with someone in a tavern (partner). Marker: `mark_dice`.
    * Partner: after the conversation, the dice game starts and you can play.
71. **The clock** (partner): once in a while compare the time with the host
    (the host says it). Marker: `mark_time`.
    * The two never differ by more than a minute or so, also while the host
      talks to someone.
72. **Sitting on a bed** (partner): sit on a bed's edge, then get up. Marker:
    `mark_bed`.
    * Host: the partner's figure lies on that bed while you sit, then gets up.
73. **Wanted** (partner): if a red icon stays at the top of your screen, write
    down when it came and send a screenshot. Marker: `mark_icon`.

Lines worth a look — host: `MP-PEERS`, `replaces its older connection`,
`WO131-FACTION`; both: `[appearance] ghost`, `WO144-DRESS`, `WO144-LIGHT`,
`WO144-SHOW`, `WO144-CLOCK`, `WO144-FLOAT`, `MP-W144`, `WO137-TALK`.

## WO-147 — the partner fights, the leash pulls, the host's quests are safe

The fixes from the first long evening on 0.42.2. Four new switches, all on:
`mp_hostile_engage` (partner), `mp_quest_safety` (host), `mp_leash_cap_s`
(host, seconds; 0 = no limit), `mp_npc_catchup` (partner). `off` (or 0) goes
back to 0.42.2's way.

74. **Bandits fight the partner.** Find bandits (or let them find you). The
    partner draws a weapon near one the host is *not* fighting. Marker:
    `mark_fight`.
    * Partner: you can lock on to the bandit, block and hit it; it fights you
      back. Your hits hurt it on the host's screen too.
    * Host: the bandit turns on the partner's figure. Nobody gets a crime for it.
75. **Wolves or dogs** attack the partner while the host stands away. Marker:
    `mark_animals`.
    * Partner: you can lock on to them and kill them; they die on the host's
      screen too.
76. **A friendly person** stays friendly: the partner walks past villagers with
    the weapon drawn. Marker: `mark_friendly`.
    * Partner: no villager comes into combat with you on its own.
77. **Blocks count.** The partner blocks a bandit's blows, then hits back with a
    damaged or blunt weapon. Marker: `mark_block`.
    * Host: the bandit tires on your screen too (its stamina), not only its
      health.
78. **The leash while the host is in a menu.** The partner walks off, past
    650 m, while the host sits in the inventory or the map. Marker:
    `mark_leash`.
    * Partner: the countdown runs down and you are brought back beside the host,
      standing on the ground.
79. **The leash while the partner talks.** The partner walks past 650 m and
    starts a conversation there. Marker: `mark_leash_talk`.
    * Partner: after about a minute the conversation ends and you are brought
      back.
80. **Far away.** The partner rides or runs a long way (a kilometre or more)
    before the countdown ends. Marker: `mark_leash_far`.
    * Partner: you land beside the host, or on a spot the host just stood on,
      without fall damage.
81. **The map and fast travel** (partner): open the map a few times, try to fast
    travel once. Marker: `mark_map`.
    * Partner: "Only the host can fast travel in co-op." Nothing else happens;
      nobody is brought anywhere.
82. **A fist fight** (the partner's quest with one). Marker: `mark_fistfight`.
    * Both: a knocked-out fighter is knocked out on both screens, never dead on
      one of them; the quest does not fail for the host.
83. **The host sleeps a quest's sleep** (one that cuts to a scene). Marker:
    `mark_questsleep`.
    * Partner: afterwards your figures move normally; nothing says "the host is
      paused" for long.
84. **A busy town** (both): ten minutes together in the biggest town. Marker:
    `mark_town`.
    * Partner: the villagers walk on smoothly; nobody freezes and then jumps;
      the game keeps its speed. (Agent log: `MP-W147-STATS ... catchup=on
      lag_max_ms=` stays low; kcd.log has few `NPC-SYNC release ... (stream
      silent)` lines.)

Lines worth a look — host: `MP-LEASH host:` (`held`, `the hold is over`,
`pull #`), `MP-W147 watching`, `MP-ATTRIB`, `WO139-JUDGE`, `DESTRUCTIVE`,
`[timeskip] the engine cancelled the skip`; partner: `MP-W147 engage on`,
`is no enemy of this player`, `MP-DMG dir=out`, `MP-LEASH pulled`,
`placed on a spot the host stood on`, `ApplyDamage non-lethal` (native log),
`MP-W147-STATS`; after a load in the session: `WO147-REANNOUNCE`,
`WO147-NEWBODY` (kcd.log) and `MP-WO131 copy guard on` (agent log).

## WO-148 — carrying on the other screen

When one of you picks up a body or a sack, the other now sees it. Two new
switches, both on: `mp_carry_sync` (bodies, and the whole carry layer) and
`mp_carry_objects` (sacks). `off` goes back to 0.42.5's way: a carry shows only on
the carrier's own screen. The dice keys are now made on each machine from its own
game files, by Setup and by the launcher before every start; nothing to do.

85. **The partner carries a body.** After a fight, the partner picks up a dead
    body, carries it twenty steps or so and puts it down. Then the same with
    someone knocked out. Marker: `mark_carry`.
    * Host: the partner's figure lifts the body onto its shoulder, walks with it
      and puts it down. The body lies where the partner put it, on both screens.
    * A knocked-out person who wakes up on the way is left alone; nobody alive is
      ever moved.
86. **The host carries a body.** The same the other way round. Marker:
    `mark_carry_host`.
    * Partner: the host's figure carries it; it lies where the host put it down.
87. **Both reach for the same body.** Stand by one body and pick it up at the
    same moment. Marker: `mark_carry_both`.
    * Both: only one of you ends up carrying it. On the other's screen the body
      is put down ("Your partner has that body in the host's world.") and the
      winner's figure picks it up.
88. **A sack.** If one of you has a task with sacks to carry, carry a few and drop
    one on the way. Marker: `mark_sack`.
    * The other: the figure holds a sack while it carries one; a dropped sack
      lies where it fell and goes away when it is picked up again. It is only
      shown: the other's task does not count it.
89. **A burial** (a quest where a body is carried to a grave). The partner carries
    the body while the host watches. Marker: `mark_bury`.
    * Host: write down what the quest does (nothing, moves on, or fails). The
      partner's carrying is only shown; it does not count for the host's quest
      yet. The host's own carrying counts as always.
90. **The dice keys.** Play dice once each. Marker: `mark_dice_keys`.
    * Both: picking and throwing the dice work with the keys, as before.

Lines worth a look — host: `MP-CARRY player`, `refused:`, `MP-CARRY
quest-reaction`; both: `MP-CARRY land`, `MP-CARRY avatar`, `MP-CARRY loser`,
`WO148-BUILD` (kcd.log), `MP-WO148-STATS` (agent log), `keys pak:` (launcher
log).

## WO-151 — the long session's fixes

0.43.0 changes what the 2026-10-01 evening showed. Every check below names its
switch; each is on, and `off` goes back to 0.42.8's way. Everything here was
seen working solo against a scripted partner; these checks need two people.

91. **One fight.** Fight the same bandit together, for a while. Marker: `mark_fight_same`.
    * Partner: the bandit shows the host's reactions (stagger, knockdown); it never
      falls over by itself, sinks into the ground or takes out a tool, and its health
      matches the host's. Switch: `mp_npc_reactions`, `mp_copy_fight`.
92. **Friendly fire, host on partner.** With friendly fire on, the host hits the
    partner a few times. Marker: `mark_ff`.
    * Partner: health drops by the hit. (No reaction is played yet: known.)
93. **Riding together.** Each of you mounts a horse (the partner one of the host's
    if a quest gives one), rides, gallops, dismounts. Marker: `mark_ride`.
    * Both: each rider sits on a horse on both screens, with the riding pose; the
      partner can ride and gallop as in single player; no stutter at the gallop.
      Frames of both screens, please: this is the trailer's shot. Switch: `mp_ride_owner`.
94. **The host reloads.** Mid-quest, the host loads a recent save. Marker: `mark_reload`.
    * Partner: after the rejoin, quest steps and conversations wait a few seconds
      ("Catching up with the host's world"), then work; no step the host has already
      done happens again. Switch: `mp_quest_catchup`.
95. **A scene.** Play a quest scene together (a cutscene or a conversation that turns
    into one). Marker: `mark_scene`.
    * Partner: never black for long; write down how long if it is black at all.
      Switch: `mp_scene_guard`.
96. **A living person carried.** In a quest where you carry someone alive (the
    wounded hunter), each of you carries him once. Marker: `mark_carry_alive`.
    * The other: the carrier's figure carries him; nobody floats; "Grab body" is not
      offered meanwhile. Switch: `mp_carry_living`.
97. **The whistle.** Each of you calls your horse (the whistle). Marker: `mark_whistle`.
    * The other: hears the whistle at the caller's figure. Switch: `mp_whistle`.
98. **Weather.** Play an hour or more. Marker: `mark_weather` when it changes.
    * Both: the same weather when it changes on the host's side. Switch: none (the
      host's own game decides).
99. **The join.** The partner joins while the host stands among people. Marker: `mark_join`.
    * Host: the world stands still while the partner loads (nobody walks on), then
      runs again. Switch: `mp_join_engine_hold`.
100. **Doors.** The partner opens and closes a few doors; the host opens one the
     partner can see; let an NPC walk through one. Marker: `mark_door`.
     * Both: every door is open or shut the same on both screens. Switch: `mp_door_sync`.
101. **The forge.** The partner forges a sword while the host watches, then the other
     way round. Marker: `mark_forge`.
     * The watcher: the figure works at the anvil where it stands; the anvil stays free
       for the watcher. Both can always leave the minigame. Switch: `mp_minigame_align`
       (off = never at the station's object).
102. **A crime together.** One of you steals something seen by someone. Marker: `mark_crime`.
     * Both are wanted; paying the fine (either of you) clears both, and the wanted icon
       goes. Switch: `mp_crime_mode joint|individual`.
103. **Stuck?** If either of you is stuck in a bed, a minigame or anything else, type
     `mp_unstuck` (then `mp_unstuck hard`). Marker: `mark_stuck`.

Lines worth a look: `MP-W151` (agent log), `WO151-` (kcd.log), `FRAME` and `FAULT`
(kcdmp-native.log; a `FAULT` line is worth a report).

## WO-153 — the tutorial session's fixes

Throwaway saves only; nothing here needs a real playline. Markers as before (`mark_<word>`).

104. **Herbs together.** Both of you pick herbs in one patch, close together, for a few minutes.
     Marker: `mark_herbs`.
     * Neither game crashes. The other's figure stands where it is while its player picks (it no
       longer plays the pick). Switch: `mp_avatar_herbs on` brings the old loop back (it crashed the
       joiner twice: only for a test).
105. **A dog.** The joiner walks past a village dog (Bozhena's dog, or any farm dog) with a weapon
     drawn. Marker: `mark_dog`.
     * The dog stands where it is; it never bites or chases. Agent/kcd log: `WO131-GUARD pause npc=… class=Dog`.
106. **The host fights alone.** The host fights a bandit or a villager while the joiner stands within
     ten metres. Marker: `mark_hostfight`.
     * The joiner is not drawn into the fight and has no "in combat" state when it ends. No
       `MP-W151 copy … its own hit reactions off` line for anyone who is not fighting.
107. **A death.** The host kills an NPC the joiner can see (best: a road ambush). Marker: `mark_death`.
     * The joiner's copy lies dead where the host's does, once; loot opens once; no standing copy.
       kcd.log: `MP-OWNERDEATH … applied=dead` (or `UNRESOLVED` if it did not land).
108. **A stall.** The joiner joins the host's world (the load takes ~40 s), and once during play the
     host opens the map and the joiner alt-tabs for 5 s. Marker: `mark_stall`.
     * agent.log: `MP-BATCH-HOLD down` then `MP-BATCH-HOLD up after N s`, and **no** `MP-BATCH-DROP`.
       After the hold the world is the host's (NPCs where the host's are, doors as the host's).
109. **Doors after the join.** Right after a join load, watch a few doors the host opened earlier.
     Marker: `mark_door2`. * Each door is in the host's state; none flaps open and shut.
110. **Quiet logs.** At the end, in kcdmp-native.log: no `FAULT motion::` lines; on screen: no
     "an NPC is being moved by this machine's own AI" notice at any time. Marker: `mark_quiet`.

## WO-154 — the public beta: questing, fighting and joining together

Throwaway saves only. Markers as before (`mark_<word>`). Each item says what WO-154 proved alone on one machine and
what only two players can show. The mod menu (**Insert**) has every switch named here.

111. **Questing together.** The host does a quest step the joiner can see (a conversation that moves a quest on,
     an item picked up); then the joiner wins a duel or a brawl step, or picks up a quest item. Marker: `mark_quest`.
     * Both journals follow each other. agent.log: `MP-W137` lines with `applied`, and no `came through no port` or
       `not sent to the host (no-port)`. Proven solo: the field's 43 host steps replayed into a real joiner game,
       0 failed to apply.
112. **Friendly fire and the partner's figure.** The host hits the joiner (fist, then a weapon), then the other way
     round. Marker: `mark_ff`.
     * The other player's figure never barks "threatened", never draws or attacks. kcd.log: no `Skirmish is getting
       armed because of 'kcd2mp_`.
113. **Knocked down.** Each of you knocks the other down once (friendly fire). Marker: `mark_knock`.
     * The figure falls on the other screen, lies there while its player is down, and gets up when they do.
       kcd.log: `WO154-FALL` and `WO154-RISE`. Proven solo both ways (a scripted partner).
114. **The host turns an enemy.** An enemy fights the joiner; the host hits it from behind. Marker: `mark_turn`.
     * It reacts to the host's blows and turns to the host the way it would in the game. kcdmp-native.log:
       `WO136-FORCED ... cleared (read back) -- the host struck it` or `WO139-PURSUE host-struck`.
115. **The partner falls.** The joiner is knocked out or killed in a fight with two enemies. Marker: `mark_partnerdown`.
     * Every enemy stops fighting the joiner's figure at once; after the respawn no guard or enemy goes for them for
       two minutes. Lines: `WO136-FORGET`, agent.log `MP-W154 ... down` / `respite`.
116. **A fight that won't end.** The joiner types `mp_unstuck` (or the menu's "I'm stuck") during a fight that does not
     end. Marker: `mark_endfight`. * The host's world ends every fight against the joiner's figure.
117. **The join bar.** The host watches the panel while the joiner joins (start, middle, end). Marker: `mark_joinbar`.
     * The panel shows through the whole hold and goes away when the joiner is in.
118. **A slow or frozen load.** If the joiner's load takes long, the launcher says the game is busy and waits; if the
     joiner's game ever freezes at the loading screen, the launcher says to restart the game and join again, and the
     host gets the world back. Marker: `mark_joinslow` (only if it happens). agent.log: `MP-JOIN ... FROZEN`;
     `MP-JOIN ... the main menu's video stopped (wh_ui_StopMovie)` before every join's load.
119. **Join with a new character.** A joiner with no usable save of their own: the launcher's message says why and
     offers **Join with a new character**. Marker: `mark_newchar`.
120. **Riding together.** Ride side by side at a trot and a gallop for a minute, then the other way round.
     Marker: `mark_ride`. * The other's horse moves smoothly, no stutter or sliding, the rider moves with it.
121. **Fast travel.** With the menu's fast travel off (the default), the map refuses fast travel with the plain line;
     the host turns it on: the host fast travels and the joiner is brought along. Marker: `mark_fasttravel`.
122. **The mod menu.** Both open it (Insert). On the joiner's screen the host's settings show "[set by the host]" and
     cannot be changed; name badges off and on; clean screen on and off. Marker: `mark_menu`.
123. **The joiner's own wait.** With the menu's "Sleep and wait together" off on the joiner, the joiner waits an hour.
     Marker: `mark_skip`. * The time goes back to the host's, and the game says so in one line.
124. **Voice.** On a new install voice chat is off (no microphone opened). Turn it on in the menu on both: you hear
     each other. Marker: `mark_voice`.
125. **A caravan.** Walk past a caravan (people sitting on a cart) on the joiner's screen. Marker: `mark_caravan`.
     * They stay seated and move with the cart. kcdmp-native.log: `MP-NPCBIND ... seated=held`.
126. **Talking.** The joiner talks to a few villagers, men and women. Marker: `mark_talk`.
     * A refusal for low reputation is the game's own; "This person can't talk to you right now" only when that
       person is busy (talking to the host, for example).

Lines worth a look: `MP-W154` and `MP-JOIN` (agent log), `WO154-` (kcd.log), `FRAME` and `FAULT`
(kcdmp-native.log; a `FAULT` line is worth a report).

## WO-155 — hits never knock a player down; a figure falls only on a death (0.45.1)

Throwaway saves only. Markers as before. What WO-155 proved alone on one machine (a scripted partner, a synthetic host,
every number in `docs/WO-155-findings.md`) and what only two players can show.

127. **Guards fighting the joiner.** Let the host's guards or enemies fight the joiner: ten blows or more (a bandit, a
     guard after a crime, a wolf). Marker: `mark_hitnofall`.
     * The joiner's health drops by each blow and he is **never knocked down**; the host's screen shows his figure
       standing the whole time, flinching at most. kcdmp-native.log (joiner): no `WO154-DOWN … DOWN`; agent.log (joiner):
       `[playerhit] took N damage … (no knockdown: WO-155)`.
128. **Swinging and blocking between blows.** The joiner swings and blocks between the guards' blows. Marker:
     `mark_hitnofall`. * Nothing in the way: no fall, no lost input, no stuck animation. (Not shown solo: no key input.)
129. **A death on each side.** The host dies in a fight, then the joiner does. Marker: `mark_deathfall`.
     * On the other screen the dead player's figure falls where he stood and **lies on the ground, not in it** until he
       respawns; then it vanishes and a fresh figure stands where he woke (in a normal pose, no T-pose). The grave
       appears as before. kcd.log: `WO155-COLLAPSE`, `WO155-REPLACE`.
130. **Friendly fire both ways.** The host hits the joiner (fist, then a weapon), then the other way round; hit the same
     player again 1–2 s later and again 8 s later. Marker: `mark_ffwindow`.
     * The victim falls on his own screen and **not twice within 5 s**; the second hit still takes its health. On the
       attacker's screen the victim's figure falls, lies and gets up with the game's own animations and stands in a normal
       pose with its animation playing. kcdmp-native.log (victim): `WO155-FF knockdown=allowed|no(he is down)|no(inside
       the 5 s window)`; kcd.log (attacker): `WO155-FALL`, `WO155-STATE`, `WO155-RISE`.
131. **A figure's pose.** Watch the other player's figure after it spawns (a join, a respawn) and after it got up.
     Marker: `mark_tpose` if it ever stands with its arms out for more than a second. kcdmp-native.log: `WO155-NUDGE`.

Lines worth a look: `MP-W155` (agent log), `WO155-` (kcd.log), `WO155-FF` and `WO155-NUDGE` (kcdmp-native.log).

## WO-157 — the first public-beta patch (the release candidate)

Throwaway saves only; crime sharing **on** (the host's mod menu). What WO-157 proved alone on one machine (every number
in `docs/WO-157-findings.md`) and what only two players can show. This build is a **release candidate**: not
soak-tested; the maintainer tags it after this session.

132. **Both launch from the launcher.** Host and joiner click PLAY and CONNECT as always. Marker: `mark_launcher`.
     * No KCDMP_LauncherInjector.exe anywhere; the plugin attaches (launcher log `MP-INJECT injected … (sha256 matches the
       install manifest)`; kcdmp-native.log `KCDMP.dll attached`, `tick is live`). No "A MOD FILE WAS REMOVED".
133. **Shops are open for the joiner.** In daytime the joiner walks into an open shop (a blacksmith, a trader, an inn),
     then into a house at night. Markers: `mark_shop` in the shop, `mark_trespass` in the house.
     * In the shop: nothing happens on either screen; host kcd.log `WO157-TRESPASS … here=open -> not a trespass in this
       world`. In the house at night: the host's world judges it as before (`here=private -> judged`, witnesses). The
       joiner's own screen shows **no** trespass warning in either (kcdmp-native.log `quieted=`), and the host is never
       punished for the shop.
134. **A returning player's record.** After a judged trespass (133's house), the joiner leaves and joins again. Marker:
     `mark_rejoincrime`. * Host agent.log: `… is his again (N crime(s)); M trespass(es) his own game reported were dropped
     (WO-157)`; no guard stops him for it.
135. **The first walk-away.** Let a guard stop the joiner (a real crime, a theft) and walk away. Marker: `mark_walkaway`.
     * He is told it does not count as fleeing (kcd.log `WO157-STOP walked away`); the guard stops him again later; walking
       away then is fled (as 0.45.1).
136. **Enemies hurt the joiner.** Let bandits or guards fight the joiner; he does not block. Marker: `mark_blows`.
     * His health and stamina fall blow by blow (agent.log `[playerhit] took N damage and M stamina`); host agent.log
       `MP-W132 npc hit on avatar … forwarded` with stamina-only blows forwarded too. **If he blocks on his own screen,
       does the blow still land?** (yes in this build: his own block is not yet his game's decision — note it).
137. **The joiner's blows on bandits.** Marker: `mark_joinerblows`. * The bandits fight back on the joiner's screen (no
       T-pose, no standing still); agent.log (joiner) `MP-W157 engage … its one-shot stopped for the fight`.
138. **Talk and trade after smithing.** The joiner does a smithing step at the blacksmith, then talks to him and trades,
     then talks to someone else. Markers: `mark_talkafter`, `mark_trade`.
     * Every conversation starts; Trade opens the shop. kcd.log (joiner): `WO157-TALK free npc=…`.
139. **Sleeping together.** The joiner asks to sleep 12 h, the host says yes (F11); then the host asks for 4 h. Markers:
     `mark_sleeptogether`, `mark_sleepcut` if anyone wakes early.
     * Both sleep the full length; neither is woken by the other (agent.log `runs to its end (WO-157`); both are rested
       afterwards (kcd.log `WO157-REST start/end`, and `WO157-REST rested:` where the game gave nothing).
140. **A bad connection, said.** If the ping is high (or play over a slow link on purpose): the mod menu's Connection line
     says "the connection is struggling (ping N ms)", and after 30 s one line on screen. Marker: `mark_conn`.
141. **Riding far apart.** The joiner rides more than 150 m from the host. Marker: `mark_horsefar`. * The host's game does
     not freeze (kcd.log `WO157-MOUNT … no ForceMount that far`); the figure is mounted again when near.
142. **The host's figure on the joiner's screen.** Over a long session: no `Animation-queue overflow` lines for
     `kcd2mp_` in the joiner's kcd.log (kcd.log `WO157-LOOK … look_ik=off`). Marker: `mark_lookflood` if they appear.
143. **"Your character was saved".** The host saves (or an autosave); the joiner sees the line once. Marker: `mark_saved`.
144. **The same start save, two groups.** If possible: a player who hosted the start save joins someone else's game of it.
     Marker: `mark_startsave`. * He is offered Bring (his own character) and "Join with a new character".

Lines worth a look: `MP-W157`, `MP-INJECT` (agent / launcher logs), `WO157-` (kcd.log), `WO157-QUIET` (kcdmp-native.log).

## Logs to send afterwards

Both machines: Report a bug in the launcher. Since 0.42.2 it also collects the
logs of the six launches before (the launcher keeps them), so a restart no
longer loses the logs of a crash.
