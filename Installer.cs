using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace YouTubeMp3.Setup
{
    internal sealed class Installer : Form
    {
        private static readonly string InstallFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "YouTube MP3");
        private static readonly string AppPath = Path.Combine(InstallFolder, "YouTube-MP3.exe");
        private static readonly string SetupPath = Path.Combine(InstallFolder, "YouTube-MP3-Setup.exe");
        private static readonly string ScriptPath = Path.Combine(InstallFolder, "setup-tools.ps1");
        private static readonly string Programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        private static readonly string Shortcut = Path.Combine(Programs, AppInfo.Name + ".lnk");
        private static readonly string LegacyShortcut = Path.Combine(Programs, "YouTube MP3.lnk");
        private const string Entry = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\YouTubeMP3";
        private static readonly string Desktop = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppInfo.Name + ".lnk");
        private readonly Label status;
        private readonly PillButton install;
        private readonly Toggle launch = new Toggle("Open " + AppInfo.Name + " after setup") { Checked = true };
        private readonly Toggle desktop = new Toggle("Add a desktop shortcut") { Checked = true };
        private readonly Toggle tools = new Toggle("Download audio tools now (yt-dlp, FFmpeg, Deno · about 150 MB)") { Checked = true };
        private readonly ProgressLine progress = new ProgressLine();
        private bool busy;
        private bool installed;

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && args[0] == "--uninstall") { StartUninstall(); return; }
            int pid;
            if (args.Length == 2 && args[0] == "--finish-uninstall" && Int32.TryParse(args[1], out pid))
            {
                try { using (var process = Process.GetProcessById(pid)) process.WaitForExit(7000); }
                catch (ArgumentException) { }
                Uninstall();
                return;
            }
            Application.Run(new Installer());
        }

        public Installer()
        {
            Text = AppInfo.Name + " Setup";
            Icon = Theme.AppIcon();
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(600), Theme.S(470));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            HandleCreated += delegate { NativeTheme.DarkTitleBar(this); };

            var card = new Card { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(20)), BackColor = Theme.Background };
            outer.Controls.Add(card);
            Controls.Add(outer);
            var stack = Layouts.Stack();
            var masthead = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            if (Icon != null)
            {
                var art = Theme.AppImage(Theme.S(44));
                masthead.Controls.Add(new PictureBox { Image = art, SizeMode = PictureBoxSizeMode.Zoom,
                    Size = new Size(Theme.S(44), Theme.S(44)), Margin = new Padding(0, 0, Theme.S(12), 0) });
                Disposed += delegate { art.Dispose(); };
            }
            masthead.Controls.Add(new Label { Text = AppInfo.Name, Font = Theme.PageTitle, ForeColor = Theme.Text, AutoSize = true,
                Margin = new Padding(0, Theme.S(2), 0, 0), UseMnemonic = false });
            Layouts.Add(stack, masthead, true, 12);
            bool update = File.Exists(AppPath);
            Layouts.Add(stack, Layouts.Note((update ? "Update to " : "Install ") + AppInfo.Name + " " + AppInfo.Version +
                " for your Windows account. Your music, library tags and settings are kept.", Theme.Ink2), true, 4);
            Layouts.Add(stack, Layouts.Note(InstallFolder, Theme.Faint), true, 14);
            Layouts.Add(stack, tools, false, 2);
            Layouts.Add(stack, desktop, false, 2);
            Layouts.Add(stack, launch, false, 14);
            progress.Value = -2;
            Layouts.Add(stack, progress, true, 8);
            status = Layouts.Note("Setup adds Start menu and desktop shortcuts and an entry in Windows Installed apps.");
            Layouts.Add(stack, status, true, 14);
            install = Theme.Button(update ? "Update app" : "Install app", 140, true);
            Layouts.Add(stack, install, false, 0);
            card.Fit(stack);
            card.Dock = DockStyle.Fill;
            AcceptButton = install;
            FormClosing += delegate(object _, FormClosingEventArgs e) { e.Cancel = busy; };
            install.Click += async delegate { if (installed) Close(); else await RunInstall(); };
        }

        private async Task RunInstall()
        {
            install.Enabled = launch.Enabled = desktop.Enabled = tools.Enabled = false;
            busy = true;
            status.ForeColor = Theme.Muted;
            status.Text = "Installing the app…";
            progress.Value = -1;
            bool addDesktop = desktop.Checked;
            try
            {
                await Task.Run(() => Exclusive(() => Install(addDesktop)));
                installed = true;
                string toolNote = "";
                if (tools.Checked)
                {
                    IProgress<ToolProgress> report = new Progress<ToolProgress>(p =>
                    {
                        status.Text = p.Message + (p.Percent >= 0 ? " · " + p.Percent + "%" : "…");
                        progress.Value = p.Percent;
                    });
                    try
                    {
                        toolNote = " " + await ToolManager.InstallAsync(false, report, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        if (ex is OutOfMemoryException) throw;
                        // The app is installed either way; it offers the same download on its first screen.
                        toolNote = " The audio tools couldn't be downloaded (" + ex.Message + "). The app will offer to install them.";
                    }
                }
                progress.Value = 100;
                status.ForeColor = Theme.Pass;
                status.Text = "Installed." + toolNote;
                install.Text = "Finish";
                if (launch.Checked)
                    Process.Start(new ProcessStartInfo(AppPath) { UseShellExecute = true, WorkingDirectory = InstallFolder });
            }
            catch (Exception ex)
            {
                progress.Value = -2;
                status.ForeColor = installed ? Theme.Pass : Theme.Fail;
                status.Text = installed ? "Installed. Open " + AppInfo.Name + " from the Start menu." : ex.Message;
            }
            finally { busy = false; install.Enabled = true; launch.Enabled = !installed; }
        }

        private static void Exclusive(Action action)
        {
            using (var mutex = new Mutex(false, @"Local\YouTubeMP3.Setup"))
            {
                bool owned;
                try { owned = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned) throw new InvalidOperationException("Close the other setup window for " + AppInfo.Name + " and retry.");
                try { action(); }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static void VerifyAppClosed()
        {
            foreach (var process in Process.GetProcessesByName("YouTube-MP3"))
            {
                using (process)
                {
                    string executable;
                    try { executable = process.MainModule.FileName; }
                    catch (InvalidOperationException) { continue; }
                    catch (System.ComponentModel.Win32Exception) { continue; }
                    if (String.Equals(executable, AppPath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Close " + AppInfo.Name + " and try again.");
                }
            }
        }

        private static void Install(bool addDesktop)
        {
            VerifyAppClosed();
            Directory.CreateDirectory(InstallFolder);
            var updates = new List<FileUpdate>();
            try
            {
                AddFile(updates, AppPath, Assembly.GetExecutingAssembly().GetManifestResourceStream("app"));
                if (!String.Equals(Application.ExecutablePath, SetupPath, StringComparison.OrdinalIgnoreCase))
                    AddFile(updates, SetupPath, File.OpenRead(Application.ExecutablePath));
                using (var key = Registry.CurrentUser.CreateSubKey(Entry))
                {
                    key.SetValue("DisplayName", AppInfo.Name);
                    key.SetValue("DisplayVersion", AppInfo.Version);
                    key.SetValue("DisplayIcon", AppPath);
                    key.SetValue("InstallLocation", InstallFolder);
                    key.SetValue("UninstallString", "\"" + SetupPath + "\" --uninstall");
                    key.SetValue("NoModify", 1);
                    key.SetValue("NoRepair", 1);
                }
                CreateShortcut(Shortcut);
                if (addDesktop) CreateShortcut(Desktop);
            }
            catch
            {
                for (int i = updates.Count - 1; i >= 0; i--) updates[i].Restore();
                if (!File.Exists(AppPath)) Registry.CurrentUser.DeleteSubKeyTree(Entry, false);
                throw;
            }
            finally { foreach (var update in updates) update.Cleanup(); }
            RemoveLegacyShortcut();
            // 1.3 sets up the audio tools itself, so the old WinGet script is no longer used.
            try { File.Delete(ScriptPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void CreateShortcut(string path)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            dynamic shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = AppPath;
            shortcut.WorkingDirectory = InstallFolder;
            shortcut.IconLocation = AppPath + ",0";
            shortcut.Description = "Find songs, tag them and get them ready for Apple Music and CarPlay";
            shortcut.Save();
        }

        private static void RemoveIfOurs(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                string target = shell.CreateShortcut(path).TargetPath;
                if (String.Equals(target, AppPath, StringComparison.OrdinalIgnoreCase)) File.Delete(path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Runtime.InteropServices.COMException) { }
        }

        private static void RemoveLegacyShortcut()
        {
            try
            {
                if (!File.Exists(LegacyShortcut)) return;
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                string target = shell.CreateShortcut(LegacyShortcut).TargetPath;
                if (String.Equals(target, AppPath, StringComparison.OrdinalIgnoreCase)) File.Delete(LegacyShortcut);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Runtime.InteropServices.COMException) { }
        }

        private static void AddFile(List<FileUpdate> updates, string target, Stream contents)
        {
            if (contents == null) throw new InvalidOperationException("The installer payload is missing.");
            var update = new FileUpdate(target);
            updates.Add(update);
            using (contents) update.Write(contents);
        }

        private static void StartUninstall()
        {
            if (MessageBox.Show("Remove " + AppInfo.Name + ", its shortcuts and its audio tools? Your music, library tags and settings will stay on this PC.",
                "Uninstall " + AppInfo.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            if (!String.Equals(SetupPath, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) { Uninstall(); return; }
            try
            {
                string helper = Path.Combine(Path.GetTempPath(), "YouTube-CarPlay-Sync-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(SetupPath, helper);
                int pid;
                using (var current = Process.GetCurrentProcess()) pid = current.Id;
                Process.Start(new ProcessStartInfo(helper, "--finish-uninstall " + pid) { UseShellExecute = false });
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Uninstall " + AppInfo.Name); }
        }

        private static void Uninstall()
        {
            try
            {
                Exclusive(delegate
                {
                    VerifyAppClosed();
                    RemoveLegacyShortcut();
                    RemoveIfOurs(Desktop);
                    foreach (string file in new[] { AppPath, ScriptPath, SetupPath, Shortcut }) File.Delete(file);
                    // The downloaded tools are not personal data; music, catalogs and settings are left alone.
                    try { if (Directory.Exists(DownloadTools.ManagedFolder)) Directory.Delete(DownloadTools.ManagedFolder, true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    if (Directory.Exists(InstallFolder) && Directory.GetFileSystemEntries(InstallFolder).Length == 0)
                        Directory.Delete(InstallFolder);
                    Registry.CurrentUser.DeleteSubKeyTree(Entry, false);
                });
                MessageBox.Show(AppInfo.Name + " was removed.", "Uninstall " + AppInfo.Name);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Uninstall " + AppInfo.Name); }
        }

        private sealed class FileUpdate
        {
            private readonly string target;
            private readonly string temporary;
            private readonly string backup;
            private bool replaced;

            public FileUpdate(string path)
            {
                target = path;
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                backup = temporary + ".previous";
            }

            public void Write(Stream source)
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    source.CopyTo(file);
                if (File.Exists(target)) File.Replace(temporary, target, backup);
                else File.Move(temporary, target);
                replaced = true;
            }

            public void Restore()
            {
                if (!replaced) return;
                if (File.Exists(backup)) File.Replace(backup, target, null);
                else File.Delete(target);
            }

            public void Cleanup()
            {
                try { File.Delete(temporary); File.Delete(backup); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
