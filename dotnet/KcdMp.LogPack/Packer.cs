// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace KcdMp.LogPack;

/// <summary>One file for the bundle: its name inside it, and where its bytes come from (a path, read with full sharing, or bytes).</summary>
public sealed record SourceFile(string EntryName, string? Path = null, byte[]? Bytes = null);

public sealed class PackOptions
{
    /// <summary>The largest part. Discord's free per-file limit is 20 MB (50 MB with Nitro Basic): 19 MB leaves room.</summary>
    public long MaxPartBytes { get; set; } = 19_000_000;
    /// <summary>zstd level (19 is the strongest level that is still seconds per 10 MB).</summary>
    public int Level { get; set; } = 19;
    /// <summary>zstd window log: 26 = 64 MB, inside what every zstd reader accepts by default.</summary>
    public int WindowLog { get; set; } = 26;
    /// <summary>Collapse runs of repeated lines in *.log files (off = pack the bytes as they are).</summary>
    public bool Collapse { get; set; } = true;
}

public sealed class FileReport
{
    public string Entry = "";
    public long OriginalBytes, StoredBytes;
    public string Sha256 = "";           // of the original bytes
    public string? SameAs;               // another entry with identical content: stored once, there
    public CollapseStats? Collapse;      // null when the file was not collapsed
    public int Part;                     // 1-based
}

public sealed class PackResult
{
    public List<string> Parts = new();
    public List<FileReport> Files = new();
    public long OriginalBytes, PartBytes;
    public CollapseStats Totals = new();
    public int OddMarks;
    /// <summary>WO-201 B24: every mark the agents' logs hold: "&lt;entry&gt; &lt;time&gt; &lt;word&gt; [note]".</summary>
    public List<string> Marks = new();
    public string Manifest = "";
}

/// <summary>
/// WO-171 L: the bundle. Reads every file, drops byte-identical copies (said in the manifest, stored once), collapses the noise in
/// the logs, and writes a tar whose every file is its own zstd frame; frames are packed into parts of at most
/// <see cref="PackOptions.MaxPartBytes"/>, each a complete valid <c>.tzst</c> (7-Zip opens it). Part 1 begins with MANIFEST.txt.
/// </summary>
public static class Packer
{
    private const int ManifestReserve = 256 * 1024;

    public static PackResult Pack(IReadOnlyList<SourceFile> sources, string outDir, string baseName, PackOptions? opt = null, Action<string>? progress = null)
    {
        opt ??= new PackOptions();
        Directory.CreateDirectory(outDir);
        var res = new PackResult();
        void Say(string s) => progress?.Invoke(s);

        // pass 1: the marks (they decide which lines stay in full), over every log
        var windows = new OddWindows();
        if (opt.Collapse)
            foreach (var s in sources)
                if (IsLog(s.EntryName))
                {
                    byte[]? b = Read(s);
                    if (b is null) continue;
                    using var r = new StreamReader(new MemoryStream(b, false), Encoding.Latin1, false, 1 << 16);
                    var lines = Lines(r).ToList();
                    windows.Scan(lines);
                    if (s.EntryName.StartsWith("agent", StringComparison.OrdinalIgnoreCase)) MarkList.Collect(s.EntryName, lines, res.Marks);
                }
        res.OddMarks = windows.Count;

        // pass 2: dedupe, collapse, compress one frame per entry
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);   // sha256 -> first entry
        var frames = new List<(string Name, byte[] Frame, FileReport Rep)>();
        using var comp = new Compressor(opt.Level);
        comp.SetParameter(ZSTD_cParameter.ZSTD_c_windowLog, opt.WindowLog);
        comp.SetParameter(ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
        foreach (var s in sources)
        {
            byte[]? raw = Read(s);
            if (raw is null) continue;
            var rep = new FileReport { Entry = s.EntryName, OriginalBytes = raw.Length, Sha256 = Hex(SHA256.HashData(raw)) };
            res.OriginalBytes += raw.Length;
            if (seen.TryGetValue(rep.Sha256, out var first)) { rep.SameAs = first; res.Files.Add(rep); Say($"{s.EntryName}: identical to {first}"); continue; }
            seen[rep.Sha256] = s.EntryName;

            byte[] data = raw;
            if (opt.Collapse && IsLog(s.EntryName))
            {
                var (d, st) = Collapser.CollapseBytes(raw, windows);
                data = d; rep.Collapse = st; res.Totals.Add(st);
                Say($"{s.EntryName}: {st.LinesIn} lines -> {st.LinesKept + st.Summaries} ({st.Elided} collapsed)");
            }
            rep.StoredBytes = data.Length;
            res.Files.Add(rep);
            DateTime mtime = s.Path is not null && File.Exists(s.Path) ? File.GetLastWriteTimeUtc(s.Path) : DateTime.UtcNow;

            AddEntry(frames, comp, opt, s.EntryName, data, mtime, rep, Say);
        }

        // bins: biggest first into the first part with room; part 1 keeps room for the manifest
        long budget = opt.MaxPartBytes - 64 * 1024;   // the closing blocks and slack
        var bins = new List<List<(string Name, byte[] Frame, FileReport Rep)>> { new() };
        var used = new List<long> { ManifestReserve };
        foreach (var f in frames.OrderByDescending(f => f.Frame.Length))
        {
            int b = 0;
            while (b < bins.Count && used[b] + f.Frame.Length > budget) b++;
            if (b == bins.Count) { bins.Add(new()); used.Add(0); }
            bins[b].Add(f); used[b] += f.Frame.Length;
            f.Rep.Part = b + 1;
        }
        // a part with nothing in it is not written
        for (int i = bins.Count - 1; i > 0 && bins[i].Count == 0; i--) { bins.RemoveAt(i); used.RemoveAt(i); }
        int n = bins.Count;

        foreach (var r in res.Files.Where(r => r.SameAs is not null))
            r.Part = res.Files.First(x => x.Entry == r.SameAs).Part;
        res.Manifest = BuildManifest(res, n, baseName, opt);
        byte[] manFrame = comp.Wrap(Tar.Entry("MANIFEST.txt", Encoding.UTF8.GetBytes(res.Manifest), DateTime.UtcNow)).ToArray();

        for (int p = 0; p < n; p++)
        {
            string name = n == 1 ? $"{baseName}.tzst" : $"{baseName}-part{p + 1}of{n}.tzst";
            string path = System.IO.Path.Combine(outDir, name);
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (p == 0) fs.Write(manFrame);
                foreach (var f in bins[p].OrderBy(f => f.Name, StringComparer.Ordinal)) fs.Write(f.Frame);
                fs.Write(comp.Wrap(Tar.End()));
            }
            res.Parts.Add(path);
            res.PartBytes += new FileInfo(path).Length;
        }
        return res;
    }

    private static void AddEntry(List<(string, byte[], FileReport)> frames, Compressor comp, PackOptions opt, string name, byte[] data, DateTime mtime, FileReport rep, Action<string> say)
    {
        byte[] frame = comp.Wrap(Tar.Entry(name, data, mtime)).ToArray();
        long limit = opt.MaxPartBytes - ManifestReserve - 128 * 1024;
        if (frame.Length <= limit) { frames.Add((name, frame, rep)); return; }
        // one file that alone would not fit a part: cut it at a line end into pieces that each do
        int pieces = (int)Math.Ceiling(frame.Length * 1.15 / limit) + 1;
        long step = data.Length / pieces + 1;
        int from = 0, k = 1;
        say($"{name}: alone {frame.Length} bytes compressed -> {pieces} pieces");
        while (from < data.Length)
        {
            int to = (int)Math.Min(data.Length, from + step);
            if (to < data.Length) { int nl = Array.IndexOf(data, (byte)'\n', to); to = nl < 0 ? data.Length : nl + 1; }
            var piece = data.AsSpan(from, to - from).ToArray();
            frames.Add(($"{name}.piece{k}of{pieces}", comp.Wrap(Tar.Entry($"{name}.piece{k}of{pieces}", piece, mtime)).ToArray(), rep));
            from = to; k++;
        }
    }

    private static string BuildManifest(PackResult res, int parts, string baseName, PackOptions opt)
    {
        var sb = new StringBuilder();
        sb.Append("Kingdom Come: Together -- log bundle\r\n");
        sb.Append("Parts: ").Append(parts).Append(" (each a complete .tzst archive: a tar compressed with zstd. 7-Zip 24 or newer: open the .tzst, then the .tar inside it. Or: tar --zstd -xf <part>  /  zstd -d <part>)\r\n");
        sb.Append("Compression: zstd level ").Append(opt.Level).Append(", the logs' repeated lines collapsed (see below), identical files stored once\r\n");
        sb.Append("Original size: ").Append(res.OriginalBytes.ToString(CultureInfo.InvariantCulture)).Append(" bytes in ").Append(res.Files.Count).Append(" files\r\n");
        sb.Append("Marks (mark_odd) found: ").Append(res.OddMarks).Append(" -- every line within 5 minutes of one is kept in full\r\n");
        if (res.Marks.Count > 0)
        {
            sb.Append("Marks typed on these machines (entry, time, word, the player's note):\r\n");
            foreach (var m in res.Marks.Take(200)) sb.Append("  ").Append(m).Append("\r\n");
        }
        sb.Append("Kept in full everywhere: MP-MARK, FAULT, STATS / summary lines; a collapsed run keeps its first and last line and is replaced in between by a line starting \"")
          .Append(Collapser.SummaryPrefix.Trim()).Append("\" with the count\r\n\r\n");
        sb.Append("part | entry | original bytes | stored bytes | sha256 of the original | note\r\n");
        foreach (var f in res.Files)
        {
            string note = f.SameAs is not null ? $"same content as {f.SameAs}"
                        : f.Collapse is { } c && c.Elided > 0 ? $"{c.LinesIn} lines -> {c.LinesKept} kept + {c.Summaries} summaries, {c.Elided} collapsed"
                        : f.Collapse is { } c2 ? $"{c2.LinesIn} lines, none collapsed" : "";
            sb.Append(f.Part).Append(" | ").Append(f.Entry).Append(" | ").Append(f.OriginalBytes).Append(" | ")
              .Append(f.SameAs is null ? f.StoredBytes.ToString(CultureInfo.InvariantCulture) : "0").Append(" | ").Append(f.Sha256).Append(" | ").Append(note).Append("\r\n");
        }
        _ = baseName;
        return sb.ToString();
    }

    private static bool IsLog(string entry) => entry.EndsWith(".log", StringComparison.OrdinalIgnoreCase);

    private static byte[]? Read(SourceFile s)
    {
        if (s.Bytes is not null) return s.Bytes;
        try
        {
            if (string.IsNullOrWhiteSpace(s.Path) || !File.Exists(s.Path)) return null;
            using var fs = new FileStream(s.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var ms = new MemoryStream((int)Math.Min(fs.Length, int.MaxValue / 2));
            fs.CopyTo(ms);
            return ms.ToArray();
        }
        catch { return null; }   // one unreadable file never sinks the bundle
    }

    private static IEnumerable<string> Lines(TextReader r) { string? l; while ((l = r.ReadLine()) is not null) yield return l; }

    private static string Hex(byte[] h) => Convert.ToHexString(h).ToLowerInvariant();
}

/// <summary>A minimal ustar writer: one regular file per entry, names up to 255 characters.</summary>
public static class Tar
{
    public static byte[] Entry(string name, byte[] data, DateTime mtimeUtc)
    {
        name = name.Replace('\\', '/');
        string prefix = "";
        if (Encoding.UTF8.GetByteCount(name) > 100)
        {
            int cut = name.LastIndexOf('/', Math.Min(name.Length - 1, 155));
            if (cut > 0 && name.Length - cut - 1 <= 100 && cut <= 155) { prefix = name[..cut]; name = name[(cut + 1)..]; }
            else name = name[^100..];
        }
        var h = new byte[512];
        Put(h, 0, name, 100);
        Octal(h, 100, 0x1A4, 8);                // mode 0644
        Octal(h, 108, 0, 8); Octal(h, 116, 0, 8);
        Octal(h, 124, data.Length, 12);
        long mt = new DateTimeOffset(mtimeUtc, TimeSpan.Zero).ToUnixTimeSeconds();
        Octal(h, 136, mt < 0 ? 0 : mt, 12);
        for (int i = 148; i < 156; i++) h[i] = 0x20;   // the checksum is summed over spaces
        h[156] = (byte)'0';
        Put(h, 257, "ustar", 6); h[263] = (byte)'0'; h[264] = (byte)'0';
        Put(h, 345, prefix, 155);
        long sum = 0; foreach (var b in h) sum += b;
        string cs = Convert.ToString(sum, 8).PadLeft(6, '0');
        for (int i = 0; i < 6; i++) h[148 + i] = (byte)cs[i];
        h[154] = 0; h[155] = 0x20;
        int pad = (int)((512 - data.Length % 512) % 512);
        var o = new byte[512 + data.Length + pad];
        Buffer.BlockCopy(h, 0, o, 0, 512);
        Buffer.BlockCopy(data, 0, o, 512, data.Length);
        return o;
    }

    /// <summary>The two empty blocks that end a tar.</summary>
    public static byte[] End() => new byte[1024];

    private static void Put(byte[] h, int at, string s, int max)
    {
        var b = Encoding.UTF8.GetBytes(s);
        Buffer.BlockCopy(b, 0, h, at, Math.Min(b.Length, max));
    }

    private static void Octal(byte[] h, int at, long v, int width)
    {
        string s = Convert.ToString(v, 8).PadLeft(width - 1, '0');
        for (int i = 0; i < width - 1; i++) h[at + i] = (byte)s[i];
        h[at + width - 1] = 0;
    }
}
