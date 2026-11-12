# The scoreboard: 0.65.0, the base line (WO-220)

Three scenes runs of `python tools/rig/scenarios.py score` on the 0.65.0 build and one checks run, read by `python tools/rig/scoreboard.py`.
The bench: two copies of the game on one PC, joined through our relay with the field's link shape, both at low settings 1280x720.
What each number is: `docs/WO-220-findings.md` section 1 and the tool's own header.
`base1-b8` is the first full run (scenes and checks in one run): its scenes were complete when the rig's own low-memory guard ended it in
the checks; the tool reports that as a game that exited at `works-drop`. It was the rig's quit, not a crash. The checks of all three
rows are the checks run's (`works1-b8`: complete, no crash).

## The three runs side by side

| | base1-b8 | base3-b8 | base4-b8 | mean | spread (max - min) |
|---|---:|---:|---:|---:|---:|
| place | 80.3 | 82.3 | 91.4 | 84.7 | 11.2 |
| thing | 79.4 | 79.0 | 86.2 | 81.5 | 7.2 |
| step | 1.58 | 1.76 | 1.68 | 1.68 | 0.18 |
| slide | 39 | 49 | 43 | 44 | 10 |
| pulls | 1.45 | 1.15 | 0.65 | 1.09 | 0.80 |
| flips | 0.017 | 0.210 | 0.151 | 0.126 | 0.193 |
| lane99 | 674 | 653 | 728 | 685 | 75 |
| cost95 | 12.6 | 14.0 | 12.6 | 13.0 | 1.4 |
| works | 5 | 5 | 5 | 5.0 | 0 |

`SCORE place=80.3 thing=79.4 step=1.58 slide=39 pulls=1.45 flips=0.017 lane99=674 cost95=12.6 works=5/10 EXITED=joiner@works-drop`  (base1-b8)
`SCORE place=82.3 thing=79.0 step=1.76 slide=49 pulls=1.15 flips=0.210 lane99=653 cost95=14.0 works=5/10`  (base3-b8)
`SCORE place=91.4 thing=86.2 step=1.68 slide=43 pulls=0.65 flips=0.151 lane99=728 cost95=12.6 works=5/10`  (base4-b8)
`SCORE place=84.7 thing=81.5 step=1.68 slide=44 pulls=1.09 flips=0.126 lane99=685 cost95=13.0 works=5/10`  (mean of 3)

## Scoreboard: base1-b8 (14.2 minutes measured)

`SCORE place=80.3 thing=79.4 step=1.58 slide=39 pulls=1.45 flips=0.017 lane99=674 cost95=12.6 works=5/10 EXITED=joiner@works-drop`

**A game exited during the run: joiner, at the step `works-drop` (crash-20261111-135851-5080-agent). Every check after it is "not run".**

| the same place, the same thing | samples | within 0.5 m | within 1 m | over 2 m | median m | the same activity |
|---|---:|---:|---:|---:|---:|---:|
| every body (87 bodies) | 9396 | 80.3 % | 89.9 % | 4.4 % | 0.06 | 79.4 % |
| under 15 m from the joiner | 2559 | 79.7 % | 84.5 % | 8.1 % | 0.05 | 73.2 % |
| 15-40 m from the joiner | 6679 | 80.5 % | 91.9 % | 2.9 % | 0.07 | 81.9 % |
| beyond 40 m from the joiner | 158 | 80.4 % | 93.0 % | 3.2 % | 0.04 | 72.2 % |
| the host's NPC stands | 709 | 85.9 % | 87.4 % | 11.7 % | 0.00 | 33.9 % |
| the host's NPC walks | 2944 | 75.7 % | 92.0 % | 4.2 % | 0.22 | 85.2 % |
| the host's NPC does anything else | 5743 | 81.9 % | 89.1 % | 3.5 % | 0.02 | 82.0 % |
| smithy (scene) | 2504 | 89.1 % | 93.9 % | 0.2 % | 0.04 | 85.2 % |
| village (scene) | 3676 | 81.1 % | 88.4 % | 7.3 % | 0.12 | 78.1 % |
| tavern (scene) | 3216 | 72.5 % | 88.5 % | 4.2 % | 0.07 | 76.4 % |

| the step (the village walk) | on the host | its copy on the joiner |
|---|---:|---:|
| bodies that walked on both | 20 | 20 |
| median step period | 1.00 s | 1.55 s (x1.58) |
| median planted-foot slide, of the body's speed | 8 % | 39 % |
| every walking stretch of the host's own NPCs | 71 stretches: 1.00 s, slide 8 % | |
| the sampler on the host | 49 bodies with joints, 28 that walked, 0 rows without joints | [W206B-FIRST] bodies=10 with_joints=10 |
| the sampler on the joiner | 50 bodies with joints, 21 that walked, 0 rows without joints | [W206B-FIRST] bodies=10 with_joints=10 |
| SpawnedAnimal_Pig_41285AE8_0 | 1.19 m/s, 0.65 s, slide 30 % | 1.26 m/s, 0.65 s, slide 31 % (x1.00) |
| extraGuards_trosky_soldiers_guards_3 | 1.27 m/s, 1.10 s, slide 5 % | 1.26 m/s, 1.90 s, slide 41 % (x1.73) |
| extraGuards_trosky_soldiers_guards_7 | 1.30 m/s, 1.10 s, slide 7 % | 1.34 m/s, 1.10 s, slide 19 % (x1.00) |
| ttkc_bailiffSon | 2.27 m/s, 0.95 s, slide 21 % | 2.23 m/s, 0.85 s, slide 47 % (x0.89) |
| ttkc_dog_3 | 0.93 m/s, 0.90 s, slide 22 % | 0.96 m/s, 1.45 s, slide 39 % (x1.61) |
| ttkc_dog_4 | 0.91 m/s, 0.90 s, slide 21 % | 0.98 m/s, 1.45 s, slide 37 % (x1.61) |
| ttkc_drozd | 2.71 m/s, 0.60 s, slide 20 % | 1.57 m/s, 0.95 s, slide 39 % (x1.58) |
| ttkc_man_1 | 1.45 m/s, 1.00 s, slide 7 % | 1.45 m/s, 1.85 s, slide 38 % (x1.85) |
| ttkc_man_15 | 1.38 m/s, 1.05 s, slide 7 % | 1.43 m/s, 1.85 s, slide 38 % (x1.76) |
| ttkc_man_22 | 1.40 m/s, 1.00 s, slide 6 % | 1.40 m/s, 2.15 s, slide 41 % (x2.15) |
| ttkc_man_25 | 1.19 m/s, 1.35 s, slide 4 % | 1.19 m/s, 1.85 s, slide 40 % (x1.37) |
| ttkc_man_28 | 4.30 m/s, 0.60 s, slide 24 % | 4.20 m/s, 0.75 s, slide 39 % (x1.25) |
| ttkc_man_29 | 1.45 m/s, 1.00 s, slide 6 % | 1.46 m/s, 1.45 s, slide 72 % (x1.45) |
| ttkc_milena | 1.45 m/s, 0.95 s, slide 8 % | 1.63 m/s, 1.60 s, slide 47 % (x1.68) |
| ttkc_prasta | 1.49 m/s, 1.30 s, slide 8 % | 1.49 m/s, 1.35 s, slide 33 % (x1.04) |
| ttkc_woman_17 | 1.38 m/s, 1.00 s, slide 8 % | 1.44 m/s, 1.55 s, slide 47 % (x1.55) |
| ttkc_woman_4 | 0.85 m/s, 1.30 s, slide 4 % | 0.86 m/s, 1.60 s, slide 31 % (x1.23) |
| ttkc_woman_8 | 1.32 m/s, 0.95 s, slide 12 % | 1.43 m/s, 1.55 s, slide 45 % (x1.63) |
| ttkc_woman_9 | 3.89 m/s, 0.60 s, slide 19 % | 3.91 m/s, 0.65 s, slide 35 % (x1.08) |
| tzda_man_10 | 1.43 m/s, 1.05 s, slide 8 % | 1.41 m/s, 1.90 s, slide 42 % (x1.81) |

| pulls and flips (the joiner's copies) | |
|---|---|
| 10 s windows of one copy | 357 (62 bodies, 58.6 body-minutes) |
| with a single-frame pull over 50 cm | 85 = 1.45 per body-minute |
| flips (a pull over 5 m, moves in over 40 % of the frames) | 1 = 0.017 per body-minute |
| by scene (windows / pulls / flips) | smithy 51 / 0 / 0; tavern 171 / 56 / 0; village 135 / 29 / 1 |
| most pulled | ttkc_scribe (26), ttkc_jakes (12), ttkc_drozd (6), ttkc_man_2 (6), ttkc_man_34 (4) |

| the lanes on the joiner, the cost on both | p50 | p99 |
|---|---:|---:|
| the frame processor's lag | 0 ms | 55 ms |
| the activity lane's age | 136 ms | 674 ms |
| the position writer's play-out allowance | 85 ms | 300 ms |

| our main-thread time a frame | mean | p95 (mean of the minutes' own) | worst minute's p95 | the game held | faults |
|---|---:|---:|---:|---:|---:|
| the host | 2.73 ms | 12.62 ms | 15.30 ms | 36 fps (lowest minute 21) | 0 |
| the joiner | 2.08 ms | 2.89 ms | 3.40 ms | 59 fps (lowest minute 45) | 0 |

The checks are from their own run, `works1-b8`.

| works at all | | |
|---|---|---|
| the join | yes | ok |
| a talk to its choices | not run | no villager ready to talk on the host |
| a trade screen | no | no shop screen (2 shopkeepers tried) |
| a forced dialogue | no | no dialogue started |
| a loot take | not run | body-no-body |
| a door use | yes | used by both, the same on both |
| a drop picked up by the other | yes | 2 of 2 drops taken by the other |
| a knockdown and get-up | yes | 2 of 2 falls shown, 2 get-ups |
| a mount | no | the copy did not follow the mount |
| a sleep vote | yes | ok |

The players left alone (fights on a player ended / checks, per scene): smithy host 0/95 joiner 0/95; village host 63/206 joiner 0/207; tavern host 99/154 joiner 6/155.
Settings: host `sys_spec_Shading=1 sys_spec_Shadows=1 sys_spec_ObjectDetail=1 sys_spec_Vegetation=1 r_Width=1280 r_Height=720`; joiner `sys_spec_Shading=1 sys_spec_Shadows=1 sys_spec_ObjectDetail=1 sys_spec_Vegetation=1 r_Width=1280 r_Height=720`.

The commonest differences within 40 m (host -> copy: samples): MotionMovement -> -: 191; MotionIdle -> MotionMovement: 186; Cooking -> MotionIdle: 142; WaitingStand2 -> -: 118; MotionIdle -> CombatIdle: 88; MotionMovement -> MotionIdle: 82; MotionIdle -> -: 63; Leaning_Back -> -: 60.

## Scoreboard: base3-b8 (14.3 minutes measured)

`SCORE place=82.3 thing=79.0 step=1.76 slide=49 pulls=1.15 flips=0.210 lane99=653 cost95=14.0 works=5/10`

| the same place, the same thing | samples | within 0.5 m | within 1 m | over 2 m | median m | the same activity |
|---|---:|---:|---:|---:|---:|---:|
| every body (77 bodies) | 8007 | 82.3 % | 91.7 % | 2.6 % | 0.06 | 79.0 % |
| under 15 m from the joiner | 2085 | 77.3 % | 85.9 % | 7.8 % | 0.05 | 62.0 % |
| 15-40 m from the joiner | 5882 | 84.1 % | 93.7 % | 0.8 % | 0.06 | 85.1 % |
| beyond 40 m from the joiner | 40 | 87.5 % | 100.0 % | 0.0 % | 0.02 | 67.5 % |
| the host's NPC stands | 673 | 98.8 % | 99.3 % | 0.4 % | 0.00 | 48.4 % |
| the host's NPC walks | 1704 | 71.5 % | 95.5 % | 0.4 % | 0.27 | 80.6 % |
| the host's NPC does anything else | 5630 | 83.6 % | 89.7 % | 3.6 % | 0.04 | 82.1 % |
| smithy (scene) | 2381 | 84.0 % | 90.1 % | 3.3 % | 0.04 | 86.1 % |
| village (scene) | 3143 | 85.4 % | 94.1 % | 2.4 % | 0.01 | 71.3 % |
| tavern (scene) | 2483 | 76.7 % | 90.3 % | 2.3 % | 0.14 | 81.7 % |

| the step (the village walk) | on the host | its copy on the joiner |
|---|---:|---:|
| bodies that walked on both | 13 | 13 |
| median step period | 1.00 s | 1.85 s (x1.76) |
| median planted-foot slide, of the body's speed | 9 % | 49 % |
| every walking stretch of the host's own NPCs | 36 stretches: 1.00 s, slide 9 % | |
| the sampler on the host | 32 bodies with joints, 15 that walked, 0 rows without joints | [W206B-FIRST] bodies=10 with_joints=10 |
| the sampler on the joiner | 33 bodies with joints, 13 that walked, 0 rows without joints | [W206B-FIRST] bodies=10 with_joints=10 |
| extraGuards_trosky_soldiers_guards_2 | 1.22 m/s, 1.15 s, slide 4 % | 1.22 m/s, 1.85 s, slide 45 % (x1.61) |
| ttkc_drozd | 3.52 m/s, 0.95 s, slide 14 % | 3.19 m/s, 2.95 s, slide 72 % (x3.11) |
| ttkc_inkeeper | 1.21 m/s, 1.00 s, slide 10 % | 1.34 m/s, 2.25 s, slide 66 % (x2.25) |
| ttkc_man_22 | 1.35 m/s, 1.00 s, slide 5 % | 1.38 m/s, 1.60 s, slide 34 % (x1.60) |
| ttkc_man_3 | 1.50 m/s, 1.00 s, slide 9 % | 1.73 m/s, 1.90 s, slide 44 % (x1.90) |
| ttkc_man_5 | 1.44 m/s, 0.90 s, slide 8 % | 1.61 m/s, 1.85 s, slide 44 % (x2.06) |
| ttkc_man_6 | 1.39 m/s, 1.05 s, slide 4 % | 1.38 m/s, 1.85 s, slide 45 % (x1.76) |
| ttkc_marketa | 1.34 m/s, 1.00 s, slide 15 % | 1.11 m/s, 0.55 s, slide 52 % (x0.55) |
| ttkc_prasta | 1.29 m/s, 1.05 s, slide 10 % | 1.60 m/s, 2.40 s, slide 75 % (x2.29) |
| ttkc_woman_1 | 1.12 m/s, 1.00 s, slide 13 % | 1.15 m/s, 1.55 s, slide 53 % (x1.55) |
| ttkc_woman_17 | 1.35 m/s, 1.00 s, slide 9 % | 1.34 m/s, 1.60 s, slide 52 % (x1.60) |
| ttkc_woman_2 | 1.24 m/s, 1.00 s, slide 12 % | 1.45 m/s, 1.60 s, slide 49 % (x1.60) |
| tzda_man_10 | 1.37 m/s, 1.00 s, slide 7 % | 1.36 m/s, 1.80 s, slide 38 % (x1.80) |

| pulls and flips (the joiner's copies) | |
|---|---|
| 10 s windows of one copy | 460 (52 bodies, 76.3 body-minutes) |
| with a single-frame pull over 50 cm | 88 = 1.15 per body-minute |
| flips (a pull over 5 m, moves in over 40 % of the frames) | 16 = 0.210 per body-minute |
| by scene (windows / pulls / flips) | smithy 35 / 0 / 0; tavern 191 / 28 / 7; village 234 / 60 / 9 |
| most pulled | ttkc_jakes (15), ttkc_prasta (15), ttkc_man_29 (10), ttkc_man_25 (8), extraGuards_trosky_soldiers_guards_3 (7) |

| the lanes on the joiner, the cost on both | p50 | p99 |
|---|---:|---:|
| the frame processor's lag | 0 ms | 57 ms |
| the activity lane's age | 119 ms | 653 ms |
| the position writer's play-out allowance | 73 ms | 300 ms |

| our main-thread time a frame | mean | p95 (mean of the minutes' own) | worst minute's p95 | the game held | faults |
|---|---:|---:|---:|---:|---:|
| the host | 3.08 ms | 13.96 ms | 17.65 ms | 37 fps (lowest minute 21) | 0 |
| the joiner | 2.08 ms | 3.03 ms | 3.35 ms | 59 fps (lowest minute 45) | 352 |

The checks are from their own run, `works1-b8`.

| works at all | | |
|---|---|---|
| the join | yes | ok |
| a talk to its choices | not run | no villager ready to talk on the host |
| a trade screen | no | no shop screen (2 shopkeepers tried) |
| a forced dialogue | no | no dialogue started |
| a loot take | not run | body-no-body |
| a door use | yes | used by both, the same on both |
| a drop picked up by the other | yes | 2 of 2 drops taken by the other |
| a knockdown and get-up | yes | 2 of 2 falls shown, 2 get-ups |
| a mount | no | the copy did not follow the mount |
| a sleep vote | yes | ok |

The players left alone (fights on a player ended / checks, per scene): smithy host 0/95 joiner 0/95; village host 72/206 joiner 5/208; tavern host 0/154 joiner 0/155.
Settings: host `sys_spec_Shading=1 sys_spec_Shadows=1 sys_spec_ObjectDetail=1 sys_spec_Vegetation=1 r_Width=1280 r_Height=720`; joiner `sys_spec_Shading=1 sys_spec_Shadows=1 sys_spec_ObjectDetail=1 sys_spec_Vegetation=1 r_Width=1280 r_Height=720`.

The commonest differences within 40 m (host -> copy: samples): MotionIdle -> MotionMovement: 168; SittingIdle -> ADLG_Speak: 124; MotionMovement -> MotionIdle: 115; MotionMovement -> -: 103; Leaning_Back -> -: 76; IngameDialogPose_SittingWithOrWithoutTable -> ADLG_Listen: 60; Embroidery -> SittingIdle: 54; MotionMovement -> Bartender_CleaningTable: 52.

## Scoreboard: base4-b8 (14.1 minutes measured)

`SCORE place=91.4 thing=86.2 step=1.68 slide=43 pulls=0.65 flips=0.151 lane99=728 cost95=12.6 works=5/10`

| the same place, the same thing | samples | within 0.5 m | within 1 m | over 2 m | median m | the same activity |
|---|---:|---:|---:|---:|---:|---:|
| every body (80 bodies) | 8874 | 91.4 % | 95.7 % | 1.4 % | 0.04 | 86.2 % |
| under 15 m from the joiner | 2160 | 90.2 % | 93.9 % | 2.4 % | 0.01 | 85.4 % |
| 15-40 m from the joiner | 6652 | 91.9 % | 96.3 % | 1.0 % | 0.06 | 86.4 % |
| beyond 40 m from the joiner | 62 | 88.7 % | 91.9 % | 6.5 % | 0.15 | 82.3 % |
| the host's NPC stands | 327 | 99.1 % | 100.0 % | 0.0 % | 0.01 | 34.3 % |
| the host's NPC walks | 2564 | 84.8 % | 97.1 % | 0.5 % | 0.21 | 87.9 % |
| the host's NPC does anything else | 5983 | 93.9 % | 94.9 % | 1.9 % | 0.01 | 88.2 % |
| smithy (scene) | 2465 | 90.1 % | 93.3 % | 1.6 % | 0.06 | 82.5 % |
| village (scene) | 3726 | 86.8 % | 94.4 % | 2.3 % | 0.04 | 79.8 % |
| tavern (scene) | 2683 | 99.1 % | 99.8 % | 0.0 % | 0.01 | 98.4 % |

| the step (the village walk) | on the host | its copy on the joiner |
|---|---:|---:|
| bodies that walked on both | 21 | 21 |
| median step period | 1.00 s | 1.85 s (x1.68) |
| median planted-foot slide, of the body's speed | 10 % | 43 % |
| every walking stretch of the host's own NPCs | 56 stretches: 1.00 s, slide 7 % | |
| the sampler on the host | 48 bodies with joints, 23 that walked, 0 rows without joints | [W206B-FIRST] bodies=10 with_joints=10 |
| the sampler on the joiner | 49 bodies with joints, 24 that walked, 0 rows without joints | [W206B-FIRST] bodies=10 with_joints=10 |
| extraGuards_trosky_soldiers_guards_1 | 1.26 m/s, 1.10 s, slide 4 % | 1.27 m/s, 1.85 s, slide 43 % (x1.68) |
| extraGuards_trosky_soldiers_guards_2 | 2.22 m/s, 0.70 s, slide 16 % | 2.40 m/s, 0.85 s, slide 45 % (x1.21) |
| extraGuards_trosky_soldiers_guards_4 | 1.29 m/s, 1.10 s, slide 7 % | 1.27 m/s, 1.85 s, slide 44 % (x1.68) |
| extraGuards_trosky_soldiers_guards_5 | 1.18 m/s, 1.15 s, slide 6 % | 1.21 m/s, 1.85 s, slide 42 % (x1.61) |
| extraGuards_trosky_soldiers_guards_9 | 1.36 m/s, 1.05 s, slide 6 % | 1.39 m/s, 1.85 s, slide 44 % (x1.76) |
| tneb_man_30 | 1.64 m/s, 0.95 s, slide 5 % | 1.66 m/s, 1.85 s, slide 45 % (x1.95) |
| ttkc_dog_1 | 0.90 m/s, 0.90 s, slide 23 % | 0.93 m/s, 1.45 s, slide 32 % (x1.61) |
| ttkc_drozd | 1.57 m/s, 0.95 s, slide 6 % | 1.66 m/s, 1.80 s, slide 44 % (x1.89) |
| ttkc_inkeeper | 1.30 m/s, 1.00 s, slide 10 % | 1.30 m/s, 1.55 s, slide 54 % (x1.55) |
| ttkc_man_11 | 1.39 m/s, 1.00 s, slide 10 % | 1.40 m/s, 2.10 s, slide 43 % (x2.10) |
| ttkc_man_2 | 1.37 m/s, 1.00 s, slide 6 % | 1.40 m/s, 2.10 s, slide 43 % (x2.10) |
| ttkc_man_22 | 1.36 m/s, 1.00 s, slide 4 % | 1.36 m/s, 2.10 s, slide 43 % (x2.10) |
| ttkc_man_24 | 1.18 m/s, 1.35 s, slide 3 % | 1.19 m/s, 1.85 s, slide 38 % (x1.37) |
| ttkc_man_5 | 1.60 m/s, 0.95 s, slide 14 % | 1.69 m/s, 1.85 s, slide 46 % (x1.95) |
| ttkc_tradersMom | 1.23 m/s, 1.00 s, slide 16 % | 3.59 m/s, 2.00 s, slide 58 % (x2.00) |
| ttkc_woman_1 | 1.23 m/s, 1.00 s, slide 15 % | 1.22 m/s, 1.60 s, slide 36 % (x1.60) |
| ttkc_woman_10 | 4.01 m/s, 0.60 s, slide 15 % | 4.01 m/s, 0.65 s, slide 37 % (x1.08) |
| ttkc_woman_17 | 1.40 m/s, 0.95 s, slide 11 % | 1.39 m/s, 1.60 s, slide 46 % (x1.68) |
| ttkc_woman_2 | 0.87 m/s, 1.30 s, slide 5 % | 0.86 m/s, 1.55 s, slide 27 % (x1.19) |
| ttkc_woman_3 | 3.61 m/s, 0.60 s, slide 29 % | 3.37 m/s, 0.65 s, slide 42 % (x1.08) |
| ttkc_woman_4 | 1.22 m/s, 1.15 s, slide 15 % | 1.27 m/s, 1.55 s, slide 48 % (x1.35) |

| pulls and flips (the joiner's copies) | |
|---|---|
| 10 s windows of one copy | 360 (48 bodies, 59.8 body-minutes) |
| with a single-frame pull over 50 cm | 39 = 0.65 per body-minute |
| flips (a pull over 5 m, moves in over 40 % of the frames) | 9 = 0.151 per body-minute |
| by scene (windows / pulls / flips) | smithy 42 / 0 / 0; tavern 91 / 1 / 0; village 227 / 38 / 9 |
| most pulled | ttkc_drozd (20), extraGuards_trosky_soldiers_guards_2 (6), ttkc_woman_8 (3), ttkc_tradersMom (3), ttkc_jakes (2) |

| the lanes on the joiner, the cost on both | p50 | p99 |
|---|---:|---:|
| the frame processor's lag | 0 ms | 49 ms |
| the activity lane's age | 143 ms | 728 ms |
| the position writer's play-out allowance | 67 ms | 300 ms |

| our main-thread time a frame | mean | p95 (mean of the minutes' own) | worst minute's p95 | the game held | faults |
|---|---:|---:|---:|---:|---:|
| the host | 2.57 ms | 12.55 ms | 19.30 ms | 42 fps (lowest minute 22) | 0 |
| the joiner | 1.90 ms | 2.68 ms | 3.50 ms | 66 fps (lowest minute 46) | 0 |

The checks are from their own run, `works1-b8`.

| works at all | | |
|---|---|---|
| the join | yes | ok |
| a talk to its choices | not run | no villager ready to talk on the host |
| a trade screen | no | no shop screen (2 shopkeepers tried) |
| a forced dialogue | no | no dialogue started |
| a loot take | not run | body-no-body |
| a door use | yes | used by both, the same on both |
| a drop picked up by the other | yes | 2 of 2 drops taken by the other |
| a knockdown and get-up | yes | 2 of 2 falls shown, 2 get-ups |
| a mount | no | the copy did not follow the mount |
| a sleep vote | yes | ok |

The players left alone (fights on a player ended / checks, per scene): smithy host 0/95 joiner 0/95; village host 28/201 joiner 0/203; tavern host 0/154 joiner 0/155.
Settings: host `sys_spec_Shading=1 sys_spec_Shadows=1 sys_spec_ObjectDetail=1 sys_spec_Vegetation=1 r_Width=1280 r_Height=720`; joiner `sys_spec_Shading=1 sys_spec_Shadows=1 sys_spec_ObjectDetail=1 sys_spec_Vegetation=1 r_Width=1280 r_Height=720`.

The commonest differences within 40 m (host -> copy: samples): MotionMovement -> MotionIdle: 155; Cooking -> MotionIdle: 137; MotionIdle -> CombatIdle: 82; SittingIdle -> ADLG_Speak: 70; SittingIdle -> ADLG_Listen: 50; MotionIdle -> MotionMovement: 47; MotionMovement -> Bartender_CleaningTable: 36; MotionMovement -> -: 36.

