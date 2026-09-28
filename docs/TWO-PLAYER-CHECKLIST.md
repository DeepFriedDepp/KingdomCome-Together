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
