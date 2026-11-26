# Rules for AI tools working in this repository

This file is for any AI coding tool that opens this repository (Codex, Cursor, Claude Code and others). It holds the project's hard
rules in plain words. They apply to every change, whoever asks for it. If a request conflicts with one of them, say so and do not
make the change.

Kingdom Come: Together is an unofficial, free co-op mod for Kingdom Come: Deliverance II. It is not affiliated with or endorsed by
Warhorse Studios or PLAION. Its code is GPL-3.0-only with the additional terms in `NOTICE`.

## The hard rules

1. **No input capture.** Nothing in this project may read what a person types or clicks: no keyboard or mouse hooks, no key-state
   polling, no raw-input registration, no clipboard reads, no screenshots of anything but the game's own picture where a test needs
   one. `tools/Check-NoInputCapture.ps1` must pass on every binary and script; never weaken it or add an exemption for your change.
   Test tools may *send* keys to the game's own window while it is in front. They never read input, and they are never shipped.

2. **Nothing outside the mod's own install is copied, moved or altered.** The mod writes only its own files: its install folder, its
   own log and settings files, and its mod folder inside the game. It never changes the game's own files, the player's other mods,
   system settings, the registry outside its own uninstall entry, or anything else on the player's PC.

3. **Save safety is absolute.** The mod must never damage, overwrite or delete a player's save. A joiner's save game never takes the
   host's world into it. Tests run on copies of saves in a throwaway slot; a real save slot is never written. A change that could
   touch saving or loading needs the save round-trip gate to pass.

4. **No NPC brain runs on the joiner.** The host's world decides what every NPC does. On the joiner's machine an NPC is a copy that
   shows what the host's NPC does. Do not wake, run or "briefly enable" a copy's own AI on the joiner to make something look right:
   it makes two worlds that disagree. Actions go to the host; values come back.

5. **Reliable events are never dropped.** A message the game's state depends on (a death, a hit's verdict, loot, a quest change, a
   door, a join) is delivered or retried. It is never thrown away to save time or bandwidth, and never silently: when something is
   refused, a log line says what and why. Only streams that the next sample replaces (positions, poses) may be skipped.

6. **A modified version must not call itself official.** Official builds are signed by the project's maintainer with a key that is
   not in this repository; every build says "Official build" or "Unofficial build" by checking that signature offline
   (`docs/OFFICIAL-BUILDS.md`). Do not change that check so a build without the maintainer's signature reads "Official", do not
   remove or hide the "Unofficial build" text, and do not replace the public key in order to present your own build as the
   project's. Forks are welcome under the licence; they say what they are.

## Also always

- **Privacy.** Nothing committed names a person, a handle, an IP address, a host name or a user path. Write "the host", "the
  joiner", "a tester", "a contributor", "the maintainer".
- **The version string is the maintainer's.** Never change `VERSION`, never tag, never publish a release.
- **`README.md` is the maintainer's.** Do not edit it.
- **No network calls beyond the mod's own session traffic.** No telemetry, no update checks, no "phone home". The official-build
  check is offline.
- **New mechanisms ship switched on, each with its own switch**, and with a twin test: the same build with the switch off must fail
  the test that the switch-on build passes.
- **Third parties' terms stand.** Do not copy code from other mods or tools into this repository, and do not ship the game's own
  files.
- **Engine calls** are made on the thread and at the point in the frame where the game itself makes them. Read the notes in
  `docs/` for the area you touch before you add one.

## Where to look

- `docs/PROJECT-STATE.md` — what exists and how the parts fit.
- `docs/KNOWN-ISSUES.md` — what is known not to work.
- `docs/WO-TEMPLATE.md` — how a piece of work is specified, tested and reported here.
- `tools/rig/` — the two-copy test bench; `tools/Build-Installer.ps1` — the release path and its gates.
