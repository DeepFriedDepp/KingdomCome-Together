// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Farkle;

/// <summary>
/// A die's identity, distinct from its face value. v1 has only
/// <see cref="Standard"/> -- badges, weighted dice, and the Devil's Head are
/// out of scope here, but scoring and the wire format both key off this
/// rather than assuming every die is plain, so they can be added later
/// without changing either.
/// </summary>
public enum DieKind : byte
{
    Standard = 0,
}

/// <summary>One die as it currently sits on the table: a face value 1-6 and a kind.</summary>
public readonly record struct Die(byte Face, DieKind Kind = DieKind.Standard);
