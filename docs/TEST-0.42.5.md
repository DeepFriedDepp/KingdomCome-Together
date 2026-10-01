<p align="center"><img src="branding/banner-1280.jpg" alt="Kingdom Come: Together" width="640"></p>

# Testing 0.42.5 (both players)

**Kingdom Come: Together.** Unofficial. Not affiliated with or endorsed by
Warhorse Studios.

0.42.5 is about what your long evening on 0.42.2 found: the partner could not
really fight, the leash almost never brought anyone back, and a fist fight
failed the host's quest. One page for the host and the partner: install, then
the checks below. Tick what you saw, write the time next to anything odd, and
send the logs at the end.

## Install

Both of you: run `KingdomComeTogether-Setup-0.42.5.exe` (the maintainer sends
it; it is not on GitHub). It installs over 0.42.2. Both computers need it:
0.42.5 refuses every other version. Afterwards, `tools\Verify-Install.ps1` (if
you have it) must end with "all present".

## What's new in 0.42.5

- **The partner can fight.** Bandits and wild animals near the partner fight
  the partner now, even when the host is not fighting them: the partner can
  lock on, block and hit them, and they fight back. Friendly people never join
  in by themselves.
- **The partner's hits count.** Hits that only tire an enemy (a block, a blunt
  or damaged weapon) reach the host's world too, and the host's enemies turn on
  the partner's figure when the partner hits them. Hitting a bandit or a wolf
  is no crime.
- **The leash brings you back.** When the partner wanders past 650 m, the
  countdown now runs even while the host sits in the inventory, the map or a
  conversation. If the partner is busy (talking, in a scene), the wait lasts at
  most a minute; then the conversation ends and the partner is brought back.
  Far away, the partner lands beside the host, or on a spot the host just stood
  on, without fall damage.
- **The host's quests are safe.** A step on the partner's side that would fail
  a quest, cancel an objective or count someone as dead only happens in the
  host's world if it really happened there too. A knocked-out fighter stays
  knocked out on both screens, never dead on one, also after a load or a
  rejoin.
- **No more "the host is paused" for ages** after a quest's sleep that cuts to
  a scene.
- **Flying around is not fast travel.** If the partner flies or is teleported
  by a quest, the host is not told "tried to fast travel".
- **Busy towns.** In a crowded town the host's villagers keep moving on the
  partner's screen; they no longer stop, snap back and start again, and the
  partner's game no longer slows down for it.

> **If something looks wrong, turn it off with…** (the console, `~`; `on`
> switches it back; nothing else changes)
>
> | what looks wrong | who | type |
> |---|---|---|
> | enemies fighting the partner that should not | partner | `mp_hostile_engage off` |
> | a quest step of the partner's that does not reach the host | host | `mp_quest_safety off` |
> | the leash pulling in the middle of a conversation | host | `mp_leash_cap_s 0` |
> | the host's villagers acting strangely on the partner's screen | partner | `mp_npc_catchup off` |
>
> Say which one you switched off, and when (`mark_odd` first).

**Still known**

- Carrying a body or an object shows only on the carrier's screen.
- Where the host's animals (a wolf pack, a deer) stand can differ on the
  partner's screen until they come close.
- A cutscene plays for each of you separately; on the partner's screen it can
  stay black until the host's next step.
- An escort (the sheep in "Find Mutt!") follows only the player who leads it.
- The partner does not hear the host whistle, and the other way round.
- The journal's marker letters can differ between the two of you.
- At the grindstone the blade is not in the other player's figure's hands.

## Setup (both of you)

1. **Install** the same build on both machines. Marker: `mark_setup`.
2. **The host** starts the game from the launcher, loads their save, and clicks
   CONNECT once they can move.
3. **The partner** starts the game from the launcher and **waits at the main
   menu. Don't load a save.** Click CONNECT at the menu; the partner joins the
   host's world by itself. Marker: `mark_join`.

**Markers.** Each check has a one-word marker. When you start it, open the
console (`~`) and type it, for example `mark_fight`. It writes one line into
the logs so we can find the moment. Anything odd with no check to fit:
`mark_odd`.

## The checks

1. **Bandits.** Find bandits. The partner draws a weapon near one the host is
   *not* fighting. Marker: `mark_fight`.
   * Partner: you can lock on to the bandit, block and hit it; it fights back.
   * Host: the bandit turns on the partner's figure; its health drops when the
     partner hits it. Nobody gets a crime for it.
2. **Wolves or dogs** attack the partner while the host stands away. Marker:
   `mark_animals`.
   * Partner: you can lock on to them and kill them; they die on the host's
     screen too.
3. **Friendly people.** The partner walks past villagers with the weapon drawn.
   Marker: `mark_friendly`.
   * Partner: no villager comes into combat with you by itself.
4. **Blocks.** The partner blocks a bandit's blows, then hits back with a
   damaged or blunt weapon. Marker: `mark_block`.
   * Host: the bandit tires on your screen too.
5. **The leash while the host is in a menu.** The partner walks off past
   650 m while the host sits in the inventory or the map. Marker: `mark_leash`.
   * Partner: the countdown runs down and you are brought back beside the host,
     standing on the ground.
6. **The leash while the partner talks.** The partner walks past 650 m and
   starts a conversation there. Marker: `mark_leash_talk`.
   * Partner: after about a minute the conversation ends and you are brought
     back.
7. **Far away.** The partner rides or runs a long way (a kilometre or more)
   before the countdown ends. Marker: `mark_leash_far`.
   * Partner: you land beside the host, or on a spot the host just stood on,
     and you are not hurt by the landing.
8. **The map and fast travel** (partner): open the map a few times and try to
   fast travel once. Marker: `mark_map`.
   * Partner: "Only the host can fast travel in co-op." Nobody is brought
     anywhere because of it.
9. **A fist fight** (a quest with one, for example a fight club). Marker:
   `mark_fistfight`.
   * Both: the loser is knocked out on both screens, never dead on one; the
     quest goes on for the host.
10. **A quest's sleep** (host), one that cuts to a scene. Marker:
    `mark_questsleep`.
    * Partner: afterwards the host's figure moves normally; "the host is
      paused" does not stay.
11. **A busy town** (both): spend ten minutes together in the biggest town,
    walking about. Marker: `mark_town`.
    * Partner: the villagers walk on smoothly; nobody stands frozen and then
      jumps; the game keeps its speed.
12. **Again from 0.42.2:** a restart in the middle (`mark_rejoin`), loading a
    save in the session (`mark_load`), clothes (`mark_clothes`), dice
    (`mark_dice`), the time (`mark_time`).

## Logs to send afterwards

**Before you restart the game or the launcher after anything odd**, and at the
end of the evening: both of you click **Report a bug** in the launcher and send
the zip it puts on your desktop. It holds this evening's logs and those of the
six launches before.
