// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System;
using System.Threading;
using System.Threading.Tasks;
using KCDMP_launcher.Models;
using Serilog;

namespace KCDMP_launcher.Pages
{
    /// <summary>
    /// WO-175 S2: the player's whole saves folder is backed up before every game this launcher starts
    /// (Models/Wo175Backup.cs), off the UI thread, with a short line at the bottom of the window. A failure is
    /// logged and shown in plain words, and the game starts anyway. RESTORE SAVES (the bar) puts one back.
    /// </summary>
    public partial class Home
    {
        private string wo175StatusLine = "";
        private bool wo175StatusBad;
        private bool showRestoreSaves;
        private int wo175StatusSeq;

        /// <summary>From LaunchGame, before anything touches the saves folder (WO-159 stages a start save).</summary>
        private async Task Wo175BackupBeforeSessionAsync()
        {
            if (!settings.BackupSavesBeforeSession)
            {
                Log.Information("WO175-BACKUP off (BackupSavesBeforeSession=false): no backup this session");
                return;
            }
            string? saves = Wo175Backup.ResolveSavesDir();
            if (saves is null)
            {
                Log.Information("WO175-BACKUP made=none files=0 bytes=0 verified=no kept=0 removed=0 (no saves folder yet: nothing to back up)");
                return;
            }

            Wo175Status("Backing up your saves...", bad: false, clearAfter: null);
            Wo175Backup.BackupResult r;
            try
            {
                r = await Task.Run(() => Wo175Backup.Backup(saves, Wo175Backup.DefaultRoot(), DateTime.Now));
            }
            catch (Exception ex)
            {
                r = new Wo175Backup.BackupResult(null, 0, 0, false, ex.Message, 0, 0);
            }

            if (r.Verified)
            {
                Log.Information(r.LogLine);
                Wo175Status($"Your saves are backed up ({r.Files} files).", bad: false, clearAfter: TimeSpan.FromSeconds(8));
            }
            else if (r.Skipped)
            {
                Log.Information(r.LogLine);
                Wo175Status("", bad: false, clearAfter: null);
            }
            else
            {
                Log.Warning(r.LogLine);
                Wo175Status($"Could not back up your saves: {r.Why}", bad: true, clearAfter: TimeSpan.FromMinutes(2));
            }
        }

        private void Wo175Status(string text, bool bad, TimeSpan? clearAfter)
        {
            int seq = Interlocked.Increment(ref wo175StatusSeq);
            wo175StatusLine = text;
            wo175StatusBad = bad;
            _ = InvokeAsync(StateHasChanged);
            if (clearAfter is { } wait)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(wait);
                    if (Volatile.Read(ref wo175StatusSeq) != seq) return;   // a newer line replaced it
                    wo175StatusLine = "";
                    await InvokeAsync(StateHasChanged);
                });
            }
        }
    }
}
