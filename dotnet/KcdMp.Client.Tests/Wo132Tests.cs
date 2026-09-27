using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace KcdMp.Client.Tests;

/// <summary>WO-132: the save rebuild's block limits (docs/WO-132-findings.md s1).</summary>
public partial class Wo132Tests
{
    // ---------------------------------------------------------------- 1. block size

    /// <summary>The splicer before WO-132: every block deflated and written compressed, whatever its size.</summary>
    private static byte[] OldDeflate(byte[] desc, byte[] raw, byte[] tail)
    {
        using var ms = new MemoryStream();
        var head = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(head, 0xFFFFFFFFu);
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(4), desc.Length);
        ms.Write(head); ms.Write(desc);
        for (int i = 0; i < raw.Length; i += WhsSave.ChunkRaw)
        {
            int n = Math.Min(WhsSave.ChunkRaw, raw.Length - i);
            using var zms = new MemoryStream();
            using (var zs = new ZLibStream(zms, CompressionLevel.Optimal, leaveOpen: true)) zs.Write(raw, i, n);
            var z = zms.ToArray();
            BinaryPrimitives.WriteInt32LittleEndian(head, z.Length);
            BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(4), n);
            ms.Write(head); ms.Write(z);
        }
        return Resign(ms.ToArray(), tail);
    }

    /// <summary>Append a correct footer, so only the block rule can fail.</summary>
    private static byte[] Resign(byte[] body, byte[] tail)
    {
        using var md5 = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.MD5);
        md5.AppendData(body); md5.AppendData("0XBP"u8); md5.AppendData(new byte[16]); md5.AppendData(tail);
        var h = md5.GetHashAndReset();
        return [.. body, .. "0XBP"u8.ToArray(), .. h, .. tail];
    }

    private static readonly byte[] Tail = Enumerable.Range(0, 44).Select(i => (byte)(i == 0 ? 0x5A : 0)).ToArray();

    private static List<(int Clen, int Rlen)> Blocks(byte[] file)
    {
        int dl = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(4));
        int pos = 8 + dl;
        var o = new List<(int, int)>();
        while (pos + 8 <= file.Length - 64)
        {
            int c = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(pos)), r = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(pos + 4));
            o.Add((c, r));
            pos += 8 + (c == -1 ? r : c);
        }
        return o;
    }

    [Fact]
    public void An_incompressible_chunk_failed_the_old_rebuild_and_passes_the_new_one()
    {
        // The field: an AI chunk that does not compress, deflated to 32770/32775
        // bytes and written compressed; the engine refused the block and the join
        // ended on the loading screen. The old Verify passed such a file.
        var (hs, js) = WhsSaveTests.Pair();
        hs.Incompressible = true;
        byte[] h = WhsSaveTests.File(hs), j = WhsSaveTests.File(js);
        var res = WhsSave.Splice(h, j, WhsSaveTests.Quest, WhsSave.QuestItemMode.Strip);

        var raw = WhsSave.Inflate(res.File).Raw;
        var old = OldDeflate(WhsSave.Inflate(h).DescBytes, raw, Tail);
        Assert.Contains(Blocks(old), b => b.Clen > WhsSave.BlockBuffer);         // the old code overflows the engine's buffer
        var ov = WhsSave.Verify(old);
        Assert.False(ov.Ok);
        Assert.Contains("block size: compressed", ov.Reason);

        Assert.True(WhsSave.Verify(res.File).Ok);                                  // the new rebuild verifies
        var nb = Blocks(res.File);
        Assert.Contains(nb, b => b.Clen == -1 && b.Rlen == WhsSave.ChunkRaw);      // stored, as the game writes it
        Assert.All(nb, b => Assert.True(b.Clen == -1 || b.Clen < WhsSave.BlockBuffer));
        Assert.Equal(raw, WhsSave.Inflate(res.File).Raw);                          // same stream either way
        Assert.Empty(WhsSave.Check(h, j, res.File, WhsSaveTests.Quest, WhsSave.QuestItemMode.Strip));
    }

    [Fact]
    public void A_compressible_stream_stays_compressed()
    {
        var f = WhsSaveTests.File(new WhsSaveTests.Spec());
        Assert.All(Blocks(f), b => Assert.True(b.Clen > 0 && b.Clen < WhsSave.BlockBuffer));
    }

    private static byte[] Frame(params (int Clen, int Rlen, byte[] Data)[] blocks)
    {
        var desc = Encoding.UTF8.GetBytes("<SaveGame/>");
        var body = new List<byte>();
        var h = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(h, 0xFFFFFFFFu); BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(4), desc.Length);
        body.AddRange(h); body.AddRange(desc);
        foreach (var (c, r, d) in blocks)
        {
            BinaryPrimitives.WriteInt32LittleEndian(h, c); BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(4), r);
            body.AddRange(h); body.AddRange(d);
        }
        return Resign(body.ToArray(), Tail);
    }

    private static byte[] Z(byte[] raw)
    {
        using var zms = new MemoryStream();
        using (var zs = new ZLibStream(zms, CompressionLevel.Optimal, leaveOpen: true)) zs.Write(raw);
        return zms.ToArray();
    }

    [Fact]
    public void Verify_enforces_the_engines_reader_limits()
    {
        // C_SaveInputZlibStream::ReadBlock: stored <= 0x8000, compressed <= 0x8000,
        // inflated into a 0x8000 buffer.
        var ok = new byte[WhsSave.BlockBuffer];
        Assert.Null(Record(() => WhsSave.Inflate(Frame((-1, ok.Length, ok)))));

        var bigStored = new byte[WhsSave.BlockBuffer + 1];
        Assert.Contains("uncompressed 32769", Record(() => WhsSave.Inflate(Frame((-1, bigStored.Length, bigStored))))!.Message);

        var bigRaw = new byte[WhsSave.BlockBuffer * 2];       // compresses tiny, inflates past the buffer
        var zb = Z(bigRaw);
        Assert.Contains("buffer", Record(() => WhsSave.Inflate(Frame((zb.Length, bigRaw.Length, zb))))!.Message);
        // even when the header lies about it
        Assert.Contains("buffer", Record(() => WhsSave.Inflate(Frame((zb.Length, 100, zb))))!.Message);

        var rnd = new byte[WhsSave.BlockBuffer]; new Random(7).NextBytes(rnd);
        var zr = Z(rnd);
        Assert.True(zr.Length > WhsSave.BlockBuffer);
        Assert.Contains($"compressed {zr.Length}", Record(() => WhsSave.Inflate(Frame((zr.Length, rnd.Length, zr))))!.Message);

        Assert.Contains("neither stored", Record(() => WhsSave.Inflate(Frame((-2, 4, new byte[4]))))!.Message);
    }

    private static Exception? Record(Action a) { try { a(); return null; } catch (Exception e) { return e; } }

    [Fact]
    public void Deflate_never_writes_a_compressed_block_the_engines_writer_would_store()
    {
        var raw = new byte[WhsSave.ChunkRaw * 3 + 100];
        new Random(1).NextBytes(raw.AsSpan(0, WhsSave.ChunkRaw));                 // block 0 incompressible
        for (int i = WhsSave.ChunkRaw; i < raw.Length; i++) raw[i] = (byte)(i % 13);   // the rest compress
        var f = WhsSave.Deflate(Encoding.UTF8.GetBytes("<SaveGame/>"), raw, Tail);
        var b = Blocks(f);
        Assert.Equal(4, b.Count);
        Assert.Equal((-1, WhsSave.ChunkRaw), b[0]);
        Assert.All(b.Skip(1), x => Assert.InRange(x.Clen, 1, WhsSave.BlockBuffer - 1));
        Assert.Equal(100, b[3].Rlen);
        Assert.Equal(raw, WhsSave.Inflate(f).Raw);
        Assert.True(WhsSave.VerifyFooter(f).Ok);
    }

    [Fact]
    public void A_henry_without_an_item_list_splices_both_ways()
    {
        // 16 of 117 1.5.5 saves on the test machine: an early-game Henry whose
        // inventory record holds no item list. Both as the joiner and as the host.
        var (hs, js) = WhsSaveTests.Pair();
        js.NoItemList = true; js.Equipped = []; js.Keys = [];
        byte[] h = WhsSaveTests.File(hs), j = WhsSaveTests.File(js);
        var res = WhsSave.Splice(h, j, WhsSaveTests.Quest, WhsSave.QuestItemMode.Strip);
        Assert.True(WhsSave.Verify(res.File).Ok);
        Assert.Empty(WhsSave.Check(h, j, res.File, WhsSaveTests.Quest, WhsSave.QuestItemMode.Strip));
        Assert.Empty(res.Report.QuestItemsRemoved);

        var (hs2, js2) = WhsSaveTests.Pair();
        hs2.NoItemList = true; hs2.Equipped = [];
        byte[] h2 = WhsSaveTests.File(hs2), j2 = WhsSaveTests.File(js2);
        var res2 = WhsSave.Splice(h2, j2, WhsSaveTests.Quest, WhsSave.QuestItemMode.Strip);
        Assert.True(WhsSave.Verify(res2.File).Ok);
        Assert.Empty(WhsSave.Check(h2, j2, res2.File, WhsSaveTests.Quest, WhsSave.QuestItemMode.Strip));
    }
}
