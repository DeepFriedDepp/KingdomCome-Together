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
2. **The host** starts the game from the launcher and, on the game's main menu,
   presses **Start Game** (one of their worlds, or New adventure). Since 0.45.5
   nobody clicks CONNECT: the launcher connects the game by itself once the world
   is loaded.
3. **The partner** starts the game from the launcher and **stays at the main
   menu. Don't load a save.** Press **Join Game**, then **Join with a new
   character** or **Bring my character**; the partner joins the host's world by
   itself. Marker: `mark_join`.
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

## WO-159 — Start Game / Join Game on the main menu (0.45.5)

145. **The menu.** Both start from the launcher: our logo, Start Game / Join Game on top, Continue / New Game / Load Game
     greyed, no "New Game Debug" entries. Settings → Back returns to Start / Join. Marker: `mark_menu`.
146. **Join Game before the host is ready.** The joiner presses Join Game → Join with a new character → a playstyle while
     the host is still at the menu. * "Waiting for the host…" with Cancel; nobody clicks CONNECT; once the host is in its
     world the joiner joins by itself. Marker: `mark_joinwait`.
147. **The host's Start Game.** New adventure → a playstyle → Skip the prologue (needs the start saves). * The host is
     connected without touching the launcher (~10 s after the world loads); Henry stands where Hans and Henry part ways.
     Marker: `mark_newadv`.
148. **The new-character default.** On a joiner with no Modding Tools save of their own: Join with a new character is
     selected and Bring my character greyed with the plain reason; the joiner arrives as the bundled playstyle's Henry.
     Marker: `mark_fresh`.
149. **Watch the prologue** (host, once): the HUD says "Skipping one skips them all", then the cutscenes play with their
     sound and the world silent; the joiner reads "Your host is watching the prologue (about N min left)"; the host skips
     one with the game's own skip — the rest is skipped too and the joiner joins. Marker: `mark_recap`.

Lines worth a look: `MP-W159` (launcher log), `WO159-MENU`, `WO159-RECAP` (kcd.log), `MP-HENRY joiner: a new character`
(agent log).

## WO-160 — the joiner's NPC copies, the false "own save", and conversations (0.46.0)

Evidence and what each item is for: `docs/WO-160-findings.md`. Use a throwaway copy of the host's save, as always. For
items 150–155 both of you type `mp_npc_trace on` first; the first thing to look at afterwards is kcd.log on the **joiner**:
the planner's `Couldn't find actions to get NPC into game loaded state` lines were 2,478 in the 0.45.8 session (the host:
0) — this build should show **almost none** for copies. The agent log prints `MP-WO160-STATS planner refused=… unreached=…
npcs=… worst=[…]` once a minute while there is anything to say.

150. **The joiner sells to a shopkeeper the host just traded with.** The host trades with a keeper (blacksmith, innkeeper,
     a market stall) and walks off; the joiner then talks to the same keeper and chooses Trade, and sells him something.
     Do this **early in the session and again after a night** (the field's blacksmith and innkeeper copies were left in their
     night state). Marker: `mark_sellkeeper`. * The conversation starts within about two seconds (no 20 s wait, no "can't talk
     to you right now" for an idle keeper) and Trade opens the shop. Note any keeper whose talk does not start, with the time.
     * kcdmp-native.log `WO160-WAKE` / `WO160-NEUTRAL npc=<keeper>` show the two repairs (a night state dressed; a refused
       placement made neutral) when one was needed.
     * kcd.log (joiner) `WO157-TALK free npc=…` and no `WO137-TALK resume … timed out`; agent log `MP-W139 joiner: <npc> ready
     to sell here … set` (WO-159's shop context); agent log `WO160-CTX npc=… placed=… released=… loaded=kept
     cleared=no errors_before=… errors_after=…` per copy.
151. **No false "You loaded your own save".** The joiner starts from the launcher, stays at the menu, connects, and joins
     (item 3). Marker: `mark_joinclean`. * **No** toast and no launcher line about "own save" at any point from CONNECT to
     the loaded world — including when the game was restarted by the launcher after a crash earlier in the session.
     * kcd.log may show `WO160-SEPARATE held at the menu` / `… at the loading` (that is the guard working, not a fault). The
     real case still works: load your own save first, then CONNECT — both games say so at once (item 40).
152. **The talking NPC stands still on the other screen, both ways.** (a) The joiner talks to an NPC who is walking (a
     passer-by, a guard on his rounds): on the **host's** screen that NPC stops for the whole conversation and walks on after.
     (b) The host talks to a walking NPC: on the **joiner's** screen the copy stops for the conversation too, and does not
     jump when it ends. Marker: `mark_convstand`. * No jitter, no sliding. Host kcd.log `WO137-HOLD on … why=talk` / `off … why=talk
     held_s=…` (a hold that says `block-only(…)` instead of freezing is a **finding**: send the line). Joiner kcd.log `WO160-CONV
     host-talk start` (on the host) and `WO160-CONV copy-held npc=…` / `copy-released npc=…` (on the joiner). A guard's
     stop stays block-only (`why=w139-stop`) by design.
153. **Naked-NPC check after a wait.** Wait 2 hours in a town (T, wait), then look at the NPCs on the joiner's screen: how
     many are in underclothes who were dressed on the host's screen? Marker: `mark_naked`. * Write the number and the time.
     **Expected: none** — this build dresses a copy whose state still holds the night's undress while the host's NPC is awake
     (WO-160 §1.8). kcdmp-native.log (joiner) `WO160-WAKE npc=… state still holds the sleep undress…` per body (once a
     minute), agent log `WO160-CTX settle after …: N copies`; the status line's `woke_dressed N`. A body that is still naked
     with no `WO160-WAKE` line for it is a **finding** (send the time and the NPC).
154. **Naked-NPC check after a sleep.** The same, after a sleep to morning (item 139's way). Marker: `mark_naked`.
     * Also look for the copies being **in step** with the host's NPCs (sitting where they sit) and no flood of planner
       lines: agent log `MP-WO160-STATS` for the minute after the sleep.
155. **T-pose count.** After 15–20 minutes of walking about together, count the times either of you saw a T-pose figure (the
     other's avatar or an NPC). Marker: `mark_tpose` each time. * kcdmp-native.log `WO160-NUDGE body=… …` lines are the mod's
     answer (a body that stood still after a one-shot or a stopped clip got its walk pulse again).
156. **Herbs together.** One player picks herbs (the minigame) while the other watches. Marker: `mark_herbs`. * The watching
     player sees the picker **bend and pick** with a plain loop (it is a plain clip; if it does not bend, **say so** — this
     was not visible in our solo run). No crash, no T-pose after he stops. kcd.log `WO160-HERB id=… on clip=…` / `off held_s=…`.
     `mp_avatar_herbs off` makes the avatar stand instead.
157. **Mount and dismount, five times each.** Each of you mounts and dismounts a horse five times in a row, the other
     watching. Marker: `mark_mount5`. * The host's game does not freeze or crash. kcd.log `WO160-MOUNT id=… ready after N s`
     (it waited for the horse) or `refused after N s: <reason>` (it declined; it is tried again). Send any mount that did not
     happen, and any freeze, with the time and the marker.
158. **The joiner buys a horse and whistles once.** The joiner buys a horse from a seller, rides it a little and dismounts a
     way off; the host is near the joiner. The joiner whistles **once**. Marker: `mark_ownhorse`, then **`mark_odd` right after the
     whistle** (so the moment is easy to find). * The horse appears 7 m from the joiner's avatar on the host's screen *and* on
     the joiner's own screen (it is a placement, not a gallop). Host kcd.log `WO160-HORSE marked <horse> as ghost 0's own horse
     here` and `WO160-HORSE fetch <horse>: was N m away, now 7 m beside …` (or `refused fetch … <reason>` — that is a finding);
     joiner kcd.log `WO160-HORSE own <horse> -- told to the host`.
159. **Doors.** (a) The host opens a door (not a locked one) and walks away: it stays open on the joiner's screen and does not
     shut by itself. (b) The joiner walks up to a door that is merely **shut** (not locked): **no lockpick prompt**. Markers:
     `mark_door` (a), `mark_door2` (b). * kcd.log (joiner) `mp_door_sync` status `privacy_skipped`; nothing like `WO151-DOOR ask
     … unlocked here` for a door he did not pick.
160. **A host kill, watched by the joiner.** The host kills an NPC (a bandit, a deer) while the joiner watches from a few
     metres. Marker: `mark_hostkill`. * The NPC falls on the joiner's screen when it dies and not before; no "standing dead
     copy" afterwards. Note the time if it stands up again or stays upright.
     **And the sword:** an enemy who draws a sword on the host's screen — does the joiner see the sword in its hand? Marker:
     `mark_odd` right after if not, with the enemy's name (WO-160 §1.8: probably the night's undress, unverified).
161. **Check from 0.45.5–0.45.8 (in this build).** (a) The prologue recap: the sound plays, the world is silent, and one skip
     skips all (item 149, `mark_recap`; kcd.log `WO159-RECAP sound setup on … / off`). (b) The joiner is never shown a trespass
     warning for a house the host owns or a shop that is open (item 133, kcdmp-native.log `WO139-BUILD trespass HUD gate ARMED`).
     (c) The keeper "ready to sell" check is item 150; the doors check is item 159.

Lines worth a look: `WO160-` (kcd.log), `WO160-CTX` (agent log and kcdmp-native.log), `WO160-NUDGE` (kcdmp-native.log),
`MP-WO160-STATS` (agent log). Switches (all default on): `mp_ctx_release`, `mp_conv_hold`, `mp_horse_fetch`, `mp_avatar_herbs`.

## WO-161 — shared combat: what the host decides, what is counted, and what the next work order needs (0.46.5)

Evidence and the design call: `docs/WO-161-findings.md`. **What changed in 0.46.5 is bookkeeping, not combat:** every blow an
enemy lands on a player is now numbered, applied once and written to the log with the swing it came from — or the reason
there was none (`WO161-HIT` on both machines, `MP-WO161-STATS` once a minute). **What did not change:** the host's game
still decides whether a blow hits or is blocked (against the host's copy of the player; the player's own block is not asked),
enemies are not made lockable for the other player, two players on one enemy and a figure that steps in a fight are as in
0.46.0. These items give the next work order its numbers; none of them expects a fix. Use a throwaway copy of the host's
save, as always.

162. **Both players on one enemy.** Both of you fight the same bandit or guard at once, for about a minute. Marker:
     `mark_gang`. * Write down: does the enemy turn back and forth between you (how often)? Do both players' blows count (its
     health falls with both)? Host kcd.log `WO136-TARGET npc=… -> …` (each retarget, with `threat=a/b`) and `WO136-FORCED …`;
     agent.log `MP-W132 npc hit on avatar …`. This is the baseline, not a pass/fail.
163. **The host and a guard that beats the partner.** On the throwaway save, the partner commits a theft and lets a guard attack
     them; the host tries to lock onto the guard and to hit it (a blow at a guard is an assault in the host's own world —
     only on the throwaway). Marker: `mark_guard`. * Write down: could the host lock on at all? Did the guard turn to the
     host? Host native log `WO139-PURSUE host-struck …`; agent log `host_struck=` in the minute line (it was 0 in the first
     0.45.8 session).
164. **An enemy attacks the joiner: the swing, the hit, once.** An enemy fights the joiner; both of you watch. Marker:
     `mark_hit` each time the joiner takes a hit. * The host sees the enemy swing; the joiner's screen shows the same enemy
     swing a moment before the damage; the joiner takes the hit **once**. Host agent.log `WO161-HIT victim=<n> by=<npc>
     sid=… hid=… verdict=… sent=verdict`; joiner agent.log `WO161-HIT victim=me … shown=yes applied=yes`. Every damage line
     has `shown=yes` or a `reason=…` (`no-swing-captured`: the host's game made a blow the mod did not see; `row-not-received`;
     `row-refused-…`). A line with `applied=dup` or `refused=malformed` is a **finding** (it was caught, never applied twice —
     send it). `MP-WO161-STATS`: `applied` equals the `[playerhit] took …` lines; `not_shown_why=[…]` is the count by cause.
165. **Block and parry, each side.** The joiner blocks an enemy's blow, then the host does, each once with a parry. Marker:
     `mark_block`. * Write down what happened. In 0.46.5 the host's copy of the joiner decides the joiner's block (the
     verdict reads `blocked` when the blow cost only stamina); a block the joiner raised late or early may not count —
     note it. A parry is **not** decided by the victim in this build: note, do not expect.
166. **The sword and the phantom stance.** (a) An enemy who draws a sword on the host's screen: does the joiner see the sword in
     its hand? (WO-160's item 160, once more.) (b) An enemy that fights the joiner on the host's screen but stands as if
     holding a weapon you cannot see. Marker: `mark_odd`, with the enemy's name. * Joiner agent.log `[npcsync] <npc>: N
     equipped item class(es) read for swing resolution` for that enemy.
167. **A swing with no combat; damage with no swing.** While any fight runs, `mark_odd` the moment you see a swing and no combat
     mode coming up on the other screen, or take damage with no swing visible on the other screen. * Send `WO161-HIT … shown=no
     reason=…` and `MP-W132 engage on …` — the reason names the cause (in the 0.45.8 sessions 16 of 45 enemy blows had no
     captured swing: 9 of 35 in the first session, 7 of 10 in the second, six of those a cat's bites).
168. **A three-minute brawl, with `mark_snap`.** Both of you fight two or three enemies for about three minutes, moving about.
     Marker: `mark_snap` on **every jump or step** you see in the other player's figure or in an enemy. * Count them (each
     machine its own number) and send both logs: kcd.log `MP-GHOSTCORR … corr_max_m` per 10 s window and `TELEPORT` lines
     next to each marker are the data for the snapping work (the old counter counts only steps over 5 m; the fight's are
     smaller).
169. **The sleeping NPC.** An NPC lies asleep for the host but not for the joiner (or the other way round). Marker: `mark_odd`,
     the NPC's name and the time. * Joiner kcd.log `[NPCStateSearch]` / `Couldn't find actions…` lines for that NPC;
     kcdmp-native.log `WO160-WAKE` / `WO160-NEUTRAL npc=…`. Not fixed in 0.46.5 (Part B).
170. **Still pending from 0.46.0:** items 150–161 above — nothing in them changed in 0.46.5; send them as before.

Lines worth a look: `WO161-HIT`, `MP-WO161-STATS` (agent log, both machines), `MP-WO161 verdict … not sent` (the fallback), `WO136-TARGET`
(kcd.log, host). Switch (default on): `HitVerdictEnabled` in `kcdmp-client.json`, or `--no-hit-verdict` on the agent.

## WO-163 — shared combat, the certain fixes (a build after 0.46.5; not released, not built as an installer)

Evidence: `docs/WO-163-findings.md`. **What is in this build:** an enemy blow that arrives with no swing of its own now shows one
(a generic lunge), sync attacks (combos, throws) are read correctly, a counter-strike after a perfect block is a swing, the host
pairs a blow with its swing by the swing's own timing, the fight "snap" is measured as a log line, an enemy is turned from its
opponent only by clearly more *damage*, and the host's assault judge asks the engine whether the victim is in a fight with the host.
**What is NOT in it** (the probes did not pass or were not run; see the findings): the player's own block deciding the hit on his
own machine, the host locking onto an enemy that fights the partner, the attacker's recoil on a block. Use a throwaway copy of the
host's save, as always.

171. **A blow with nothing behind it.** Let a cat, a dog or a man whose swing you did not see hit you. Marker: `mark_odd`. * Send
     `WO161-HIT … shown=generic reason=…` (the copy lunged once before the damage) and `shown=no …` (nothing could be shown: a
     missile, no named attacker). In the 0.45.8 sessions 16 of 45 blows had no swing; the generic lunge is for those.
172. **A combo or a throw.** Fight someone who throws or chains attacks (a bandit chief, a guard). Marker: `mark_odd` if the
     other screen shows nothing. * Send the host agent's `MP-ACTION section=outbound kind=NpcAttack … table=combat_action_sync_attack`
     lines (the joiner's `dispatch=native-row` for the same row) and `MP-WO163-STATS rows_out_sync=… rows_out_unknown_table=…`;
     `WO163-SYNCGUID` must never appear. (`dropped-unknown-row` was 8 % and 20 % of the host's rows before.)
173. **A counter after a perfect block.** Perfect-block a guard's swing and watch the guard's counter on both screens. * Send
     `MP-WO163-STATS master_strikes_out=… perfect_block_rows_dropped=…`.
174. **Two players on one enemy, with the snap line.** Both of you fight one enemy for a minute, moving about. Marker: `mark_snap` on
     every jump you see. * Send both native logs: `MP-FIGHTSNAP … resume_max_cm … post_hold_step_max_cm …` per 10 s window (the
     baseline from the development traces: resume step median 13.1 cm, 90th percentile 15.4 cm; the largest step in the next second
     median 13.3 cm, 90th percentile 30.4 cm) — a later snap fix is kept only if these 90th percentiles fall.
175. **A guard that fights your partner, and you hit it.** The host hits a guard that is fighting the joiner's avatar; wait six
     seconds. * Send `WO163-JUDGE …` and `MP-WO163 hostile? … -> hostile opponents in one skirmish | not hostile` (agent log): does
     the engine say the guard is in a skirmish with you, and was the blow then judged a crime (`WO139-JUDGE … assault`) or not?
     `mp_hostile_crime off` (host) returns to the 0.46.5 rule.
176. **Frame rate.** The WO-148 rule, with the game window in front: the menu, a town, and a fight with three enemies — before and
     after. Not measured by the development session (it never brings the game forward).

Lines worth a look: `WO161-HIT`, `MP-WO161-STATS` (`generic=`), `MP-WO163-STATS`, `MP-FIGHTSNAP`, `WO163-JUDGE`, `WO136-TARGET … damage=<to>/<cur>`.
Switches: `mp_hostile_crime on|off` (host console, default on); `GenericSwingEnabled` in `kcdmp-client.json`, or `--no-generic-swing`
on the agent (default on).

## WO-164 — talking to the host's people, the map crash, fleeing enemies, sitting, random events (0.47.5)

**Not run in the game by the development session** (the maintainer skipped the solo run, 2026-10-09): every item here is the
first live proof. **Press "Something's wrong here" (`mark_odd`) at every miss** — the snapshot is written on both machines under
one `mark=` id (`MP-MARK-SNAP mark=…` in kcd.log and agent.log), then send both logs. Each talk writes `WO164-TALK` (agent.log):
its `cause=` names why one failed (`busy` the host was talking to or carrying them, `preempted` they greeted first, `quest` a
quest value disagreed, `torn` their placement was refused just before, `wedged` another request was open, `neither` — a new cause).

177. **A wait, then talk to five people** (joiner). Wait 8 hours, then talk to five people, one of them seated (a bench) and one at
     work. * Send `WO164-SWEEP after …` and the `WO164-SWEEP npc=… trigger=skip … errors_before=… errors_after_6s=…` lines, the five
     `WO164-TALK` pairs, `MP-WO160-STATS planner refused=… (+N/min)` before and after the wait (the 0.47.0 joiner: 2,614 refusals in
     75 minutes, ~35 per minute; the target: under twice the pre-wait rate within a minute).
178. **Haggle with a standing keeper.** * `WO164-TALK … kind=haggle … commands=` (≥ 5; the host's played 7–11) and `ended why=dialog-ended`.
179. **Haggle with a bartender.** As 178.
180. **Haggle with a stall seller** (one who works a stall: `seller2`). As 178. The 0.47.0 talk right after a haggle stuck here.
181. **Haggle, then talk to a different seller.** The second talk starts (`started_ms=` under 2,000).
182. **Mash the key on a blacksmith, then talk to someone else.** Press the talk key ten times fast on the blacksmith, then walk to
     another person and talk. * The blacksmith: one `WO164-TALK … ask`, no repeated `WO157-TALK free` within 3 s; the other person
     starts within ~5 s. If a request hangs 4 s: `WO164-TALK retry npc=… cancel=… reissue=…` (then "Try again in a moment.").
183. **The same quest giver three times in a row** (mid-quest). * Three `WO164-TALK … started_ms=…`; a failure names its cause and,
     for `quest`, `first_mismatch=`; `WO164-QFIX var=… applied=yes` lines if a quest value was set back.
184. **The Mutt line from the bait onward** (both). Lead Ignatius to the bait; watch the joiner's
     `MP-W137 MISMATCH … numberOfMealsIgnazHasEaten` — it must not stay for minutes: `WO164-QFIX var=…numberOfMealsIgnazHasEaten …
     applied=yes` within ~10 s. (Ignatius following the joiner on the host is not built — note what the host sees.)
185. **Open the map after joining as a fresh character** (joiner; the host has a grave somewhere). * No crash. The grave shows on
     the map. In the native log: no `WO164-MAPMARK FAULT`; `WO164-MAPMARK queued (…)` during the join and `added … (from the queue)`
     after. If a fault ever appears: the game goes on and says "Map markers are off this session".
186. **Chase the lackey bandits and let them run** (Vostatek). Wait 60–70 s without fighting. * Combat ends by itself on both
     machines (you can rest or save); the joiner's game does not crash; `WO164-FLEE npc=… host_flee=true`, at most a few
     `re-asserted sheathed` per bandit (was 118), `WO164-FLEE … backoff` or `disengage` lines.
187. **Sit on a bench and be seen sitting** (each player in turn). * The other screen shows the figure seated, or standing beside
     the bench with `WO164-SIT body=… apply=failed … fallback=stand` (never frozen in a pose without a seat).
188. **Reload the joiner's save while seated** (joiner sits, then loads a save). * The host sees him where he is, not sitting in
     the old place: `WO164-SIT cleared why=rejoin|stale` on the host.
189. **"I'm stuck" while seated, then again.** Sit on a bench, open the mod menu, Help → I'm stuck; then again within 10 s.
     * `WO164-UNSTUCK step=1 stood=asked …` (are you standing?), then `step=2 … moved=yes` (beside your partner).
190. **The random-event road walk** (both, the road by the tavern). * The joiner's kcd.log has no `<RandomEvent> … starting` of its
     own (`WO164-EVENTS joiner: this game's own random events OFF`); both see the same riders and caravans, no doubled carts.
191. **The host lights a torch at night.** * The joiner sees it (`MP-W136 peer 0 torch OUT …` on the joiner, `(side-channel)` if
     the old path missed it); away again when the host puts it away.
192. **The herbalist and a guard at dawn after a sleep** (both). Dressed on both screens? (Clothing is not synced in 0.47.5: report
     what you see; `mark_odd` on a mismatch.)
193. **The host reloads** (joiner watching). * "Your host is reloading the world - please wait", then "Back with your host".
194. **The joiner washes twice at a tub.** * `WO164-USE enter|leave obj=… kind=…` lines for each wash (joiner agent.log); neither
     figure stays in a washing pose.
195. **The joiner tries dice.** * `WO164-TALK … kind=dice` and `WO164-TALK kind=minigame line="Enable of minigame entity …"` — where it
     stops says which part fails.
196. **Frame rate** (the WO-148 rule, the window in front): the menu, a town, a fight with three enemies — before and after.

**Added in the second pass (the same build):**

197. **Your partner on the map** (both). Open the map: a pin where your partner is; walk apart and open it again (it moved); the
     partner leaves the session (the pin goes); Display → "Partner on the map" off (no pin). * `WO164-MAPMARK pin owner=… added|moved|removed`.
198. **People dressed at dawn** (both, after a sleep or an 8 h wait at night). The herbalist and a guard look the same on both
     screens. * Joiner: `WO164-OUTFIT npc=… applied` for each copy that was still in night clothes; host: `WO141-CAPTURE … night-undress`.
199. **Leading Ignatius** (the Mutt bait, joiner leads). The host sees Ignatius walk behind the joiner's figure, and the bait step
     advances on both. * Joiner: `WO164-ESCORT follower npc=…`; host: `WO164-ESCORT logic=… leader=player1 accepted=…` and
     `WO164-ESCORT host npc=… on`.
200. **A rider on the road** (joiner watching a host rider pass). The rider sits on the horse, they move as one. * Joiner:
     `WO164-RIDER mount rider=… horse=… ok=true`, `dismount` when he gets off. Off: `mp_rider_unit off`.
201. **The sleep screen of the partner who did not start the sleep.** It shows the rest gained. * `WO164-REST pre-applied …`.
202. **Enemies keep their swords.** In a fight with bandits (the lackeys), their figures on the joiner's screen keep their weapons
     out while they fight (no punches). * Few or no `re-asserted sheathed` lines.
203. **Talk to a villager standing next to one of the host's people** (joiner): only the villager reacts; the host's NPC beside
     them stays where it is.
204. **The host mounts a horse, rides a minute, dismounts, then lights a torch** (joiner watching): the host's walk, gait and torch
     look right after the ride. * Host agent: `MP-POSNATIVE re-armed after 60 s` if its read was refused during the ride.

Switches (console, default on): `mp_talk_sweep`, `mp_talk_guard`, `mp_flee_limit`, `mp_joiner_events`, `mp_partner_marker`,
`mp_rider_unit`; `mp_w164_status` prints
their counters. Lines worth a look: `WO164-TALK`, `WO164-SWEEP`, `WO164-QFIX`, `WO164-FLEE`, `WO164-SIT`, `WO164-UNSTUCK`,
`WO164-MAPMARK`, `MP-MARK-SNAP`, `MP-WO164-STATS`, `MP-W143 stats … idle_window …`.

## WO-165 — the host's lock-on; your own block and the bounce-back (switched off) (0.48.0)

Checked in the game with a stand-in partner (the maintainer at the keyboard for the lock-on). **Press "Something's wrong here"
(`mark_odd`) at every jump or odd moment**, then send both logs. Lines worth a look: `WO165-LOCK` (host: `pair=set|removed|forgotten|not-set
why=…`), `WO161-HIT` (joiner), `MP-FIGHTSNAP`, `MP-WO165-STATS`.

205. **Both on one enemy** (any bandit). Both hit it for a minute. No snapping between the two figures; both players' hits count
     (the enemy's health falls for both). * Joiner: `WO161-HIT … applied=yes`, never `applied=dup`.
206. **The host locks onto a guard who is beating the joiner.** The joiner provokes a guard (or bandit) and lets it fight him; the
     host walks within 6 m, faces it and locks on **without hitting**. The lock takes, and the guard keeps fighting the joiner for
     at least 10 s. * Host: `WO165-LOCK npc=… pair=set why=near-and-facing`; the game's own `Player: Opponent change from … to …`.
207. **Then the host hits that guard once.** What happens: does the guard turn to the host, or stay on the joiner (the joiner has
     been hurting it all along)? Is it a crime (`WO154-JUDGE` / `WO163-JUDGE` on the host)? The host walks 15 m away afterwards:
     `WO165-LOCK … pair=removed why=host-left-10m` — unless the guard now fights the host (`pair=forgotten`).
208. **The joiner blocks three enemy swings and lets one through** (`mp_victim_decides` on, the default): blocked = stamina, not
     health, the same on both screens; the one let through hurts as much as a hit from that enemy should. * Joiner: the blocks
     `WO165-REPLAY … fallback=victim-blocking` (the host's verdict), the hit `WO165-REPLAY … engine=hit` + `WO161-HIT … applied=engine`;
     never a blow applied twice.
209. **A perfect block** (joiner, timed as the blow lands): what does the joiner see, what does `WO161-HIT … verdict=` say?
210. **The bounce-back** (both; `mp_block_recoil` is on by default): an enemy with a longsword, short sword,
     halberd or sword-and-shield whose blow the joiner blocks should visibly bounce back on the joiner's screen.
     `WO165-RECOIL … recoil="…"` (or `none why=…`).
211. **A 3-minute brawl** with three enemies: press `mark_odd` at every jump of a figure. Afterwards both logs; the measure is
     `MP-FIGHTSNAP` (resume and post-hold steps).

## WO-166 — the loot crash, talking, quest values, enemies that fight the joiner, weather, the map pin, respawn, Steam friends (0.48.2)

Built and checked unattended with a stand-in partner (the synthetic peer); nothing here was played by two people yet. **Press
"Something's wrong here" (`mark_odd`) at every miss**, then send both logs. Lines worth a look: `WO166-LOOT`, `WO166-TALK`,
`WO164-QFIX`, `WO166-STRIKE` / `WO166-LOCALHIT` (joiner), `WO161-HIT` (joiner), `WO166-C1` (host), `WO166-WEATHER`,
`WO164-MAPMARK`, `WO166-AMNESTY`, `MP-WO166-STATS`.

212. **Loot three corpses** (joiner), each while the host also takes something from the same body or kills nearby. No crash; the
     loot screen never changes under you; when you close it, the body shows the host's list. * Joiner: `WO166-LOOT deferred …` while
     open, `WO166-LOOT applied … close=element|audio` after; never a `WO134-BODY state` line between `open` and `applied`.
213. **Talk to five of the host's people** (joiner), among them **the smith and the blacksmith**, then the same five again. Every
     one answers. If one does not: `mark_odd`, and the joiner's `WO166-TALK timeout npc=… state=… stream=…` line says why. Your own
     press is never blocked by a talk the mod started (`WO166-TALK own-request suppressed|cancelled` only).
214. **Talk to someone the host is talking to** (joiner): "They're busy with your partner." on screen, no stuck request
     (`WO166-TALK busy … why=host-talking`).
215. **Haggle with a seller, then with another seller** (joiner). Both haggles open and close; no frozen copy afterwards.
216. **The smith's sword-quality talk** (host, then the joiner speaks to the smith): the joiner's game takes the host's value —
     `WO164-QFIX var=…kvalitaMece … applied=yes why=changed|changed-hibernated type=uint`.
217. **Two bandits fight the joiner** (the host stands back): they swing at the joiner's figure on the host's screen and at the joiner
     on his; the joiner sees the block and riposte prompts; **block one swing and take one**: the blocked one costs stamina, the
     taken one costs health, each once. * Joiner: `WO161-HIT … applied=yes`, never `applied=dup`, no `reason=row-stale`. Host: if a
     bandit stands on the figure without swinging, a `WO166-C1 npc=…` line says what both models read.
218. **Both on one enemy** for a minute. Both players' hits count; no snapping between the figures (`MP-FIGHTSNAP`).
219. **The host dies far away while the joiner fights** (the host 150 m off, killed by something there). The joiner's enemies keep
     moving and fighting near him (the host keeps streaming around both players; `WO166-SCAN fallback` on the host if its own loop
     stops).
220. **Respawn after an NPC kill** (joiner, then host). After the wake, the NPCs that killed you stop fighting
     (`WO166-AMNESTY … stopped=N`), nobody fights an empty spot; a crime you committed is still remembered by the guards.
221. **Open the map** (both, a few times, also while a partner rejoins): no fast-travel message unless you actually confirm a trip
     (then once per session); **one pin per partner** with the companion icon, never one for yourself, gone within 2 s when the
     partner leaves.
222. **Weather for an hour** (both): it rains on both screens or on neither. * Joiner: `WO166-WEATHER applied rain=…`; host:
     `WO166-WEATHER sent …`.
223. **The joiner finds the host in Join through Steam** while the host's game is still starting: the host appears as
     "(starting...)" and becomes clickable by itself within about 10 s of the host's session being ready.

## WO-167 — the last talk and trade gaps, the stuck scene, stand-in bandits, weapons, swings, shared fast travel (0.48.5)

Built and checked unattended with a stand-in partner (the synthetic peer); nothing here was played by two people yet. **Press
"Something's wrong here" (`mark_odd`) at every miss**, then send both logs. Lines worth a look: `WO167-TALK`, `WO167-T2`,
`WO167-NOHOOK`, `WO166-TALK timeout … behaviour= … bt=`, `WO167-SCENE`, `WO167-PULSE`, `WO167-WEAPON`, `WO167-SHOWN`,
`WO167-SYNCFALLBACK`, `WO167-FT`, `WO167-FAULT`, `MP-WO167-STATS` (agent) and `WO167-STATUS` (`mp_w167_status`).

224. **Trade and haggle with a seller, then talk to three other people** (joiner). Every one of them answers. If one does not:
     `mark_odd` at once; the joiner's `WO166-TALK timeout …` line now also names the person's behaviour, minigame and tree path, and
     a `WO167-NOHOOK npc=… prompt=yes|no reason=…` line appears when you stood in front of someone and your press never reached the
     mod.
225. **The host trades with someone, then the joiner talks to the same person** within a minute (and once more a minute later).
     They answer within about 2 seconds of the host finishing. * Joiner: `WO167-T2 npc=… swept=true … ended=none|…` right after the
     host's trade; host: no `WO167-BUMP … by=figure` unless the joiner's figure really walked into them.
226. **Talk to the jugglers ("Find a Lute")** (joiner, with the host nearby). If the screen goes black and stays black for 20
     seconds, it comes back by itself, you are put on your feet and you read "That scene couldn't play here — your partner's world
     runs it." (`WO167-SCENE stuck …`, then `released …`). A normal conversation scene is never cut short.
227. **Talk to the dice player twice** (joiner): both times the talk starts (and the game, if you play). If the second one does not
     start: `mark_odd`.
228. **Each player fast travels once.** The host picks a place on the map: **both** see the travel map and the route, and both
     arrive at the same place together; afterwards both clocks show the same time. Then the joiner picks a place: the host's game
     travels there for both (no vote, no question); if the host can't travel right then (a fight, a conversation), the joiner reads
     "Your partner can't travel right now." and nothing moves. * Host: `WO167-FT host travel seq=… started dest=… by=…`; joiner:
     `WO167-FT the host's travel seq=… started …`, `WO167-FT pick id=… dest=…` (please send this line: the map's point id is the one
     thing not seen yet).
229. **Finish a quest that forces a travel** (the host's quest): the joiner reads "Travelling with your partner…" and arrives beside
     the host when the host's travel ends. Neither game stays stuck on the map. If a map stays open with nothing happening, close it
     (Esc) and `mark_odd`.
230. **A road ambush while travelling** (if one happens): the travel stops for both; the joiner is placed beside the host where it
     stopped (`WO167-FT … INTERRUPTED`).
231. **A fight where new bandits appear** (a road ambush near the joiner): nobody stands in a T-pose, **every enemy holds the
     weapon it fights with** (drawn when the host's enemy draws, put away when it does), and **every hit you take comes with a swing
     or a lunge**. * Joiner: `WO167-PULSE npc=… asked=ok` for each new bandit, `WO167-WEAPON npc=… drawn=…`, never `re-asserted
     sheathed`; `WO161-HIT … shown=yes|generic`, never `applied=dup`.

## WO-170 — the partner's figure never fights on its own, riders, the dialogue scene (0.48.6)

Two players. Nothing here has been played by two people yet. The host needs 0.48.6 for the first two; the joiner for the last two.

232. **A villager picks a fight with the joiner** (the host: walk away and let an NPC start on your partner's figure — a push, an insult,
     the joiner hits first and stops). The host sees the figure **defend only as the joiner does**: it swings only when the joiner
     swings, it never draws or puts away its weapon by itself, **no blow of its own lands on anyone**, and **no guard is called on the
     joiner** for something he did not do. The villager still fights the joiner on the joiner's screen. * Host:
     `WO170-AGGRO npc=… target=figure reason=…` (the engine's own reason: please send it), `WO121-MOTION … combat automation OFF …
     kept off for the whole body`, never a `WO170-FIGUREHIT dropped` followed by a hurt bystander; a stray blow, if one ever happens,
     reads `WO170-FIGUREHIT dropped npc=… why=not-relayed … put back`.
233. **The joiner swings at an enemy** (host: watch his figure). Every swing the joiner makes is a swing of his figure on the host's
     screen, and its hit lands once. * Host: one `MP-ACTION section=inbound … kind=Attack … dispatch=swing-queued` per swing (the
     `kinds=Attack:N` in the summary line counts them), `WO170-FIGUREHIT relayed` at most once for each.
234. **Meet a riding noble on the road** (joiner: the "dummyWanderer" men on horses): **no crash**; he stays on foot for a moment and
     is then put on his horse. * Joiner: `WO170-RIDER mount=wait|ok|gave-up npc=… horse=… why=…`.
235. **Talk to the jugglers, or any quest giver whose conversation ends with a scene** (joiner): **no long black screen** after the
     conversation — the people stay awake until their scene is over. * Joiner: `WO170-SCENE hold npc=…` then `WO170-SCENE repause …
     why=scene-ended`. If a black screen does come, it is ended after 20 s as in 0.48.5 (`WO167-SCENE`).

## WO-172 — weapons, dead enemies, riding, travelling, the HUD, trading, pickpocketing (0.48.8)

Two players, both on 0.48.8 (and the relay). Nothing here has been played by two people yet. **Type `mark_odd` (or press "Something's wrong
here") at every miss** — it now also saves a screenshot in the game's screenshots folder; please send the screenshot with the logs.

236. **Weapons on the host's screen** (host). Before anything else, look at a guard or any armed villager and at your own sword (draw it).
     Then: ride with your partner, travel together once, and talk to someone with a conversation scene; after each, look again. **Every
     weapon stays visible.** The moment they are gone: `mark_odd` at once, then save, load that save and say whether they came back.
237. **If weapons vanished (host):** type `mp_oneshots off` and `mp_hand_items off`, load your save, play on and say whether it happens
     again. If it does, try one session with the photo-mode mod taken out of the game's `Mods` folder.
238. **Kill an enemy the joiner is fighting** (host kills it, or the joiner's blows finish it). **It falls on both screens at the same
     moment and stays down** on the joiner's screen: it does not stand up, does not keep swinging, and the joiner's character stops
     fighting it. * Joiner: `WO172-DEAD npc=… via=owner-death`, `dispatch=dropped-dead` for any swing of it that still arrives.
239. **Two enemies on two players** (each of you fights one of a pair of bandits). They split between you and **do not twitch between
     you**: an enemy turns to the player who is hitting it, at most once every 4 s.
240. **Ride together for 15 minutes** (both on horses; get off and on again a few times; one of you saves on the way, or tries to). Both
     of you **stay on your horses on both screens**, and **no horse is ever under the ground**. * Either: `WO172-RIDE remount|dismount`
     (a repair) or `flag repaired`; `WO172-RIDE horse ground-snapped` if a horse fell through.
241. **A quest that forces a travel** (the host plays it, the joiner is with him). **No black screen** for the joiner afterwards, and the
     HUD is there. * Joiner: `WO172-FT fader restored name=quest-travel-refused` if the black was lifted. If the screen is still black:
     `mp_unstuck`, then `mark_odd`.
242. **The joiner haggles, cancels, haggles again** (with a trader). **The trader stays** for the whole trade on the host's screen, and the
     haggle option is still there the second time. After the joiner closes the trade screen, the trader goes back to work. (The game's
     own "you're wasting my time" after a cancelled haggle is normal.) * Joiner: `WO172-TRADE hold`, then `WO172-TRADE release …
     why=screen-closed`.
243. **The joiner pickpockets someone** (host: watch the person). The joiner **gets the item once**; on the host the person no longer has
     it. If the host stole it first, the joiner's item is taken back with "Someone already took that." If the joiner is caught, it is
     **the joiner's crime**, not the host's. * Joiner: `WO172-PICK start npc=…`, `WO134-BODY take … result … ok|gone`.

## WO-171 — the partner's own knight on the map, smaller bug-report bundles (0.48.9)

Two players, both on 0.48.9 (and the relay). The map marker was checked live on one machine against a synthetic partner (both roles; its
colour, turning, name, compass and map sizes by eye); never between two real players. **Type `mark_odd` at every miss.**

244. **Open the map** (both). **The host is a red knight on the joiner's map, the joiner a purple one on the host's.** Your own figure stays
     the game's own. The game's own markers (the golden pins, the quest shields, the others) look as before.
245. **Walk in a circle** (one of you, the other watches the map). The partner's knight **turns with them**, in eight steps, and **its back
     shows when they face north**. If it is off by a step: `mp_marker_dir_offset 1` (try 1..7). If it turns the wrong way:
     `mp_marker_dir_flip on`. Tell us the values that looked right. * Either: `WO171-MARK slot=… dir=… turned`.
246. **Hover the partner's knight.** The scroll says **their name** (the same name the name badge shows).
247. **Look at the compass** (the strip at the top). The partner's knight is **small there, like the other compass icons**, and large on the
     map.
248. **Ride a horse** (the one who is watched). The knight keeps up and turns with the horse.
249. **A rejoin keeps the colour.** The joiner leaves and comes back (or crashes and comes back): the **same colour**, and the name still shows.
     A different player taking the joiner's place gets the same colour with his own name.
250. **Three or four players, if you can** (host + 2–3 joiners): red, purple, blue, green, in the order they joined; a fifth gets the game's
     plain pin. (Slots are worked out on each machine in the order it first sees a player: with three or more, two machines may disagree on who
     is blue and who is green. Tell us.)
251. **The switches:** `mp_partner_marker off` (no marker at all), `mp_partner_marker_style flat` (one picture, facing north),
     `mp_partner_marker_style companion` (the game's plain pin, no name), the mod menu's "Partner on the map" and "Marker style (knight)".
252. **Report a bug → Collect logs** after a session (both). **The file fits in Discord** (a normal session is 2–9 MB; a very long one comes as
     numbered parts, `…-part1of3.tzst`, each under 19 MB — send all of them). It opens with 7-Zip: open the `.tzst`, then the `.tar` inside
     (or `tar --zstd -xf <file>`); `MANIFEST.txt` is the first file of part 1. Tick "include earlier launches" only if the problem was in an
     earlier run of the game. If the lines look thin around a `mark_odd`, tell us: everything within five minutes of a mark is kept in full.

Switches (console): `mp_partner_marker`, `mp_partner_marker_style rotating|flat|companion`, `mp_marker_dir_offset 0..7`,
`mp_marker_dir_flip on|off`, `mp_w171_status`.

## WO-173 — the joiner's blows as real hits in the host's world (0.49.0)

Two players, both on 0.49.0 (and the relay). Checked live on one machine against a synthetic joiner (host role) and a synthetic host
(joiner role); never between two real players. **The new path is switched OFF in 0.49.0** (one of its checks could not be run without two
people). Run items 253-258 twice: first as shipped (`mp_real_hits off`, the old damage with the knock-out fix), then with the host typing
**`mp_real_hits on`** in the console. **Type `mark_odd` at every miss.**

253. **The joiner fights a guard (or a bandit) while the host watches from a few metres.** On the **host's** screen the enemy **flinches,
     staggers, blocks and parries** like it does when the host fights it, and **never falls over from one blow** at full strength.
254. **The same moment on the joiner's screen:** the enemy flinches when the host's does, blocks when the host's does. Tell us any blow that
     looked different on the two screens (`mark_odd` on both).
255. **Knock an enemy down** (keep hitting until it drops, unconscious, not dead). It **falls on both screens** within half a second of each
     other and **gets up on both** when it wakes. **No T-pose** (arms straight out) afterwards.
256. **The enemy fights the joiner back with real swings**, not standing in a ready stance. Count roughly: does it attack the joiner as often
     as it attacks the host?
257. **Two players on one enemy, both hitting.** Every blow counts once (the enemy does not die suspiciously fast) and nobody's blows vanish.
258. **The host knocks down an enemy the joiner is not fighting** (both nearby). It falls and gets up on the joiner's screen too.
259. **The switch:** the host types `mp_real_hits off` mid-fight: the joiner's blows go back to the old way at once; `mp_real_hits on` brings
     the new way back. `mp_w173_status` prints the counters (WO173-STATUS here; MP-WO173-STATS in agent.log).
260. **Report a bug → Collect logs** afterwards (both machines).

Switches (console, host): `mp_real_hits on|off` (default off in 0.49.0), `mp_w173_status`.

## WO-174 — the host's stuck weapons, the repair, the clean-up (0.49.1)

Evidence: `docs/WO-174-findings.md`. Plain-words repair guide: `docs/HOW-TO-REPAIR-WEAPONS.md`. Everything below was run on one machine against
a synthetic partner; none of it with two real players. **Type `mark_odd` at every miss.**

261. **Punch your partner (friendly fire), then he leaves the game; save; load that save.** Your weapons draw. (`agent.log`: *peer … friendly-fire
     knockdown … its avatar left its skirmish*, then *… partner left …*; the game log: `WO174-CLEAN reason=leave`.)
262. **Play the petty-feud side quest's neighbours' brawl** (Tachov pub, `koneczabomysichvalek`) **with your partner nearby, hitting the
     brawlers.** He is **not pulled in** (no figure in the brawl; the game log shows `WO174-BRAWL … figure_kept_out=yes`); **the brawl ends by
     itself**, **the quest advances** as alone, and **your weapons draw afterwards**.
263. **A village brawl you join, then you leave the area and save.** Weapons draw after loading that save.
264. **`mp_repair_combat` on a good save** says *Nothing was wrong* and changes nothing (your sword comes out and goes away once: that is its test).
265. **If your weapons ever stick** (or after sleeping, stealing from someone, looting): `mp_repair_combat` — they come back — then save into a
     **new slot**, load it: they still draw (the re-apply `WO174-REPAIR step=autofix-after-load` is in the game log). **Tell us what you did
     before they stuck.** After a game restart type the command again.
266. **The mod menu:** Insert → Help → *Fix my weapons / stuck fight* does the same as the console command.
267. **The switches:** `mp_combat_autoclean on|off`, `mp_weapon_autofix on|off`, `mp_real_hits on|off` (on by default since 0.49.1; the WO-173
     items 253-260 apply again with it on). **Report a bug → Collect logs** afterwards (both machines).

## WO-175 — save safety: your own save untouched, backups, weapons after sleep / pickpocket / looting / fights (0.49.2)

Evidence: `docs/WO-175-findings.md`. The 0.49 weapon lock was a quest fistfight (the Tachov–Zelejov meadow fight) that ended without a
verdict and left the game's "unarmed only" rule on; the mod's save had landed in the first second of that fight. **Type `mark_odd` at every miss.**

268. **Host one of your worlds** (Start Game → your world), play a little, save, quit. The menu next time shows **"Together: <your world>"**;
     your **original playline's files are unchanged** (same sizes and times; `agent.log`: `WO175-COOP copy from=playline<a>/… to=playline<b>/…`).
     The joiner's world (Bring my character) also lands in a co-op playline, never in the joiner's own.
269. **No free save slot** (all five playlines hold your own worlds): hosting your world refuses with a plain message and loads nothing.
270. **Before every session the launcher backs up your saves** (status line *Your saves are backed up (N files)*; launcher log `WO175-BACKUP
     … verified=yes`). **RESTORE SAVES** (bottom bar) lists the backups; pick one, confirm, it restores (refused while the game runs); your
     current saves are backed up first (`WO175-RESTORE … verified=yes`).
271. **Sleep a night together, pickpocket someone, loot a chest together, save, reload** — your weapons draw with your own weapon key (1)
     and the HUD shows the weapon.
272. **A quest dialogue that leads into a fight** (or any dialogue/scene/fight) when the mod's 5-minute save is due: the save waits
     (`WO175-SAVE deferred reason=dialogue|scene|combat|arranged-fight`) and happens afterwards, never mid-scene.
273. **While your partner takes from a body or chest you share**, try to save from the menu: saving waits a moment (*"Saving waits a moment:
     the co-op mod is finishing a change"*), max 10 s.
274. **Load an old save with the weapon lock** (if you still have one from 0.49.x): the mod says it made a repaired copy and loads it; your
     weapons draw; the original file is still there unchanged (`WO175-HEALTH when=load result=repaired`).
275. **The switches:** `mp_coop_playline`, `mp_save_guard`, `mp_save_health` (all on). **Report a bug → Collect logs** afterwards (both machines).

## WO-176 — a quest's own fights stay the story's, fighting an armed enemy together, your own block (0.50.0)

Evidence: `docs/WO-176-findings.md`. **Type `mark_odd` at every miss.**

276. **The joiner fights an armed bandit with the host** (a camp or roadside bandit with a sword or axe). The bandit blocks and parries the
     joiner's blows about as often as the host's, and both screens show the same thing: a blow it blocks or parries shows its block on the
     joiner's screen too (the host's `agent.log`: `MP-W173 hit … verdict=blocked|pb`; the joiner's: `WO173-OUTCOME … verdict=blocked`, and
     when the host sent no reaction of its own `WO176-COPYBLOCK … row="…"`).
277. **The joiner blocks an enemy's blow themselves** (hold the block button as the enemy swings): it counts as blocked on their own screen —
     stamina, not health (`WO165-REPLAY … engine=blocked`). With `mp_victim_decides` on, a well-timed block is a perfect block
     (`engine=pb`) and the enemy recoils (`WO165-RECOIL … recoil="…"`). They are never knocked down by an NPC's blow.
278. **A quest's arranged fight with the partner nearby** (e.g. the Tachov–Zelejov meadow fight, or any quest fistfight): the partner is
     kept out of it — their blows do nothing to the fighters and they are told once *"This fight belongs to the story — your partner fights it."*;
     the fight ends the way the story says; afterwards both players' weapons draw with their weapon key (1) and the HUD shows the weapon.
     The host's `kcdmp-native.log`: `WO176-ARRANGED … quest=context`, `WO176-SHIELD figure=… on`; `kcd.log`: `WO176-PAUSE figure=… on`.
279. **Play your real world** (Start Game → your world) and save: afterwards **your original save files are untouched**; the menu offers
     **"Together: <your world>"** for the next session.
280. **The switches:** `mp_arranged_guard`, `mp_copy_blocks`, `mp_real_hits` (all on), `mp_victim_decides` (off: a joiner's held block
     still counts by the host's verdict). **Report a bug → Collect logs** afterwards (both machines).

## WO-177 — the partner's own soul, pickups, corpses and stations (no version yet: code only, no installer)

Evidence: `docs/WO-177-findings.md`. **Type `mark_odd` at every miss.**

281. **The partner stands near a village brawl** (Tachov's pub neighbours) **and near a quest fight** (the Tachov–Zelejov meadow fight): nobody
     turns on them for standing there, no villager attacks them, and the quest's own fight ends the way the story says. Afterwards both players'
     weapons draw with the weapon key (1). (`kcd.log`: `Skirmish event: SoulAdded on kcd2mp_N` at most once at their arrival, `(target kcd2mp_N)` none.)
282. **Drop items for each other, both ways** (drop an apple, a bandage, a weapon; the partner picks it up; then the other way round; 20 times):
     each drop appears for the other player, the first to pick it up gets it, the other's copy is gone, nothing is doubled or lost. Two items of
     the same kind dropped next to each other stay two items: picking up one leaves the other for both.
283. **The joiner picks up what the host dropped; one apple of two lying together; an item beside a corpse after looting it**: pressing the use key
     picks it up (or says plainly "Someone already took that." / "Asking the host..."), and it is gone for both screens. If the use key does
     nothing, type `mark_odd` and say what the prompt on screen read (a hold-to-steal prompt would be the finding).
284. **Corpses match after you both die and respawn**: stand where bodies lie, both die and come back; the bodies the host's world lost are gone
     on the joiner's screen within seconds; `WO177-CORPSE gone on the host: removed here npc=…` in the joiner's `kcd.log`.
285. **The joiner uses a station in the host's world**: cooks at a campfire (pan and pot), uses the anvil **while the blacksmith works** (the
     blacksmith's hammer does no damage to them: the host's `agent.log` says `MP-W177 tool swing … NOT forwarded`), washes at a trough or tub.
     **Never stuck**: if they cannot get away from a station, `mp_unstuck` (Help > "I'm stuck") frees them; pressed twice within 10 s it also
     does the game's own hit reaction with no damage (`WO177-STATION`, `WO151-UNSTUCK … station-end(…)`).
286. **Start a join, a leave or a repair load while an enemy is still hitting you**: the load waits ("A fight is on: the load waits until it is
     over.") and starts about 10 seconds after the last hit; it is never issued mid-fight (`MP-W177 load why=… waiting`).
287. **The switches:** `mp_figure_soul`, `mp_corpse_sync`, `mp_tool_swing` (all on). **Report a bug → Collect logs** afterwards (both machines);
     the native logs of the last five launches are in the bundle (`kcdmp-native.1..5.log`).

## WO-179 — watching videos together, being brought along, no long black screens (0.50.7)

288. **A story video together**: when one of you starts a pre-rendered video, both screens play it and it starts and ends together.
289. **Skip**: one of you presses skip. Say what happened on both screens (the "both must press" rule is not built yet; the press is
     logged as `WO179-VIDEO skip pressed`).
290. **The partner is talking, fighting, riding or far away** when a video or a story trip starts: he ends up beside the other player when
     it is over, on his feet, nobody chasing him (`WO179-BRING ... placed`, `WO179-LADDER cleared=...`).
291. **The meadow trip** (the feud quest) and **the kidnapping that ends with Henry tied up**: you both end up in the same place, and the
     captors leave the partner alone (`WO179-IGNORE start`).
292. **The same with the joiner starting it**: the host is brought along in his own world; if the scene skipped time, the clock moves
     once for both (`WO179-TIME ... applies it once`).
293. **No black screen longer than a moment** on either screen. A talk that cannot start ends within about 4 seconds with a message
     (`WO179-RELEASE`); a stuck scene end lets go within about 2 seconds.
294. **Type `mark_odd` at every miss.** Switches (all on): `mp_scene_guard`, `mp_scene_bus`, `mp_scene_video_shared`,
     `mp_scene_bring_along`, `mp_scene_partner_ignored`, `mp_scene_fight_clear`.
295. **Kill the joiner's agent mid-session** (or close the launcher hard): within about 20 seconds the joiner's game can save again.

## WO-183 -- the 0.50.8 playtest patch: videos only, the joiner's saves, arrests, haggling, block icons, the T-pose (0.50.9)

296. **A conversation followed by walking, riding or fast travel teleports nobody.** Talk to a villager (the host, then the joiner), end it,
     then walk 200 m, ride, fast travel, die and respawn. Nobody is ever pulled to the other player; the log has `WO179-JUMP ... logged only`
     lines and no `WO179-RELOCATE` or `WO179-BRING`. (Items 290-292 of WO-179 are gone: scenes that move Henry are not shared any more.)
297. **After a shared video you stand together, never inside someone's house.** Start a story video (either of you); both watch it. When it ends
     the partner is beside the one who started it, on open ground (`WO179-LAND ... placed ... public ground`). Try it with the one who started
     it standing inside a building: the partner stays where he is, sees "Couldn't bring you to your partner, they're somewhere private.", and
     the log says `NOT placed (no public spot ...)`. Never moved while arrested.
298. **The joiner haggles to the end.** Haggle with a trader (a shop keeper such as the one in Tachov), start to finish, 3 times. The talk is
     never cut at 4 seconds, no "That conversation couldn't start here" appears for a talk that is running. (A trader that never opens the
     talk at all still ends within about 4 seconds with that message: that is right.)
299. **The joiner is arrested, and afterwards his saves work.** Trespass on purpose, get arrested (pay or talk out of it). Then fight, loot, and
     have the host save. `agent.log` never says `another save is pending` or `REFUSED the snapshot`; every host save has a `PAIRED host save`
     line (or `WO183-SNAP owed` then `taken ... late`).
300. **The host saves (also right after a fight), the joiner reloads, and his inventory is there.** The joiner picks up something, the host saves
     (also at once after a fight, and while the joiner stands in a fade or a scene: `WO183-SNAP owed` is fine, it is taken a few seconds
     later), the joiner leaves and rejoins: the item is there, not the join-time inventory.
301. **The shield and swords icons show on blocks.** Let an enemy hit you while you hold block: the green shield (and the blue swords on a
     perfect block) show on every blocked blow, not once per fight. `WO165-REPLAY ... engine=blocked` per blocked blow; no
     `fallback=victim-blocking` while the block is held. Switch: `mp_replay_blocked on|off`.
302. **An enemy switches from one player to the other with no T-pose.** Two of you fight the same enemy; it turns from one to the other. The
     joiner's copy keeps fighting, no stiff T-pose, no `engage off ... left combat` in the middle (`MP-W132 ... combat=0 for a moment`
     is the debounce); `WO164-SWEEP ... SKIPPED: the copy is in combat` is right.
303. **Type `mark_odd` at every miss.** Switches (all on): `mp_scene_bus`, `mp_scene_video_shared`, `mp_scene_bring_along` (videos only now),
     `mp_scene_partner_ignored`, `mp_scene_fight_clear`, `mp_replay_blocked`.

## WO-184 — the joiner's progress is saved, every time (0.60.0)

304. **The items are there after a reload, three times in a row.** The host drops a few items, the joiner picks them up, the host saves
     (the host sees "Partner saved." a few seconds later), the joiner quits, starts the game and rejoins: every item is in the joiner's inventory.
     Do it three times, with more items each time. `agent.log` (joiner) has one `PAIRED host save seq=N` per host save.
305. **The same right after the joiner talked to someone.** The joiner talks to a villager (or the talk does not even start), and the host
     saves at once, while the talk runs or just after: still `PAIRED` within a few seconds, and the items are there after the rejoin.
306. **The same right after a fight.** The joiner fights (shouting and getting hit), the host saves during the fight or just after: `PAIRED`
     within a few seconds. This is the case that lost the joiner's progress on 0.50.8 and 0.50.9.
307. **`mp_save_status` on both machines.** After a host save: the joiner's line says "Your character was saved with your host's save #N
     (… ago). Nothing waiting."; the host's says the partner was saved with its save #N.
308. **The joiner quits right after picking something up.** No host save in between: the joiner picks an item up, opens the menu (Esc),
     Quit, Yes, as fast as they like. The launcher says "Your character is saved, up to the moment you quit."; after the rejoin the item is
     there. (Alt+F4 or closing the window skips the menu: then what was picked up since the last save is gone, and the launcher says so.)
309. **Nothing is lost silently.** If any line says `WO184-SNAP STUCK`, `WO184-HEALTH ... NOT paired`, or the game shows "Your character
     can't be saved right now", type `mark_odd` and send the logs. Switches (all on): `mp_save_on_leave`, `mp_save_recover`,
     `mp_partner_saved`.

## WO-189 — the joiner never stays stuck in the host's quest; his saves never stall (0.60.6)

320. **A whole session without the host saving.** Play 30 minutes or more; the host never presses Home. The joiner quits (Esc, Quit),
     starts again and rejoins: everything the joiner picked up is still there. The joiner's `agent.log` has
     `MP-HENRY joiner: PAIRED ... (the joiner's own snapshot)` about every 10 minutes and one at the quit.
321. **`mp_save_status` stays fresh.** The joiner types `mp_save_status` now and then: the last save is never older than about 10 minutes,
     and the line says when the next own save comes. Nothing says "pending" for more than a minute.
322. **The host is reminded.** After 15 minutes in a session without the joiner being saved, the host sees "Your partner hasn't been saved in
     15 minutes - press Home to save." Once per 15 minutes; a save of the joiner starts the clock again.
323. **A quest with an arena never leaves the joiner stuck.** The host starts the master-strike tutorial (the fight trainer at the arena),
     the joiner stands by. The joiner sees the same arena fence as the host (it comes and goes with the host's), and after the duel's last
     talk nothing hangs: no endless black screen, no frozen scene. Lines on the joiner: `WO189-PROFILE host '+masterstrikeTutorial_arenaBarrier'
     ... enabled`, and if a scene or the end fade hangs, `WO189-POSITIONING ...` within 20 s or `WO189-CUTSCENE stuck ... released` within 10 s.
324. **No stuck save, ever.** If any line says `WO189-WATCHDOG`, `WO189-SAVE cancelled` or `WO189-READY not ready`, note what was happening
     (a talk, a scene, a fight). The joiner is told once if a stuck save was cleared; both of you if it could not be.
325. **Type `mark_odd` at every miss.** Switches (all on): `mp_joiner_autosnap <minutes>`, `mp_save_reminder <minutes>`, `mp_profile_mirror`,
     `mp_cutscene_release`, `mp_positioning_bound`.

## WO-191 — a restarted joiner's swings count, fights are never reset, attackers are always visible, no long black screen (0.60.7)

326. **The joiner restarts the game, rejoins, and fights.** Fight a little first. Then the joiner quits the game completely, starts it again,
     rejoins, and fights an enemy (or a villager, crime off). On the host's screen the joiner's figure swings each time and the enemy reacts
     normally to their blows: it does not just fall over. Lines on the host: `WO191-ACTION forgot sender=1 why=disconnected` when they left, and
     no `MP-ACTION section=inbound reject=StaleOrDuplicate` afterwards; if a `WO191-ACTION rejected kind=...` line shows, send the logs.
327. **An enemy fighting the joiner never freezes or resets.** The joiner draws a weapon, walks up to an enemy, looks at it and fights it a
     while, then stops and looks at it again. Its fight never stalls and it never snaps back into a villager pose mid-fight. Lines on the
     joiner: `WO191-SWEEP-LUA npc=<it> ... SKIPPED` (once per fight) instead of a `WO164-SWEEP-LUA npc=<it>` while it fights.
328. **A dog or wolf attacking the joiner is visible and moves.** Find a village dog (Troskowitz has some) or a wolf pack and let it go
     for the joiner. On the joiner's screen the animal is there, runs at them and bites: no damage from nothing. Lines: host `WO191-BIND pinned
     npc=<it> class=Dog` (or `... already streamed`), joiner no `WO191-GHOST by=<it>`. If a `WO191-GHOST` line shows, type `mark_odd`.
329. **No long black screen after talking to a trader.** The joiner talks to a trader, closes the talk, and talks to them again at once (and
     opens the trade). The screen is never black for more than about 3 seconds. Line on the joiner if it would have stayed black:
     `WO191-FADER released after 3.0 s (...)`.
330. **Type `mark_odd` at every miss.** Switches (all on): `mp_action_forget`, `mp_sweep_fight_guard`, `mp_attacker_bind`, `mp_fader_bound`.

## WO-190 — both of you see the same thing: walking, fighting, working, riding, standing (0.60.9)

331. **Watch each other move.** Take turns while the other one only watches: walk, run, sprint, stop, turn on the spot, crouch, jump, climb
     over something, sit on a bench and stand up, open a door and a chest, pick something up, loot a body, drink, eat, light a torch, read,
     mount a horse, ride at a walk, a trot and a gallop, and get off. The watcher sees the same move at the same moment: never a figure gliding
     along in its standing pose, never one walking on the spot after it stopped, never a T-pose (arms out, stiff). Type `mark_odd` at anything
     that looks different from what you did.
332. **Watch each other work.** Each of you uses a station the other can see: the alchemy table, the grindstone, the smithy, picking herbs,
     lockpicking, a game of dice. The watcher sees the work animation, not a figure standing still.
333. **Watch each other fight, at day and at night.** Fight bandits or villagers (crime off) with a sword, with a mace or an axe, and with
     fists; once in daylight and once after dark. The watcher sees each swing, block, dodge, kick and hit reaction as its own move, and the
     fighting guard in between. At night an enemy that fights keeps its weapon on the joiner's screen: never a torch in its hand, never a
     T-pose. Lines on the joiner: `WO190-TORCH npc=<it> held a torch ... put away` (a torch was taken out of a fighting enemy's hand).
334. **An enemy's every swing shows on the joiner's side, even fast ones.** A guard or a bandit that swings again right after it was hit (or
     after its last swing) still winds up on the joiner's screen, and the joiner gets the green shield for it. Lines on the joiner:
     `WO190-TAKEOVER npc=<it> the copy still read 0x20 ... the new swing winds up`, and far fewer `WO166-STRIKE ... refused` than before.
335. **An enemy switching between you keeps moving.** One enemy, both of you in reach: it turns from the joiner to the host and back. On the
     joiner's screen it keeps fighting the whole time and its blows on the joiner are always animated. Line on the joiner:
     `MP-W190 engage hold <it>: its host NPC turned to the host -- it stays engaged and animating 3 s`.
336. **A clinch or a throw shows as a blow.** When an enemy grabs the joiner (a clinch, a throw), the joiner sees at least its plain attack,
     never nothing. Lines on the joiner: `WO167-SYNCFALLBACK ... why=it-fights-me(the-clinch-gate-refuses-its-half)` or `WO190-CLINCH`.
337. **Watch the town.** Stand in a village by day and again in the evening, near people working, sitting, talking and leaning. The joiner sees
     them do what the host sees them do (the same work, the same seat), standing still where they stand: no twitching on the spot. Line count to
     send: the `Animation-queue overflow` lines in both `kcd.log`s (fewer on the joiner than before).
338. **Type `mark_odd` at every miss.** The new lines `WO190-ANIM` (on by itself during fights: what each figure and enemy had on at that
     moment) and `WO190-IDLE` (a fighting body with no animation got its guard back) are what we look at after a `mark_odd`. Switches (all
     on): `mp_anim_idle_watch`, `mp_anim_standin_weapon`, `mp_copy_weapon_hold`, `mp_anim_requeue_gate`, `mp_anim_gait_hold`,
     `mp_copy_swing_takeover`, `mp_engage_hold`; `mp_anim_scope_log auto` (the default); `mp_anim_trace on` logs every animation change
     (research, off).

## WO-192 — saves, simplified (the next build)

339. **The joiner's progress survives a cutscene.** Play past an in-game cutscene together, the joiner picks something up, then the host saves
     (Home), or both just play on until the host's autosave (every 30 minutes of play; it waits while a fight, a conversation or a cutscene
     runs). The host reloads. The joiner has the item. Lines on the joiner: `MP-HENRY joiner: PAIRED host save seq=N` after every host save,
     `WO192-SAVE during a scene` when one was taken while a scene ran; on the host: `WO192-AUTOSAVE done` (or `waiting why=...`).
340. **The partner's marker moves.** Walk apart. Each of you opens the map and watches the compass: the partner's marker follows them (it no
     longer stays where it first appeared and only turns). Line on each machine's `kcdmp-native.log`: `WO192-MAPMARK pin ... moved to`.
341. **After a shared sleep, both wake together.** Sleep (and once, wait) together. If the joiner's screen comes back first, it stays dark
     until the host wakes (15 seconds at most), so the world never jumps under the joiner's eyes. Line on the joiner: `WO192-WAKE released
     after N s (the host woke)`.
342. **Washing at a trough never leaves anyone stuck.** Each of you washes at a trough, once while the other is washing at the same trough.
     Both can walk away afterwards and the host can save. Line if the game's wash never ended: `WO192-STATION ... ended the game's own way`.
343. **Type `mark_odd` at every miss.** A mark no longer takes a screenshot. Switches (all on): `mp_host_autosave`, `mp_host_autosave_min`
     (30), `mp_wake_hold`, `mp_station_release`. Gone: `mp_joiner_autosnap`, `mp_save_reminder`, `mp_mark_shot`.

## WO-195 — dead enemies fall, sneaking past sleepers, a body's key (the next build)

344. **An enemy that dies while fighting the joiner falls, and the fight ends.** The host (or the joiner) kills an NPC or a bandit that is
     fighting the joiner. On the joiner's screen it drops to the ground within about a second (never stays standing or slumped), and the
     joiner's combat indicator goes away a moment later if no one else is fighting him. Lines on the joiner: `WO195-RELEASE npc=<it>`,
     `WO195-FALL npc=<it> ... living=no(ragdoll)`, `WO195-PLAYER after npc=<it> combat_mode=false`. A `STILL in combat` line: send the logs.
345. **An enemy killed far from the joiner lies dead when he gets there.** The host kills someone 100 m or more from the joiner; the joiner
     walks there: the body lies on the ground (same `WO195-FALL` line, when the host's death reached his game).
346. **The joiner sneaks into a house at night past people in bed, and nobody raises the alarm.** At night the joiner crouches, no torch,
     into a house where people sleep, and walks past their beds. No one wakes, no guard comes, no fine for either of you. The host's
     `kcd.log` has one `WO195-WITNESS ... asleep=1(...) ... ignored=asleep` line per sleeper. Then the joiner stands up with a torch out in
     daylight in front of someone in the same house: that is seen (`counted=seen`).
347. **The joiner loots a body's key and opens its chest with it.** Kill an NPC who has a home (a villager); the joiner loots the body: the
     key is on the loot screen and stays in his pack after he takes it (it is no longer taken away again); he opens that NPC's chest with
     it. The host then loots the same body: there is no second key. Lines: joiner `WO195-LOOT screen-item`, host `WO195-LOOT host-screen
     ... -> ok`, later `host-dup` if the host's screen made one.
348. **Type `mark_odd` at every miss.** Switches (all on): `mp_dead_release`, `mp_stealth_witness`, `mp_body_loot_host`.

## WO-195B — sneaking, locks, takedowns and thieving the way single player has them (the next build)

Both of you play it at night, one session. The joiner does each of these as he would alone; the host stays out of the way (not in front of
the people, not noisy), then checks the result. At the end both say, for each item, whether it felt like single player. Evidence:
`docs/WO-195B-findings.md`.

349. **The joiner sneaks past people at night.** Night, no torch. The joiner crouches and walks slowly into a house where people sleep and
     where others are awake, and out again; then does the same standing up and running past the same people. Crouching and slow: nobody wakes
     or reacts, no guard comes. Standing and running: those near him wake or turn and react, as they would for the host's own Henry. Host
     `kcd.log`, per person near him: `WO195-WITNESS ... vib=<his visibility> noise=<his 2 s footstep peak> ... counted=seen|heard|woken` or
     `ignored=asleep|far|unseen`; a woken sleeper also has `WO195-WAKE sound=3` and gets up. Still beside a sleeper: `noise=0.00 ... ignored=asleep`.
350. **The joiner picks a lock and opens a door and a chest with a key.** The joiner picks a locked chest, and a locked door, and opens one more
     with a key he owns. Afterwards, on the host: the same chest and door are open with no lockpick prompt. The host saves (Home) and
     reloads: both are still unlocked, and a lock the joiner did not touch is still locked. Lines: host `WO195B-CHEST ask from <n> ...
     unlocked (match=name)`, `WO151-DOOR ask from <n> ... unlock=true: applied`. Picked in front of someone awake: they react as they would
     to the host.
351. **The joiner takes someone down.** From behind and crouching, the joiner knocks out an awake person (the game's takedown). On the host
     the person drops unconscious (not dead), lies a few minutes, and gets up on his own, as in single player. Seen by someone, a crime is
     raised against the joiner (and, in joint mode, the host); unseen, none. Lines: host `WO135-TAKEDOWN host ... done`, `WO135-KOWAKE
     npc=<it> in=<s>`, then `... it gets up`; `WO139-JUDGE ... witnesses=<n>`.
352. **The joiner pickpockets in the dark.** The joiner pickpockets a villager from behind, crouching, at night: it works and nobody reacts.
     Then face to face in the light: the villager notices and it is a crime. Lines: host `WO195-PICK src=<n> npc=<it>`, then `WO139-JUDGE ...
     witnesses=0 ... nobody saw it` or `witnesses=1 ... a crime in this world`.
353. **Did it feel like single player?** Both: for each of 349 .. 352, say yes or no, and what felt wrong. Type `mark_odd` at every miss.
     Switches (all on): `mp_stealth_witness`, `mp_wake_engine`, `mp_chest_sync`.

## WO-194 — story cutscenes together (the next build)

One session at a quest step just before a story cutscene played in the engine (the kind with "Skip part / Skip all", not a video), both of
you near it. Evidence: `docs/WO-194-findings.md`.

354. **Trigger it standing together: both see it from the start, at the same moment.** Walk into the scene together (or let one of you
     trigger it). Both screens go black briefly and the same scene starts on both, in step (talk about what you see: the same line at the
     same time). Joiner `agent.log`: `WO194-SCENE ... runs here`, and either `WO194-START ... started` (the host's started it) or
     `WO194-HOLD ... released: the host's copy runs` (his own trigger came first); then `WO194-CLOCK ... aligned`.
355. **One of you skips: it skips for both.** In the scene, one of you skips a part (press skip twice), later the other skips the rest. Each
     skip jumps both screens to the same next part; the last one ends the scene on both. Lines: `WO194-SKIP this player skipped ...` on the
     one who pressed, `WO194-SKIP player <n> skipped ...` on the other.
356. **Afterwards you both stand where the quest continues; nobody is stuck in the cutscene view.** The scene ends and both of you can move
     at once (no long black, no frozen camera), standing next to each other, not inside each other. Joiner `agent.log`: `WO194-END ...
     positioned 0.x s after its content` (well under 2 s) and `WO194-NUDGE`. If the joiner's screen stays black or frozen, type `mark_odd`.
357. **The host saves right after and the joiner reloads: his things are there.** The host saves (Home) right after the scene; the joiner
     quits and rejoins. His inventory and his place in the story are as at the save. Joiner `agent.log`: `PAIRED host save ...`.
358. **Type `mark_odd` at any difference** (the scene on one screen only, out of step, a skip that skipped one screen only, someone left
     standing in the scene's spot). Switches (all on): `mp_scene_ingame_shared`, `mp_scene_ingame_hold`, `mp_scene_ingame_ffstop`,
     `mp_scene_ingame_nudge`, `mp_scene_ingame_figure_hide`, `mp_scene_ingame_skip_both`, `mp_scene_ingame_time_host`.

## 0.61.0 — the animation-sync release: everything since 0.60.9, in one session

One session, both of you on 0.61.0, about an hour: a village by day and the same village at night, a sword fight, a brawl, a story
cutscene if one is near, a host save and a rejoin. It covers WO-192 (saves), WO-195 / WO-195B (dead enemies, stealth, loot, locks,
takedowns), WO-194 (cutscenes together) and WO-197 (both of you see the same moves); the sections above hold their details and log lines.
**Type `mark_odd` at every difference you see**, then tell each other what you saw. The mod takes no screenshots any more: if a picture
helps, take your own (F12) and send it. Evidence: `docs/WO-197-findings.md`.

**Saves**

359. **The host saves; the joiner rejoins with everything.** Each of you picks something up, the host saves (Home), the joiner quits and
     rejoins: their things and their place are as at the save (joiner `agent.log`: `PAIRED host save`). Once more with the host saving during a
     cutscene: it pairs too.
360. **The host's own 30-minute save.** Play 30 minutes without saving: the host's game saves by itself (no schnapps used), never in the
     middle of a fight, a talk or a scene, and the joiner's character is saved with it (`PAIRED` on the joiner).
361. **The partner's marker follows the partner** on the map and the compass, the whole session.
362. **Sleep or wait together:** the joiner's screen stays dark until the host wakes (15 s at most). A trough or a workbench never leaves
     anyone stuck or unable to save.

**Story cutscenes** (if one is near)

363. **Both see it from the start, at the same moment; one skip skips it for both; afterwards both of you can move at once, next to each
     other.** Then the host saves and the joiner rejoins: their progress is there.

**Dead enemies, stealth, loot, locks**

364. **An enemy that dies fighting the joiner falls on the joiner's screen within a second, and the fight ends.** One killed far away lies
     dead when the joiner gets there.
365. **At night the joiner sneaks past sleepers and awake people:** crouching and slow, nobody reacts; standing and running, they react as
     they would for the host. A walking joiner beside a sleeper can wake him; a still one cannot.
366. **The joiner loots a body's key and opens its chest;** the host then finds no second key. **A chest or a door the joiner unlocks stays
     unlocked in the host's world,** also after the host saves and reloads.
367. **The joiner knocks someone out from behind:** the person lies unconscious (not dead) and gets up on their own a few minutes later.
     **The joiner pickpockets** in the dark: nobody reacts; face to face in the light: a crime.

**Fights: both of you see the same moves**

368. **A blocked stab recoils on both screens.** Each of you, with a longsword, fights a bandit that blocks and stabs into its guard
     (the attack aimed at the bottom of the combat cursor): when your stab is blocked or perfect-blocked, your figure on the partner's
     screen jerks back the way yours does. A blocked slash has no recoil in the game: it runs on, on both screens. Lines: sender
     `MP-ACTION section=outbound kind=AvatarRecoil`, the other `kind=AvatarRecoil ... result=ok`.
369. **A brawl: paired punches and throws play on both bodies.** One of you fist-fights a villager; the other watches. When a punch combo or
     a throw lands, the villager plays the receiving half (stagger, thrown), not only the attacker its own half. The same when a villager does it to
     the partner. Lines: `MP-W197 pair ... -> played`.
370. **A clinch shows both halves.** Get into a clinch (a brawl, or longswords): both bodies lock together on the other screen too, and
     each clinch move plays on both. Lines: sender `kind=AvatarClinch`, the other `dispatch=rows` / `dispatch=pair`.
371. **Knocked out in a brawl:** the one knocked out falls on the partner's screen and lies there as long as they lie; when they get up,
     their figure stands where they woke (a fresh figure: the game's own get-up animation is not shown yet). Lines: `MP-W197 peer <n> KNOCKED OUT`,
     then `woke`; `WO197-KO avatar=... down`, `WO197-KO-RISE`.
372. **Kicks, stop moves, master strikes:** use them; each shows as itself on the other screen, and an enemy swinging again right after its
     last swing always shows its wind-up.

**Everyday moves** (look at your partner, and at the villagers around you, on the joiner's screen)

373. **Doors:** a door one of you (or a villager) opens or closes moves on the other screen too (joiner `kcd.log`: `WO197-DOOR ... -> moved`).
374. **Seats and beds:** sitting on a bench, a chair or the ground, lying in a bed, getting up: your partner, and the villagers at their
     benches and beds, show the same on the joiner's screen, about as soon as on the host's.
375. **Hand items:** a torch, a drink, food, a potion, a book, the bow: what your partner and the villagers hold shows in their hands.
376. **Work stations and minigames:** grindstone, alchemy, smithing, digging; villagers chopping, hoeing, at their spindles: the same on both
     screens.
377. **Riding:** mounting, dismounting, walk, trot, gallop: your partner on their horse looks the same on both screens.
378. **Gestures and emotes:** villagers turning to the one they talk to, your partner's whistle.
379. **Type `mark_odd` at every difference.** Switches (all on): `mp_anim_recoil`, `mp_anim_paired`, `mp_anim_clinch`, `mp_anim_knockout`,
     `mp_copy_guard_engine`, `mp_anim_seats`, `mp_anim_work`, `mp_anim_routines`, and the ones the sections above name.

## 0.61.5 — one world, one story (WO-200)

The host's world decides; the joiner's game shows it. The joiner's conversations now run in the host's world too, his quest clocks no
longer fire on his own, the host's people show the host's health, stamina and horses, and the clock follows the host's. Evidence:
`docs/WO-200-findings.md`. **Type `mark_odd` at every difference, and say out loud what you saw.**

**Tester script** (one session, about an hour; start from a save before Sir Semine's departure in "The Jaunt", the host near Semine's yard
in the morning; both of you on 0.61.5; the joiner joins there)

1. *Joiner:* talk to Semine and pick the answer that starts the ride. *Both:* watch the yard. Mark if the lords do not set off on both
   screens within a minute of the conversation's end.
2. *Both:* ride with the lords to Troskowitz (do not skip time). Mark at every moment a lord is off his horse on one screen and on it on the
   other, or slides along the ground.
3. *Host:* let the clock pass 13:00 on the way (it will). Mark if either of you sees "they gave up on you" lines, or the lords turn back.
4. *Host:* do the training bout with Semine; *Joiner:* watch from close by. Mark if Semine falls on the joiner's screen while standing on
   the host's, or looks exhausted on one screen only.
5. *Joiner:* walk up to Semine and Suk in Troskowitz and start the conversation with both (the trialog). Mark if it does not start within
   a few seconds, or the screen goes black.
6. *Each of you:* one conversation that gives a reward (money or a reward line), and one quest-item handover. Mark if the reward reaches
   the one who did not talk, or the quest item is in one inventory and not the other after a minute.
7. *Host:* save (Home), then load that save. *Joiner:* rejoin when asked ("Continue with your character (saved … ago)"). Mark if Semine or
   Suk is missing on the joiner's screen after the rejoin.
8. *Host:* start a fight right after the rejoin (a sparring match, or a bandit). *Joiner:* watch the host's figure. Mark if the host's
   swings stop showing after a clinch.
9. *Both:* walk through the village by day and talk to five villagers holding something (a broom, a scoop, firewood). Mark any talk that
   does not start within a second or two.

**Checklist**

380. **The departure runs once, in the host's world.** After the joiner's departure conversation the lords set off on both screens; the
     host's `agent.log` has `MP-W200 host: replay ... -> ran` and its `kcdmp-native.log` `WO137-CHANGE ... closure=`; the joiner's has
     `MP-W200 joiner: the host RAN replay`, never `the host REFUSED replay`.
381. **No ride 1 or ride 2.** Past 13:00 no `hrac_se_na_to_vykaslal` change on either machine; the joiner's `kcdmp-native.log` has
     `WO200-QUIET joiner: ... SaS_docekali :: OnTimeOfDay not run`, never `WO137-CHANGE ... CekaniNaNavratSeminaSuka`.
382. **Riders follow the host.** On the joiner: `WO200-RIDER mount rider=tsem_seminsr ...` and no `WO200-RIDER dismount` while the host's
     lords ride; no "the streams parted" lines.
383. **The bout shows the host's health and stamina.** On the joiner: `[npcdmg] in: the host's blow ... not applied here`, never a copy
     falling between the host's blows.
384. **The joiner's own conversations with two people start.** On the joiner: `WO200-TWINS wake npc=<second speaker>` at the trialog's start,
     and no `WO179-RELEASE conversation made no dialogue twin` line.
385. **Rewards to the talker, quest items to both.** A reward of the joiner's conversation appears only in the joiner's inventory (host:
     `WO200-GATE host: ... (talker-only-host ...)` lines); a quest item reaches both, and the joiner's `kcd.log` shows no `WO200-QITEM differs` that
     lasts past a minute.
386. **The lords stay after a rejoin.** After the host's reload and the joiner's rejoin both lords are there on the joiner's screen; the
     joiner's `kcd.log` may say `WO200-STANDIN forget` or `kept`, never a lord missing.
387. **The figure keeps fighting after a clinch.** The host's swings after a clinch all show on the joiner's screen; the joiner's
     `agent.log` has `dispatch=none (StartSyncGuard refused: no clinch rows ...)` and `WO200-CLINCH end`.
388. **Village talks start at once.** Talks with villagers holding tools start within a second or two, with no "This person can't talk to
     you right now"; their hands may show empty on the joiner's screen (a known gap: `mp_copy_neutral_always off` brings the old hands back).
389. **Saves wait only for real reasons.** A save during Henry's stomach growl is not delayed; a delayed save names its reason on screen
     and logs `WO200-SAVEWAIT reason=...` every 5 s.
390. **Noise:** the joiner picking a lock near a sleeper on the host's side wakes the sleeper there too (host `kcd.log`:
     `WO200-NOISE kind=lockpick ... ok`).
391. **One clock.** The joiner's clock stays within a game-minute of the host's (joiner `agent.log`: `WO200-CLOCK joiner: this clock's ratio`
     lines, the error shrinking). Switches (all on): `mp_s1_replay`, `mp_s1_route`, `mp_qitem_mirror`, `mp_joiner_quiet_clocks`,
     `mp_joiner_no_puppet_quests`, `mp_joiner_scoped_twins`, `mp_copy_health_from_host`, `mp_rider_from_host`, `mp_copy_neutral_always`,
     `mp_world_clock_from_host`, `mp_stream_dogs`, `mp_joiner_noise`; `mp_w200_status` prints them all.

## Logs to send afterwards

Both machines: Report a bug in the launcher. Since 0.48.9 it collects the current launch by default as a small file (or a few numbered
parts) that fits a Discord message; tick "include earlier launches" for the logs of the launches before (the launcher keeps the last six), so a
restart does not lose the logs of a crash.
