# WO-144 — Field fixes: progress

Answer and evidence: `docs/WO-144-findings.md`. This page is what was done, what
it touched, and what is left.

## Phase status

| phase | status | notes |
|---|---|---|
| 0 — the bookmark | done | the tag `v0.42.0` on `53d3ba0` pushed before any code |
| 1.1 — phantom partner | done | partners from the relay's live connections; the relay replaces a player's old connection; the vote's members (observed) |
| 1.2 — the host's crash on a load | done | the faction node's own `SetParent`; ten loads in a row (observed) |
| 1.3 — the talk hold | done | held only once the player is in the conversation (observed) |
| 1.4 — tutorial-era joins | done | money decides, items warn, every abort says why (synthetic; joins observed) |
| 2.1 — clothes both ways | done | the avatar's live soul; dressed from its own inventory; read back every 10 s; layers, back-off, the game's reason (observed) |
| 2.2 — crouch | done | the activity reconcile never touches an avatar's crouch (observed) |
| 2.3 — horses | partly | hidden copies shown; non-living horse physics written; parked encounters with no physics not bound (observed) |
| 2.4 — the lantern | done | lights never outfit pieces; NPC lamps taken out (observed) |
| 3.1 — fighting animals | precondition done | living animal copies bind and show; the hit not run (no input) |
| 3.2 — dice | done | the copy keeps its brain for the minigame after the conversation (synthetic) |
| 3.3 — one clock | done | the joiner's clock stands with the host's (observed); tolerance one game-minute; pulls fixed |
| 3.4 — the HUD icon | identified | the wanted indicator; clearing needs the guard copies' forget message (not built) |
| 3.5 — player sounds | not done | needs one live key press to confirm the hook |
| 4.1 — corrections | done | a missed port is not fired again (unit) |
| 4.2 — a stuck scene | detection | logged; not ended |
| 4.3 — a forced conversation | not done | its evidence is in the unattached group 2 |
| 4.4 — escort | not done | |
| 4.5 — NPC clothing state | half | placements keep the copy's equipment state (observed); the host's not sent |
| 4.6 — journal letters | not done | |
| 5 — the small things | partly | sit-down, host claim, NullRefs, floating log line, log retention done; grindstone blade, alchemy anchor not done; seated tools: rule kept |
| 6 — gates, docs, build | done | every gate green; docs; `VERSION` 0.42.2; the local installer from a fresh clone |

## The field bundles

| bundle | what it is |
|---|---|
| `HOST1-Precrash-KCDMP-logs-20260929-194158.zip` | the host, before its crash (session 3) |
| `JOINER1-Precrash-KCDMP-logs-20260929-194208.zip` | the joiner, the same moment |
| `HOST2-Postcrash-KCDMP-logs-20260929-202818.zip` | the host after the crash and a restart; its `agent.prev.log` is the whole pre-crash session |
| `JOINER2-Postcrash-KCDMP-logs-20260929-202817.zip` | the joiner, the end of the evening |

The two earlier groups the work order mentions were not attached. The crash's
minidumps (three with the same fault) were on this machine, in the crash
reporter's store.

## Live runs (all solo, one machine, the Modding Tools game)

| run | what | build |
|---|---|---|
| L0 | host + a stale connection + the scripted partner on **0.42.0**: `peers=2` reproduced; the first in-session load crashed at `RPGModule+0x4394A4` (the report was not sent) | 0.42.0 |
| L1 | the fixed partner set and relay: the stale connection replaced after 3.2 s, `peers=1`; five loads, no crash — but the re-created avatar was not re-attached (fixed after) | a1 |
| L1b | ten in-session loads in a row, each with the avatar attached: no crash; a sleep vote at a bed: asked of one partner, yes, the picker, the sleep | a1 |
| H2 | host: the dressed avatar, crouch and the sneak walk; the engine taking the outfit off at the end of a crouched walk; REST equips failing; the mod's dress and the 10 s watch; the lantern at night, the torch on and off; riding; the bed-edge sit | a2–a4 |
| J1 | joiner of a synthetic host: a tutorial-era character joined; the joiner's clock standing with the host's; **the saved soul answering for `kcd2mp_0`** | a5 |
| J2 | joiner: the live soul found, the host's avatar dressed and stable; horse copies hidden (and shown); a wolf copy; the soul's own lamp taken out; a copy undressed for the night placed in bed with its equipment kept; a canceled talk request | a6 |
| J3 | joiner: the talk hold only in the conversation; `WO144-SHOW` from the pak | a7 |

Frames (`docs/wo144-shots/`): 1 the host's screen, the partner's avatar dressed;
2 crouched; 3 at night, no light; 4 the torch with its player's; 5 riding; 6 the
joiner's screen, the host's avatar dressed (J2); 7 a horse copy shown on the
joiner.

## What changed

* **Agent** (`dotnet/KcdMp.Client`): `Wo144.cs` (the partner set, the vote's
  members, clothes' layers and back-off, lights, the claim's grace, the live
  soul's key match, the correction ledger), `GameBridge.Wo144.cs` (the partner
  events, the clock, the engine lines, the dress and lights switches, the outfit
  watch, the live soul, the corrections), and the loops that used the old name
  table; `HttpGameTransport` (routes an avatar's calls to its live soul);
  `CombatPipe` (never uses a dropped pipe); `WhsSave` (a Henry's item list
  present or not); the Henry check (`JudgeHenry`); the vote (`Left` drops a
  member).
* **Relay** (`dotnet/KcdMp.Server`, `KcdMp.Steam`): a connection's identity (the
  address, the Steam identity) and the immediate replacement.
* **DLL** (`native/KCDMP`): the faction node's own `SetParent`; the crouch never
  in the activity reconcile for avatars; the bed-edge sit as lying; a horse's or
  animal's non-living physics written; the copy's equipment state kept through
  placements; the clock pull's verdicts.
* **Mod** (`kdcmp.lua`, the pak): the talk hold's start, the minigame after a
  conversation, the canceled request; the clock follow; the dress, lights,
  avatar key, float, show functions and their switches.
* **Launcher**: the log history before each launch; Report a bug collects it.
* **Tests**: agent 637 (was 622 in 0.42.0), relay 59, native 298, WO-137's suite
  89 checks; `Verify-Install.ps1` has the WO-144 markers.

## Side effects of the runs

* The throwaway playline (`playline4`, save copies made for this work) got four
  autosaves during L1's load series: an in-session load drops the test's save
  lock. No real playline was written: at the end all 317 save files matched the
  backup's checksums, and the throwaway playline was moved out of the saves
  folder.
* The Modding Tools mod folder carried the test paks during the runs; the
  original pak and manifest were put back.
* The host's clock in H2 was moved forward by console for daylight frames (in
  the throwaway world only).
* No input was sent; the game ran minimized behind other windows (about 26 fps).
* The maintainer's own launcher, agent and relay were not stopped or touched.

## Gates

Relay round trip 59/59; agent unit tests 637/637; every `Test-*Synthetic.ps1`
(39 suites; `Test-WO137Synthetic` 89/89); both static checks; native unit tests
298/298; the local publish; the payload smoke. The installer build ran every
gate again inside a fresh clone of `origin/main`.

## Runbook: how the live runs were made

One machine, no second player. The harness is the repo's own tools:
`tools/wo121/avatarpeer` (a scripted partner that sends positions, the state
block, outfits, activities, rides and torches from a control file) and
`tools/wo118/synthpeer` (a synthetic host that serves a join from a save copy
and streams NPC rows, pauses, clock skips and activities on command).

1. Back up every playline with a checksum list; make a throwaway playline of
   save copies.
2. Start the Modding Tools game minimized (the host: load the copy; the joiner:
   stay at the menu), inject the build's DLL, start a relay on a free port and
   the agent (`--hosting` for the host).
3. Host runs: the avatarpeer as the partner; its control lines drive the
   checks (`appearance`, `state crouch=1`, `move`, `ride`, `torch`, `activity`).
   Joiner runs: the synthpeer as the host with `--join-host125 <save copy>`;
   its control lines (`pos`, `appearance`, `npc`, `pause 80`, `sleeptime`,
   `activity npc`) drive them.
4. Read the agent's log, `kcd.log` and the DLL's mirror log; aim the camera and
   take frames by console and window capture only.
5. At the end: quit the game by console, put the mod folder back, move the
   throwaway playline out, compare every save's checksum with the backup.
