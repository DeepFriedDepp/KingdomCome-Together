<#
.SYNOPSIS
    Answers two different questions that are easy to confuse: did the build
    produce the right files, and did the installer actually put them on disk.

.DESCRIPTION
    Written in WO-34 after a 0.11.8 install turned out to be half-applied --
    the mod pak updated, the agent did not -- which made WO-32's NPC sync a
    silent no-op: the Lua half was present and waiting, the agent half was a
    build old and never called it. Nothing errored. Nothing looked wrong.

    Three layers are checked independently, because a failure in any one of
    them looks identical from the game:

      BUILT     release\KCDMP\*        what Publish-Release.ps1 produced
      SHIPPED   kdcmp\Data\kdcmp.pak   what Build-And-Install-Mod.ps1 produced
      INSTALLED %LocalAppData%\KCDMP   what the player's machine is running
                <ModdingTools>\Mods\kdcmp\Data\kdcmp.pak

    WO-74 added two layers below those, both driven by the install manifest
    Setup now ships inside the install directory: the installer's own verdict
    (install-verify.txt), and an independent sha256 re-check of every
    component plus a hunt for files that no release ships. A foreign assembly
    sitting in the install directory is not a cosmetic problem -- .NET loads
    by filename out of that folder, and one is enough to stop the relay
    cold-starting.

    Feature presence is probed by string literal, not by version number. A
    .NET assembly keeps the same AssemblyVersion across builds, so "is it the
    new one" cannot be answered from metadata -- but the Lua entry points the
    agent calls (KCD2MP_ApplyNpcState and friends) are plain literals in
    GameBridge.cs and land verbatim in the assembly's string heap. If the
    literal is absent, the code that calls it is absent.

    Read-only. Touches nothing.

.PARAMETER AppDir
    Override the installed app directory. Defaults to %LocalAppData%\KCDMP,
    which is DefaultDirName in installer\KCDMP.iss.

.PARAMETER ModDir
    Override the installed mod directory (the folder holding Data\kdcmp.pak).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Verify-Install.ps1
#>
[CmdletBinding()]
param(
    [string] $AppDir = (Join-Path $env:LOCALAPPDATA 'KCDMP'),
    [string] $ModDir = 'D:\SteamLibrary\steamapps\common\KCD2Mod\Mods\kdcmp'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$rel  = Join-Path $root 'release\KCDMP'
$fail = 0

function Get-Strings([string] $path) {
    # Both encodings: .NET string literals live in the #US heap as UTF-16,
    # but metadata names and native strings are ASCII/UTF-8.
    #
    # The two decodes from offset 0 AND offset 1 are not paranoia -- they are
    # the whole correctness of this probe. A UTF-16 literal can begin at either
    # byte parity, and decoding only from 0 silently misses every literal that
    # starts on an odd offset. That is a coin flip per string, and it changes
    # between builds as the heap shifts. It cost a false "WO-28 regression"
    # alarm the first time this script ran: KCD2MP_ReconcileGhosts read as
    # ABSENT from a build that contained it perfectly well.
    #
    # A miss here reads as "the feature is not in this build", which is exactly
    # the conclusion this script exists to make trustworthy.
    $b = [IO.File]::ReadAllBytes($path)
    $u0 = [Text.Encoding]::Unicode.GetString($b, 0, $b.Length - ($b.Length % 2))
    $u1 = [Text.Encoding]::Unicode.GetString($b, 1, $b.Length - 1 - (($b.Length - 1) % 2))
    $u0 + "`n" + $u1 + "`n" + [Text.Encoding]::ASCII.GetString($b)
}

# marker -> the work order that would break if it were missing
$AsmMarkers = @(
    @{ File = 'KcdMpClient.dll'; Marker = 'KCD2MP_ApplyNpcState';   Owner = 'WO-32 NPC sync (agent half)' },
    @{ File = 'KcdMpClient.dll'; Marker = 'KCD2MP_ReconcileGhosts'; Owner = 'WO-28/34 ghost reconcile' },
    @{ File = 'KcdMpClient.dll'; Marker = 'KCD2MP_SetGhostDead';    Owner = 'WO-28/34 death tag' },
    @{ File = 'KcdMpServer.dll'; Marker = 'MasterAnnounce';         Owner = 'WO-35 master server' },
    @{ File = 'KcdMp.Protocol.dll'; Marker = 'MasterApi';           Owner = 'WO-35 master API' },
    # 0.20.6 and later. Added in WO-84 because the list above stopped at
    # WO-35: four releases of agent- and relay-side work shipped with nothing
    # here able to tell a new build from an old one, which is exactly the
    # half-applied-install failure this script was written for.
    @{ File = 'KcdMpClient.dll'; Marker = 'CutscenePlayer::PlayCutscene called for Rendered cutscene'
                                                                  ; Owner = 'WO-80 cutscene pause detection' },
    @{ File = 'KcdMpServer.dll'; Marker = '[CLAIM-CONTESTED]';      Owner = 'WO-81 relay claim logging' },
    # 0.21.1 (WO-86). Death sync spans all three artefacts; an agent that
    # predates it ignores the FATAL bit and never kills the peer's copy.
    @{ File = 'KcdMpClient.dll'; Marker = 'KCD2MP_NpcRemoteDeath';  Owner = 'WO-86 NPC death apply (agent half)' },
    @{ File = 'KcdMpServer.dll'; Marker = '[NPCDEATH]';             Owner = 'WO-86 relay FATAL logging' },
    # 0.21.5 (WO-88, built in WO-89). Agent-only fixes; an agent that predates
    # these still clears a death tag on a health=0 packet and never re-dresses
    # a respawned ghost's body.
    @{ File = 'KcdMpClient.dll'; Marker = 'body respawned';         Owner = 'WO-88 appearance-after-respawn fix' },
    @{ File = 'KcdMpClient.dll'; Marker = 're-sending the convergence'; Owner = 'WO-88 reload time-sync convergence resend' },
    # 0.22.0 (WO-94). Shared Quests spans agent + pak; an agent that predates
    # it drops StoryBeat kinds 2-4 and never raises the prompt.
    @{ File = 'KcdMpClient.dll'; Marker = 'CATCH-UP FIRED HERE';    Owner = 'WO-94 Shared Quests (agent half)' },
    @{ File = 'KcdMpClient.dll'; Marker = 'CATCHUP-HAZARD';         Owner = 'WO-94 catch-up hazard tagging' },
    # 0.29.9 (WO-127): Steam as a connection path, the host claim, the leash recorder.
    @{ File = 'KcdMp.Steam.dll';   Marker = 'SteamJoinCode';         Owner = 'WO-127 Steam connection path' },
    @{ File = 'KcdMpServer.dll'; Marker = 'MP-HOST-CLAIM';          Owner = 'WO-127 host claim (relay authority)' },
    @{ File = 'KcdMpClient.dll'; Marker = 'MP-LEASH-COST';          Owner = 'WO-127 leash recorder (agent half)' },
    # 0.30.0 (WO-129): the gait tag hook (native), the clock-skew removal and
    # the Discord merge fix (agent).
    @{ File = 'KCDMP.dll';       Marker = 'WO129-GAIT tag hook';    Owner = 'WO-129 avatar/NPC-copy gait (native)' },
    @{ File = 'KcdMpClient.dll'; Marker = 'no (no clock sample yet)'; Owner = 'WO-129 host-stamp skew removal' },
    # WO-114: the leash (agent decides and pulls), the wake within the leash
    # (native), the leash messages on the join channel (protocol v10).
    @{ File = 'KcdMpClient.dll'; Marker = 'MP-LEASH pulled from=';  Owner = 'WO-114 leash pull (agent half)' },
    @{ File = 'KCDMP.dll';       Marker = 'MP-RESPAWN-LEASH partner at'; Owner = 'WO-114 wake within the leash (native)' },
    @{ File = 'KcdMp.Protocol.dll'; Marker = 'leash-state';         Owner = 'WO-114 leash wire rows (protocol v10)' },
    # WO-131: combat and bodies -- the joiner's hit gate and copy guard (agent),
    # the pipe 0x21 ops, the ragdoll drop and the death StopFight (native).
    @{ File = 'KcdMpClient.dll'; Marker = 'MP-WO131-STATS';         Owner = 'WO-131 hit gate / copy guard (agent half)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO131-STOPFIGHT';        Owner = 'WO-131 StopFight on deaths (native)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO131-FACTION';          Owner = 'WO-131 avatar in the player faction (native)' },
    @{ File = 'KcdMpClient.dll'; Marker = 'MP-W132-STATS';          Owner = 'WO-132 damage safety / engagement (agent half)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO132-LEAVEFIGHT';       Owner = 'WO-132 one soul leaves its skirmish (native)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO132-ENGAGE';           Owner = 'WO-132 copy engagement on a joiner (native)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO132-HITS';             Owner = 'WO-132 NPC hits on avatars measured at the chokepoint (native)' },
    # WO-133: the old quest layer off in a shared world; only the host moves the clock.
    @{ File = 'KcdMpClient.dll'; Marker = 'KCD2MP_Wo133Gate';       Owner = 'WO-133 shared-world quest gate (agent half)' },
    @{ File = 'KcdMpClient.dll'; Marker = "only the host's clock moves the world"; Owner = 'WO-133 host drops joiner time skips' },
    @{ File = 'KCDMP.dll';       Marker = 'WO133-PORTGATE';         Owner = 'WO-133 file-armed port trigger never fires in a session (native)' },
    # WO-134: world items (bodies, loose items, chest ledgers) and the rebrand
    @{ File = 'KcdMpClient.dll'; Marker = 'KCD2MP_W134BodyState';   Owner = 'WO-134 bodies shared in the host world (agent half)' },
    @{ File = 'KcdMpClient.dll'; Marker = 'MP-WO134-STATS';         Owner = 'WO-134 chest ledgers and loot requests (agent half)' },
    @{ File = 'KcdMpClient.dll'; Marker = 'MP-WO135-STATS';         Owner = 'WO-135 avatar puppet, knockouts, same-build Henry (agent half)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO135-DIALOG';           Owner = 'WO-135 the avatar is never heard (native dialogue gate)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO135-QUIET';            Owner = 'WO-135 the avatar puppet contexts (native)' },
    @{ File = 'KcdMpClient.dll'; Marker = 'MP-WO136-STATS';         Owner = 'WO-136 load hold, stand-ins, rider horse, torch (agent half)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO136-HANDOVER';         Owner = 'WO-136 the fight goes on with the partner (native)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO136-FORCED';           Owner = 'WO-136 enemies choose the joiner: combat_forcedTarget (native)' },
    # WO-137: shared quests (the mirror, the requests, talking, dead is dead)
    @{ File = 'KcdMpClient.dll'; Marker = 'MP-WO137-STATS';         Owner = 'WO-137 shared quests: mirror, requests, checkpoints (agent half)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO137-BUILD';            Owner = 'WO-137 quest State detector and Set-port apply (native)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO137-TIMESET';          Owner = "WO-137 quest time sets are the host's only (native time gate)" },
    @{ File = 'KcdMp.Protocol.dll'; Marker = 'quest-host';          Owner = 'WO-137 quest messages on the join channel (wire)' },
    # WO-138: no pausing + the host's NPC stream in the DLL
    @{ File = 'KcdMpClient.dll'; Marker = 'MP-WO138-STATS';         Owner = 'WO-138 native NPC sender, pause announcement, hold, levers (agent half)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO138-GATE';             Owner = 'WO-138 no menu pauses the world in a session (native PauseGame gate)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO138-SEND';             Owner = 'WO-138 the NPC stream at the frame hook (native sender)' },
    # WO-139: crime and guards (the joiner's crimes in the host's world, the stop, the pursuit)
    @{ File = 'KcdMpClient.dll'; Marker = 'MP-WO139-STATS';         Owner = 'WO-139 crime and guards: judging, stops, pursuits (agent half)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO139-TRESPASS';         Owner = "WO-139 the joiner's trespass from the HUD's own state (native)" },
    @{ File = 'KCDMP.dll';       Marker = 'WO139-PURSUE';           Owner = "WO-139 the host's guards fight the joiner's avatar: combat_forcedTarget (native)" },
    @{ File = 'KCDMP.dll';       Marker = 'WO139-TIMESET';          Owner = 'WO-139 the punishment moves no clock in a session (native time gate)' },
    @{ File = 'KcdMp.Protocol.dll'; Marker = 'crime-host';          Owner = 'WO-139 crime messages on the join channel (wire)' },
    @{ File = 'KcdMp.Protocol.dll'; Marker = 'loot-host';           Owner = 'WO-134 loot messages on the join channel (wire)' },
    # WO-140: sleeping together (the vote, everyone's sleep screen, one clock) and the own-world trap
    @{ File = 'KcdMpClient.dll'; Marker = 'MP-WO140-STATS';         Owner = 'WO-140 sleeping together: the vote, the Begin, the pin (agent half)' },
    @{ File = 'KcdMpClient.dll'; Marker = 'connected from its own world -- NOT joined (separate)'; Owner = 'WO-140 the own-world trap: nothing of the host applied (agent half)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO140-HELD';             Owner = "WO-140 the sleep gate on the game's own picker (native)" },
    @{ File = 'KCDMP.dll';       Marker = 'WO140-START';            Owner = "WO-140 the accepter's own sleep, no bed (native)" },
    @{ File = 'KCDMP.dll';       Marker = 'WO140-PULL';             Owner = "WO-140 one clock: a joiner ahead is pulled back (native)" },
    @{ File = 'KcdMp.Protocol.dll'; Marker = 'sleep-vote';          Owner = 'WO-140 the sleep vote on the join channel (wire)' },
    @{ File = 'KCDMP_launcher.dll'; Marker = "Stay at the main menu. Don't load a save."; Owner = "WO-140 the joiner's Ready-to-connect line (launcher)" },
    @{ File = 'KCDMP_launcher.dll'; Marker = 'YOU LOADED YOUR OWN SAVE'; Owner = 'WO-140 the own-world modal (launcher)' },
    # WO-141: activities (sitting, lying, leaning, working, the trough's wash) and animal attacks
    @{ File = 'KcdMpClient.dll'; Marker = 'MP-W141 stats:';         Owner = 'WO-141 activities: the rows, the players, the one-shots (agent half)' },
    @{ File = 'KcdMpClient.dll'; Marker = 'leaves its activity first'; Owner = 'WO-141 a copy leaves its activity for a fight, a talk, a knockout or a death (agent half)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO141-BUILD';            Owner = "WO-141 the NPC-state read and the game's own placement (native)" },
    @{ File = 'KCDMP.dll';       Marker = 'WO141-APPLY';            Owner = 'WO-141 the reconcile: a copy takes the activity, the writer yields (native)' },
    @{ File = 'KCDMP.dll';       Marker = 'WO141-SHOW';             Owner = "WO-141 the player's one-shot shown as an NPC activity (native)" },
    @{ File = 'KCDMP.dll';       Marker = 'is not a human -- the actor system'; Owner = "WO-141 an animal's bite plays on the joiner's copy (native)" },
    @{ File = 'KcdMp.Protocol.dll'; Marker = 'activity-host';       Owner = 'WO-141 activities on the join channel (wire)' },
    # WO-144: the field fixes (the partner set, the crash, the clothes, the lights, the claim)
    @{ File = 'KcdMpClient.dll'; Marker = 'a removed ghost stays removed'; Owner = 'WO-144 partners only from live connections (agent half)' },
    @{ File = 'KcdMpServer.dll'; Marker = 'replaces its older connection'; Owner = "WO-144 the relay replaces a player's old connection at once" },
    @{ File = 'KCDMP.dll';       Marker = 'SetParent REFUSED';      Owner = "WO-144 the faction node's own SetParent (the load crash, native)" },
    @{ File = 'KcdMpClient.dll'; Marker = 'is another soul too';    Owner = "WO-144 an avatar's live soul, not a saved one of the same name (agent half)" },
    @{ File = 'KcdMpClient.dll'; Marker = 'came off since the last check'; Owner = "WO-144 the avatar's outfit read back every 10 s (agent half)" },
    @{ File = 'KcdMpClient.dll'; Marker = 'not fired again for that value'; Owner = 'WO-144 a checkpoint correction that misses is not repeated (agent half)' },
    @{ File = 'KCDMP.dll';       Marker = 'it lies on it';          Owner = "WO-144 a player's bed-edge sit shown lying (native)" },
    @{ File = 'KCDMP.dll';       Marker = 'body=horse-or-animal (entity-written)'; Owner = 'WO-144 a horse or animal copy written like any body (native)' },
    @{ File = 'KCDMP_launcher.dll'; Marker = 'log-history';         Owner = "WO-144 earlier launches' logs kept (launcher)" },
    # WO-147: the joiner fights, the leash pulls, destructive quest steps checked
    @{ File = 'KcdMpClient.dll'; Marker = 'MP-W147-STATS';          Owner = 'WO-147 the joiner fights, the leash, quest safety (agent half)' },
    @{ File = 'KcdMpClient.dll'; Marker = 'is no enemy of this player'; Owner = "WO-147 only an enemy's copy is engaged (agent half)" },
    @{ File = 'KcdMpClient.dll'; Marker = 'held until this world agrees'; Owner = 'WO-147 a destructive quest step waits for the host world (agent half)' },
    @{ File = 'KcdMpClient.dll'; Marker = 'the hold is over: the countdown runs'; Owner = 'WO-147 the leash hold lasts at most 60 s (agent half)' },
    @{ File = 'KcdMpClient.dll'; Marker = 'the engine cancelled the skip'; Owner = "WO-147 a cancelled skip ends the host's pause (agent half)" },
    @{ File = 'KCDMP.dll';       Marker = 'ApplyDamage non-lethal'; Owner = "WO-147 the host's non-fatal hit never kills a copy (native)" },
    @{ File = 'KCDMP.dll';       Marker = 'exact (no ground search)'; Owner = "WO-147 the leash's pull onto the host's own spot (native)" },
    @{ File = 'KCDMP.dll';       Marker = 'WO147-TEST';             Owner = "WO-147 mp_test_hit, the stand-in for the player's blow (native)" },
    @{ File = 'KcdMpClient.dll'; Marker = 'ghost_superseded=';      Owner = 'WO-147 the frame backlog: superseded samples skipped (agent half)' },
    @{ File = 'KCDMP.dll';       Marker = 'no silence of the streams'; Owner = "WO-147 the frame backlog: a stall is no stream silence (native)" },
    @{ File = 'KCDMP.dll';       Marker = 'the player is on the ground (or 60 s passed)'; Owner = "WO-147 the pull's fall hold lasts until there is ground under the player (native)" },
    @{ File = 'KCDMP_launcher.dll'; Marker = 'Kingdom Come: Together'; Owner = 'WO-134 rebrand (launcher window title)' }
)

$PakMarkers = @(
    @{ Marker = 'function mp_ghost_is_corpse';     Owner = 'WO-34 corpse freeze' },
    @{ Marker = 'local frozen = mp_ghost_is_corpse'; Owner = 'WO-34 InterpTick freeze' },
    @{ Marker = 'is DEAD in this world';           Owner = 'WO-34 corpse recycling' },
    @{ Marker = 'function KCD2MP_ApplyNpcState';   Owner = 'WO-32 NPC sync (mod half)' },
    @{ Marker = 'npc_state';                       Owner = 'WO-32 npc_state event' },
    # 0.20.6 (WO-78). The probe-confirmed chain-restart gate and the two leak
    # detectors. If these are absent the pak predates the fix and every menu
    # longer than a second still starts a duplicate update loop.
    @{ Marker = 'was suspended, not dead';         Owner = 'WO-78 probe-confirmed restart gate' },
    @{ Marker = 'GHOST CHAIN LEAK CONFIRMED';      Owner = 'WO-78 ghost chain leak detector' },
    @{ Marker = 'NPC-SYNC CHAIN LEAK CONFIRMED';   Owner = 'WO-78 puppet chain leak detector' },
    # 0.20.8 (WO-84). The three fixes that only exist in the pak: without a
    # rebuild the Lua in the repo and the Lua in the pak silently disagree,
    # which is the specific thing WO-84's own progress note warned about.
    @{ Marker = 'local function mp_anim_loop';     Owner = 'WO-84 ghost animation throttle' },
    @{ Marker = 'KCD2MP._npcPuppetRetired';        Owner = 'WO-84 chain generation retirement' },
    @{ Marker = 'SweepStrayGhosts: untracked';     Owner = 'WO-84 stray ghost-body sweep' },
    @{ Marker = 'faction attempt ghost';           Owner = 'WO-84 faction attempt logging' },
    # 0.20.9 (WO-83). The roster swap lives only in the pak; a pak that still
    # carries ttac_man_9 as a roster ROW (not this note) spawns guard-class
    # ghosts that enforce the drawn-weapon crime on the other player.
    @{ Marker = 'WO-83: was ttac_man_9';           Owner = 'WO-83 authority-soul roster swap' },
    # 0.21.1 (WO-86). A pak without these still lets a locally-dead body follow
    # a living stream and never announces a death.
    @{ Marker = 'NPC-DEATH DIVERGENCE';            Owner = 'WO-86 corpse-drag safeguard' },
    @{ Marker = 'function KCD2MP_NpcRemoteDeath';  Owner = 'WO-86 NPC death apply (mod half)' },
    # 0.21.5 (WO-88, built in WO-89). Read-only diagnostic; absence just means
    # mp_probe_dialog is not yet available, no behavior depends on it.
    @{ Marker = 'function KCD2MP_ProbeDialog';     Owner = 'WO-88 mp_probe_dialog' },
    # 0.22.0 (WO-94). The registry, the prompt and the hazard window live
    # only in the pak; without a rebuild the agent talks to a mod that has
    # no KCD2MP_QuestShowPrompt and every approach is a silent no-op.
    @{ Marker = '@@WO94-MAINQUEST-REGISTRY-BEGIN@@'; Owner = 'WO-94 main-quest registry' },
    @{ Marker = 'function KCD2MP_QuestShowPrompt'; Owner = 'WO-94 readiness prompt' },
    @{ Marker = 'CATCHUP-HAZARD';                 Owner = 'WO-94 hazard window (mod half)' },
    @{ Marker = 'MP_NPC_DIVERGE_COOLDOWN_S = 180'; Owner = 'WO-94 180 s divergence stand-off' },
    # 0.29.9 (WO-127): mp_leash_trace lives in the pak.
    @{ Marker = 'function KCD2MP_SetLeashTrace';  Owner = 'WO-127 mp_leash_trace' },
    # 0.30.0 (WO-129): the host's join bar and the shared-world scan anchors.
    @{ Marker = 'function KCD2MP_JoinBarText';     Owner = 'WO-129 host join bar (stage + seconds)' },
    # WO-114: the leash settings, the countdown row and the joiner's fast-travel switch.
    @{ Marker = 'function KCD2MP_Wo114Countdown';  Owner = 'WO-114 leash countdown row' },
    @{ Marker = 'function KCD2MP_Wo114FastTravelBlock'; Owner = 'WO-114 host-only fast travel' },
    @{ Marker = 'function KCD2MP_W131Tick';        Owner = 'WO-131 joiner copy guard' },
    @{ Marker = 'function KCD2MP_W131StandIn';     Owner = 'WO-131 stand-ins for host-only NPCs' },
    @{ Marker = 'function KCD2MP_W131LootAllowed'; Owner = 'WO-131 joiner loot block' },
    @{ Marker = 'function KCD2MP_Wo129SharedAnchors'; Owner = 'WO-129 every player an NPC scan anchor' },
    # WO-133: the old quest layer is off in a shared world.
    @{ Marker = 'function KCD2MP_Wo133Gate';       Owner = 'WO-133 shared-world quest gate (mod half)' },
    @{ Marker = 'function KCD2MP_W134LootRequest'; Owner = 'WO-134 a joiner loot is a request to the host (mod half)' },
    @{ Marker = 'function KCD2MP_W134HostItem';    Owner = 'WO-134 loose world items per world (mod half)' },
    @{ Marker = 'function KCD2MP_W134ChestApply';  Owner = 'WO-134 chests per player across rejoins (mod half)' },
    @{ Marker = 'function W134.takeAway';          Owner = 'WO-134 world items leave for good (no item-slot respawn)' },
    @{ Marker = 'function KCD2MP_W135KoTick';      Owner = 'WO-135 knockouts follow the host (mod half)' },
    @{ Marker = 'function KCD2MP_W135HostTakedown'; Owner = 'WO-135 takedowns are requests to the host (mod half)' },
    @{ Marker = 'function KCD2MP_W136Hold';        Owner = 'WO-136 the load hold (mod half)' },
    @{ Marker = 'function KCD2MP_W136RideTake';    Owner = 'WO-136 the rider owns the horse (mod half)' },
    @{ Marker = 'function KCD2MP_W136AvatarTorch'; Owner = 'WO-136 the avatar holds the torch (mod half)' },
    @{ Marker = 'function KCD2MP_W137Session';     Owner = 'WO-137 shared quests: the session and mp_quest_sync (mod half)' },
    @{ Marker = 'function KCD2MP_W137TalkResume';  Owner = "WO-137 the joiner talks to the host's NPC (mod half)" },
    @{ Marker = 'function KCD2MP_W137HostHold';    Owner = "WO-137 the host holds its NPC busy (mod half)" },
    @{ Marker = 'function KCD2MP_W137DeadBody';    Owner = 'WO-137 dead is dead: a corpse is never paused (mod half)' },
    @{ Marker = 'function KCD2MP_W138Hold';        Owner = "WO-138 hold, don't hide: the host's copies stay while it is paused (mod half)" },
    @{ Marker = 'function KCD2MP_W138PushTrack';   Owner = "WO-138 the rescan set for the DLL's NPC sender (mod half)" },
    @{ Marker = 'function KCD2MP_W139Stop';        Owner = "WO-139 the stop: the game's own arrest against the joiner's Henry (mod half)" },
    @{ Marker = 'function KCD2MP_W139HostJudge';   Owner = "WO-139 the host judges the joiner's crime: witnesses, guards (mod half)" },
    @{ Marker = 'function KCD2MP_W139HostAttack';  Owner = "WO-139 the pursuit starts the guard's own attack (mod half)" },
    @{ Marker = 'function KCD2MP_W139RobRefused';  Owner = 'WO-139 the players never rob each other (mod half)' },
    @{ Marker = "You can't steal from each other in co-op."; Owner = 'WO-139 the plain refusal line' },
    @{ Marker = 'Quest catch-up is off in a shared world.'; Owner = 'WO-133 the plain refusal line' },
    @{ Marker = 'function KCD2MP_W140ReportUse';   Owner = "WO-140 the bed held before the lie-down (mod half)" },
    @{ Marker = 'function KCD2MP_W140Prompt';      Owner = "WO-140 the other player's sleep prompt (mod half)" },
    @{ Marker = 'function KCD2MP_W140Separate';    Owner = 'WO-140 the own-world message (mod half)' },
    @{ Marker = 'function KCD2MP_W144Equip';       Owner = "WO-144 an avatar dressed from its own inventory (mod half)" },
    @{ Marker = 'function KCD2MP_W147Hostiles';    Owner = "WO-147 the enemies' copies near the joiner (mod half)" },
    @{ Marker = 'function KCD2MP_W147EndDialog';   Owner = 'WO-147 a forced leash pull ends the conversation first (mod half)' },
    @{ Marker = 'function KCD2MP_SetQuestSafety';  Owner = 'WO-147 mp_quest_safety (mod half)' },
    @{ Marker = 'function KCD2MP_NpcSilenceRelease'; Owner = "WO-147 the frame backlog: a puppet released on the agent's word (mod half)" },
    @{ Marker = 'when their bodies come';          Owner = 'WO-147 a copy re-created by a load is reported whatever its id (mod half)' },
    @{ Marker = 'function KCD2MP_W144AvatarKey';   Owner = "WO-144 the live avatar's entity key (mod half)" },
    @{ Marker = 'function KCD2MP_W144LightTick';   Owner = 'WO-144 an avatar holds a light only while its player does (mod half)' },
    @{ Marker = 'function KCD2MP_W144FollowHostClock'; Owner = "WO-144 the joiner's clock stands with the host's (mod half)" },
    @{ Marker = 'function KCD2MP_W144ShowCopy';    Owner = 'WO-144 a hidden horse or animal copy shown (mod half)' },
    @{ Marker = 'WO144-FLOAT';                     Owner = 'WO-144 a copy held far above the ground is logged (mod half)' },
    @{ Marker = 'Other players are not ready to sleep yet!'; Owner = 'WO-140 the plain refusal line' },
    # WO-141: activities and animal attacks
    @{ Marker = 'function KCD2MP_SetActivities';   Owner = 'WO-141 mp_activities (mod half)' },
    @{ Marker = 'function KCD2MP_SetAnimalAttacks'; Owner = 'WO-141 mp_animal_attacks (mod half)' },
    @{ Marker = 'function KCD2MP_W141OnTriggerUse'; Owner = "WO-141 the player's one-shots at an object reach the agent (mod half)" },
    @{ Marker = 'function KCD2MP_W141AvatarStance'; Owner = "WO-141 the nameplate over a sitting or lying avatar (mod half)" }
)

function Test-Assembly($dir, $label) {
    Write-Host "`n[$label] $dir" -ForegroundColor Cyan
    if (-not (Test-Path $dir)) {
        # release\KCDMP is a build output, not part of an install. On a tester's
        # machine -- or any tree where nobody has run Publish-Release.ps1 since
        # the last clean-up -- its absence says nothing about the install, and
        # failing on it would make this script useless everywhere it matters
        # most. The manifest re-check below answers the same question without
        # needing a build tree at all.
        if ($label -like 'BUILT*') {
            Write-Host "  not present -- skipped (run tools\Publish-Release.ps1 to compare against a build)" -ForegroundColor Yellow
            return
        }
        Write-Host "  MISSING DIRECTORY" -ForegroundColor Red; $script:fail++; return
    }
    foreach ($m in $AsmMarkers) {
        $p = Join-Path $dir $m.File
        if (-not (Test-Path $p)) {
            Write-Host ("  {0,-22} {1,-24} FILE MISSING" -f $m.File, $m.Marker) -ForegroundColor Red
            $script:fail++; continue
        }
        $ok = (Get-Strings $p) -match [regex]::Escape($m.Marker)
        $colour = if ($ok) { 'Green' } else { 'Red' }
        if (-not $ok) { $script:fail++ }
        Write-Host ("  {0,-22} {1,-24} {2,-7} {3}" -f $m.File, $m.Marker, $(if($ok){'present'}else{'ABSENT'}), $m.Owner) -ForegroundColor $colour
    }
}

function Test-Pak($pak, $label) {
    Write-Host "`n[$label] $pak" -ForegroundColor Cyan
    if (-not (Test-Path $pak)) { Write-Host "  MISSING" -ForegroundColor Red; $script:fail++; return }
    $f = Get-Item $pak
    Write-Host ("  {0:N0} bytes   modified {1}   created {2}" -f $f.Length, $f.LastWriteTime, $f.CreationTime)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $z = [IO.Compression.ZipFile]::OpenRead($pak)
    try {
        $e = $z.Entries | Where-Object { $_.FullName -match 'kdcmp\.lua$' }
        if (-not $e) { Write-Host "  kdcmp.lua NOT IN PAK" -ForegroundColor Red; $script:fail++; return }
        $sr = New-Object IO.StreamReader($e.Open())
        $lua = $sr.ReadToEnd(); $sr.Close()
    } finally { $z.Dispose() }

    # Roster size is the WO-34 fix's own signature: 24 male entries means the
    # five bandit souls are back (or this pak predates the fix).
    $seg   = $lua.Substring($lua.IndexOf('KCD2MP.faceRoster = {'), 4000)
    $male  = ([regex]'male = \{(?s)(.*?)\n    \},').Match($seg).Groups[1].Value
    $count = ([regex]'\{"').Matches($male).Count
    $ok    = ($count -eq 19)
    if (-not $ok) { $script:fail++ }
    Write-Host ("  {0,-48} {1,-7} {2}" -f 'male roster entries', $count, 'WO-34 expects 19') -ForegroundColor $(if($ok){'Green'}else{'Red'})

    $bandits = @('tbuk_man_5','tkop_man_1','tkop_man_2','tzda_man_6','tzda_man_9') |
               Where-Object { $male -match [regex]::Escape($_) }
    if ($bandits) {
        Write-Host ("  HOSTILE SOULS BACK IN THE ROSTER: {0}" -f ($bandits -join ', ')) -ForegroundColor Red
        $script:fail++
    }

    foreach ($m in $PakMarkers) {
        $hit = $lua -match [regex]::Escape($m.Marker)
        if (-not $hit) { $script:fail++ }
        Write-Host ("  {0,-48} {1,-7} {2}" -f $m.Marker, $(if($hit){'present'}else{'ABSENT'}), $m.Owner) -ForegroundColor $(if($hit){'Green'}else{'Red'})
    }
}

Write-Host '=== KCD2-MP install verification ===' -ForegroundColor Cyan
Write-Host 'BUILT = what the build produced.  INSTALLED = what this machine runs.'
Write-Host 'They disagree when an installer could not overwrite a file that was in use.'

Test-Assembly $rel     'BUILT     app'
Test-Assembly $AppDir  'INSTALLED app'
Test-Pak (Join-Path $root 'kdcmp\Data\kdcmp.pak') 'BUILT     pak'
Test-Pak (Join-Path $ModDir 'Data\kdcmp.pak')     'INSTALLED pak'

# WO-74. The installer now proves itself and leaves the verdict behind, so the
# first question here is no longer "do these two folders look alike" but "what
# did Setup say when it finished". A stale PASS cannot survive an aborted run
# any more: install-verify.txt is stamped FAIL before the first file is copied
# and only becomes PASS at the end.
Write-Host "`n[INSTALLED] the installer's own verdict" -ForegroundColor Cyan
$verifyPath = Join-Path $AppDir 'install-verify.txt'
if (-not (Test-Path $verifyPath)) {
    Write-Host "  install-verify.txt MISSING -- this install predates WO-74, or Setup never finished" -ForegroundColor Red
    $fail++
} else {
    $verdict = Get-Content $verifyPath
    $green = $verdict[0] -like 'PASS*'
    if (-not $green) { $fail++ }
    $verdict | ForEach-Object { Write-Host ("  {0}" -f $_) -ForegroundColor $(if ($green) { 'Green' } else { 'Red' }) }
}

# Re-check the shipped manifest independently of Setup: same three questions
# (is every component there, is it the right content, is anything there that no
# release ships), asked by a different program. The installer computes this at
# install time; a machine can be damaged afterwards.
Write-Host "`n[INSTALLED] manifest re-check (sha256)" -ForegroundColor Cyan
$manifestPath = Join-Path $AppDir 'install-manifest.txt'
if (-not (Test-Path $manifestPath)) {
    Write-Host "  install-manifest.txt MISSING" -ForegroundColor Red
    $fail++
} else {
    $appRel = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $bad = 0; $checked = 0
    foreach ($line in Get-Content $manifestPath) {
        $line = $line.Trim()
        if ($line -eq '' -or $line.StartsWith('#')) { continue }
        $f = $line -split '\|'
        if ($f.Count -lt 4) { continue }
        $base = if ($f[0] -eq 'MOD') { $ModDir } else { $AppDir }
        if ($f[0] -eq 'APP') { [void]$appRel.Add($f[1]) }
        $full = Join-Path $base $f[1]
        $checked++
        if (-not (Test-Path $full)) {
            Write-Host ("  {0,-6} {1,-46} MISSING" -f $f[0], $f[1]) -ForegroundColor Red; $bad++; continue
        }
        $item = Get-Item $full
        if ($item.Length -ne [int64]$f[2]) {
            Write-Host ("  {0,-6} {1,-46} {2:N0} bytes, expected {3:N0}" -f $f[0], $f[1], $item.Length, [int64]$f[2]) -ForegroundColor Red
            $bad++; continue
        }
        if ((Get-FileHash $full -Algorithm SHA256).Hash -ne $f[3]) {
            Write-Host ("  {0,-6} {1,-46} WRONG CONTENT (sha256 differs)" -f $f[0], $f[1]) -ForegroundColor Red
            $bad++
        }
    }
    # The closed-set half. A file no release ships is exactly how a relay ends
    # up unable to cold-start (WO-69): .NET loads by filename out of this
    # directory, so a foreign assembly here wins over nothing at all.
    $strays = @()
    if (Test-Path $AppDir) {
        $appFull = (Get-Item $AppDir).FullName
        $strays = Get-ChildItem $appFull -Recurse -File |
            Where-Object { $_.Name -notlike 'unins*' -and
                           ($_.Extension -in '.dll', '.exe', '.pdb' -or
                            $_.Name -like '*.deps.json' -or $_.Name -like '*.runtimeconfig.json') } |
            ForEach-Object { $_.FullName.Substring($appFull.Length + 1) } |
            Where-Object { -not $appRel.Contains($_) }
    }
    foreach ($s in $strays) { Write-Host ("  STRAY  {0,-46} not in the manifest -- no release ships it" -f $s) -ForegroundColor Red }
    $bad += $strays.Count
    if ($bad -eq 0) { Write-Host ("  {0} components verified, no strays" -f $checked) -ForegroundColor Green }
    else { $fail++ }
}

# A file that is byte-identical in both places is the only proof the install
# actually landed -- timestamps are preserved by Inno and lie about this.
Write-Host "`n[BUILT vs INSTALLED] size comparison" -ForegroundColor Cyan
foreach ($n in @('KcdMpClient.dll','KcdMpServer.dll','KcdMp.Protocol.dll','KCDMP_launcher.dll')) {
    $b = Get-Item (Join-Path $rel $n)    -ErrorAction SilentlyContinue
    $i = Get-Item (Join-Path $AppDir $n) -ErrorAction SilentlyContinue
    if (-not $b -or -not $i) { Write-Host ("  {0,-22} cannot compare" -f $n) -ForegroundColor Yellow; continue }
    $same = ($b.Length -eq $i.Length)
    if (-not $same) { $script:fail++ }
    Write-Host ("  {0,-22} built {1,9:N0}   installed {2,9:N0}   {3}" -f `
        $n, $b.Length, $i.Length, $(if($same){'match'}else{'STALE INSTALL'})) -ForegroundColor $(if($same){'Green'}else{'Red'})
}

# Anything holding the install open is why a re-run would fail the same way.
$busy = Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -match 'KCDMP_launcher|KcdMpClient|KcdMpServer' }
if ($busy) {
    Write-Host "`nRUNNING -- these lock the files an installer needs to replace:" -ForegroundColor Yellow
    $busy | ForEach-Object { "  pid {0,-6} {1,-16} {2}" -f $_.Id, $_.ProcessName, $_.Path }
    Write-Host "  Close them before re-running Setup." -ForegroundColor Yellow
}

Write-Host ''
if ($fail -eq 0) { Write-Host 'ALL CHECKS PASSED' -ForegroundColor Green; exit 0 }
Write-Host "$fail CHECK(S) FAILED" -ForegroundColor Red
exit 1
