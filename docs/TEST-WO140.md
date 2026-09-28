# What's new since 0.40.0 (WO-136 to WO-140) — draft

**Kingdom Come: Together.** Unofficial. Not affiliated with or endorsed by
Warhorse Studios.

*Draft for the next installer (WO-141 finishes this page and builds it). The
checks to do together are in `docs/TWO-PLAYER-CHECKLIST.md`.*

## How to join now

- **The partner waits at the main menu.** Start the game from the launcher and
  **don't load a save**: click CONNECT at the menu and you join your host's
  world by yourself. The launcher says so. Only the host loads a save first.
- **If you load your own save by mistake,** the game and the launcher tell you
  plainly: "You loaded your own save. To play in your host's world, quit the
  game, start it again and wait at the main menu." Until you do, you are in
  your own world: you still see the host's figure, but nothing of the host's
  world happens in yours (no people, fights, loot, quests or crimes of the
  host's, and nobody pulls you back to the host).

## Playing together

- **Sleeping together.** When one of you picks "Sleep" at a bed (or waits),
  the other is asked first: "Moose wants to sleep. Sleep too?" — F11 yes, F12
  no. Until the answer comes, nothing happens but "Waiting for other
  players...", and you don't lie down. If the answer is yes, you lie down and
  choose how long, and when you do, the other player's screen shows the same
  sleep screen for the same hours, wherever they stand — no bed needed. You
  wake together, you are both rested, and the time of day is the same on both
  screens. If the answer is no (or nothing in 30 s), nothing happens: "Other
  players are not ready to sleep yet!".
- **One clock for both.** The host's clock is the world's. The partner's clock
  can no longer run ahead of the host's (before, the partner's own sleep moved
  only their clock until the next join); if it ever does, it is brought back at
  once.
- **Nobody's menu stops the other's world.** The inventory, the map, the
  journal, the ESC menu, a conversation or a cutscene of one player no longer
  freezes the other's world: people keep walking on both screens. In a session
  your own Henry keeps standing in the world while you read a menu, as in an
  online game. (Alone, the game pauses as it always did.)
- **Quests are shared.** Main quests and side quests like "Find Mutt!" live in
  the host's world: when either of you makes a step, both journals update. The
  partner can talk to people; the conversation counts for both. A step counts
  once, rewards are your own, and dead stays dead.
- **Crimes count in the host's world.** If the host's people see the partner
  steal, pick a lock, take a horse or trespass, it is a crime there, and the
  host's guards deal with the partner — the game's own arrest, fine, punishment
  or fight. Violence is answered with force. Punishment no longer skips hours.
  You can't rob each other, and nothing between the two of you is a crime. The
  host's horses are free to ride for the partner.
- **The world is there for both of you.** The host's animals appear for the
  partner too; enemies fight both of you (and keep fighting the partner if the
  host goes down); knocked-out enemies go down on both screens; whoever rides a
  horse owns it; clothes, torches and crouching show on the other screen;
  "Someone already took that" only appears when the other really took it.
  Joining is calmer: the loading screen ends normally.

## Off switches (the console, `~`)

`mp_sleep_vote off` (each of you sleeps alone again), `mp_quest_sync off`
(host: quests not shared), `mp_crime_shared off` (host: crimes not shared).
`on` switches each back.

## Still known

- The partner does not see animals' bite animations yet.
- A surrendering enemy does not kneel on the partner's screen yet.
- A sleep in a bed that isn't yours (a stranger's house, a barn) can be woken
  early by the owner or a guard, as in the game; sleep somewhere you're allowed.
- If you are already well rested, the game cuts a sleep short (or refuses it),
  also for the one who said yes.
- Waiting (instead of sleeping) together has not been tried by two players yet:
  the other screen should show the game's own "Waiting" wheel — tell us.
