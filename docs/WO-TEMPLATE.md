# WO-NNN — <title>

**Model:** <model>. **Target:** <hours>; every box is a hard stop. **Version:** <the maintainer's exact string, or "no bump">.

## Scope, in one line
<what is built, and what stays exactly as it is>

## Standing rules (short form, all mandatory)
> **Actions go to the authority; values come back.** The host's world decides. The joiner's game shows, and asks; it
> never decides. What the host must simulate is sent as an *action*. What the joiner only displays is sent as a
> *value*. Never the other way round.

- Modding Tools build only; never launch, test on, inject into or change anything for retail.
- Port-aware native code: RTTI-name lookups, fields by what they point at, no fixed addresses/exports/vtable slots in code,
  the hook-prologue registry, one resolver with a build-identity check, a thread id on every native log line.
- Engine calls on the main thread, `fault::guarded`, pointers validated that frame; no silent guards.
- Lua never depends on the game's debug console commands; check the effect, not the return value.
- Saves: back up the whole saves folder byte-equal before anything; copies of saves in the throwaway `playline4` only; never
  write a real slot. The mod must never damage a player's save; a workaround that has to be re-run is not a fix.
- Protocol append-only. "The host" / "the joiner". Privacy in commits (no names, hosts, addresses, user paths).
- **Publishing (WO-179):** never write the names of untracked, private or reference folders (or of the files in them) into
  anything public -- code, comments, docs, test names, log tags, file names, commit messages. `tools/Check-Publishing.ps1`
  scans every pending commit against a denylist kept outside the repository (`KCDMP_PUBLISH_DENYLIST`) and blocks the push;
  `tools/Build-Installer.ps1 -Push` pushes only through it.
- Input policy: real input only to the game, with the game in focus; nothing that could work as a keylogger is ever shipped.
- New mechanisms ship on, each with its own switch.
- **Mutation twins (WO-206):** every fix and every check ships with a twin -- the same build with that fix or rule switched
  off -- and its test must FAIL on the twin. A test that passes on both proves nothing. Show the twin's failure in the findings.
- **Test co-op with co-op (WO-206):** a change that touches sync, joining, quests, conversations, combat or copies gets a pair
  run on the two-copy bench (`tools/rig/`) before an installer is built. A synthetic-partner pass is a development step, not
  a proof.
- **Start the way players start (WO-206):** the bench starts the agent the launcher's way (the host: load, "Gameplay started",
  10 s, inject, verify, agent; the joiner: its main menu, the host in, inject, verify, agent, the Join answer). A harness
  that starts things in another order says so in its findings.
- **The release gate (WO-206):** an installer is built only after the pair smoke test passes on that exact build -- join,
  walk, a sleep, a wait, a fight, a joined conversation, a loot take -- and its result is printed in the findings.

## Parts
<Part 0 gates, then the boxed items, each with its acceptance check>

## Tests
<saves used (copies in playline4), live runs, what needs two players>

## Build and output
<installer and gates; findings (public, short); checklist; final reply shape>
