# The mod menu

Kingdom Come: Together has one menu inside the game for its own settings. You do
not need the console any more (it still works, see the end of this page).

## Opening and closing it

- In the world, press **Insert**. Press **Insert** again to close it. The game's
  own **Esc** (or opening the inventory, the map or the journal) closes it too.
- While it is open: **PgUp** and **PgDn** choose an item, **End** changes it.
- You can keep walking and playing while it is open; the game's own keys work as
  always.
- It does not open during a cutscene, a conversation, a loading screen, while one
  of the game's own menus is open, or while you are dead or knocked out. If one of
  those starts while the menu is open, the menu closes.
- It appears on the right of the screen, out of the way of the middle.

Each line shows a setting and its value. The chosen line starts with `>`, and
the bottom of the menu says in one line what the chosen setting does.

## What is in it

### Display (each player's own)

| Item | What it does |
|---|---|
| Partner's name badge | Your partner's name (and health) above their figure. On or Off. |
| Ping and clock line | The line in the top left corner with your ping and the clock difference. On or Off. |
| Clean screen | Hides everything the mod draws: its lines, the ping line, the name badges and its messages. For screenshots and recording. The menu itself still opens, and its first line tells you the clean screen is on. The board of a dice match you are playing stays. |

### Gameplay

| Item | What it does | Whose |
|---|---|---|
| Friendly fire | Players can hurt each other. | the host's |
| Crime: Shared / Individual | Shared: a crime by either of you counts for both of you, and paying the fine (either of you) clears both. Individual: each answers for his own crimes. | the host's |
| Fast travel | Fast travel during a co-op session. **Off** unless the host turns it on (see below). | the host's |
| Keep players together | Warns the joining player, then brings them back beside the host, when they wander too far apart. | the host's |
| Sleep and wait together | Your sleep or wait asks your partner first. Off: you sleep and wait alone. | your own |
| Hear each other's whistle | Your whistle is heard at your figure on your partner's screen, and theirs on yours. | your own |
| Partner's herb picking | Shows your partner's figure picking herbs. Off (the default): it stands while they pick. | your own |

### Voice and keys (each player's own)

| Item | What it does |
|---|---|
| Voice chat | Talk to your partner through your microphone. |
| Menu key | The key that opens this menu: Insert, Numpad + or Numpad -. A new key works from the next time the game starts. |

### Help

| Item | What it does |
|---|---|
| I'm stuck | Frees you from a stuck state: lying in a bed, a sleep picker that did not close, a knockdown. Try it twice; still stuck, reload a save. |
| Something's wrong here | Marks this moment in the logs. Then send your logs with **Report a bug** in the launcher **before** you restart the game, so the moment is in them. |
| Version | The mod's version. |
| Connection | Whether you are hosting, joined, or not connected. |

## Who may change what

The settings that change the world you share are the **host's**: friendly fire,
crime, fast travel and keeping players together. On the joining player's screen
they show the host's value, marked **[set by the host]**, and cannot be changed
there. Everything else is each player's own: what you change only changes your
game.

## Your choices are remembered

What you choose in the menu is remembered for the next time you play, in the
mod's own file `mod-settings.json` in the Kingdom Come: Together folder, never in
a game save. The host's settings you chose are used whenever you host; when you
join someone, theirs apply.

## Changing the menu key

- In the menu, choose **Menu key** and press **End**: Insert, then Numpad +, then
  Numpad -, then Insert again. The menu says "(from the next game start)": the
  launcher sets the new key up when it starts the game the next time. Until then
  the old key still opens the menu.
- Or, with the launcher closed, edit the launcher's `settings.json` in the same
  folder: `"MenuKey": "insert"`, `"np_add"` (Numpad +) or `"np_subtract"`
  (Numpad -). Anything else means Insert.
- PgUp, PgDn and End cannot be changed.

## Fast travel in co-op

While you play together, fast travel is **off** for both of you unless the host
turns it on in the menu, so nobody is left behind. When a session starts with it
off, the game says once: "Fast travel is off in this co-op session (your host can
turn it on in the mod menu)". When the host turns it on, the host can fast travel
and the joining player is brought along (in the host's world only the host
travels). When the session ends, fast travel is as it was before.

## The console commands still work

Open the console with `~` and type any of these; they do exactly what the menu
does.

| Menu item | Console command |
|---|---|
| (the menu itself) | `mp_menu open`, `mp_menu close`, `mp_menu up`, `mp_menu down`, `mp_menu change`, `mp_menu select <number or name>`, `mp_menu status` |
| Partner's name badge | `mp_name_badges on` / `off` |
| Ping and clock line | `mp_ping_line on` / `off` |
| Clean screen | `mp_clean_screen on` / `off` |
| Friendly fire | `mp_friendly_fire on` / `off` (the host's) |
| Crime | `mp_crime_mode joint` / `individual` (the host's) |
| Fast travel | `mp_fast_travel on` / `off` (the host's) |
| Keep players together | `mp_leash on` / `off` (the host's) |
| Sleep and wait together | `mp_sleep_vote on` / `off` |
| Hear each other's whistle | `mp_whistle on` / `off` |
| Partner's herb picking | `mp_avatar_herbs on` / `off` |
| I'm stuck | `mp_unstuck` (or `mp_unstuck hard`) |
| Something's wrong here | `mark_odd` |

A command typed in the console works for this game session; the menu is what
remembers your choice for the next time.
