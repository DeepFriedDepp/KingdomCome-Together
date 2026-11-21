// KUBA 32b (Kuba's fork, 19.11: "on player 2's screen neither the weapons nor the clothes match -- and that changes the NPC's health,
// it may have no armour after the brain switch"): an NPC's copy wears what the host's NPC wears. The game dresses a spawn (SpawnedEnemy_*,
// a road event's bandits) from its preset with a random pick on each machine, and nothing synced it (the authors' WO-203 II.8 "worn
// sets" was planned on the side channel -- kind 5 -- and never written). Now:
//   * the joiner, every 2 s: the copies within 35 m of this player (and every copy in a local duel first) it has not asked for in 5 min
//     are asked for, up to 6 a message (W203 kind 12 "name,name,..." to the host);
//   * the host reads each NPC's own equipped set (the game's REST: EquippedArmorsByClassId + EquippedWeaponsByClassId, the read the
//     avatars' outfits use) and answers each with W203 kind 5 "name|class,class,..." (a failed read answers nothing: asked again later);
//   * the joiner dresses its copy in exactly that set the figures' way: the missing pieces on in one frame (the mod's own inventory equip,
//     under-layers first), then the pieces the host's NPC does not wear off; read back after 1.5 s, the rest once more. Its weapons are
//     the host NPC's too -- so a copy whose brain resumes (KUBA 30's duel) holds the host's weapon and keeps it (no draw of its own).
// Lights stay the torch sync's. Off: KCDMP_KUBA_NPC_WORN=off in the agent's environment. Log lines: KUBA-32b.
using System.Collections.Concurrent;
using System.Text;
using KcdMp.Wire;

namespace KcdMp.Client;

public partial class GameBridge
{
    // 0.65 merge: OFF by default -- the authors' WO-216 D (outfits) now dresses every copy as its host NPC (pushed by the host on each
    // change); two dressers on one copy would race. KCDMP_KUBA_NPC_WORN=on brings this one back.
    private static readonly bool KubaWornOn = string.Equals(Environment.GetEnvironmentVariable("KCDMP_KUBA_NPC_WORN"), "on", StringComparison.OrdinalIgnoreCase);
    private const double KubaWornRadiusM = 35.0;
    private const long KubaWornAgainMs = 300_000, KubaWornPendingMs = 20_000, KubaWornDuelAgainMs = 60_000;

    private readonly ConcurrentDictionary<string, long> _kubaWornAskedMs = new(StringComparer.Ordinal);   // joiner: asked at
    private readonly ConcurrentDictionary<string, long> _kubaWornDoneMs = new(StringComparer.Ordinal);    // joiner: dressed (or same) at
    private readonly SemaphoreSlim _kubaWornGate = new(1, 1);
    private long _kubaWornTickMs, _kubaWornAsks, _kubaWornAnswers, _kubaWornIn, _kubaWornChanged, _kubaWornSame, _kubaWornFailed, _kubaWornReadFails;
    private int _kubaWornLogged;

    /// <summary>The joiner, from the 30 Hz duel loop: every 2 s, the copies near this player the host is asked to name the clothes of.</summary>
    private async Task KubaWornTickAsync()
    {
        if (!KubaWornOn) return;
        long now = Environment.TickCount64;
        if (now - _kubaWornTickMs < 2000) return;
        _kubaWornTickMs = now;
        if (Interlocked.Exchange(ref _kubaWornLogged, 1) == 0)
            Console.WriteLine("KUBA-32b NPC worn sets: the copies near this player are dressed as the host's NPCs (KCDMP_KUBA_NPC_WORN=on turned it on)");
        var pick = new List<string>(6);
        // the duel copies first: their brain fights with what they hold
        foreach (var name in _kubaDuel.Keys)
        {
            if (pick.Count >= 6) break;
            if (_kubaWornAskedMs.TryGetValue(name, out long a) && now - a < KubaWornPendingMs) continue;
            if (_kubaWornDoneMs.TryGetValue(name, out long dn) && now - dn < KubaWornDuelAgainMs) continue;
            pick.Add(name);
        }
        var near = new List<(string Name, double D)>();
        foreach (var (name, p) in _w164NpcPos)
        {
            if (now - p.AtMs > 5000 || name.StartsWith("kcd2mp_", StringComparison.Ordinal) || pick.Contains(name)) continue;
            double d = Math.Sqrt((p.X - _lastX) * (p.X - _lastX) + (p.Y - _lastY) * (p.Y - _lastY));
            if (d > KubaWornRadiusM) continue;
            if (_kubaWornAskedMs.TryGetValue(name, out long a) && now - a < KubaWornPendingMs) continue;
            if (_kubaWornDoneMs.TryGetValue(name, out long dn) && now - dn < KubaWornAgainMs) continue;
            near.Add((name, d));
        }
        foreach (var (name, _) in near.OrderBy(t => t.D))
        {
            if (pick.Count >= 6) break;
            pick.Add(name);
        }
        if (pick.Count == 0) return;
        foreach (var n in pick) _kubaWornAskedMs[n] = now;
        Interlocked.Add(ref _kubaWornAsks, pick.Count);
        await Wo203SendSideAsync(Protocol.JoinTargetHost, KubaW203WornAsk, string.Join(",", pick));
    }

    /// <summary>The host: a partner asks for these NPCs' worn sets -- each read from this world and answered on its own (kind 5).</summary>
    private async Task KubaOnWornAskAsync(byte src, string text)
    {
        if (!W137Host) return;
        foreach (var raw in text.Split(',', StringSplitOptions.RemoveEmptyEntries).Take(6))
        {
            string name = raw.Trim();
            if (!NpcNamePattern.IsMatch(name) || name.Length > Protocol.MaxNpcNameLen) continue;
            Guid[]? set = null;
            for (int i = 0; i < 2 && set is null; i++)
            {
                try { set = await _transport.ReadGhostEquippedItemClassesAsync(name); } catch { set = null; }
                if (set is null) await Task.Delay(300);
            }
            if (set is null)
            {
                long f = Interlocked.Increment(ref _kubaWornReadFails);
                if (f <= 20 || f % 50 == 0) Console.WriteLine($"KUBA-32b worn set of {name} could not be read here -- the partner asks again later [n={f}]");
                continue;
            }
            var body = new StringBuilder(name).Append('|');
            int k = 0;
            foreach (var g in set.Distinct())
            {
                string s = g.ToString("D");
                if (body.Length + s.Length + 1 > Protocol.W203TextMax) break;
                if (k++ > 0) body.Append(',');
                body.Append(s);
            }
            Interlocked.Increment(ref _kubaWornAnswers);
            await Wo203SendSideAsync(src, Protocol.W203NpcWorn, body.ToString());
        }
    }

    /// <summary>W203 kinds 5 (an NPC's worn set, host to joiner) and 6 (not used by this fork).</summary>
    partial void Wo203AppearOnFrame(byte src, LootMsg m)
    {
        if (m.Kind != Protocol.W203NpcWorn || !W137Joiner) return;
        int bar = m.Text.IndexOf('|');
        if (bar <= 0) return;
        string name = m.Text[..bar];
        if (!NpcNamePattern.IsMatch(name)) return;
        var set = new List<Guid>();
        foreach (var s in m.Text[(bar + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (Guid.TryParse(s, out var g)) set.Add(g);
        Interlocked.Increment(ref _kubaWornIn);
        _ = Task.Run(() => KubaApplyWornAsync(name, set));
    }

    /// <summary>The joiner's side of the W203 kinds this fork adds (12 the host's, 13 the joiner's).</summary>
    private void KubaOnSideFrame(byte src, LootMsg m)
    {
        if (m.Kind == KubaW203WornAsk) _ = Task.Run(() => KubaOnWornAskAsync(src, m.Text));
        else if (m.Kind == KubaW203DuelRelease) _ = Task.Run(() => KubaOnDuelReleaseAsync(m.Text.Trim()));
        else if (m.Kind == KubaW203DuelOver && W137Host)   // KUBA 33: the partner's duel ended -- this NPC is its own now, not 1 s later
            _ = Task.Run(() => KubaHostEndAsync(m.Text.Trim(), src, "the partner's duel ended (his word)"));
    }

    /// <summary>The joiner: its copy <paramref name="name"/> dressed in exactly the host NPC's set (one at a time).</summary>
    private async Task KubaApplyWornAsync(string name, List<Guid> hostSet)
    {
        await _kubaWornGate.WaitAsync();
        try
        {
            var target = new List<Guid>(hostSet.Count);
            foreach (var g in hostSet) target.Add(ItemAliasToSource.TryGetValue(g, out var src) ? src : g);
            var want = Wo144WithoutLights(target.Distinct());
            if (want.Count == 0) { _kubaWornDoneMs[name] = Environment.TickCount64; return; }   // an animal, or nothing worn there
            var actual = await ReadGhostSetAsync(name, CancellationToken.None);
            if (actual is null) { Interlocked.Increment(ref _kubaWornFailed); Console.WriteLine($"KUBA-32b {name}: its own set could not be read here -- asked again later"); return; }
            actual = Wo144WithoutLights(actual);
            var add = await Wo144InLayerOrderAsync(Wo135Rules.AppearanceAdditions(actual, want));
            var remove = Wo135Rules.AppearanceRemovals(actual, want);
            if (add.Count == 0 && remove.Count == 0)
            {
                _kubaWornDoneMs[name] = Environment.TickCount64;
                long s = Interlocked.Increment(ref _kubaWornSame);
                if (s <= 20 || s % 100 == 0) Console.WriteLine($"KUBA-32b {name} already wears the host NPC's {want.Count} piece(s) [n={s}]");
                return;
            }
            for (int round = 0; round < 2; round++)
            {
                if (add.Count > 0)
                {
                    try { await Wo202EquipAllOnGhostAsync(name, add, CancellationToken.None); }
                    catch (Exception ex) { Console.WriteLine($"KUBA-32b {name}: equip of {add.Count} piece(s) failed: {ex.Message}"); }
                }
                foreach (var cls in remove)
                {
                    try { await _transport.UnequipItemOnGhostAsync(name, cls); }
                    catch (Exception ex) { Console.WriteLine($"KUBA-32b {name}: unequip {cls} failed: {ex.Message}"); }
                }
                await Task.Delay(1500);
                var back = await ReadGhostSetAsync(name, CancellationToken.None);
                if (back is null) break;
                back = Wo144WithoutLights(back);
                var addLeft = await Wo144InLayerOrderAsync(Wo135Rules.AppearanceAdditions(back, want));
                var remLeft = Wo135Rules.AppearanceRemovals(back, want);
                long c = round == 0 ? Interlocked.Increment(ref _kubaWornChanged) : Interlocked.Read(ref _kubaWornChanged);
                if (c <= 60 || c % 50 == 0 || round == 1)
                    Console.WriteLine($"KUBA-32b {name} dressed as the host's NPC (round {round + 1}): +{add.Count} -{remove.Count} -> " +
                                      $"{(addLeft.Count == 0 && remLeft.Count == 0 ? "matches" : $"{addLeft.Count} still missing, {remLeft.Count} still extra")} [n={c}]");
                if (addLeft.Count == 0 && remLeft.Count == 0) break;
                add = addLeft; remove = remLeft;
            }
            _kubaWornDoneMs[name] = Environment.TickCount64;
            RefreshNpcEquipped(name);   // its swing rows from what it wears now
        }
        catch (Exception ex) { Interlocked.Increment(ref _kubaWornFailed); Console.WriteLine($"KUBA-32b {name}: dressing failed: {ex.GetType().Name}: {ex.Message}"); }
        finally { _kubaWornGate.Release(); }
    }

    private string KubaWornStats() =>
        $"kuba32b_worn on={(KubaWornOn ? 1 : 0)} asked={Interlocked.Read(ref _kubaWornAsks)} answered={Interlocked.Read(ref _kubaWornAnswers)} in={Interlocked.Read(ref _kubaWornIn)}"
        + $" changed={Interlocked.Read(ref _kubaWornChanged)} same={Interlocked.Read(ref _kubaWornSame)} failed={Interlocked.Read(ref _kubaWornFailed)} read_fails={Interlocked.Read(ref _kubaWornReadFails)}";
}
