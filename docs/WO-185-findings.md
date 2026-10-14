# WO-185 findings: a perfect block pressed ahead of contact now holds through the blow (0.60.1)

## The symptom
On the joiner, a perfect block that the game credited (the copy staggers, the riposte opens) still took the full hit about 0.65 s later, and the
shield felt too brief to time.

## What the traces showed (read-only `mp_w178_trace`, same build)
Vanilla (a real NPC in single player, 28 attack cycles). Times from the attacker's wind-up start:
- the perfect-block slot is about 0.63 s long and ends 0.14-0.15 s before contact (`PlanSlot`: start = max(0, (countdown - tuning) - window), end = countdown - tuning;
  `wh_cs_StandardSlotDuration` 1, `wh_cs_PLSlotDurationMod` 0.5, `wh_cs_SlotEndHitOffset` 0.15);
- the attacker goes 0x4 -> 0x8 at +0.40 and **0x8 -> 0x10 at contact (+0.78..0.82)**, 0x10 -> 0x20 0.42 s later, back to Guard 0.67..0.76 s after contact;
- the player's perfect block (state 0x80/0x100, `PerfectBlockState` 1) lasted 0.7-0.95 s and ended about 0.05 s after the attacker entered 0x20, so it always covered contact.
Joiner copy (heavy stab row: start 0.533 s, hit 0.7 s; the engine's own collision lands at +1.16..1.21 s, the verdict at +1.23):
- `WO178-PBHIT` put the copy into 0x10 at the player's press (+0.5..0.65 s) and 0x20 0.5 s later; the player's `PerfectBlockState` ended about 0.52 s after the press,
  before the verdict for any press earlier than about +0.70 s. In two harness runs 14 of 15 credited in-reach swings still took the hit; in the traced runs the
  swings where the copy stayed in 0x20 past the verdict held the player's perfect block, and the engine's own hit core judged them perfect blocks.

## The change
`mp_pb_at_contact` (default on; off = the 0.60.0 behaviour). The press only arms the step (`WO185-PBARM`; a copy the engine dropped to Guard at the press is put back to
striking); at the blow's contact (`hitAt`) the copy goes 0x10 with OpponentHit if the player still holds the perfect block (`WO178-PBHIT ... [at contact]`), 0x20 after
0.42 s, its prior state 0.72 s after contact. A perfect block that ended before contact logs `WO185-PBLOST` and the verdict is the engine's. Native `wo166.cpp`,
`wo166_rules.h`, `wo178.cpp/.h`; agent `Wo178.cs`, `GameBridge.Wo178.cs`; Lua `mp_pb_at_contact`.

## Not shown
Whether a real host's enemy behaves the same (no second game was available: a synthetic host streams no NPCs, so a joiner has none); whether the player's perfect block
survives to contact with the copy sitting in Guard/striking instead of 0x10 for the 0.6 s between press and contact (to be read from the trace after this build).

## 0.60.3: the riposte after a perfect block
The riposte slot (the blue swords) opens at contact in vanilla and on the copy alike (46 of 46 vanilla perfect blocks). The wait before the player's own attack can
start is the attacker's 0x10 phase: his perfect block ends about 0.05 s after the attacker enters 0x20. In vanilla that phase depends on the swing (one NPC with a
one-handed sword; medians, from contact): stab zone 5 0.41 s, slash zone 0 0.50 s, slash zone 1 0.29 s (0.28 or 0.37), slash zone 2 0.41 s (0.40 or 0.48). The 0.60.2 copy
held 0x10 for a fixed 0.42 s (right for the stab, up to 0.14 s too long for the fast slash). `pb_contact_stage2(type, zone)` in `wo166_rules.h` now gives the vanilla
value per swing (0.42 s for any swing not measured) and the copy returns to its prior state 0.30 s after 0x20 (vanilla: 0.25-0.35 s).
The medians come from one weapon class; other weapons may differ and are still on the fallback.
The harness gained `W178_LAG` (the synthetic host's verdict delay) and `W178_ROWS` rows other than the charged stab.
