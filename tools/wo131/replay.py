"""WO-131: a recorded host stream -> a synthpeer plan that replays it into a joiner game.

The recorder is avatarpeer --record F (tools/wo121/avatarpeer): one line per packet the
host sent, "<ms> <type hex> <payload hex>", for NpcStateDown (0x27), ActionDown (0x3C)
and NpcDamageDown (0x31). The replay turns each Down packet back into its Up form
(the leading source-ghost byte dropped) and writes synthpeer `raw` lines at the recorded
times; synthpeer re-stamps the sender clock on the way out (tools/wo118/synthpeer).

  python replay.py <recording> <out plan> [--from MS] [--to MS] [--npc name,name] [--start S]

  --from/--to  the recording's own ms window (default: everything)
  --npc        keep only these NPC names (states, rows and hits)
  --start      seconds of stream time before the first packet (default 3)

Only NpcAttack and (WO-132) NpcCombat actions are kept from 0x3C (the host's other
actions are about its own avatar). Nothing here ships.
"""
import sys


def arg(name, default=None):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv else default


def main():
    src, out = sys.argv[1], sys.argv[2]
    lo = int(arg('--from', '0')); hi = int(arg('--to', str(1 << 62)))
    keep = set(n for n in (arg('--npc', '') or '').split(',') if n)
    start = float(arg('--start', '3'))
    rows = []
    for line in open(src, encoding='utf-8'):
        parts = line.split()
        if len(parts) != 3:
            continue
        ms, typ, hexs = int(parts[0]), parts[1].upper(), parts[2]
        if ms < lo or ms > hi:
            continue
        b = bytes.fromhex(hexs)
        if typ == '27' and len(b) > 2:            # [src][nameLen][name][x y z rot hp:4f*5][flags][seq:2][senderMs:4]
            nl = b[1]; name = b[2:2 + nl].decode('utf-8', 'replace')
            if keep and name not in keep:
                continue
            rows.append((ms, '26', b[1:], nl + 24))
        elif typ == '3C' and len(b) > 10 and b[1] in (13, 14):   # NpcAttack / NpcCombat (WO-132): [src][kind][seq:2][phase][gen:4][len][payload]
            pl = b[10:10 + b[9]]
            if b[1] == 13:
                nl = pl[21]; name = pl[22:22 + nl].decode('utf-8', 'replace')
            else:   # NpcCombat: [senderMs:4][state:12][target][targetGhost][nameLen][name]
                nl = pl[18]; name = pl[19:19 + nl].decode('utf-8', 'replace')
            if keep and name not in keep:
                continue
            rows.append((ms, '3B', b[1:], 9))
        elif typ == '31' and len(b) > 2:          # [src][nameLen][name][st:4f][hp:4f][flags]
            nl = b[1]; name = b[2:2 + nl].decode('utf-8', 'replace')
            if keep and name not in keep:
                continue
            rows.append((ms, '30', b[1:], -1))
    if not rows:
        sys.exit('nothing in that window')
    t0 = rows[0][0]
    with open(out, 'w', encoding='utf-8') as f:
        f.write('# replayed host stream (tools/wo131/replay.py)\n')
        f.write('start %.1f\n' % start)
        for ms, typ, body, stamp in rows:
            f.write('raw %.3f %s %s %d\n' % ((ms - t0) / 1000.0, typ, body.hex().upper(), stamp))
    n27 = sum(1 for r in rows if r[1] == '26'); n3b = sum(1 for r in rows if r[1] == '3B')
    print('wrote %s: %d states, %d attack rows, %d hits, %.1f s' % (out, n27, n3b, len(rows) - n27 - n3b, (rows[-1][0] - t0) / 1000.0))


if __name__ == '__main__':
    main()
