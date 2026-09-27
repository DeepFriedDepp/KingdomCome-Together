namespace KcdMp.Client;

/// <summary>
/// WO-133: the quest safety rules (docs/WO-133-findings.md). Pure, so every
/// gate is unit-tested; <see cref="GameBridge"/> feeds them its live flags.
/// </summary>
public static class Wo133Rules
{
    /// <summary>
    /// A shared-world session is running on this machine: a session is up (the
    /// relay's role has arrived on this connection) and the session's mode is a
    /// shared world -- the host's own <c>mp_shared_world</c> on the host, the
    /// host's announced mode (else the local toggle) on a joiner. Everything the
    /// old separate-worlds quest layer does is off while this holds.
    /// </summary>
    public static bool SharedWorldSession(bool roleKnown, bool isHost, bool localShared, bool joinerSharedEffective)
        => roleKnown && (isHost ? localShared : joinerSharedEffective);

    /// <summary>This machine is the host of a shared-world session: its clock is the world's clock.</summary>
    public static bool HostOfSharedWorld(bool roleKnown, bool isHost, bool localShared)
        => roleKnown && isHost && localShared;

    /// <summary>
    /// The host drops every time skip that did not come from itself: only the
    /// host's own clock moves the world (hazard H2). The relay may still route
    /// a joiner's skip (first come); it stops here. A source equal to our own
    /// ghost id (an echo) is never dropped by this rule.
    /// </summary>
    public static bool DropInboundTimeSkip(bool hostOfSharedWorld, byte sourceGhostId, byte myGhostId)
        => hostOfSharedWorld && sourceGhostId != myGhostId;

    /// <summary>
    /// The joiner in a shared world does not read or send save fingerprints and
    /// does not send its story marker: both come from its own solo world.
    /// </summary>
    public static bool JoinerQuietStory(bool sharedWorldSession, bool isHost) => sharedWorldSession && !isHost;

    /// <summary>The Lua gate call the agent pushes (on a change and as a heartbeat).</summary>
    public static string GateLua(bool on, string role, string why)
        => $"if KCD2MP_Wo133Gate then KCD2MP_Wo133Gate({(on ? "true" : "false")}, \"{role}\", \"{why}\") end";

    /// <summary>Why the gate reads as it does, for the log and the Lua line (fixed vocabulary, no user text).</summary>
    public static string Why(bool roleKnown, bool isHost, bool localShared, bool hostModeKnown, bool hostShared)
    {
        if (!roleKnown) return "no-session";
        if (isHost) return localShared ? "host-shared-world" : "host-separate-worlds";
        if (hostModeKnown) return hostShared ? "host-announced-shared-world" : "host-announced-separate-worlds";
        return localShared ? "local-shared-world" : "local-separate-worlds";
    }
}
