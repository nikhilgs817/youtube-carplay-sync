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
        private static readonly string Shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "YouTube MP3.lnk");
        private const string Entry = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\YouTubeMP3";
        private const string Version = "1.1.0";
        private static readonly Color Background = Color.FromArgb(15, 23, 39);
        private static readonly Color Foreground = Color.FromArgb(238, 245, 253);
        private static readonly Color Accent = Color.FromArgb(104, 225, 203);
        private readonly Label status;
        private readonly Button install;
        private readonly CheckBox launch;
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
            Text = "YouTube MP3 Setup";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            BackColor = Background;
            ForeColor = Foreground;
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(530, 350);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(27, 22, 27, 20), ColumnCount = 1 };
            foreach (int height in new[] { 57, 53, 47, 40, 53, 45 })
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            var masthead = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            if (Icon != null)
            {
                var art = Icon.ToBitmap();
                masthead.Controls.Add(new PictureBox { Image = art, SizeMode = PictureBoxSizeMode.Zoom,
                    Size = new Size(44, 44), Margin = new Padding(0, 0, 12, 0) });
                Disposed += delegate { art.Dispose(); };
            }
            masthead.Controls.Add(Label("YouTube MP3", 23, true));
            layout.Controls.Add(masthead, 0, 0);
            layout.Controls.Add(Label("Install or update YouTube MP3 " + Version + " for your Windows account."), 0, 1);
            layout.Controls.Add(Label(InstallFolder, 9), 0, 2);
            launch = new CheckBox { Text = "Open YouTube MP3 after setup", Checked = true, AutoSize = true, Margin = Padding.Empty };
            layout.Controls.Add(launch, 0, 3);
            status = Label("Setup adds a Start menu shortcut and an entry in Windows Installed apps.", 9);
            layout.Controls.Add(status, 0, 4);
            install = new Button { Text = File.Exists(AppPath) ? "Update app" : "Install app", Width = 133, Height = 36,
                BackColor = Accent, ForeColor = Background, FlatStyle = FlatStyle.Flat, Margin = Padding.Empty };
            install.FlatAppearance.BorderColor = Accent;
            layout.Controls.Add(install, 0, 5);
            Controls.Add(layout);
            AcceptButton = install;
            FormClosing += delegate(object _, FormClosingEventArgs e) { e.Cancel = busy; };
            install.Click += async delegate { if (installed) Close(); else await RunInstall(); };
        }

        private static Label Label(string value, int size = 10, bool bold = false)
        {
            return new Label { Text = value, ForeColor = Foreground, AutoSize = true, MaximumSize = new Size(465, 0),
                Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = Padding.Empty };
        }

        private async Task RunInstall()
        {
            install.Enabled = launch.Enabled = false;
            busy = true;
            status.Text = "Installing...";
            try
            {
                await Task.Run(() => Exclusive(Install));
                installed = true;
                status.Text = "Installed. You can open the app from the Start menu.";
                install.Text = "Finish";
                if (launch.Checked)
                    Process.Start(new ProcessStartInfo(AppPath) { UseShellExecute = true, WorkingDirectory = InstallFolder });
            }
            catch (Exception ex) { status.Text = installed ? "Installed. Open YouTube MP3 from the Start menu." : ex.Message; }
            finally { busy = false; install.Enabled = true; launch.Enabled = !installed; }
        }

        private static void Exclusive(Action action)
        {
            using (var mutex = new Mutex(false, @"Local\YouTubeMP3.Setup"))
            {
                bool owned;
                try { owned = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned) throw new InvalidOperationException("Close the other YouTube MP3 setup window and retry.");
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
                        throw new InvalidOperationException("Close YouTube MP3 and try again.");
                }
            }
        }

        private static void Install()
        {
            VerifyAppClosed();
            Directory.CreateDirectory(InstallFolder);
            var updates = new List<FileUpdate>();
            try
            {
                AddFile(updates, AppPath, Assembly.GetExecutingAssembly().GetManifestResourceStream("app"));
                AddFile(updates, ScriptPath, Assembly.GetExecutingAssembly().GetManifestResourceStream("tools"));
                if (!String.Equals(Application.ExecutablePath, SetupPath, StringComparison.OrdinalIgnoreCase))
                    AddFile(updates, SetupPath, File.OpenRead(Application.ExecutablePath));
                using (var key = Registry.CurrentUser.CreateSubKey(Entry))
                {
                    key.SetValue("DisplayName", "YouTube MP3");
                    key.SetValue("DisplayVersion", Version);
                    key.SetValue("DisplayIcon", AppPath);
                    key.SetValue("InstallLocation", InstallFolder);
                    key.SetValue("UninstallString", "\"" + SetupPath + "\" --uninstall");
                    key.SetValue("NoModify", 1);
                    key.SetValue("NoRepair", 1);
                }
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                dynamic shortcut = shell.CreateShortcut(Shortcut);
                shortcut.TargetPath = AppPath;
                shortcut.WorkingDirectory = InstallFolder;
                shortcut.IconLocation = AppPath + ",0";
                shortcut.Description = "Download MP3 audio and organize music with a local model";
                shortcut.Save();
            }
            catch
            {
                for (int i = updates.Count - 1; i >= 0; i--) updates[i].Restore();
                if (!File.Exists(AppPath)) Registry.CurrentUser.DeleteSubKeyTree(Entry, false);
                throw;
            }
            finally { foreach (var update in updates) update.Cleanup(); }
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
            if (MessageBox.Show("Remove YouTube MP3 and its Start menu shortcut? Your music and model settings will stay on this PC.",
                "Uninstall YouTube MP3", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            if (!String.Equals(SetupPath, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) { Uninstall(); return; }
            try
            {
                string helper = Path.Combine(Path.GetTempPath(), "YouTube-MP3-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                File.Copy(SetupPath, helper);
                int pid;
                using (var current = Process.GetCurrentProcess()) pid = current.Id;
                Process.Start(new ProcessStartInfo(helper, "--finish-uninstall " + pid) { UseShellExecute = false });
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Uninstall YouTube MP3"); }
        }

        private static void Uninstall()
        {
            try
            {
                Exclusive(delegate
                {
                    VerifyAppClosed();
                    foreach (string file in new[] { AppPath, ScriptPath, SetupPath, Shortcut }) File.Delete(file);
                    if (Directory.Exists(InstallFolder) && Directory.GetFileSystemEntries(InstallFolder).Length == 0)
                        Directory.Delete(InstallFolder);
                    Registry.CurrentUser.DeleteSubKeyTree(Entry, false);
                });
                MessageBox.Show("YouTube MP3 was removed.", "Uninstall YouTube MP3");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Uninstall YouTube MP3"); }
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
