// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

public class Wo224Tests
{
    [Fact]
    public void TheSwitchesAndTheirBits()
    {
        Assert.Equal(["w224_clinch", "w224_chains", "w224_death", "w224_clinch_log"], W224Switch.NativeNames);
        Assert.Equal(0xFu, W224Switch.NativeBits(_ => true));
        Assert.Equal(0xEu, W224Switch.NativeBits(n => n != "w224_clinch"));
        Assert.Equal(0u, W224Switch.NativeBits(_ => false));
    }

    [Fact]
    public void TheTwinTurnsEverythingOffButTheInstrument()
    {
        Assert.True(W224Switch.TwinOff("all", "w224_clinch"));
        Assert.True(W224Switch.TwinOff("all", "w224_chains"));
        Assert.True(W224Switch.TwinOff("all", "w224_death"));
        Assert.False(W224Switch.TwinOff("all", "w224_clinch_log"));
        Assert.True(W224Switch.TwinOff("w224_chains, w224_clinch", "w224_clinch"));
        Assert.False(W224Switch.TwinOff("w224_chains", "w224_clinch"));
        Assert.False(W224Switch.TwinOff(null, "w224_clinch"));
        Assert.False(W224Switch.TwinOff("  ", "w224_clinch"));
    }

    /// <summary>What was wrong since WO-202: a clinch event that names its body does not survive its own wire form.</summary>
    [Fact]
    public void TheOldClinchEventIsRefusedByItsOwnParser()
    {
        var enter = Wo197Rules.ClinchEvent(7, Wo197Rules.ClinchEnter, Guid.Empty, Guid.Empty, "tkop_vrahBandit_5");
        Assert.False(RowEvent.TryFromBytes(enter.ToBytes(), out _));   // the '|' before the name: not a RowEvent name
        var move = Wo197Rules.ClinchEvent(7, (byte)(Wo197Rules.ClinchMove | Wo197Rules.ClinchRows | Wo197Rules.ClinchMaster), Guid.NewGuid(), Guid.NewGuid(), "tkop_vrahBandit_5");
        Assert.Throws<ArgumentOutOfRangeException>(() => move.ToBytes());   // 32 digits, a bar and the name: past the name's 42 bytes
    }

    [Fact]
    public void TheClinchEventSurvivesTheWire()
    {
        Guid m = Guid.NewGuid(), s = Guid.NewGuid();
        foreach (string name in new[] { "tkop_vrahBandit_5", "a", "tvez_man_18", "a_long_entity_name_of_the_games_own_that_never_fits_42_bytes" })
        {
            var enter = Wo197Rules.ClinchEvent(7, Wo197Rules.ClinchEnter, Guid.Empty, Guid.Empty, name, hashed: true);
            Assert.True(RowEvent.TryFromBytes(enter.ToBytes(), out var eb));
            Assert.Equal(Guid.Empty, Wo197Rules.ClinchSlaveRow(eb));
            Assert.Equal(Wo224Rules.NameHash(name), Wo224Rules.ClinchOpponentHash(eb.Name));
            var move = Wo197Rules.ClinchEvent(9, (byte)(Wo197Rules.ClinchMove | Wo197Rules.ClinchRows | Wo197Rules.ClinchMaster), m, s, name, hashed: true);
            Assert.True(move.Name.Length <= RowEvent.MaxNameLen);
            Assert.True(RowEvent.TryFromBytes(move.ToBytes(), out var mb));
            Assert.Equal(m, mb.Row);
            Assert.Equal(s, Wo197Rules.ClinchSlaveRow(mb));
            Assert.Equal(Wo224Rules.NameHash(name), Wo224Rules.ClinchOpponentHash(mb.Name));
            Assert.Equal((byte)(Wo197Rules.ClinchMove | Wo197Rules.ClinchRows | Wo197Rules.ClinchMaster), mb.Flags);
        }
        // no body named: the bare forms of WO-197
        var bare = Wo197Rules.ClinchEvent(7, Wo197Rules.ClinchLeave, Guid.Empty, Guid.Empty, null, hashed: true);
        Assert.Equal("", bare.Name);
        Assert.Equal(0u, Wo224Rules.ClinchOpponentHash(bare.Name));
        var rows = Wo197Rules.ClinchEvent(7, Wo197Rules.ClinchMove, m, s, null, hashed: true);
        Assert.Equal(s.ToString("N"), rows.Name);
        Assert.Equal(0u, Wo224Rules.ClinchOpponentHash(rows.Name));
        Assert.Equal(s, Wo197Rules.ClinchSlaveRow(rows));
        // a name the old rule refused is not hashed either
        Assert.Equal("", Wo197Rules.ClinchEvent(7, Wo197Rules.ClinchEnter, Guid.Empty, Guid.Empty, "bad name\"", hashed: true).Name);
    }

    [Fact]
    public void TheSlaveRowIsReadFromEveryForm()
    {
        Guid s = Guid.NewGuid();
        Assert.Equal(s, Wo224Rules.ClinchSlaveRow(s.ToString("N")));
        Assert.Equal(s, Wo224Rules.ClinchSlaveRow(s.ToString("N") + "|tvez_man_18"));
        Assert.Equal(s, Wo224Rules.ClinchSlaveRow(s.ToString("N") + "_0a1b2c3d"));
        Assert.Equal(Guid.Empty, Wo224Rules.ClinchSlaveRow("_0a1b2c3d"));
        Assert.Equal(Guid.Empty, Wo224Rules.ClinchSlaveRow(""));
        Assert.Equal(Guid.Empty, Wo224Rules.ClinchSlaveRow("zz"));
    }

    [Fact]
    public void TheHashNamesOneBodyOrNone()
    {
        Assert.Equal(0u, Wo224Rules.NameHash(null));
        Assert.Equal(0u, Wo224Rules.NameHash(""));
        // the DLL's wo224_rules.h name_hash, value for value (the host's DLL hashes the body its engine clinched a figure with)
        Assert.Equal(0xE40C292Cu, Wo224Rules.NameHash("a"));
        Assert.Equal(0x88988F7Fu, Wo224Rules.NameHash("tkop_vrahBandit_5"));
        Assert.Equal(0x8FE98974u, Wo224Rules.NameHash("kcd2mp_1"));
        // the host's word for a joiner's figure: "<ghost>_<hash>" reads as that hash
        Assert.Equal(0x88988F7Fu, Wo224Rules.ClinchOpponentHash("1_88988f7f"));
        Assert.NotEqual(0u, Wo224Rules.NameHash("a"));
        Assert.Equal(Wo224Rules.NameHash("tkop_vrahBandit_5"), Wo224Rules.NameHash("tkop_vrahBandit_5"));
        Assert.NotEqual(Wo224Rules.NameHash("tkop_vrahBandit_5"), Wo224Rules.NameHash("tkop_vrahBandit_4"));
        string[] known = ["tkop_vrahBandit_1", "tkop_vrahBandit_5", "ttac_blacksmith"];
        Assert.Equal("tkop_vrahBandit_5", Wo224Rules.NameOfHash(Wo224Rules.NameHash("tkop_vrahBandit_5"), known));
        Assert.Null(Wo224Rules.NameOfHash(Wo224Rules.NameHash("somebody_else"), known));
        Assert.Null(Wo224Rules.NameOfHash(0, known));
        Assert.Equal("a", Wo224Rules.NameOfHash(Wo224Rules.NameHash("a"), ["a", "a"]));   // the same name twice is one body
        // the hash field: exactly eight hex digits behind the bar
        Assert.Equal(0x0a1b2c3du, Wo224Rules.ClinchOpponentHash("_0a1b2c3d"));
        Assert.Equal(0u, Wo224Rules.ClinchOpponentHash("_0a1b2c3"));
        Assert.Equal(0u, Wo224Rules.ClinchOpponentHash("_0a1b2c3dz"));
        Assert.Equal(0u, Wo224Rules.ClinchOpponentHash("_zzzzzzzz"));
        Assert.Equal(0u, Wo224Rules.ClinchOpponentHash("0a1b2c3d"));
    }

    [Fact]
    public void TheEndsTimes()
    {
        Assert.Equal(5, Wo224Rules.WeaponHint(5, false));
        Assert.Equal(Wo224Rules.WeaponHintNothing, Wo224Rules.WeaponHint(5, true));
        Assert.Equal(Wo224Rules.WeaponHintNone, Wo224Rules.WeaponHint(null, false));
        Assert.Equal(Wo224Rules.WeaponHintNone, Wo224Rules.WeaponHint(0xFE, false));   // a class the byte cannot carry is no word
        Assert.InRange(Wo224Rules.ClinchEndGraceMs, 100, 1000);   // the bar: nothing stuck over 1 s after it ended on the other screen
        Assert.True(Wo224Rules.ClinchSilentMs > 2000);            // a clinch's own idle may last seconds with nothing to say
    }
}
