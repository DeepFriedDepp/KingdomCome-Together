# WO-203B — the listener sees the starter's conversation camera (0.62.5)

When one player talks to an NPC, the partner can walk up and choose **Join conversation** on the partner or the NPC. The listener
then sees the starter's own dialogue camera: the same position, rotation, field of view and cuts, framed on the listener's own copy
of the NPC, with the game's cinematic bars and none of the gameplay HUD. The listener's own camera comes back on every way out.

## Scope as built

The camera this work order asked for, plus the smallest part of WO-203 Act I it needs, which was not on `main`:
- Announce, Join, Snapshot, Cam, Leave and End on the Conv channel.
- The "Join conversation" prompt.
- The listen cancel action.
- The starter's camera sampler.

Subtitles, options and votes are not part of this build. Two players (the host and a joiner) in either role. With more players, a
conversation is shared between the host and its starter only.

Switches (both default on): `mp_join_conversation` (the whole session), `mp_conv_camera` (the camera).

## Part A — how the listener's view takes a full pose

**A1 won: CryAction's own view system with a hidden, mod-owned `TagPoint` entity.** The DLL:
1. creates a view in `CViewSystem`;
2. links it to the entity (a view linked to an entity without a game-object view takes the entity's world transform as the camera:
   position *and* rotation);
3. makes it the active view;
4. places the entity and sets the view's FOV every frame.

Leaving puts the previous view back, then removes the view and the entity. The player's own view is a view of the same system. The
earlier attempt, `CryAction.SetViewCamera`, kept the player's mouse-look rotation, because it clears the active view and the
game's camera manager keeps writing the rotation.

How the pieces were found (port-aware, nothing is an address):

| piece | found by |
|---|---|
| `ISystem` | the `gEnv` field whose object has `CSystem`'s RTTI vftable |
| `CCryAction` | the game interface's field with `CCryAction`'s vftable (not in `gEnv` on 1.5.5) |
| `CViewSystem` | the `CCryAction` field with `CViewSystem`'s vftable |
| the rendered camera | `ISystem::GetViewCamera`, the slot the game's own `System.GetViewCameraPos` bind calls, checked to return a `CCamera` inside `CSystem` |
| view-system and view slots (5.7.1 order) | checked by what they do: `CreateView` returns a `CView`; `LinkTo` makes `GetLinkedId` the entity; `SetActiveView` makes it active |
| entity transform | `SetPosRotScale`, checked by reading the position and rotation back |

A2–A4 were not needed.

Probe (fixed pose, a 90° sweep over 2 s, a cut, FOV 1.10 → 0.70):
- the rendered view matched on every frame (0.0001 m, 0.000°, 0.0000 rad);
- the mouse, moved by the send-only tool, changed nothing;
- the restore gave the player's own camera, with mouse-look, on the next frame.

## Part B — following the stream

- **World pose:** the listener's copy of the anchor (the conversation's main NPC, position and yaw) × the relative pose.
- **Play-out:** 100 ms behind the starter's clock. Linear position, slerped rotation, linear FOV.
- **Cuts:** the cut flag, or a jump over 0.6 m / 25°, snaps.
- **Silence:** at most 150 ms of extrapolation, then hold.
- **Framing check:** a copy more than 0.5 m from the starter's anchor is logged (`WO203B-FRAME offset`) and still framed.
- **Frame-rate finding:** the DLL serves the agent's pipe on the game's main thread, about one request a frame, shared by every
  agent request. Below about 30 fps the camera samples queued past their play-out time. The samples are now served on the pipe's
  reader thread under their own lock (no engine call), and the agent batches them.
- **Starter:** the rendered camera is sampled every frame from "Dialog camera activated." to the dialogue's end and queued at
  30 Hz; a cut is queued at once, with the frame before it. The player's own first-person view is never sent.
- **Listener HUD:** the game's own dialogue lock (the exclusive `dialog` action map), so Henry neither moves nor turns. The
  compass, crosshair, prompts, stats and quick slots are hidden and the cinematic bars shown, only what was visible before, and all
  of it is given back.

### Ways out (B.5): each ends with the player's own camera on the next rendered frame

| way out | test | result |
|---|---|---|
| the listen cancel (the player's own Back key) | real key through the send-only tool | pass |
| the conversation's End | replay | pass |
| 3 s of stream silence | the stream stops | pass |
| a hit | the player's health drops | pass |
| combat | the same watch (`IsInCombatDanger`) | wired, not triggered live |
| a shared cutscene / anything else taking the camera | another view made active, then released | the view yields and resumes |
| a load | a save loaded while listening | pass (the next frame after the loading screen) |
| the ESC menu | the DLL's menu edge | wired; Esc while listening is the listen cancel |
| the starter disconnecting | the starter's process stopped | pass |
| a fault inside the override | a deliberate fault (research build switch) | pass |

## Part C — the replay harness (a real recorded conversation, replayed into a joiner)

The recording is a two-person conversation: 16 shots, both speakers, the 14 engine cuts. A slow 6-second orbit and a three-shot
three-person framing were added to it, because the recorded conversation's shots are all static. A real trialog was not recorded:
it needs the player to walk into it.

| measure | target | result |
|---|---|---|
| recorded cuts shown | 100 % | 19 of 19 |
| cut timing (after the 100 ms play-out) | within 100 ms | median 4 ms, max 14 ms (max 56 ms at a capped frame rate) |
| steady-shot pose error | < 0.3 m, 3° | p95 0.0009 m, 0.003°; max 0.0009 m, 0.004° |
| slow-move pose error | < 0.5 m, 5° | p95 0.002 m, 0.09°; max 0.004 m, 0.14° |
| FOV error | < 0.02 rad | max 0.00003 rad |
| mouse during listening changes the view | never | never (6 s of moves; Henry turned 0.000°) |
| restore on each way out | next frame | every tested path |
| cost per frame, listener | < 0.2 ms | mean 0.006 ms, max 0.07 ms |
| cost per frame, starter's sampler | < 0.1 ms | mean 0.005 ms, max 0.08 ms |

Side-by-side shots (research): the background matches the starter's frame shot for shot, and the speaker's head is in the same
third. What differs:
- the starter's frame has the dialogue camera's depth of field;
- the NPC's body shows only where the listener's copy of the NPC stands. Placing the bodies at the dialogue twins' positions is
  not on `main`.

## Open

1. The listener's own Henry renders without his head while the view is on, the first-person body's head hide. It shows only if
   he stands in a shot. The fix needs the game camera manager's head-visible mode.
2. No depth of field on the listener's view.
3. The bodies at the dialogue twins' positions (WO-203 I.3b placement).
4. A real three-person conversation recorded and replayed.
5. More than two players: the listener and the starter must include the host.
