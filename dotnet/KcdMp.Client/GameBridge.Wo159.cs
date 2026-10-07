// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

// WO-159: the joiner's "Join with a new character" with a chosen playstyle: its Henry comes from the bundled start save
// of that playstyle (Setup installs them beside the launcher and this agent), so a new player needs no save of their own.
public partial class GameBridge
{
    /// <summary>&lt;Saved Games&gt;\KingdomCome2 (the game's user folder), whether or not it exists.</summary>
    internal static string Kcd2UserFolder()
    {
        string? root = null;
        try
        {
            if (SHGetKnownFolderPath(FolderIdSavedGames, 0, IntPtr.Zero, out var p) == 0)
            {
                root = System.Runtime.InteropServices.Marshal.PtrToStringUni(p);
                System.Runtime.InteropServices.Marshal.FreeCoTaskMem(p);
            }
        }
        catch { }
        root ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
        return Path.Combine(root, "KingdomCome2");
    }

    /// <summary>The playstyle the launcher sent with "fresh" (fresh:soldier|adviser|scout), or null.</summary>
    private string? _w159FreshStyle;

    /// <summary>
    /// The bundled start save of the chosen playstyle as the first join's Henry, or null (logged) -- then the WO-125/157
    /// sources are tried as before. Of the host world's build only (WO-135). Its slot is where the join's file goes when
    /// this player has no save of their own: the lowest free playline.
    /// </summary>
    private HenryChoice? Wo159BundledFresh(out string why)
    {
        why = "";
        if (_w159FreshStyle is not string style) return null;
        if (Wo159Rules.StartSaveRoot() is not string root || Wo159.StyleFile(root, style) is not string file)
        {
            why = $"the {style} start save is not installed";
            Console.WriteLine($"MP-HENRY joiner: a new character: {why} -- the saves on this computer are tried instead");
            return null;
        }
        string? build = Wo135TargetBuild();
        if (build is not null && WhsSave.ReadBuildFromFile(file) is var fb && !Wo135Rules.SameBuild(fb, build))
        {
            why = Wo135Rules.WrongBuildMessage(fb, build);
            Console.WriteLine($"MP-HENRY joiner: the {style} start save is of game build {fb ?? "unknown"}, the host world's is {build} -- not used");
            return null;
        }
        int pl = ResolveSavesDirForJoin() is string saves && Wo159.FreePlaylines(saves) is { Count: > 0 } free ? free[0] : 0;
        var src = new HenrySource(pl, Path.GetFileName(file), file, ReadSaveTime(file) ?? 0);
        var c = TryHenrySave(src, WhsSave.HenryParts.OriginFreshSave, "fresh", out why, requirePristine: false);
        Console.WriteLine(c is null
            ? $"MP-HENRY joiner: the {style} start save could not be used: {why}"
            : $"MP-HENRY joiner: a new character from the bundled {style} start save");
        return c is null ? null : c with { Detail = $"the bundled {style} start save" };
    }
}
