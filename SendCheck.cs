using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace YouTubeMp3
{
    // Something stopping songs from reaching Apple Music, said the way the person can act on it.
    internal sealed class SendProblem
    {
        internal string Status;   // short, for the song's row
        internal string Title;    // what's wrong, for the banner
        internal string Fix;      // what to do about it
        internal bool Blocks;     // true when nothing can be sent until it's fixed

        internal SendProblem(string status, string title, string fix, bool blocks)
        {
            Status = status; Title = title; Fix = fix; Blocks = blocks;
        }

        internal string Text { get { return Title + " " + Fix; } }
    }

    // Apple Music for Windows has no API: songs go in through its "Automatically Add to Apple Music" folder, so most
    // problems show up as a file that never leaves that folder. These checks name the usual causes.
    internal static class SendCheck
    {
        internal const int StallSeconds = 180;
        private const long MinimumFreeBytes = 300L * 1024 * 1024;

        // Before the copy: anything that would make the send fail outright.
        internal static SendProblem BeforeSend(string folder)
        {
            if (folder == null)
                return new SendProblem("Waiting: Apple Music's import folder wasn't found",
                    "Apple Music's “Automatically Add to Apple Music” folder wasn't found.",
                    "Open Apple Music once so it creates its Media folder, or choose the folder in Settings › Apple Music.", true);
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(folder));
                if (drive.IsReady && drive.AvailableFreeSpace < MinimumFreeBytes)
                    return new SendProblem("Waiting: the disk is almost full",
                        "Drive " + drive.Name.TrimEnd('\\') + " has less than 300 MB free, so Apple Music can't add songs.",
                        "Free up some space on that drive, then select Send again.", true);
            }
            catch (ArgumentException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            string probe = Path.Combine(folder, ".carplay-sync-check-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(probe, "");
                File.Delete(probe);
            }
            catch (UnauthorizedAccessException)
            {
                return new SendProblem("Waiting: Windows blocked the Apple Music folder",
                    "Windows didn't let this app write to Apple Music's import folder.",
                    "This is usually Windows Security's Controlled folder access or another antivirus. In Windows Security › " +
                    "Virus & threat protection › Ransomware protection, allow YouTube CarPlay Sync, then select Send again.", true);
            }
            catch (IOException ex)
            {
                return new SendProblem("Waiting: the Apple Music folder can't be written",
                    "Apple Music's import folder can't be written to (" + ex.Message.TrimEnd('.') + ").",
                    "Check the drive is connected and not full, then select Send again.", true);
            }
            finally { try { if (File.Exists(probe)) File.Delete(probe); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            return null;
        }

        // After the copy, while the file is still waiting in the folder: why Apple Music hasn't taken it yet.
        internal static SendProblem Stalled(string copy, TimeSpan waiting, Running apps)
        {
            if (apps.ITunes)
                return new SendProblem("Waiting: iTunes is open",
                    "iTunes is open, and Apple Music can't add songs while it is.",
                    "Quit iTunes (also check the system tray). The songs go in once Apple Music is the only one open.", false);
            if (apps.Devices)
                return new SendProblem("Waiting: Apple Devices is busy",
                    "The Apple Devices app is open, and while it syncs or backs up a phone, Apple Music can't add songs.",
                    "Let the sync or backup finish, or close Apple Devices (also from the system tray). The songs go in by themselves after that.", false);
            if (!apps.Music)
                return new SendProblem("Open Apple Music to finish adding it",
                    "Apple Music isn't open, so it can't pick the songs up.",
                    "Open Apple Music. It adds every waiting song by itself.", false);
            if (IsLocked(copy))
                return new SendProblem("Waiting: another program has the file open",
                    "Another program is holding the song file, so Apple Music can't take it.",
                    "This is usually antivirus scanning it or OneDrive syncing it. Wait a minute; if it stays stuck, pause OneDrive " +
                    "or allow the folder in your antivirus.", false);
            if (waiting.TotalSeconds < StallSeconds)
                return new SendProblem("Apple Music is busy: waiting for it to pick the song up", null, null, false);
            string where = InOneDrive(copy)
                ? " Your Music folder is inside OneDrive, which can keep files online-only: right-click the Apple Music folder › Always keep on this device."
                : "";
            return new SendProblem("Waiting: Apple Music hasn't taken the song",
                "Apple Music is open but hasn't added songs for " + ((int)waiting.TotalMinutes) + " minutes.",
                "It's usually waiting on a window: switch to Apple Music and answer any sign-in, terms or library message. If nothing shows, " +
                "quit Apple Music fully (right-click its icon in the taskbar › Close) and open it again." + where, false);
        }

        // Which Apple apps are open, looked up once per check rather than once per song.
        internal sealed class Running
        {
            internal bool Music, ITunes, Devices;
            internal static Running Now()
            {
                return new Running { Music = AppleMusic.IsRunning(), ITunes = IsRunning("iTunes"), Devices = IsRunning("AppleDevices") };
            }
        }

        internal static bool IsRunning(string process)
        {
            try
            {
                var found = Process.GetProcessesByName(process);
                bool any = found.Length > 0;
                foreach (var p in found) p.Dispose();
                return any;
            }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }

        internal static bool IsLocked(string file)
        {
            try
            {
                using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                return false;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return true; }
        }

        internal static bool InOneDrive(string path)
        {
            if (String.IsNullOrEmpty(path)) return false;
            var roots = new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" }
                .Select(Environment.GetEnvironmentVariable).Where(r => !String.IsNullOrEmpty(r));
            foreach (var root in roots)
                if (path.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            return path.IndexOf("\\OneDrive", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
