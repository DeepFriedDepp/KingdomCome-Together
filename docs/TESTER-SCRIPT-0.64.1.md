# Kingdom Come: Together 0.64.1 — tester script (the internet session)

Two players, one host and one joiner, both on the **same 0.64.1 build**, playing **over the internet** with the launcher's default
Steam route (the host's join code). This session checks what 0.64.0's session showed: villagers walking near the joiner, bodies that
stand in the bind pose, deaths, and weapons in hand. If anything looks wrong, press the **period key** at that moment, then open the
console (~) and type `mark <what you saw>`: the note reaches the other player's bug report too.

## Before you start
- Both of you: the same build (the build id is in the launcher's About and in the installer's name; a partner on another build is told
  so when you connect).
- Connect the launcher's default way, the host's **join code over Steam** -- not a LAN or VPN address.
- **Steam must be running before either game starts** (a game started without it stops at a licence box).
- Play for **about 20 minutes**. Do not alt-tab away for long: the game's own frame rate drops behind other windows.

## 1. A village walk (about 6 minutes)
- By day, in a busy village. The **joiner follows villagers who walk**, 10 to 30 m behind them, for a few minutes -- several villagers,
  one after the other. The host stays in the village too.
- **Joiner, watch:** do the villagers you follow walk smoothly, or do they stop, slide, jump forward or fade?
- Then the **joiner walks about 70 m away from the host** and comes back. **Watch:** as you come back, does anyone stand with arms
  straight out (the bind pose), or frozen in place?

## 2. A tavern (about 4 minutes)
- A few minutes among seated drinkers, the innkeeper serving.
- **Watch:** do the seated people stay where they sit? Does anyone stand in the bind pose?

## 3. A fight to the death, by each player (about 6 minutes)
- Find two or three bandits (or any fight). **The host kills one; the joiner kills one.**
- **Both, watch:** when the enemy draws its weapon, does it have the weapon in hand on your screen too? When it dies, does it fall to
  the ground on **both** screens -- or does it stand?
- After the fight: does anyone stay frozen in a combat pose?

## 4. Draw and sheathe (about 2 minutes)
- Out of the fight: the **joiner draws and sheathes his weapon five times**, a couple of seconds each, standing beside the host.
- Then in a fight: the same, five times.
- Then the joiner opens his **inventory** for a few seconds (0.64.0's missing weapon came right after one), closes it, and draws again.
- **Host, watch:** does the joiner's figure on your screen hold the same weapon, drawn when he draws it and put away when he does?

## 5. Mark what you see
- Whenever something jitters, slides, jumps, T-poses, dies standing or holds the wrong weapon: the **period key** and
  `mark <what you saw>` at that moment.

## 6. Both bug reports, right after
- Both of you: send the bug report from the launcher as soon as you stop. Its first section, **Link**, now says in plain words how
  often walking NPCs within 40 m reached the joiner ("walking NPCs within 40 m reached you N times a second"), beside every NPC's rate
  by distance.

## The switches (only if asked)
Every part of 0.64.1's work can be turned off live from the console, one at a time, to compare: `mp_w214_status` lists them
(`mp_one_bind`, `mp_owner_base`, `mp_combat_end`, `mp_reaction_rows`, `mp_death_row`, `mp_death_ground`, `mp_draw_event`,
`mp_figure_hands`, `mp_figure_turn`, `mp_anim_hold` and the others).
