# Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
# GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
# content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
"""WO-206 Part C: the checkpoint digest's mix and bucket functions. The recorder in the agent (C#) must match exactly.

The algorithm, byte for byte:

  fnv1a64(s):  h = 0xcbf29ce484222325
               for each byte c of s encoded as UTF-8:  h = h XOR c;  h = (h * 0x100000001b3) mod 2**64
               (64-bit FNV-1a: XOR first, then multiply; C#: unchecked ulong arithmetic)
  mix(k, v)    = fnv1a64(k + "=" + v)                       (one "=" between key and value, no other separator)
  bucket(k)    = fnv1a64(k) mod 256                         (the KEY alone picks the bucket, never the value)
  bucket value = (sum of mix(k, v) over the entries in that bucket, wrapping mod 2**64) AND 0xFFFFFFFF
  digest b     = {str(bucket): format(value, "08x")} for each bucket whose value is non-zero
                 (keys "0".."255" as decimal strings, lower-case hex, exactly 8 digits; an empty map digests to {})

Because a bucket is a sum, entry order never matters; changing one entry's value changes only its own bucket (the
two values' mixes differ, so the bucket's sum differs, except for a 2**-32 coincidence in the low 32 bits).
Known answer: fnv1a64("a") == 0xaf63dc4c8601ec8c.
"""

FNV_OFFSET = 0xCBF29CE484222325
FNV_PRIME = 0x100000001B3
MASK64 = 0xFFFFFFFFFFFFFFFF


def fnv1a64(s):
    h = FNV_OFFSET
    for c in s.encode("utf-8"):
        h = ((h ^ c) * FNV_PRIME) & MASK64
    return h


def mix(key, value):
    return fnv1a64(key + "=" + value)


def bucket_of(key):
    return fnv1a64(key) % 256


def buckets(entries):
    """{key: value} (or an iterable of (key, value) pairs) -> the digest's "b" map, {str(bucket): "8 hex digits"}."""
    sums = {}
    for k, v in (entries.items() if hasattr(entries, "items") else entries):
        b = bucket_of(k)
        sums[b] = (sums.get(b, 0) + mix(k, v)) & MASK64
    return {str(b): format(s & 0xFFFFFFFF, "08x") for b, s in sorted(sums.items()) if s & 0xFFFFFFFF}
