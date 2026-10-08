WO-162 ran: read-only static reverse engineering of the Modding Tools build's DLLs and its tables for shared combat; no game, launcher, agent or relay was touched, no code changed, nothing built.
The private notes are `research/WO-162/combat-RE.md` (git-ignored, kept locally; addresses only in its appendix, marked 1.5.5 only).
Replay: no-go through the melee hit slot alone (it decides nothing: the block reads happen before it and ride in the record); needs-live-probe one level up, at the combat module's hit processor, which honours the victim's own block.
Lock-on: no-go with skirmish override 0 (stores no hostile pair); go with probes for override 1 (the engine's own rule: same skirmish as the player, active member, explicit hostile pair or negative faction value).
Capture gap: go — the sync-attack row GUID is read at the wrong offset (+0x84; it is at +0x7C), and a hit pairs with its swing by start + hit time (33 of 35), not a 1.2 s window; animal bites stay generic.
