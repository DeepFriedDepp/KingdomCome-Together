using System.Text;
using System.Buffers.Binary;
using System.IO.Pipes;
using System.Threading.Channels;
using KcdMp.Wire;

namespace KcdMp.Client;

/// <summary>
/// The agent side of the channel to KCDMP.dll.
///
/// The DLL hosts the pipe and the agent connects, because the DLL's lifetime is
/// the game's: the agent may be restarted, may start before the game, or may not
/// be running at all, and the game carries on regardless. So this reconnects
/// rather than assuming the pipe is there.
///
/// This is the only path by which a remote player's damage reaches the game.
/// The Lua channel cannot do it — writes through Lua are inert — so if the DLL
/// is not injected, combat replication is simply unavailable and the agent says
/// so once rather than failing on every packet.
/// </summary>
public sealed class CombatPipe : IAsyncDisposable
{
    private const string PipeName = "kcdmp";

    private const byte ApplyDamage       = 0x01;
    private const byte ApplyDeath        = 0x02;
    private const byte Ping              = 0x03;
    private const byte SetFactionHostile = 0x04;
    private const byte GhostSwing        = 0x06;
    private const byte GhostIsolate      = 0x07;
    private const byte ReadBodyState     = 0x09;   // WO-100.5 Phase 2, read-only
    private const byte ReadLocalState    = 0x0A;   // WO-102 Phase 1, read-only
    private const byte ScanNpcs          = 0x0B;   // WO-102.5 Phase 2, read-only
    private const byte Result            = 0x81;
    private const byte Pong              = 0x83;
    private const byte BodyStateReply    = 0x85;   // WO-100.5 Phase 2
    private const byte LocalStateReply   = 0x86;   // WO-102 Phase 1
    private const byte NpcScanReply      = 0x87;   // WO-102.5 Phase 2
    private const byte SetSession        = 0x0C;   // WO-113
    private const byte SetRespawn        = 0x0D;   // WO-113
    private const byte MirrorGrave       = 0x0E;   // WO-113
    private const byte ListGraves        = 0x0F;   // WO-113
    private const byte GraveListReply    = 0x88;   // WO-113
    private const byte NpcSamples        = 0x10;   // WO-118
    private const byte NpcBind           = 0x11;   // WO-118
    private const byte NpcHold           = 0x12;   // WO-118
    private const byte NpcConfig         = 0x13;   // WO-118
    private const byte NpcStatus         = 0x14;   // WO-118
    private const byte NpcTrace          = 0x15;   // WO-118
    private const byte NpcStatusReply    = 0x89;   // WO-118
    private const byte NpcDropped        = 0x94;   // WO-118, unsolicited
    private const byte NpcTraceDone      = 0x95;   // WO-118, unsolicited
    // ---- WO-121: movement and combat ----
    private const byte MotionConfig      = 0x16;   // [avatarGait][npcGait][avatarMoves][avatarCombat][npcRows]
    private const byte AvatarEvent       = 0x17;   // [kind:1][eid:4]
    private const byte HitsConfig        = 0x18;   // [ffOn][attributionOn][pvpHookOn]
    private const byte AttributedDamage  = 0x19;   // [guid:16][stamina:4f][health:4f][flags:1][attackerEid:4]
    private const byte ApplyPvpHit       = 0x1A;   // [stamina:4f][health:4f][flags:1][attackerGhost:1]
    private const byte Wo121Status       = 0x1B;   // -> 0x8A
    private const byte Wo121StatusReply  = 0x8A;
    private const byte AttributedReply   = 0x8B;   // [ok][seq][steps][attackerWuid:8][victimWuid:8]
    private const byte LocalAction       = 0x96;   // unsolicited, LocalActionFrame
    private const byte PvpHitOut         = 0x97;   // unsolicited: [victimEid:4][stamina:4f][health:4f][flags][material]
    // WO-124: the joiner's side of the join (native/KCDMP/join_native.h, savelist.h)
    private const byte JoinPlace         = 0x1C;   // [hostX:4f][hostY:4f][hostZ:4f][dist:4f] -> 0x8C
    private const byte JoinPlaceReply    = 0x8C;
    private const byte SaveList          = 0x1D;   // [op][playline][nameLen][name] -> 0x8D
    private const byte SaveListReply     = 0x8D;
    private const byte JoinGuard         = 0x1E;   // -> 0x8E [ok][seq][session][enabled][applied]
    private const byte JoinGuardReply    = 0x8E;
    // WO-127: the leash recorder (native/KCDMP/leash.h)
    private const byte LeashSample       = 0x1F;   // [radius:4f][n][anchors n*12][offset:2] -> 0x8F
    private const byte LeashReply        = 0x8F;
    // WO-114 Phase 2: the other player's position for the death wake choice (respawn.h set_partner)
    private const byte SetPartner        = 0x20;   // [valid][x:4f][y:4f][z:4f][radius:4f] -> 0x81
    private const byte Wo131             = 0x21;   // WO-131 [op][...] -> 0x98 [ok][seq][op][reason][payload] (native wo131.h)
    private const byte Wo131Reply        = 0x98;
    private const byte Wo132             = 0x22;   // WO-132 [op][...] -> 0x99 [ok][seq][op][reason][payload] (native wo132.h)
    private const byte Wo132Reply        = 0x99;
    private const byte NpcAvatarHit      = 0x9A;   // WO-132, unsolicited: [victimEid:4][st:4f][hp:4f][attackerEid:4][flags][nameLen][name]
    private const byte NpcCombatOut      = 0x9B;   // WO-132, unsolicited: a watched NPC's combat state (wo132.h)
    private const byte DiscardedHit      = 0x9C;   // WO-132, unsolicited: [attackerEid:4][st:4f][hp:4f]
    private const byte Wo137             = 0x23;   // WO-137 [op][...] -> 0x9D [ok][seq][op][reason][payload] (native wo137.h)
    private const byte Wo137Reply        = 0x9D;
    private const byte QuestChangeOut    = 0x9E;   // WO-137, unsolicited: one quest State change (QuestChange)
    private const byte Wo138             = 0x24;   // WO-138 [op][...] -> 0x9F [ok][seq][op][reason][payload] (native wo138.h)
    private const byte Wo138Reply        = 0x9F;
    private const byte NpcStreamOut      = 0xA0;   // WO-138, unsolicited: the native sender's NPC rows
    private const byte WorldOut          = 0xA1;   // WO-138, unsolicited: this machine's world running / slowed / frozen
    private const byte Wo139             = 0x25;   // WO-139 [op][...] -> 0xA2 [ok][seq][op][reason][payload] (native wo139.h)
    private const byte Wo139Reply        = 0xA2;
    private const byte CrimeOut          = 0xA3;   // WO-139, unsolicited: a new trespass level of the local player
    private const byte Wo140             = 0x26;   // WO-140 [op][...] -> 0xA4 [ok][seq][op][reason][payload] (native wo140.h)
    private const byte Wo140Reply        = 0xA4;
    private const byte SleepOut          = 0xA5;   // WO-140, unsolicited: a held picker, a C_SkipTime state edge
    private const byte Wo141             = 0x27;   // WO-141 [op][...] -> 0xA6 [ok][seq][op][reason][payload] (native wo141.h)
    private const byte Wo141Reply        = 0xA6;
    private const byte ActivityOut       = 0xA7;   // WO-141, unsolicited: activity rows (the host's NPCs, the local player)
    private const byte Wo143             = 0x28;   // WO-143 [op][...] -> 0xA8 [ok][seq][op][reason][payload] (native wo143.h)
    private const byte Wo143Reply        = 0xA8;
    private const byte Wo147             = 0x29;   // WO-147 [op][...] -> 0xAA [ok][seq][op][reason][payload] (native wo147.h)
    private const byte Wo147Reply        = 0xAA;
    private const byte ExtraOut          = 0xA9;   // WO-143, unsolicited: hands, gaits, looks, one-shots, need-item, one-shot done

    private const int GuidLen = 16;

    private const byte LocalHit = 0x90;
    private const byte LocalDowned    = 0x91;   // WO-113, unsolicited
    private const byte LocalRespawned = 0x92;   // WO-113, unsolicited
    private const byte LocalGrave     = 0x93;   // WO-113, unsolicited

    /// <summary>
    /// WO-113: the DLL's death guard floored the player (on=true) or finished
    /// the respawn/wake-up (on=false). kind: 0 death, 1 knockdown, 2 execution.
    /// </summary>
    public Func<bool, byte, Task>? OnLocalDowned { get; set; }

    /// <summary>WO-113: where the player stands after a respawn; reason as LocalDowned's kind.</summary>
    public Func<float, float, float, byte, Task>? OnLocalRespawned { get; set; }

    /// <summary>WO-113: a grave was made (add=true) or is gone (looted empty / expired).</summary>
    public Func<bool, ulong, float, float, float, Task>? OnLocalGrave { get; set; }

    /// <summary>
    /// WO-121: an action the local engine committed -- the player's own
    /// (eid = 0), or an NPC's (eid and name set; the owner streams it as
    /// NpcAttack). kind is <see cref="KcdMp.Wire.ActionKind"/>.
    /// </summary>
    public Func<LocalActionFrame, Task>? OnLocalAction { get; set; }

    /// <summary>WO-121: the local player hit a peer's avatar: victim eid, stamina, health (what the hit took), flags, material.</summary>
    public Func<uint, float, float, byte, byte, Task>? OnPvpHit { get; set; }

    /// <summary>WO-132: an NPC hit a peer's avatar (measured over the hit window, put back): victim eid, stamina, health, attacker eid, flags, attacker name.</summary>
    public Func<uint, float, float, uint, byte, string, Task>? OnNpcAvatarHit { get; set; }

    /// <summary>WO-132: a watched NPC's combat state changed (or its 1 s heartbeat in combat).</summary>
    public Func<NpcCombatState, Task>? OnNpcCombat { get; set; }
    /// <summary>
    /// WO-137: one quest State change in this game's concept graph. Called ON the reader, in the
    /// DLL's order, so it must only enqueue (it never makes a pipe request: the WO-131 deadlock trap).
    /// </summary>
    public Action<QuestChange>? OnQuestChange { get; set; }

    /// <summary>WO-132: an engaged copy's local hit on the player was put back (attacker eid, stamina, health).</summary>
    public Func<uint, float, float, Task>? OnDiscardedHit { get; set; }

    /// <summary>
    /// WO-138: one 0xA0 frame of the DLL's NPC rows. Called ON the reader, so it only
    /// enqueues the sends (never a pipe request: the WO-131 deadlock trap).
    /// </summary>
    public Action<IReadOnlyList<Wo138Row>>? OnNpcStream { get; set; }

    /// <summary>WO-138: the 0xA1 world state (on a change, and every 2 s).</summary>
    public Action<Wo138WorldState>? OnWorld { get; set; }

    /// <summary>
    /// WO-139: 0xA3 kind 1 -- the local player's new trespass level (0 public .. 4 prohibited)
    /// and position. Called ON the reader: it must not make a pipe request (the WO-131 trap).
    /// </summary>
    public Action<byte, byte, float, float, float>? OnTrespass { get; set; }

    /// <summary>
    /// WO-140: 0xA5 -- (kind 1 Held: id) a sleep / wait picker the gate kept; (kind 2 State:
    /// edge, id, hours, state) a C_SkipTime edge. Called ON the reader: no pipe request here.
    /// </summary>
    public Action<Wo140Frame>? OnSleepFrame { get; set; }

    /// <summary>WO-141: 0xA7 -- activity rows the DLL read: (kind 1 an NPC | 2 the local player, name, activity).</summary>
    public Action<List<Wo141DllRow>>? OnActivityFrame { get; set; }

    /// <summary>WO-143: 0xA9 -- the host's capture (hands, gaits, looks, one-shots) and the joiner's asks (need-item, one-shot done).</summary>
    public Action<Wo143DllFrame>? OnExtraFrame { get; set; }

    /// <summary>WO-118: the DLL's native writer stopped a bound puppet on its own (reason, name).</summary>
    public Func<byte, string, Task>? OnNpcDropped { get; set; }

    /// <summary>WO-118 Phase 5: a trace CSV was written (rows, path; rows 0 = nothing recorded).</summary>
    public Func<uint, string, Task>? OnNpcTraceDone { get; set; }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private bool _warnedUnavailable;

    // LocalHit arrives unsolicited, interleaved with command replies, so a
    // single background reader owns the stream and routes frames: replies to
    // whoever is waiting, hits to the callback. Reading inline per command
    // would mistake a hit for a reply.
    //
    // WO-100 Phase 4: this used to be a single `_lastReply` slot plus a
    // SemaphoreSlim, and that had a real defect. When a command timed out, its
    // reply still arrived later, set the slot and released the semaphore --
    // so the NEXT command's wait returned instantly with the PREVIOUS
    // command's answer, and every reply after one timeout was attributed to
    // the wrong request, permanently. The DLL has always echoed a per-request
    // sequence byte in the Result frame (body[1]); nothing read it. Now the
    // reader hands replies through a bounded channel and the sender drops
    // replies older than the one it is waiting for -- counted and logged,
    // never silently.
    private Task? _reader;
    private Channel<(byte Type, byte[] Body)> _replies = NewReplyChannel();

    /// <summary>Bounded so a wedged sender cannot let replies accumulate without limit.</summary>
    private static Channel<(byte, byte[])> NewReplyChannel() =>
        Channel.CreateBounded<(byte, byte[])>(new BoundedChannelOptions(ReplyInboxCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true,
        });

    private const int ReplyInboxCapacity = 8;

    /// <summary>The sequence byte we expect next, or null until the first reply latches it.</summary>
    private byte? _expectedSeq;

    /// <summary>Replies discarded as stale (a late answer to a timed-out command).</summary>
    public long StaleRepliesDropped { get; private set; }

    /// <summary>Commands that got no answer inside the deadline.</summary>
    public long TimedOut { get; private set; }

    /// <summary>
    /// Raised when the DLL reports that a nearby NPC lost health for a reason
    /// this client did not cause. The handler is expected to put it on the wire.
    /// Arguments: soul guid, stamina delta, health delta, died (WO-86: the DLL's
    /// own "this drop took it to zero" bit, once per soul; false from a
    /// pre-WO-86 DLL whose frame stops at 24 bytes).
    /// </summary>
    /// WO-121: the fifth argument -- the DLL saw the LOCAL PLAYER land this hit at
    /// the combat-hit chokepoint (byte 25; false from an older DLL).
    public Func<Guid, float, float, bool, bool, Task>? OnLocalHit { get; set; }

    public bool IsConnected => _pipe?.IsConnected == true;

    /// <summary>
    /// Connect if not already connected. Returns false when the DLL is absent,
    /// which is a normal state rather than an error.
    /// </summary>
    public async Task<bool> EnsureConnectedAsync(CancellationToken ct = default)
    {
        if (IsConnected) return true;

        await _gate.WaitAsync(ct);
        try
        {
            if (IsConnected) return true;

            _pipe?.Dispose();
            _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await _pipe.ConnectAsync(500, ct);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                _pipe.Dispose();
                _pipe = null;
                if (!_warnedUnavailable)
                {
                    _warnedUnavailable = true;
                    Console.WriteLine("[combat] KCDMP.dll not injected — damage replication is unavailable.");
                }
                return false;
            }

            _warnedUnavailable = false;
            Console.WriteLine("[combat] connected to KCDMP.dll");
            _reader = Task.Run(ReadLoopAsync);
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Apply damage from a remote peer to the soul with this SharedSoulGuid.</summary>
    /// <summary>
    /// WO-147: <paramref name="nonLethal"/> (flag 0x02) = the damage never takes the body under 1 hp -- a joiner
    /// applying the host's non-fatal hit to its copy (the host decides deaths; the field's copy died of a hit
    /// its guard should have floored, after a load had taken the guard away).
    /// </summary>
    public Task<bool> ApplyDamageAsync(Guid soul, float stamina, float health,
                                       bool suppressHitReaction, CancellationToken ct = default, bool nonLethal = false)
    {
        var payload = new byte[GuidLen + 4 + 4 + 1];
        WriteSoulGuid(soul, payload);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(16), stamina);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(20), health);
        payload[24] = (byte)((suppressHitReaction ? 0x01 : 0x00) | (nonLethal ? 0x02 : 0x00));
        return SendAsync(ApplyDamage, payload, ct);
    }

    /// <summary>Kill the soul with this SharedSoulGuid. Idempotent in the DLL.</summary>
    public Task<bool> ApplyDeathAsync(Guid soul, CancellationToken ct = default)
    {
        var payload = new byte[GuidLen];
        WriteSoulGuid(soul, payload);
        return SendAsync(ApplyDeath, payload, ct);
    }

    /// <summary>
    /// Attach or detach a locally-spawned ghost's faction node to/from the
    /// mod's one v1 hostile faction (WO-17 reactive aggro). The DLL's own
    /// SetParent recipe (WO-15's ownership fix) does the actual write.
    ///
    /// <paramref name="ghostSoul"/> is the ghost's own Soul.Guid, NOT a
    /// SharedSoulGuid -- a locally-spawned ghost proxy carries
    /// SharedSoulGuid=0, so Guid is the identity that actually resolves
    /// through the DLL's SoulsByGuid lookup for it. Callers read it once via
    /// the debug REST API (SoulList/SoulsByName/kcd2mp_&lt;id&gt;) and may
    /// cache it for the ghost's lifetime.
    /// </summary>
    public Task<bool> SetFactionHostileAsync(Guid ghostSoulGuid, bool hostile, CancellationToken ct = default)
    {
        var payload = new byte[GuidLen + 1];
        WriteSoulGuid(ghostSoulGuid, payload);
        payload[16] = hostile ? (byte)0x01 : (byte)0x00;
        return SendAsync(SetFactionHostile, payload, ct);
    }

    /// <summary>
    /// WO-46: queue a real combat-swing animation on the ghost with this
    /// CryEngine entity id (the DLL runs the WO-45 rung-2 construction on the
    /// game thread). The id comes from the mod's spawn-time "ghostid" event,
    /// not a guid — entity ids are what the combat machinery natively
    /// resolves. fragSpec is a real "FragmentId, tag1+tag2" row from the
    /// shipped combat tables. A false return means an input failed to resolve
    /// (stale id after a respawn, unknown fragment) — normal, not fatal; a
    /// visually inert success (ghost's weapon sheathed) still returns true.
    /// </summary>
    public Task<bool> GhostSwingAsync(uint entityId, string fragSpec, CancellationToken ct = default)
        => GhostSwingForResultAsync(entityId, fragSpec, ct).ContinueWith(t => t.Result.Ok, ct,
               TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    /// <summary>
    /// WO-100 Phase 4 item 3: the same swing, with the DLL's specific reason.
    /// Prefer this over <see cref="GhostSwingAsync"/> anywhere the outcome is
    /// logged -- "the swing did not apply" is four different problems with four
    /// different fixes, and the bool cannot tell them apart.
    /// </summary>
    public Task<PipeResult> GhostSwingForResultAsync(uint entityId, string fragSpec, CancellationToken ct = default)
    {
        var spec = System.Text.Encoding.UTF8.GetBytes(fragSpec);
        if (spec.Length is 0 or > 191) return Task.FromResult(PipeResult.Fail(PipeReason.BadSpec));
        var payload = new byte[4 + spec.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, entityId);
        spec.CopyTo(payload.AsSpan(4));
        return SendForResultAsync(GhostSwing, payload, ct);
    }

    /// <summary>
    /// WO-68: apply (<paramref name="on"/>) or remove the seven civic-isolation
    /// script contexts on a locally-spawned ghost, natively -- the crime half
    /// WO-65 proved has no Lua setter on this build.
    ///
    /// <paramref name="ghostSoulGuid"/> is the ghost's own Soul.Guid, the same
    /// identity (and for the same reason) as
    /// <see cref="SetFactionHostileAsync"/>.
    ///
    /// A false return is routine rather than fatal: the ghost's soul may not be
    /// resolvable yet, or the DLL may have disarmed the feature after a fault.
    /// The caller logs it and carries on -- a ghost is never blocked on this.
    /// </summary>
    public Task<bool> GhostIsolateAsync(Guid ghostSoulGuid, bool on, CancellationToken ct = default)
    {
        var payload = new byte[GuidLen + 1];
        WriteSoulGuid(ghostSoulGuid, payload);
        payload[16] = on ? (byte)0x01 : (byte)0x00;
        return SendAsync(GhostIsolate, payload, ct);
    }

    /// <summary>
    /// WO-113: tell the DLL whether a multiplayer session is live. The death
    /// guard arms only while this is on (and mp_respawn is on); the pipe
    /// dropping clears it on the DLL side. Idempotent -- re-sent as a heartbeat.
    /// </summary>
    public Task<bool> SetSessionAsync(bool on, CancellationToken ct = default)
        => SendAsync(SetSession, [on ? (byte)1 : (byte)0], ct);

    /// <summary>WO-113: mp_respawn on/off, mirrored from the mod's console toggle.</summary>
    public Task<bool> SetRespawnAsync(bool on, CancellationToken ct = default)
        => SendAsync(SetRespawn, [on ? (byte)1 : (byte)0], ct);

    /// <summary>
    /// WO-113: a peer's mirror gravestone. op 1 add, 0 remove, 2 clear every
    /// mirror of <paramref name="owner"/> (0xFF = all owners).
    /// </summary>
    public Task<bool> MirrorGraveAsync(byte op, byte owner, ulong graveId, float x, float y, float z,
                                       CancellationToken ct = default)
    {
        var payload = new byte[1 + 1 + 8 + 12];
        payload[0] = op;
        payload[1] = owner;
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(2), graveId);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(10), x);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(14), y);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(18), z);
        return SendAsync(MirrorGrave, payload, ct);
    }

    /// <summary>
    /// WO-113: every grave this player still owns. Null when the DLL refused or
    /// predates the command (a pre-WO-113 DLL answers unknown-command).
    /// </summary>
    public async Task<List<(ulong Id, float X, float Y, float Z)>?> ListGravesAsync(CancellationToken ct = default)
    {
        var (body, _) = await SendAndAwaitAsync(ListGraves, [], GraveListReply, ct);
        if (body is null || body.Length < 3 || body[0] != 1) return null;
        int n = body[2];
        if (body.Length < 3 + n * 20) return null;
        var list = new List<(ulong, float, float, float)>(n);
        for (int i = 0; i < n; i++)
        {
            int o = 3 + i * 20;
            list.Add((BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(o)),
                      BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(o + 8)),
                      BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(o + 12)),
                      BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(o + 16))));
        }
        return list;
    }

    /// <summary>WO-118: one batch of inbound NPC samples for the native writer (0x10).</summary>
    public Task<PipeResult> NpcSamplesAsync(byte[] payload, CancellationToken ct = default)
        => SendForResultAsync(NpcSamples, payload, ct);

    /// <summary>
    /// WO-118: bind (or unbind) one puppet to the native writer. The DLL verifies
    /// id, name, WUID, parent and living body on the game thread; the result's
    /// raw reason byte is a <see cref="NativeNpcReason"/>.
    /// </summary>
    public async Task<(bool Ok, byte Reason)> NpcBindAsync(bool on, uint eid, ulong wuid, float ax, float ay, float az,
                                                             ushort delayMs, string name, CancellationToken ct = default)
    {
        var (body, fail) = await SendAndAwaitAsync(NpcBind, NativeNpcCodec.BuildBind(on, eid, wuid, ax, ay, az, delayMs, name), Result, ct);
        if (body is null) return (false, (byte)fail);
        return (body[0] == 1, body.Length >= 3 ? body[2] : (byte)255);
    }

    /// <summary>WO-118: no native writes for this puppet for <paramref name="ms"/> (a swing one-shot owns it).</summary>
    public Task<PipeResult> NpcHoldAsync(string name, ushort ms, CancellationToken ct = default)
        => SendForResultAsync(NpcHold, NativeNpcCodec.BuildHold(name, ms), ct);

    /// <summary>WO-118: mirror mp_npc_native_write and mp_npc_senderclock into the DLL.</summary>
    public Task<PipeResult> NpcConfigAsync(bool nativeOn, bool senderClock, CancellationToken ct = default)
        => SendForResultAsync(NpcConfig, [nativeOn ? (byte)1 : (byte)0, senderClock ? (byte)1 : (byte)0], ct);

    /// <summary>WO-118: the writer's counters (the 1 Hz heartbeat). Null when absent or refused.</summary>
    public async Task<NativeNpcStatus?> NpcStatusAsync(CancellationToken ct = default)
    {
        var (body, _) = await SendAndAwaitAsync(NpcStatus, [], NpcStatusReply, ct);
        return NativeNpcCodec.TryParseStatus(body, out var st) ? st : null;
    }

    /// <summary>WO-118 Phase 5: start (seconds &gt; 0) or stop (0) a per-frame trace of one named entity.</summary>
    public Task<PipeResult> NpcTraceAsync(string name, ushort seconds, CancellationToken ct = default)
        => SendForResultAsync(NpcTrace, NativeNpcCodec.BuildTrace(name, seconds), ct);

    /// <summary>WO-121: mirror the movement/combat toggles into the DLL (0x16).</summary>
    public Task<PipeResult> MotionConfigAsync(bool avatarGait, bool npcGait, bool avatarMoves, bool avatarCombat, bool npcRows,
                                              byte avatarQuiet = 0x0F, CancellationToken ct = default)
        => SendForResultAsync(MotionConfig, [B(avatarGait), B(npcGait), B(avatarMoves), B(avatarCombat), B(npcRows), (byte)(avatarQuiet & 0x0F)], ct);   // WO-135: [5] the avatar's quiet groups

    /// <summary>WO-121: a one-shot on a native-written avatar (0x17): kind 1 = jump.</summary>
    public Task<PipeResult> AvatarEventAsync(byte kind, uint eid, CancellationToken ct = default)
    {
        var p = new byte[5];
        p[0] = kind;
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(1), eid);
        return SendForResultAsync(AvatarEvent, p, ct);
    }

    /// <summary>WO-121: friendly fire / NPC attribution / the hit-slot filter (0x18).</summary>
    public Task<PipeResult> HitsConfigAsync(bool friendlyFire, bool attribution, bool pvpHook, CancellationToken ct = default)
        => SendForResultAsync(HitsConfig, [B(friendlyFire), B(attribution), B(pvpHook)], ct);

    /// <summary>
    /// WO-121 Phase 5: a peer's hit on a local NPC, WITH the peer's avatar as
    /// the attacker: damage, the combat-history write, a skirmish once per
    /// engagement. Returns the steps that ran (bit 0 damage, 1 history, 2
    /// skirmish) and the two WUIDs (the agent sends the brain message).
    /// </summary>
    public async Task<(bool Ok, byte Steps, ulong AttackerWuid, ulong VictimWuid, byte Reason)> AttributedDamageAsync(
        Guid soul, float stamina, float health, byte flags, uint attackerEid, string victimName, CancellationToken ct = default)
    {
        // [guid:16][st:4f][hp:4f][flags][attackerEid:4][nameLen][name]: the
        // DLL resolves the victim's entity by name for the history writer and
        // the skirmish (a soul GUID alone does not give the entity).
        byte[] name = Encoding.ASCII.GetBytes(victimName ?? "");
        if (name.Length > 63) name = Array.Empty<byte>();
        var p = new byte[GuidLen + 4 + 4 + 1 + 4 + 1 + name.Length];
        WriteSoulGuid(soul, p);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(16), stamina);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(20), health);
        p[24] = flags;
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(25), attackerEid);
        p[29] = (byte)name.Length;
        name.CopyTo(p, 30);
        var (body, fail) = await SendAndAwaitAsync(AttributedDamage, p, AttributedReply, ct);
        if (body is null) return (false, 0, 0, 0, (byte)fail);
        if (body.Length < 19) return (body.Length > 0 && body[0] == 1, 0, 0, 0, 254);
        return (body[0] == 1, body[2], BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(3)),
                BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(11)), 0);
    }

    /// <summary>
    /// WO-121 Phase 6: a partner's friendly-fire hit on OUR Henry, as plain
    /// damage with no attacker; the flags (unarmed) reach the death guard's
    /// knockdown classifier in the same call.
    /// </summary>
    public Task<PipeResult> ApplyPvpHitAsync(float stamina, float health, byte flags, byte attackerGhost, CancellationToken ct = default)
    {
        var p = new byte[10];
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(0), stamina);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(4), health);
        p[8] = flags; p[9] = attackerGhost;
        return SendForResultAsync(ApplyPvpHit, p, ct);
    }

    /// <summary>WO-121: the movement/combat module's armed pieces and counters (text), or null.</summary>
    public async Task<string?> Wo121StatusAsync(CancellationToken ct = default)
    {
        var (body, _) = await SendAndAwaitAsync(Wo121Status, [], Wo121StatusReply, ct);
        if (body is null || body.Length < 3 || body[0] != 1) return null;
        return System.Text.Encoding.UTF8.GetString(body, 2, body.Length - 2);
    }

    // ---- WO-124 ------------------------------------------------------------

    /// <summary>The engine's save list after an optional rescan (op 1 = rescan + find, 2 = rescan + Continue only, 3 = find only).</summary>
    public sealed record SaveListReport(bool Listed, int Idx, int Count, int Current, int ContinuePlayline, int ContinueIdx, string ContinueName);

    public async Task<SaveListReport?> SaveListAsync(byte op, int playline, string name, CancellationToken ct = default)
    {
        var nb = System.Text.Encoding.ASCII.GetBytes(name ?? "");
        if (nb.Length > 63) return null;
        var payload = new byte[3 + nb.Length];
        payload[0] = op; payload[1] = unchecked((byte)(sbyte)playline); payload[2] = (byte)nb.Length;
        nb.CopyTo(payload, 3);
        var (body, _) = await SendAndAwaitAsync(SaveList, payload, SaveListReply, ct);
        if (body is null || body.Length < 12 || body[0] != 1) return null;
        int cl = body[11];
        if (body.Length < 12 + cl) return null;
        return new SaveListReport(body[2] == 1, BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(3)), BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(5)),
                                  (sbyte)body[7], (sbyte)body[8], BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(9)),
                                  System.Text.Encoding.ASCII.GetString(body, 12, cl));
    }

    public sealed record JoinPlaceReport(bool Ok, bool Snapped, bool FallHeld, float[] Target, float[] Before, float[] After, float Residual);

    /// <summary>Put the player <paramref name="dist"/> m beside (x,y,z), on the ground, with the fall damage held (main thread, WO-113's teleport).</summary>
    public async Task<JoinPlaceReport?> JoinPlaceAsync(float x, float y, float z, float dist, CancellationToken ct = default)
    {
        var payload = new byte[16];
        BinaryPrimitives.WriteSingleLittleEndian(payload, x);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(4), y);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(8), z);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(12), dist);
        var (body, _) = await SendAndAwaitAsync(JoinPlace, payload, JoinPlaceReply, ct);
        if (body is null || body.Length < 44) return null;
        float[] V(int o) => [BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(o)), BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(o + 4)), BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(o + 8))];
        return new JoinPlaceReport(body[0] == 1, body[2] == 1, body[3] == 1, V(4), V(16), V(28), BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(40)));
    }

    /// <summary>
    /// WO-114 Phase 2: where the other player is and the leash's warning
    /// distance; a death then wakes within it (native wake_pick.h). valid=false
    /// = no partner (today's rule). The DLL forgets it after 10 s without one.
    /// </summary>
    public Task<PipeResult> SetPartnerAsync(bool valid, float x, float y, float z, float radius, CancellationToken ct = default)
    {
        var payload = new byte[17];
        payload[0] = B(valid);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(1), x);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(5), y);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(9), z);
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(13), radius);
        return SendForResultAsync(SetPartner, payload, ct);
    }

    /// <summary>(session, enabled, applied) of the WO-113 death guard, or null.</summary>
    public async Task<(bool Session, bool Enabled, bool Applied)?> JoinGuardAsync(CancellationToken ct = default)
    {
        var (body, _) = await SendAndAwaitAsync(JoinGuard, [], JoinGuardReply, ct);
        if (body is null || body.Length < 5 || body[0] != 1) return null;
        return (body[2] == 1, body[3] == 1, body[4] == 1);
    }

    // ---- WO-131 (native wo131.h) ---------------------------------------------

    /// <summary>One WO-131 op: (ok, reason, payload after the 4-byte head), or null when the DLL did not answer.</summary>
    public async Task<(bool Ok, byte Reason, byte[] Payload)?> Wo131Async(byte op, byte[] args, CancellationToken ct = default)
    {
        var p = new byte[1 + args.Length];
        p[0] = op; args.CopyTo(p, 1);
        var (body, _) = await SendAndAwaitAsync(Wo131, p, Wo131Reply, ct);
        if (body is null || body.Length < 4) return null;
        return (body[0] == 1, body[3], body.AsSpan(4).ToArray());
    }

    /// <summary>op 1: the joiner's hit gate facts for one NPC copy (null = no answer).</summary>
    public async Task<Wo131HitCheck?> Wo131HitCheckAsync(Guid soul, string name, CancellationToken ct = default)
    {
        byte[] nb = Encoding.ASCII.GetBytes(name ?? "");
        if (nb.Length == 0 || nb.Length > 63) return null;
        var a = new byte[16 + 1 + nb.Length];
        WriteSoulGuid(soul, a);
        a[16] = (byte)nb.Length; nb.CopyTo(a, 17);
        var r = await Wo131Async(1, a, ct);
        if (r is not { Ok: true } ok || ok.Payload.Length < 13) return null;
        var b = ok.Payload;
        return new Wo131HitCheck(b[0] == 1, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(1)),
            BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(3)), b[7], BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(8)),
            b[12] == 0xFF ? null : b[12] == 1);
    }

    /// <summary>op 2: the copy guard (imm+upr) on an NPC copy by entity id; (ok, present-after).</summary>
    public async Task<(bool Ok, bool Present, byte Reason)> Wo131CopyGuardAsync(bool on, uint eid, CancellationToken ct = default)
    {
        var a = new byte[5];
        a[0] = B(on); BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(1), eid);
        var r = await Wo131Async(2, a, ct);
        if (r is not { } v) return (false, false, 255);
        return (v.Ok, v.Payload.Length > 0 && v.Payload[0] == 1, v.Reason);
    }

    /// <summary>op 3: set a copy's health to the host's (credited, never below 1); (ok, before, after).</summary>
    public async Task<(bool Ok, float Before, float After, byte Reason)> Wo131FollowHpAsync(Guid soul, uint eid, float hp, CancellationToken ct = default)
    {
        var a = new byte[24];
        WriteSoulGuid(soul, a);
        BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(16), eid);
        BinaryPrimitives.WriteSingleLittleEndian(a.AsSpan(20), hp);
        var r = await Wo131Async(3, a, ct);
        if (r is not { } v) return (false, -1, -1, 255);
        if (!v.Ok || v.Payload.Length < 8) return (false, -1, -1, v.Reason);
        return (true, BinaryPrimitives.ReadSingleLittleEndian(v.Payload), BinaryPrimitives.ReadSingleLittleEndian(v.Payload.AsSpan(4)), 0);
    }

    /// <summary>op 4: StopFight on a body's soul (eid 0 = the local player).</summary>
    public async Task<(bool Ok, byte Reason)> Wo131StopFightAsync(uint eid, CancellationToken ct = default)
    {
        var a = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
        var r = await Wo131Async(4, a, ct);
        return r is { } v ? (v.Ok, v.Reason) : (false, (byte)255);
    }

    /// <summary>op 5: put an avatar's health back to <paramref name="hp"/>; (ok, before, after).</summary>
    public async Task<(bool Ok, float Before, float After, byte Reason)> Wo131RestoreHpAsync(uint eid, float hp, CancellationToken ct = default)
    {
        var a = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
        BinaryPrimitives.WriteSingleLittleEndian(a.AsSpan(4), hp);
        var r = await Wo131Async(5, a, ct);
        if (r is not { } v) return (false, -1, -1, 255);
        if (!v.Ok || v.Payload.Length < 8) return (false, -1, -1, v.Reason);
        return (true, BinaryPrimitives.ReadSingleLittleEndian(v.Payload), BinaryPrimitives.ReadSingleLittleEndian(v.Payload.AsSpan(4)), 0);
    }

    /// <summary>op 6: an avatar's faction (0 detach, 1 the hostile donor, 2 the player's faction).</summary>
    public async Task<(bool Ok, byte Reason)> Wo131FactionAsync(Guid avatarSoul, byte mode, CancellationToken ct = default)
    {
        var a = new byte[17];
        WriteSoulGuid(avatarSoul, a);
        a[16] = mode;
        var r = await Wo131Async(6, a, ct);
        return r is { } v ? (v.Ok, v.Reason) : (false, (byte)255);
    }

    /// <summary>op 7: the native counters (text), or null.</summary>
    public async Task<string?> Wo131StatusAsync(CancellationToken ct = default)
    {
        var r = await Wo131Async(7, [], ct);
        return r is { Ok: true } v ? Encoding.ASCII.GetString(v.Payload) : null;
    }

    // ---- WO-132 (native wo132.h) ---------------------------------------------

    /// <summary>One WO-132 op: (ok, reason, payload after the 4-byte head), or null when the DLL did not answer.</summary>
    public async Task<(bool Ok, byte Reason, byte[] Payload)?> Wo132Async(byte op, byte[] args, CancellationToken ct = default)
    {
        var p = new byte[1 + args.Length];
        p[0] = op; args.CopyTo(p, 1);
        var (body, _) = await SendAndAwaitAsync(Wo132, p, Wo132Reply, ct);
        if (body is null || body.Length < 4) return null;
        return (body[0] == 1, body[3], body.AsSpan(4).ToArray());
    }

    /// <summary>op 1: this body leaves its skirmish (eid 0 = the local player); the fight goes on.</summary>
    public async Task<(bool Ok, byte Reason)> Wo132LeaveFightAsync(uint eid, CancellationToken ct = default)
    {
        var a = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
        var r = await Wo132Async(1, a, ct);
        return r is { } v ? (v.Ok, v.Reason) : (false, (byte)255);
    }

    /// <summary>op 2: engage (or release) a bound copy against the local player with the host NPC's combat state.</summary>
    public async Task<(bool Ok, bool First, bool Skirmish, float DistM, byte Reason)> Wo132EngageAsync(bool on, uint eid, BodyState2 st, CancellationToken ct = default)
    {
        var a = new byte[1 + 4 + BodyState2.Len];
        a[0] = B(on);
        BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(1), eid);
        st.Write(a.AsSpan(5));
        var r = await Wo132Async(2, a, ct);
        if (r is not { } v) return (false, false, false, -1, 255);
        float dist = v.Payload.Length >= 6 ? BinaryPrimitives.ReadSingleLittleEndian(v.Payload.AsSpan(2)) : -1;
        return (v.Ok, v.Payload.Length > 0 && v.Payload[0] == 1, v.Payload.Length > 1 && v.Payload[1] == 1, dist, v.Reason);
    }

    /// <summary>op 3: the host watches (or stops watching) one NPC's combat state, by name; the entity id, or null.</summary>
    public async Task<uint?> Wo132WatchAsync(bool on, string name, CancellationToken ct = default)
    {
        byte[] nb = Encoding.ASCII.GetBytes(name ?? "");
        if (nb.Length == 0 || nb.Length > 63) return null;
        var a = new byte[6 + nb.Length];
        a[0] = B(on); a[5] = (byte)nb.Length; nb.CopyTo(a, 6);
        var r = await Wo132Async(3, a, ct);
        if (r is not { Ok: true } v || v.Payload.Length < 4) return null;
        return BinaryPrimitives.ReadUInt32LittleEndian(v.Payload);
    }

    /// <summary>op 4: the native counters (text), or null.</summary>
    public async Task<string?> Wo132StatusAsync(CancellationToken ct = default)
    {
        var r = await Wo132Async(4, [], ct);
        return r is { Ok: true } v ? Encoding.ASCII.GetString(v.Payload) : null;
    }

    /// <summary>op 5: this copy's hits on the local player are measured and put back (joiner).</summary>
    public async Task<bool> Wo132DiscardAsync(bool on, uint eid, CancellationToken ct = default)
    {
        var a = new byte[5];
        a[0] = B(on); BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(1), eid);
        var r = await Wo132Async(5, a, ct);
        return r is { Ok: true };
    }

    /// <summary>op 6: one combat-state read (eid 0 = the local player), or null.</summary>
    public async Task<NpcCombatState?> Wo132ReadAsync(uint eid, CancellationToken ct = default)
    {
        var a = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
        var r = await Wo132Async(6, a, ct);
        if (r is not { Ok: true } v || v.Payload.Length < 12) return null;
        var b = v.Payload;
        return new NpcCombatState(eid, b[1] == 1, Z(b[2]), Z(b[3]), Z(b[4]), b[5] == 1, b[6] == 1, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(8)), "", b[0] == 1);
    }

    private static sbyte Z(byte v) => v == 0xFF ? (sbyte)-1 : (sbyte)v;

    private static byte B(bool v) => v ? (byte)1 : (byte)0;

    /// <summary>Round-trip check that the DLL is alive and pumping frames.</summary>
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        if (!await EnsureConnectedAsync(ct)) return false;
        await _gate.WaitAsync(ct);
        try
        {
            while (_replies.Reader.TryRead(out _)) StaleRepliesDropped++;
            await WriteFrameAsync(Ping, [], ct);
            using var slice = CancellationTokenSource.CreateLinkedTokenSource(ct);
            slice.CancelAfter(ReplyDeadline);
            try
            {
                var reply = await _replies.Reader.ReadAsync(slice.Token);
                // A Ping consumes a sequence number in the DLL even though it
                // answers with Pong rather than Result, so the expectation has
                // to advance with it or every later reply looks misordered.
                if (_expectedSeq is byte want) _expectedSeq = (byte)(want + 1);
                return reply.Type == Pong;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                TimedOut++;
                return false;
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Drop();
            return false;
        }
        finally { _gate.Release(); }
    }

    // ---- WO-138 (native wo138.h) ---------------------------------------------

    /// <summary>One WO-138 op: (ok, reason, payload after the 4-byte head), or null when the DLL did not answer.</summary>
    public async Task<(bool Ok, byte Reason, byte[] Payload)?> Wo138Async(byte op, byte[] args, CancellationToken ct = default)
    {
        var p = new byte[1 + args.Length];
        p[0] = op; args.CopyTo(p, 1);
        var (body, _) = await SendAndAwaitAsync(Wo138, p, Wo138Reply, ct);
        if (body is null || body.Length < 4) return null;
        return (body[0] == 1, body[3], body.AsSpan(4).ToArray());
    }

    /// <summary>op 4: the sender / meter / gate status, or null (no DLL, or a pre-WO-138 one).</summary>
    public async Task<Wo138Status?> Wo138StatusAsync(CancellationToken ct = default)
    {
        var r = await Wo138Async(Wo138Codec.OpStatus, [], ct);
        return r is { Ok: true } ok && Wo138Codec.TryParseStatus(ok.Payload, out var s) ? s : null;
    }

    /// <summary>op 7: the status as one line.</summary>
    public async Task<string?> Wo138TextAsync(CancellationToken ct = default)
    {
        var r = await Wo138Async(Wo138Codec.OpText, [], ct);
        return r is { } x ? Encoding.ASCII.GetString(x.Payload) : null;
    }

    // ---- WO-139 (native wo139.h) ---------------------------------------------

    /// <summary>One WO-139 op: (ok, reason, payload after the 4-byte head), or null when the DLL did not answer.</summary>
    public async Task<(bool Ok, byte Reason, byte[] Payload)?> Wo139Async(byte op, byte[] args, CancellationToken ct = default)
    {
        var p = new byte[1 + args.Length];
        p[0] = op; args.CopyTo(p, 1);
        var (body, _) = await SendAndAwaitAsync(Wo139, p, Wo139Reply, ct);
        if (body is null || body.Length < 4) return null;
        return (body[0] == 1, body[3], body.AsSpan(4).ToArray());
    }

    /// <summary>op 1: the trespass detector on/off. (armed, last level) or null.</summary>
    public async Task<(bool Armed, byte Level)?> Wo139ConfigAsync(bool on, CancellationToken ct = default)
    {
        var r = await Wo139Async(1, [(byte)(on ? 1 : 0)], ct);
        if (r is not { } x || x.Payload.Length < 2) return null;
        return (x.Payload[0] == 1, x.Payload[1]);
    }

    /// <summary>op 2: the native status line.</summary>
    public async Task<string?> Wo139StatusAsync(CancellationToken ct = default)
    {
        var r = await Wo139Async(2, [], ct);
        return r is { Ok: true } x ? Encoding.ASCII.GetString(x.Payload) : null;
    }

    /// <summary>op 3: a guard fights (on) / stops fighting (off) an avatar. 1 done, 0 already, 2 no such, 3 refused; null = no answer.</summary>
    public async Task<byte?> Wo139PursueAsync(bool on, uint avatarEid, string guard, CancellationToken ct = default)
    {
        var name = Encoding.ASCII.GetBytes(guard);
        if (name.Length is < 1 or > 63) return null;
        var a = new byte[1 + 4 + 1 + name.Length];
        a[0] = (byte)(on ? 1 : 0);
        BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(1), avatarEid);
        a[5] = (byte)name.Length;
        name.CopyTo(a, 6);
        var r = await Wo139Async(3, a, ct);
        return r is { Ok: true } x && x.Payload.Length >= 1 ? x.Payload[0] : null;
    }

    /// <summary>op 4: one allowed entity context on a named NPC / horse. 1 written, 0 already, 2 no such, 3 refused, 4 not allowed.</summary>
    public async Task<byte?> Wo139ContextAsync(bool on, string context, string entity, CancellationToken ct = default)
    {
        var c = Encoding.ASCII.GetBytes(context); var n = Encoding.ASCII.GetBytes(entity);
        if (c.Length is < 1 or > 63 || n.Length is < 1 or > 63) return null;
        var a = new byte[1 + 1 + c.Length + 1 + n.Length];
        a[0] = (byte)(on ? 1 : 0);
        a[1] = (byte)c.Length; c.CopyTo(a, 2);
        a[2 + c.Length] = (byte)n.Length; n.CopyTo(a, 3 + c.Length);
        var r = await Wo139Async(4, a, ct);
        return r is { Ok: true } x && x.Payload.Length >= 1 ? x.Payload[0] : null;
    }

    /// <summary>op 5: the punishment's time sets run nothing (on). True = taken; false = not armed; null = no answer.</summary>
    public async Task<bool?> Wo139PunishGateAsync(bool on, CancellationToken ct = default)
    {
        var r = await Wo139Async(5, [(byte)(on ? 1 : 0)], ct);
        return r is { } x ? x.Ok : null;
    }

    // ---- WO-141 (native wo141.h) ---------------------------------------------

    /// <summary>One WO-141 op: (ok, reason, payload after the 4-byte head), or null when the DLL did not answer.</summary>
    public async Task<(bool Ok, byte Reason, byte[] Payload)?> Wo141Async(byte op, byte[] args, CancellationToken ct = default)
    {
        var p = new byte[1 + args.Length];
        p[0] = op; args.CopyTo(p, 1);
        var (body, _) = await SendAndAwaitAsync(Wo141, p, Wo141Reply, ct);
        if (body is null || body.Length < 4) return null;
        return (body[0] == 1, body[3], body.AsSpan(4).ToArray());
    }

    /// <summary>op 1: capture (bit 0 the tracked NPCs, bit 1 the local player), apply, the period. (readArmed, applyArmed) or null.</summary>
    public async Task<(bool Read, bool Apply)?> Wo141ConfigAsync(byte capture, bool apply, ushort periodMs, CancellationToken ct = default)
    {
        var r = await Wo141Async(1, [capture, (byte)(apply ? 1 : 0), (byte)(periodMs & 0xFF), (byte)(periodMs >> 8)], ct);
        return r is { Payload.Length: >= 1 } x ? ((x.Payload[0] & 1) != 0, (x.Payload[0] & 2) != 0) : null;
    }

    /// <summary>op 2: the body with this name takes the activity (reconciled until changed). True = it was already known.</summary>
    public async Task<bool?> Wo141ApplyAsync(string name, ActivityState a, CancellationToken ct = default)
    {
        var r = await Wo141Async(2, Wo141Codec.NameAndActivity(name, a), ct);
        return r is { Ok: true, Payload.Length: >= 1 } x ? x.Payload[0] == 1 : null;
    }

    /// <summary>op 3: the body leaves its activity (stands up; the writer takes it back). True = it had one.</summary>
    public async Task<bool?> Wo141LeaveAsync(string name, CancellationToken ct = default)
    {
        var r = await Wo141Async(3, Wo141Codec.Name(name), ct);
        return r is { Ok: true, Payload.Length: >= 1 } x ? x.Payload[0] == 1 : null;
    }

    /// <summary>op 4: one status line, or null.</summary>
    public async Task<string?> Wo141StatusAsync(CancellationToken ct = default)
    {
        var r = await Wo141Async(4, [], ct);
        return r is { } x ? System.Text.Encoding.ASCII.GetString(x.Payload) : null;
    }

    /// <summary>op 5: one read (name empty = the local player): the activity, or null when the body has no context.</summary>
    public async Task<ActivityState?> Wo141ReadAsync(string name, CancellationToken ct = default)
    {
        var r = await Wo141Async(5, Wo141Codec.Name(name), ct);
        return r is { Payload.Length: >= 1 + Protocol.ActivityBytes } x && x.Payload[0] == 1 && ActivityState.TryRead(x.Payload.AsSpan(1), out var a) ? a : null;
    }

    /// <summary>op 6: every desired activity dropped (no apply). The count, or null.</summary>
    public async Task<int?> Wo141ForgetAsync(CancellationToken ct = default)
    {
        var r = await Wo141Async(6, [], ct);
        return r is { Ok: true, Payload.Length: >= 2 } x ? BinaryPrimitives.ReadUInt16LittleEndian(x.Payload) : null;
    }

    /// <summary>op 7: the host's capture sends every activity again on its next tick.</summary>
    public async Task<bool> Wo141ResyncAsync(CancellationToken ct = default)
    {
        var r = await Wo141Async(7, [], ct);
        return r is { Ok: true };
    }

    /// <summary>op 8: the local player's captured activity is this NPC unstance at this object for tenths/10 s (0 ends it). True = shown.</summary>
    public async Task<bool?> Wo141ShowAsync(string unstance, string objectName, byte tenths, CancellationToken ct = default)
    {
        var r = await Wo141Async(8, Wo141Codec.Show(unstance, objectName, tenths), ct);
        return r is { Payload.Length: >= 1 } x ? x.Ok && x.Payload[0] == 1 : null;
    }

    // ---- WO-143 (native wo143.h) ---------------------------------------------

    /// <summary>One WO-143 op: (ok, reason, payload after the 4-byte head), or null when the DLL did not answer.</summary>
    public async Task<(bool Ok, byte Reason, byte[] Payload)?> Wo143Async(byte op, byte[] args, CancellationToken ct = default)
    {
        var p = new byte[1 + args.Length];
        p[0] = op; args.CopyTo(p, 1);
        var (body, _) = await SendAndAwaitAsync(Wo143, p, Wo143Reply, ct);
        if (body is null || body.Length < 4) return null;
        return (body[0] == 1, body[3], body.AsSpan(4).ToArray());
    }

    public async Task<(bool Ok, byte Reason, byte[] Payload)?> Wo147Async(byte op, byte[] args, CancellationToken ct = default)
    {
        var p = new byte[1 + args.Length];
        p[0] = op; args.CopyTo(p, 1);
        var (body, _) = await SendAndAwaitAsync(Wo147, p, Wo147Reply, ct);
        if (body is null || body.Length < 4) return null;
        return (body[0] == 1, body[3], body.AsSpan(4).ToArray());
    }

    /// <summary>
    /// WO-147 op 1: a console stand-in for one landed swing of the local player on the body
    /// <paramref name="eid"/> (marked as the player's blow, the damage taken with the player as the
    /// attacker). (ok, reason, health before, health after); null = no answer.
    /// </summary>
    public async Task<(bool Ok, byte Reason, float Before, float After)?> Wo147TestPlayerHitAsync(uint eid, float hp, float st, CancellationToken ct = default)
    {
        var a = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
        BinaryPrimitives.WriteSingleLittleEndian(a.AsSpan(4), hp);
        BinaryPrimitives.WriteSingleLittleEndian(a.AsSpan(8), st);
        var r = await Wo147Async(1, a, ct);
        if (r is not { } x) return null;
        float before = x.Payload.Length >= 8 ? BinaryPrimitives.ReadSingleLittleEndian(x.Payload) : -1;
        float after = x.Payload.Length >= 8 ? BinaryPrimitives.ReadSingleLittleEndian(x.Payload.AsSpan(4)) : -1;
        return (x.Ok, x.Reason, before, after);
    }

    /// <summary>WO-147 op 3: the guid of the soul the body <paramref name="eid"/> really holds; null = none.</summary>
    public async Task<Guid?> Wo147SoulGuidOfEidAsync(uint eid, CancellationToken ct = default)
    {
        var a = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(a, eid);
        var r = await Wo147Async(3, a, ct);
        return r is { Ok: true, Payload.Length: 16 } x ? new Guid(x.Payload) : null;
    }

    public async Task<string?> Wo147StatusAsync(CancellationToken ct = default)
    {
        var r = await Wo147Async(2, [], ct);
        return r is { Ok: true } x ? System.Text.Encoding.ASCII.GetString(x.Payload) : null;
    }

    /// <summary>op 1: capture and apply masks (Wo143Rules.Bit*). The armed bits (Wo143Rules.Armed*), or null.</summary>
    public async Task<byte?> Wo143ConfigAsync(byte capture, byte apply, CancellationToken ct = default)
    {
        var r = await Wo143Async(1, [capture, apply], ct);
        return r is { Ok: true, Payload.Length: >= 1 } x ? x.Payload[0] : null;
    }

    /// <summary>op 2: the copy with this name holds these tools (WO-141's reconcile carries them). The reason byte, or null.</summary>
    public async Task<byte?> Wo143HandsAsync(string name, byte[] left, byte[] right, CancellationToken ct = default)
    {
        var r = await Wo143Async(2, Wo143Codec.Hands(name, left, right), ct);
        return r?.Reason;
    }

    /// <summary>op 3: the copy's gait contexts := the host's mask (only pairs the DLL set are cleared). (set, cleared), or null.</summary>
    public async Task<(ushort Set, ushort Cleared)?> Wo143GaitsAsync(string name, ushort mask, CancellationToken ct = default)
    {
        var r = await Wo143Async(3, Wo143Codec.Gaits(name, mask), ct);
        return r is { Ok: true, Payload.Length: >= 4 } x ? (BinaryPrimitives.ReadUInt16LittleEndian(x.Payload), BinaryPrimitives.ReadUInt16LittleEndian(x.Payload.AsSpan(2))) : null;
    }

    /// <summary>op 4: play a one-shot on the body (a copy, or an avatar's minigame). The engine's request id (-1.. refused), or null.</summary>
    public async Task<int?> Wo143OneShotAsync(string name, string fragment, string tags, ulong alignGuid, byte flags, CancellationToken ct = default)
    {
        var r = await Wo143Async(4, Wo143Codec.OneShot(name, fragment, tags, alignGuid, flags), ct);
        return r is { Payload.Length: >= 4 } x ? BinaryPrimitives.ReadInt32LittleEndian(x.Payload) : null;
    }

    /// <summary>op 5: one status line, or null.</summary>
    public async Task<string?> Wo143StatusAsync(CancellationToken ct = default)
    {
        var r = await Wo143Async(5, [], ct);
        return r is { } x ? System.Text.Encoding.ASCII.GetString(x.Payload) : null;
    }

    /// <summary>op 6: a world loads -- the gait bookkeeping and running one-shots go (hands are WO-141's Forget).</summary>
    public async Task<int?> Wo143ForgetAsync(CancellationToken ct = default)
    {
        var r = await Wo143Async(6, [], ct);
        return r is { Ok: true, Payload.Length: >= 2 } x ? BinaryPrimitives.ReadUInt16LittleEndian(x.Payload) : null;
    }

    /// <summary>op 7: the host's capture sends every hand, gait and look row again on its next tick.</summary>
    public async Task<bool> Wo143ResyncAsync(CancellationToken ct = default)
    {
        var r = await Wo143Async(7, [], ct);
        return r is { Ok: true };
    }

    /// <summary>op 9: the writer stays off this body (on) while its minigame lasts; off releases only what op 9 set.</summary>
    public async Task<bool> Wo143HoldAsync(string name, bool on, CancellationToken ct = default)
    {
        var n = Wo141Codec.Name(name);
        var p = new byte[n.Length + 1]; n.CopyTo(p, 0); p[^1] = (byte)(on ? 1 : 0);
        var r = await Wo143Async(9, p, ct);
        return r is { Ok: true };
    }

    /// <summary>op 8: end the body's running one-shot or loop (a request with no extra action).</summary>
    public async Task<bool> Wo143StopAsync(string name, CancellationToken ct = default)
    {
        var r = await Wo143Async(8, Wo141Codec.Name(name), ct);
        return r is { Ok: true };
    }

    // ---- WO-140 (native wo140.h) ---------------------------------------------

    /// <summary>One WO-140 op: (ok, reason, payload after the 4-byte head), or null when the DLL did not answer.</summary>
    public async Task<(bool Ok, byte Reason, byte[] Payload)?> Wo140Async(byte op, byte[] args, CancellationToken ct = default)
    {
        var p = new byte[1 + args.Length];
        p[0] = op; args.CopyTo(p, 1);
        var (body, _) = await SendAndAwaitAsync(Wo140, p, Wo140Reply, ct);
        if (body is null || body.Length < 4) return null;
        return (body[0] == 1, body[3], body.AsSpan(4).ToArray());
    }

    /// <summary>op 1: the sleep gate and the state edges on/off. (armed, state) or null.</summary>
    public async Task<(bool Armed, byte State)?> Wo140ConfigAsync(bool on, CancellationToken ct = default)
    {
        var r = await Wo140Async(1, [(byte)(on ? 1 : 0)], ct);
        return r is { Payload.Length: >= 2 } x ? (x.Payload[0] == 1, x.Payload[1]) : null;
    }

    /// <summary>op 2: one status line, or null.</summary>
    public async Task<string?> Wo140StatusAsync(CancellationToken ct = default)
    {
        var r = await Wo140Async(2, [], ct);
        return r is { } x ? System.Text.Encoding.ASCII.GetString(x.Payload) : null;
    }

    /// <summary>op 3: the next picker passes (ms window); a kept one is shown again. True = a kept picker opened.</summary>
    public async Task<bool?> Wo140ApproveAsync(uint ms, CancellationToken ct = default)
    {
        var a = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(a, ms);
        var r = await Wo140Async(3, a, ct);
        return r is { Ok: true, Payload.Length: >= 1 } x ? x.Payload[0] == 1 : null;
    }

    /// <summary>op 4: the kept picker forgotten, the approval cleared. True = one was kept.</summary>
    public async Task<bool?> Wo140DropAsync(CancellationToken ct = default)
    {
        var r = await Wo140Async(4, [], ct);
        return r is { Ok: true, Payload.Length: >= 1 } x ? x.Payload[0] == 1 : null;
    }

    /// <summary>op 5: this game's own skip (no bed): 1 started, 0 the game said no, 2 busy, 3 bad hours; null = no answer / not armed.</summary>
    public async Task<byte?> Wo140StartAsync(byte id, float hours, CancellationToken ct = default)
    {
        var a = new byte[5]; a[0] = id; BinaryPrimitives.WriteSingleLittleEndian(a.AsSpan(1), hours);
        var r = await Wo140Async(5, a, ct);
        return r is { Ok: true, Payload.Length: >= 1 } x ? x.Payload[0] : null;
    }

    /// <summary>op 6: the running skip ends now. True = one was running.</summary>
    public async Task<bool?> Wo140StopAsync(CancellationToken ct = default)
    {
        var r = await Wo140Async(6, [], ct);
        return r is { Ok: true, Payload.Length: >= 1 } x ? x.Payload[0] == 1 : null;
    }

    /// <summary>op 7: the clock back to the host's (world seconds). (result 1 pulled / 0 not needed / 2 mismatch, before, after) or null.</summary>
    public async Task<(byte Result, uint Before, uint After)?> Wo140PullAsync(uint expect, uint target, CancellationToken ct = default)
    {
        var a = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(a, expect); BinaryPrimitives.WriteUInt32LittleEndian(a.AsSpan(4), target);
        var r = await Wo140Async(7, a, ct);
        return r is { Ok: true, Payload.Length: >= 9 } x
            ? (x.Payload[0], BinaryPrimitives.ReadUInt32LittleEndian(x.Payload.AsSpan(1)), BinaryPrimitives.ReadUInt32LittleEndian(x.Payload.AsSpan(5)))
            : null;
    }

    // ---- WO-137 (native wo137.h) ---------------------------------------------

    /// <summary>One WO-137 op: (ok, reason, payload after the 4-byte head), or null when the DLL did not answer.</summary>
    public async Task<(bool Ok, byte Reason, byte[] Payload)?> Wo137Async(byte op, byte[] args, CancellationToken ct = default)
    {
        var p = new byte[1 + args.Length];
        p[0] = op; args.CopyTo(p, 1);
        var (body, _) = await SendAndAwaitAsync(Wo137, p, Wo137Reply, ct);
        if (body is null || body.Length < 4) return null;
        return (body[0] == 1, body[3], body.AsSpan(4).ToArray());
    }

    /// <summary>op 1: detection on/off for this role (1 host, 2 joiner); flags 1 = the joiner's time gate.</summary>
    public async Task<bool?> Wo137ConfigAsync(bool detect, byte role, byte flags, CancellationToken ct = default)
    {
        var r = await Wo137Async(1, [(byte)(detect ? 1 : 0), role, flags], ct);
        return r?.Ok;
    }

    /// <summary>op 7: the detector keeps its records back (a load) or lets them go.</summary>
    public async Task<bool?> Wo137HoldAsync(bool on, CancellationToken ct = default)
    {
        var r = await Wo137Async(7, [(byte)(on ? 1 : 0)], ct);
        return r?.Ok;
    }

    public readonly record struct QuestApplied(byte Result, bool OldOk, int Old, bool NewOk, int New, string Type);

    /// <summary>op 2: one Set&lt;Value&gt; pulse on a quest State (null = the DLL did not answer).</summary>
    public async Task<QuestApplied?> Wo137ApplyAsync(uint tag, string path, string port, CancellationToken ct = default)
    {
        byte[] pb = Encoding.ASCII.GetBytes(path), qb = Encoding.ASCII.GetBytes(port);
        if (pb.Length == 0 || pb.Length > 400 || qb.Length == 0 || qb.Length > 100) return null;
        var a = new byte[4 + 2 + pb.Length + 1 + qb.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(a, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(4), (ushort)pb.Length);
        pb.CopyTo(a, 6);
        a[6 + pb.Length] = (byte)qb.Length;
        qb.CopyTo(a, 7 + pb.Length);
        var r = await Wo137Async(2, a, ct);
        if (r is not { Ok: true } v || v.Payload.Length < 11) return null;
        var b = v.Payload;
        int tl = b[10];
        string type = b.Length >= 11 + tl ? Encoding.ASCII.GetString(b, 11, tl) : "";
        return new QuestApplied(b[0], b[1] != 0, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(2)), b[6] != 0,
                                BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(7)), type);
    }

    public readonly record struct QuestStateRead(bool Found, int Runtime, bool Ok, int Val);

    /// <summary>op 3: the current values of these quest States (batched to the pipe's frame size; null = no answer).</summary>
    public async Task<List<QuestStateRead>?> Wo137ReadStatesAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        var all = new List<QuestStateRead>(paths.Count);
        int i = 0;
        while (i < paths.Count)
        {
            var batch = new List<byte[]>();
            int size = 2;
            while (i < paths.Count && batch.Count < 100)
            {
                var pb = Encoding.ASCII.GetBytes(paths[i]);
                if (size + 2 + pb.Length > 1000) break;
                batch.Add(pb); size += 2 + pb.Length; i++;
            }
            if (batch.Count == 0) return null;   // one path longer than a frame: never (paths are <= 400)
            var a = new byte[1 + batch.Sum(x => 2 + x.Length)];
            a[0] = (byte)batch.Count;
            int o = 1;
            foreach (var pb in batch) { BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(o), (ushort)pb.Length); pb.CopyTo(a, o + 2); o += 2 + pb.Length; }
            var r = await Wo137Async(3, a, ct);
            if (r is not { Ok: true } v || v.Payload.Length < 1) return null;
            var b = v.Payload;
            int n = b[0];
            if (b.Length < 1 + n * 7 || n != batch.Count) return null;
            for (int k = 0; k < n; k++)
            {
                int q = 1 + k * 7;
                all.Add(new QuestStateRead(b[q] != 0, b[q + 1] == 0xFF ? -1 : b[q + 1], b[q + 2] != 0, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(q + 3))));
            }
        }
        return all;
    }

    /// <summary>
    /// WO-147 op 8: up to 10 States' values WITH their value type's name (the type a correction's port is
    /// derived from: "Set" + the name of the host's value in that type). Null = no answer; fewer entries
    /// than asked = the reply ran out of room (the rest unread).
    /// </summary>
    public async Task<List<(bool Found, bool Ok, int Val, string Type)>?> Wo137ReadStateTypesAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        if (paths.Count is < 1 or > 10) return null;
        var pbs = paths.Select(p => Encoding.ASCII.GetBytes(p)).ToList();
        if (pbs.Any(p => p.Length is 0 or > 400) || pbs.Sum(p => 2 + p.Length) + 2 > 1000) return null;
        var a = new byte[1 + pbs.Sum(x => 2 + x.Length)];
        a[0] = (byte)pbs.Count;
        int o = 1;
        foreach (var pb in pbs) { BinaryPrimitives.WriteUInt16LittleEndian(a.AsSpan(o), (ushort)pb.Length); pb.CopyTo(a, o + 2); o += 2 + pb.Length; }
        var r = await Wo137Async(8, a, ct);
        if (r is not { Ok: true } v || v.Payload.Length < 1) return null;
        var b = v.Payload;
        int n = b[0], q = 1;
        var list = new List<(bool, bool, int, string)>(n);
        for (int k = 0; k < n; k++)
        {
            if (q + 8 > b.Length) break;
            bool found = b[q] != 0, ok = b[q + 2] != 0;
            int val = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(q + 3));
            int tl = b[q + 7];
            if (q + 8 + tl > b.Length) break;
            list.Add((found, ok, val, Encoding.ASCII.GetString(b, q + 8, tl)));
            q += 8 + tl;
        }
        return list;
    }

    public readonly record struct QuestObjectiveRead(string Name, int Type, int Log, int Order, long Last);

    /// <summary>op 4: a quest's objectives through the exported C_Quest / C_Objective getters (null = no answer).</summary>
    public async Task<(bool Found, int Level, List<QuestObjectiveRead> Objectives)?> Wo137ReadQuestAsync(string path, CancellationToken ct = default)
    {
        var pb = Encoding.ASCII.GetBytes(path);
        if (pb.Length == 0 || pb.Length > 400) return null;
        var a = new byte[2 + pb.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(a, (ushort)pb.Length);
        pb.CopyTo(a, 2);
        var r = await Wo137Async(4, a, ct);
        if (r is not { Ok: true } v || v.Payload.Length < 6) return null;
        var b = v.Payload;
        bool found = b[0] != 0;
        int level = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(1));
        int count = b[5], o = 6;
        var objs = new List<QuestObjectiveRead>(count);
        for (int k = 0; k < count && o < b.Length; k++)
        {
            int nl = b[o++];
            if (o + nl + 12 > b.Length) break;
            string name = Encoding.ASCII.GetString(b, o, nl); o += nl;
            int type = b[o++], log = b[o++];
            int order = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o)); o += 2;
            long last = BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(o)); o += 8;
            objs.Add(new QuestObjectiveRead(name, type, log, order, last));
        }
        return (found, level, objs);
    }

    /// <summary>op 5: the native side's counters.</summary>
    public async Task<string?> Wo137StatusAsync(CancellationToken ct = default)
    {
        var r = await Wo137Async(5, [], ct);
        return r is { Ok: true } v ? Encoding.ASCII.GetString(v.Payload) : null;
    }

    /// <summary>
    /// Guid.ToByteArray already produces the little-endian field order Windows
    /// uses, which is exactly how the game holds a CryGUID in memory and how the
    /// SoulsByGuid key is laid out. So no reordering here — the wire, the game
    /// and System.Guid all agree, and the only place a conversion is needed is
    /// when a human reads the text form.
    /// </summary>
    private static void WriteSoulGuid(Guid soul, Span<byte> dest) =>
        soul.TryWriteBytes(dest);

    /// <summary>Route frames: replies to the waiting command, hits to the callback.</summary>
    private async Task ReadLoopAsync()
    {
        try
        {
            while (_pipe?.IsConnected == true)
            {
                var (type, body) = await ReadFrameAsync(CancellationToken.None);
                // WO-118: replies are logged by their callers; 0x81/0x86/0x89
                // arrive at frame-feed and heartbeat rates and would flood.
                if (type is not (Result or LocalStateReply or NpcStatusReply or BodyStateReply or LocalAction or PvpHitOut or NpcCombatOut or Wo132Reply or Wo131Reply or Wo137Reply or QuestChangeOut))
                    Console.WriteLine($"[combat] pipe frame 0x{type:X2} ({body.Length} bytes)");
                if (type == LocalHit && body.Length >= 24)
                {
                    var   soul    = new Guid(body.AsSpan(0, 16));
                    float stamina = BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(16));
                    float health  = BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(20));
                    // WO-86: trailing died byte; absent from a pre-WO-86 DLL.
                    bool  died    = body.Length >= 25 && body[24] != 0;
                    bool  byPlayer = body.Length >= 26 && body[25] != 0;   // WO-121
                    if (died) Console.WriteLine($"[npcdeath] DLL reports a FATAL local hit on {soul} (hp -{health:F1})");
                    if (OnLocalHit is { } handler)
                    {
                        try { await handler(soul, stamina, health, died, byPlayer); }
                        catch (Exception ex) { Console.WriteLine($"[combat] local hit not sent: {ex.Message}"); }
                    }
                }
                else if (type == LocalDowned && body.Length >= 2)
                {
                    // WO-113: unsolicited, like LocalHit -- never a reply.
                    if (OnLocalDowned is { } h)
                    {
                        try { await h(body[0] != 0, body[1]); }
                        catch (Exception ex) { Console.WriteLine($"[respawn] downed not handled: {ex.Message}"); }
                    }
                }
                else if (type == LocalRespawned && body.Length >= 13)
                {
                    if (OnLocalRespawned is { } h)
                    {
                        try
                        {
                            await h(BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(0)),
                                    BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(4)),
                                    BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(8)), body[12]);
                        }
                        catch (Exception ex) { Console.WriteLine($"[respawn] respawned not handled: {ex.Message}"); }
                    }
                }
                else if (type == LocalGrave && body.Length >= 21)
                {
                    if (OnLocalGrave is { } h)
                    {
                        try
                        {
                            await h(body[0] != 0, BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan(1)),
                                    BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(9)),
                                    BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(13)),
                                    BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(17)));
                        }
                        catch (Exception ex) { Console.WriteLine($"[grave] local grave not handled: {ex.Message}"); }
                    }
                }
                else if (type == LocalAction && LocalActionFrame.TryParse(body, out var la))
                {
                    // WO-121: unsolicited, never a reply.
                    if (OnLocalAction is { } h)
                    {
                        try { await h(la); }
                        catch (Exception ex) { Console.WriteLine($"[wo121] local action not sent: {ex.Message}"); }
                    }
                }
                else if (type == PvpHitOut && body.Length == 14)
                {
                    if (OnPvpHit is { } h)
                    {
                        try
                        {
                            await h(BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0)),
                                    BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(4)),
                                    BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(8)), body[12], body[13]);
                        }
                        catch (Exception ex) { Console.WriteLine($"[wo121] pvp hit not sent: {ex.Message}"); }
                    }
                }
                else if (type == NpcAvatarHit && body.Length >= 18 && body.Length == 18 + body[17])
                {
                    // WO-132: never awaited on this reader (the WO-131 deadlock trap:
                    // a handler that makes a pipe request would wait on this loop).
                    if (OnNpcAvatarHit is { } h)
                    {
                        uint v = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0));
                        float st = BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(4)), hp = BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(8));
                        uint at = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12));
                        byte fl = body[16];
                        string an = Encoding.ASCII.GetString(body, 18, body[17]);
                        _ = Task.Run(async () => { try { await h(v, st, hp, at, fl, an); } catch (Exception ex) { Console.WriteLine($"[wo132] npc avatar hit not handled: {ex.Message}"); } });
                    }
                }
                else if (type == NpcCombatOut && NpcCombatState.TryParse(body, out var ncs))
                {
                    if (OnNpcCombat is { } h)
                        _ = Task.Run(async () => { try { await h(ncs); } catch (Exception ex) { Console.WriteLine($"[wo132] npc combat not handled: {ex.Message}"); } });
                }
                else if (type == QuestChangeOut)
                {
                    // WO-137: in order -- the handler only enqueues (its work runs on the agent's own loop).
                    if (QuestChange.TryParse(body, out var qc))
                    {
                        try { OnQuestChange?.Invoke(qc); }
                        catch (Exception ex) { Console.WriteLine($"[wo137] quest change not queued: {ex.Message}"); }
                    }
                    else Console.WriteLine($"[wo137] malformed quest change frame ({body.Length} bytes)");
                }
                else if (type == NpcStreamOut)
                {
                    if (Wo138Codec.TryParseStream(body, out var rows)) { try { OnNpcStream?.Invoke(rows); } catch (Exception ex) { Console.WriteLine($"[wo138] rows not queued: {ex.Message}"); } }
                    else Console.WriteLine($"[wo138] malformed NpcStream frame ({body.Length} bytes)");
                }
                else if (type == WorldOut)
                {
                    if (Wo138Codec.TryParseWorld(body, out var ws)) { try { OnWorld?.Invoke(ws); } catch { } }
                }
                else if (type == ActivityOut)
                {
                    // WO-141: [count]{[kind][nameLen][name][activity:30]}
                    if (Wo141Codec.TryParseFrame(body, out var rows)) { try { OnActivityFrame?.Invoke(rows); } catch (Exception ex) { Console.WriteLine($"[wo141] activity rows not handled: {ex.Message}"); } }
                    else Console.WriteLine($"[wo141] malformed Activity frame ({body.Length} bytes)");
                }
                else if (type == ExtraOut)
                {
                    // WO-143: [kind][...]
                    if (Wo143DllFrame.TryParse(body, out var xf)) { try { OnExtraFrame?.Invoke(xf); } catch (Exception ex) { Console.WriteLine($"[wo143] extra frame not handled: {ex.Message}"); } }
                    else Console.WriteLine($"[wo143] malformed extra frame ({body.Length} bytes)");
                }
                else if (type == SleepOut)
                {
                    // WO-140: [kind=1 Held][id] | [kind=2 State][edge][id][hours:4f][state]
                    if (Wo140Frame.TryParse(body, out var sf)) { try { OnSleepFrame?.Invoke(sf); } catch (Exception ex) { Console.WriteLine($"[wo140] sleep frame not handled: {ex.Message}"); } }
                    else Console.WriteLine($"[wo140] malformed Sleep frame ({body.Length} bytes)");
                }
                else if (type == CrimeOut)
                {
                    // WO-139: [kind=1][level][prev][x:4f][y:4f][z:4f]
                    if (body.Length == 15 && body[0] == 1)
                    {
                        float tx = BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(3)), ty = BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(7)),
                              tz = BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(11));
                        try { OnTrespass?.Invoke(body[1], body[2], tx, ty, tz); } catch (Exception ex) { Console.WriteLine($"[wo139] trespass edge not handled: {ex.Message}"); }
                    }
                    else Console.WriteLine($"[wo139] malformed Crime frame ({body.Length} bytes)");
                }
                else if (type == DiscardedHit && body.Length == 12)
                {
                    if (OnDiscardedHit is { } h)
                    {
                        uint at = BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0));
                        float st = BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(4)), hp = BinaryPrimitives.ReadSingleLittleEndian(body.AsSpan(8));
                        _ = Task.Run(async () => { try { await h(at, st, hp); } catch { } });
                    }
                }
                else if (type == NpcDropped && body.Length >= 2 && body.Length == 2 + body[1])
                {
                    // WO-118: unsolicited, never a reply.
                    if (OnNpcDropped is { } h)
                    {
                        try { await h(body[0], System.Text.Encoding.UTF8.GetString(body, 2, body[1])); }
                        catch (Exception ex) { Console.WriteLine($"[npcwrite] drop not handled: {ex.Message}"); }
                    }
                }
                else if (type == NpcTraceDone && body.Length >= 5 && body.Length == 5 + body[4])
                {
                    if (OnNpcTraceDone is { } h)
                    {
                        try { await h(BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(0)), System.Text.Encoding.UTF8.GetString(body, 5, body[4])); }
                        catch (Exception ex) { Console.WriteLine($"[npctrace] trace-done not handled: {ex.Message}"); }
                    }
                }
                else if (!_replies.Writer.TryWrite((type, body)))
                {
                    // DropOldest means TryWrite only fails on a completed
                    // writer, which happens on Drop(). Say so rather than
                    // losing the frame silently.
                    Console.WriteLine($"[combat] reply 0x{type:X2} arrived after the channel closed");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Normal on shutdown or if the game exits.
        }
        catch (Exception ex)
        {
            // Anything else would otherwise become an unobserved task exception:
            // the reader stops and the agent goes quiet with no output at all,
            // which is indistinguishable from the DLL never writing.
            Console.WriteLine($"[combat] reader stopped: {ex.GetType().Name}: {ex.Message}");
        }
        Console.WriteLine("[combat] pipe reader exited");
    }

    private async Task<bool> SendAsync(byte type, byte[] payload, CancellationToken ct)
        => (await SendForResultAsync(type, payload, ct)).Ok;

    /// <summary>
    /// One request/reply exchange. Returns whether the DLL applied it and, when
    /// the DLL is new enough to send one, its specific reason code (WO-100
    /// Phase 4 item 3). The reason is <see cref="PipeReason.Unknown"/> from a
    /// pre-WO-100 DLL whose Result frame stops at two bytes, and
    /// <see cref="PipeReason.NoAnswer"/> when the deadline expired -- which is
    /// NOT a refusal and is reported as its own thing.
    /// </summary>
    private async Task<PipeResult> SendForResultAsync(byte type, byte[] payload, CancellationToken ct)
    {
        var (body, fail) = await SendAndAwaitAsync(type, payload, Result, ct);
        if (body is null) return PipeResult.Fail(fail);
        var reason = body.Length >= 3 ? (PipeReason)body[2] : PipeReason.Unknown;
        return new PipeResult(body[0] == 1, reason);
    }

    /// <summary>
    /// WO-100.5 Phase 2: read one actor's live Mannequin body state (entityId 0
    /// = the local player). Returns null on any refusal -- a gate said no, the
    /// pipe is down, or the DLL predates this command and never answers. The
    /// caller counts those; nothing here logs per call, because this runs at
    /// the position stream's cadence.
    /// </summary>
    public async Task<LocalBodyState?> ReadBodyStateAsync(uint entityId, CancellationToken ct = default)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, entityId);
        var (body, _) = await SendAndAwaitAsync(ReadBodyState, payload, BodyStateReply, ct);
        BodyStateReads++;
        // [ok:1][seq:1][pace:1][dir:1][stance:1][animSpeedCenti:2 LE][unknownTags:1]
        //   + WO-100.5 Phase 3: [haveCombat:1][inputClass:1][zone:1][atkType:1][prepared:1]
        if (body is null || body.Length < 8 || body[0] != 1) { BodyStateRefused++; return null; }
        BodyStateUnknownTags += body[7];
        var b = new BodyState(
            (BodyPace)body[2], (BodyDir)body[3], (BodyStance)body[4],
            BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(5)));

        // Length-checked, not assumed: a DLL that predates Phase 3 answers with
        // the 8-byte form and this degrades to "no combat state", which is
        // exactly right rather than a fabricated -1 triple.
        if (body.Length < 13) return new LocalBodyState(b, false, -1, -1, -1, false);
        return new LocalBodyState(b, body[8] == 1,
            (sbyte)body[9], (sbyte)body[10], (sbyte)body[11], body[12] != 0);
    }

    /// <summary>WO-100.5: how many body-state reads were attempted.</summary>
    public long BodyStateReads { get; private set; }

    /// <summary>WO-102 Phase 1: 0x0A reads issued.</summary>
    public long LocalStateReads { get; private set; }
    /// <summary>WO-102 Phase 1: 0x0A reads the DLL refused or never answered.</summary>
    public long LocalStateRefused { get; private set; }
    /// <summary>WO-102 Phase 1: refusals by reason code (index = <see cref="LocalStateRefuse"/>; 255 folds into slot 7).</summary>
    public long[] LocalStateRefuseByCode { get; } = new long[8];

    /// <summary>
    /// WO-102 Phase 1: one native read of the local player's position, yaw,
    /// riding state and body state, all from one frame (pipe 0x0A -> 0x86).
    /// Null on any refusal; the reason is counted, never guessed. A DLL that
    /// predates the command answers nothing and lands in "Unknown" after the
    /// reply deadline -- the caller gives up on the path after a run of those.
    /// </summary>
    public async Task<LocalState?> ReadLocalStateAsync(CancellationToken ct = default)
    {
        var payload = new byte[4];   // entityId 0 = the local player (reserved for a per-entity read)
        var (body, _) = await SendAndAwaitAsync(ReadLocalState, payload, LocalStateReply, ct);
        LocalStateReads++;
        if (body is null)
        {
            LocalStateRefused++; LocalStateRefuseByCode[7]++;
            return null;
        }
        if (!LocalStateCodec.TryParse(body, out var st, out var why))
        {
            LocalStateRefused++;
            int slot = (byte)why < 7 ? (byte)why : 7;
            LocalStateRefuseByCode[slot]++;
            return null;
        }
        BodyStateUnknownTags += LocalStateCodec.UnknownTags(body);
        return st;
    }
    /// <summary>WO-100.5: how many of those the DLL refused (a gate said no, or it is an older DLL).</summary>
    public long BodyStateRefused { get; private set; }
    /// <summary>WO-100.5: running total of tags the native decode could not place. Healthy value is 0.</summary>
    public long BodyStateUnknownTags { get; private set; }

    /// <summary>WO-102.5 Phase 2: 0x0B scans issued.</summary>
    public long NpcScanReads { get; private set; }
    /// <summary>WO-102.5 Phase 2: 0x0B scans the DLL refused or never answered.</summary>
    public long NpcScanRefused { get; private set; }
    /// <summary>WO-102.5 Phase 2: refusals by reason code (index = <see cref="NpcScanRefuse"/>; 255 folds into slot 7).</summary>
    public long[] NpcScanRefuseByCode { get; } = new long[8];

    /// <summary>
    /// WO-102.5 Phase 2: one batched native NPC scan (pipe 0x0B -> 0x87).
    /// <paramref name="anchors"/> is 1..8 world positions (self + peer
    /// ghosts); an entity is returned if it is within <paramref name="radius"/>
    /// of ANY anchor. Null on any refusal, the reason counted, never guessed
    /// -- same discipline as <see cref="ReadLocalStateAsync"/>.
    /// </summary>
    public async Task<NpcScanResult?> ScanNpcsAsync(
        IReadOnlyList<(float X, float Y, float Z)> anchors, float radius, CancellationToken ct = default)
    {
        if (anchors.Count is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(anchors));
        var payload = new byte[1 + 4 + anchors.Count * 12];
        payload[0] = (byte)anchors.Count;
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(1), radius);
        int o = 5;
        foreach (var a in anchors)
        {
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(o), a.X); o += 4;
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(o), a.Y); o += 4;
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(o), a.Z); o += 4;
        }
        var (body, _) = await SendAndAwaitAsync(ScanNpcs, payload, NpcScanReply, ct);
        NpcScanReads++;
        if (body is null)
        {
            NpcScanRefused++; NpcScanRefuseByCode[7]++;
            return null;
        }
        if (!NpcScanCodec.TryParse(body, out var res, out var why))
        {
            NpcScanRefused++;
            int slot = (byte)why < 7 ? (byte)why : 7;
            NpcScanRefuseByCode[slot]++;
            return null;
        }
        return res;
    }

    /// <summary>
    /// WO-127: one leash sample (pipe 0x1F -> 0x8F), every page of it. The
    /// first request takes the sample on the game's main thread; later pages
    /// are served from the DLL's copy. Null when the DLL refused or never
    /// answered (a DLL older than 0.29.9 answers "unknown command").
    /// </summary>
    public async Task<(LeashPage Head, List<LeashEntry> All)?> LeashSampleAsync(
        IReadOnlyList<(float X, float Y, float Z)> anchors, float radius, CancellationToken ct = default)
    {
        var all = new List<LeashEntry>();
        LeashPage? head = null;
        ushort offset = 0;
        for (int guard = 0; guard < 64; guard++)
        {
            var (body, _) = await SendAndAwaitAsync(LeashSample, LeashCodec.BuildRequest(anchors, radius, offset), LeashReply, ct);
            if (body is null || !LeashCodec.TryParse(body, out var page) || page is null || !page.Ok) return null;
            head ??= page;
            all.AddRange(page.Entries);
            if (page.Entries.Count == 0 || all.Count >= page.Total) break;
            offset = (ushort)all.Count;
        }
        return head is null ? null : (head, all);
    }

    /// <summary>
    /// The shared send-and-wait core. Sequence matching is identical for every
    /// reply kind because the DLL puts seq at body[1] in all of them (WO-100
    /// S3.1's fix, which this generalises rather than duplicates).
    /// </summary>
    private async Task<(byte[]? Body, PipeReason Fail)> SendAndAwaitAsync(
        byte type, byte[] payload, byte wantType, CancellationToken ct)
    {
        if (!await EnsureConnectedAsync(ct)) return (null, PipeReason.NotConnected);

        await _gate.WaitAsync(ct);
        try
        {
            // Drop anything left over from an earlier command that timed out.
            // Without this the first read below returns that command's answer
            // as if it were ours -- the defect described at _replies.
            while (_replies.Reader.TryRead(out var leftover))
            {
                StaleRepliesDropped++;
                Console.WriteLine($"[combat] dropped a leftover reply 0x{leftover.Type:X2} " +
                                  $"before sending 0x{type:X2} (total {StaleRepliesDropped})");
            }

            await WriteFrameAsync(type, payload, ct);

            // Bounded wait with an explicit give-up, and the waited time is
            // reported on expiry (WO-100 Phase 4 item 2).
            var deadline = DateTime.UtcNow + ReplyDeadline;
            var started  = DateTime.UtcNow;
            while (true)
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero)
                {
                    TimedOut++;
                    // WO-110 R12 (docs/WO-109-audit.md R12): the DLL numbers
                    // every frame it READS, so the reply to this timed-out
                    // command -- if it ever comes -- carries exactly the seq we
                    // were waiting for. Left as is, the next command's wait
                    // would take that late reply as its own answer. Advancing
                    // past it makes the late reply read as "older" above and be
                    // dropped, which is what it is.
                    if (_expectedSeq is byte w) _expectedSeq = (byte)(w + 1);
                    Console.WriteLine($"[combat] no answer to 0x{type:X2} after " +
                                      $"{(DateTime.UtcNow - started).TotalMilliseconds:F0} ms " +
                                      $"(deadline {ReplyDeadline.TotalMilliseconds:F0} ms, timeouts {TimedOut}; seq advanced past the missing reply)");
                    return (null, PipeReason.NoAnswer);
                }

                using var slice = CancellationTokenSource.CreateLinkedTokenSource(ct);
                slice.CancelAfter(left);
                (byte Type, byte[] Body) reply;
                try { reply = await _replies.Reader.ReadAsync(slice.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { continue; }

                // WO-118: a DLL older than this command answers 0x81 with
                // reason UnknownCommand (WO-110 R12) instead of the typed reply.
                // Waiting out the whole deadline for a frame that never comes
                // held the gate for 5 s -- per heartbeat, for the 0x14 status.
                if (reply.Type == Result && wantType != Result && reply.Body.Length >= 3
                    && reply.Body[2] == (byte)PipeReason.UnknownCommand
                    && (_expectedSeq is not byte ws || reply.Body[1] == ws))
                {
                    _expectedSeq = (byte)(reply.Body[1] + 1);
                    return (null, PipeReason.UnknownCommand);
                }
                if (reply.Type != wantType || reply.Body.Length < 2)
                {
                    StaleRepliesDropped++;   // wrong frame kind, or truncated: not ours
                    continue;
                }

                byte seq = reply.Body[1];
                if (_expectedSeq is byte want && seq != want)
                {
                    // Older than what we are waiting for? Then it belongs to a
                    // command that already gave up. Anything else means we
                    // missed a reply, so resync on it rather than hanging.
                    bool older = (byte)(want - seq) is > 0 and < 128;
                    if (older)
                    {
                        StaleRepliesDropped++;
                        Console.WriteLine($"[combat] dropped stale reply seq={seq} (expected {want}, " +
                                          $"total {StaleRepliesDropped})");
                        continue;
                    }
                    Console.WriteLine($"[combat] reply seq={seq} is ahead of the expected {want} -- resyncing");
                }
                _expectedSeq = (byte)(seq + 1);
                return (reply.Body, PipeReason.Ok);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Drop();
            return (null, PipeReason.NotConnected);
        }
        finally { _gate.Release(); }
    }

    private static readonly TimeSpan ReplyDeadline = TimeSpan.FromSeconds(5);

    private async Task WriteFrameAsync(byte type, byte[] payload, CancellationToken ct)
    {
        var frame = new byte[3 + payload.Length];
        frame[0] = type;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), (ushort)payload.Length);
        payload.CopyTo(frame.AsSpan(3));
        // WO-144 5: the reader can drop the pipe while a command waits at the gate; a null pipe
        // here was the field's NullReferenceException in every tick after the game died
        var pipe = _pipe ?? throw new IOException("pipe closed");
        await pipe.WriteAsync(frame, ct);
        await pipe.FlushAsync(ct);
    }

    private async Task<(byte Type, byte[] Body)> ReadFrameAsync(CancellationToken ct)
    {
        var head = new byte[3];
        await ReadExactAsync(head, ct);
        int len = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(1));
        var body = new byte[len];
        if (len > 0) await ReadExactAsync(body, ct);
        return (head[0], body);
    }

    private async Task ReadExactAsync(byte[] buffer, CancellationToken ct)
    {
        int got = 0;
        while (got < buffer.Length)
        {
            var pipe = _pipe ?? throw new IOException("pipe closed");   // WO-144 5
            int n = await pipe.ReadAsync(buffer.AsMemory(got), ct);
            if (n <= 0) throw new IOException("pipe closed");
            got += n;
        }
    }

    private void Drop()
    {
        bool had = _pipe is not null;   // WO-144 5: one line per lost connection, not one per caller
        _pipe?.Dispose();
        _pipe = null;
        // A reconnect gets a fresh channel and no sequence expectation: the
        // DLL's counter keeps running across connections, so carrying the old
        // expectation over would reject the first real reply.
        _replies.Writer.TryComplete();
        _replies = NewReplyChannel();
        _expectedSeq = null;
        if (had) Console.WriteLine("[combat] lost the connection to KCDMP.dll");
    }

    public ValueTask DisposeAsync()
    {
        _pipe?.Dispose();
        _pipe = null;
        _replies.Writer.TryComplete();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}


/// <summary>
/// WO-121: the DLL's 0x96 frame -- an action the local engine committed.
/// <c>[kind:1][phase:1][inputClass:1][zone(table id):1][attackType:1][flags:1][rowGuid:16][eid:4][nameLen:1][name]</c>.
/// eid 0 = the local player; otherwise an NPC (by its authored entity name).
/// </summary>
public readonly record struct LocalActionFrame(byte Kind, byte Phase, sbyte InputClass, sbyte ZoneTableId, sbyte AttackType,
                                               byte Flags, Guid Row, uint Eid, string Name)
{
    public static bool TryParse(ReadOnlySpan<byte> b, out LocalActionFrame f)
    {
        f = default;
        if (b.Length < 27) return false;
        int n = b[26];
        if (b.Length != 27 + n || n > 63) return false;
        f = new LocalActionFrame(b[0], b[1], unchecked((sbyte)b[2]), unchecked((sbyte)b[3]), unchecked((sbyte)b[4]), b[5],
                                 new Guid(b.Slice(6, 16)), BinaryPrimitives.ReadUInt32LittleEndian(b[22..]),
                                 n == 0 ? "" : System.Text.Encoding.UTF8.GetString(b.Slice(27, n)));
        return true;
    }
}
