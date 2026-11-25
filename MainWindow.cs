using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YouTubeMp3
{
    // One screen, one flow: paste links, and every song goes through Download, Convert, Cover art and AI tags on its own.
    // Songs then wait for you in the review bar; one click sends the checked songs to Apple Music.
    internal sealed class MainWindow : Form
    {
        private readonly Pipeline pipeline;
        private readonly SongList list = new SongList { Dock = DockStyle.Fill };
        private readonly TextBox input = new TextBox { Multiline = true, AcceptsReturn = false, WordWrap = true,
            AccessibleName = "YouTube links or a song name" };
        private readonly InputBox inputField;
        private readonly PillButton add = Theme.Button("Add songs", 140, true);
        private readonly PillButton loadTxt = Theme.Button("Load .txt", 140);
        private readonly PillButton stop = Theme.Button("Stop", 90);
        private readonly PillButton retryAll = Theme.Button("Retry failed", 120);
        private readonly PillButton clear = Theme.Button("Clear finished", 130);
        private readonly PillButton review = Theme.Button("Review & send", 180, true);
        private readonly PillButton installTools = Theme.Button("Install tools", 130, true);
        private readonly PillButton copyGroup = Theme.Button("Copy group name", 150);
        private readonly PillButton dismiss = Theme.Button("Dismiss", 100);
        private readonly Notice banner = new Notice { Dock = DockStyle.Top, Visible = false };
        private readonly Label counts = new Label { AutoSize = true, Font = Theme.Body, ForeColor = Theme.Muted, UseMnemonic = false,
            Margin = new Padding(Theme.S(12), Theme.S(4), 0, 0), BackColor = Theme.Background };
        private readonly Label reviewText = new Label { AutoSize = true, Font = Theme.Body, ForeColor = Theme.Text, UseMnemonic = false,
            BackColor = Theme.Background };
        private readonly Card reviewBar = new Card { Dock = DockStyle.Bottom, Visible = false };
        private readonly ToolChip chip = new ToolChip();
        private readonly RoundButton gear = new RoundButton("⚙") { AccessibleName = "Settings" };
        private readonly ContextMenuStrip matches = new ContextMenuStrip();
        private readonly Image iconImage;
        private readonly List<string> newGroups = new List<string>();
        private int resent;
        private string message;

        [STAThread]
        private static void Main()
        {
            // One copy at a time: two copies would each save their own song list over the other's.
            bool first;
            using (var gate = new Mutex(true, "Local\\YouTubeCarPlaySync.MainWindow", out first))
            {
                if (!first) { FocusRunningCopy(); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainWindow());
                GC.KeepAlive(gate);
            }
        }

        private static void FocusRunningCopy()
        {
            try
            {
                var me = Process.GetCurrentProcess();
                foreach (var other in Process.GetProcessesByName(me.ProcessName))
                {
                    if (other.Id == me.Id || other.MainWindowHandle == IntPtr.Zero) continue;
                    if (IsIconic(other.MainWindowHandle)) ShowWindow(other.MainWindowHandle, 9);
                    SetForegroundWindow(other.MainWindowHandle);
                    return;
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);

        public MainWindow() : this(null) { }

        internal MainWindow(Pipeline existing)
        {
            Text = AppInfo.Name;
            Icon = Theme.AppIcon();
            iconImage = Theme.AppImage(Theme.S(44));
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(1120), Theme.S(840));
            MinimumSize = new Size(Theme.S(880), Theme.S(640));
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            KeyPreview = true;
            Padding = new Padding(Theme.S(10), 0, Theme.S(10), Theme.S(6));
            HandleCreated += delegate { NativeTheme.DarkTitleBar(this); };
            pipeline = existing ?? new Pipeline();

            inputField = Theme.Field(input, "Paste YouTube links (one per line), or type a song name and press Enter");
            inputField.Height = Theme.S(96);
            Theme.StyleMenu(matches);

            var reviewRow = Layouts.Row(reviewText, review);
            reviewRow.Dock = DockStyle.Fill;
            reviewBar.Height = Theme.S(108);
            reviewBar.Fill(reviewRow);

            var songsHeader = BuildSongsHeader();
            var listHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(0, 0, 0, Theme.S(4)) };
            listHost.Controls.Add(list);

            banner.Message.AutoEllipsis = false;
            banner.AddAction(dismiss);
            banner.AddAction(copyGroup);
            banner.AddAction(installTools);

            Controls.Add(listHost);
            Controls.Add(reviewBar);
            Controls.Add(songsHeader);
            Controls.Add(banner);
            Controls.Add(BuildInputCard());
            Controls.Add(BuildHeader());

            pipeline.Changed += RefreshAll;
            pipeline.NewGroupAdded += group => { if (!newGroups.Contains(group)) newGroups.Add(group); RefreshBanner(); };
            add.Click += delegate { AddFromInput(); };
            loadTxt.Click += delegate { LoadTextFile(); };
            input.KeyDown += (sender, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; AddFromInput(); }
            };
            stop.Click += delegate { pipeline.Stop(); };
            retryAll.Click += delegate { pipeline.RetryFailed(); };
            clear.Click += delegate { pipeline.ClearFinished(); };
            review.Click += delegate { OpenReview(null); };
            installTools.Click += async delegate { await InstallTools(); };
            copyGroup.Click += delegate { if (newGroups.Count > 0) Clipboard.SetText(String.Join(Environment.NewLine, newGroups.ToArray())); };
            dismiss.Click += delegate { newGroups.Clear(); resent = 0; pipeline.AppleMusicProblem = null; message = null; RefreshBanner(); };
            gear.Click += delegate { OpenSettings(); };
            chip.Click += delegate { OpenSettings(); };
            list.RetryRequested += job => pipeline.Retry(job);
            list.RemoveRequested += job => pipeline.Remove(job);
            list.ReviewRequested += job => OpenReview(job);
            list.RelookupRequested += job => pipeline.Relookup(job);
            pipeline.ResentAdded += job => { resent++; RefreshBanner(); };
            list.OpenRequested += job =>
            {
                if (File.Exists(job.File)) Process.Start("explorer.exe", "/select,\"" + job.File + "\"");
            };
            list.CopyRequested += job => Clipboard.SetText(job.Title + "\r\n" + job.Link + "\r\n\r\n" + job.Error + "\r\n\r\n" + job.Hint);
            Shown += delegate { input.Focus(); RefreshAll(); pipeline.Kick(); };
            FormClosing += delegate { pipeline.Shutdown(); };
            RefreshAll();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (iconImage != null) iconImage.Dispose(); matches.Dispose(); }
            base.Dispose(disposing);
        }

        // ---- layout ------------------------------------------------------------------------------------------------

        private Control BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = Theme.S(84), BackColor = Theme.Background };
            var brand = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, BackColor = Theme.Background,
                Padding = new Padding(Theme.S(18), Theme.S(18), 0, 0) };
            brand.Controls.Add(new PictureBox { Image = iconImage, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(Theme.S(44), Theme.S(44)),
                Margin = new Padding(0, 0, Theme.S(14), 0), AccessibleName = AppInfo.Name });
            var words = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false,
                BackColor = Theme.Background, Margin = Padding.Empty };
            words.Controls.Add(new Label { Text = AppInfo.Name, AutoSize = true, Font = new Font("Segoe UI Semibold", 15f),
                ForeColor = Theme.Text, Margin = Padding.Empty, UseMnemonic = false });
            words.Controls.Add(new Label { Text = "Paste a link. The app downloads, converts and tags it, then waits for you to send it to Apple Music.",
                AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, Margin = Padding.Empty, UseMnemonic = false });
            brand.Controls.Add(words);
            var tools = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Theme.Background,
                Padding = new Padding(0, Theme.S(20), Theme.S(12), 0) };
            chip.Margin = new Padding(0, Theme.S(13), Theme.S(12), 0);
            tools.Controls.Add(chip);
            tools.Controls.Add(gear);
            header.Controls.Add(tools);
            header.Controls.Add(brand);
            return header;
        }

        private Control BuildInputCard()
        {
            var card = new Card { Dock = DockStyle.Top, Margin = Padding.Empty };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false,
                BackColor = Theme.Background, Margin = Padding.Empty, Padding = Padding.Empty };
            add.Margin = new Padding(Theme.S(14), 0, 0, Theme.S(6));
            loadTxt.Margin = new Padding(Theme.S(14), 0, 0, 0);
            buttons.Controls.Add(add);
            buttons.Controls.Add(loadTxt);
            var row = Layouts.Row(inputField, buttons);
            card.Fit(row);
            return card;
        }

        private Control BuildSongsHeader()
        {
            var bar = new Panel { Dock = DockStyle.Top, Height = Theme.S(54), BackColor = Theme.Background };
            var left = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, BackColor = Theme.Background,
                Padding = new Padding(Theme.S(26), Theme.S(14), 0, 0) };
            left.Controls.Add(new Label { Text = "Songs", AutoSize = true, Font = Theme.CardTitle, ForeColor = Theme.Text, Margin = Padding.Empty,
                UseMnemonic = false });
            left.Controls.Add(counts);
            var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, BackColor = Theme.Background,
                FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, Theme.S(4), Theme.S(18), 0) };
            foreach (var b in new Control[] { clear, retryAll, stop }) { b.Margin = new Padding(Theme.S(6), 0, 0, 0); right.Controls.Add(b); }
            bar.Controls.Add(right);
            bar.Controls.Add(left);
            return bar;
        }

        // ---- state -------------------------------------------------------------------------------------------------

        private void RefreshAll()
        {
            if (IsDisposed) return;
            list.SetItems(pipeline.Jobs);
            int working = pipeline.WorkingCount, waiting = pipeline.Count(Stage.Review), done = pipeline.Count(Stage.Done);
            int failed = pipeline.Count(Stage.Failed) + pipeline.Count(Stage.Stopped);
            var parts = new List<string>();
            if (working > 0) parts.Add(working + " working");
            if (waiting > 0) parts.Add(waiting + " waiting for you");
            if (done > 0) parts.Add(done + " in Apple Music");
            if (failed > 0) parts.Add(failed + " need a retry");
            counts.Text = message ?? (parts.Count == 0 ? "" : String.Join("  ·  ", parts.ToArray()));
            stop.Visible = pipeline.IsDownloading;
            retryAll.Visible = failed > 0;
            clear.Visible = done > 0;
            reviewBar.Visible = waiting > 0;
            review.Text = "Review & send (" + waiting + ")";
            reviewText.Text = (waiting == 1 ? "1 song is" : waiting + " songs are") + " ready. Check language and genre, then send to Apple Music.";
            var current = pipeline.Jobs.FirstOrDefault(j => j.Stage == Stage.Download && j.Percent >= 0);
            if (current != null) TaskbarProgress.Set(this, current.Percent);
            else TaskbarProgress.Set(this, working > 0 ? -1 : -2);
            RefreshBanner();
        }

        private void RefreshBanner()
        {
            if (ToolManager.IsBusy) return;
            var tools = new DownloadTools();
            chip.Set(tools.Ready ? "Tools ready" : "Setup needed", tools.Ready ? Theme.Pass : Theme.Warn, tools.Ready ? PillShape.Circle : PillShape.Hollow);
            installTools.Visible = copyGroup.Visible = dismiss.Visible = false;
            banner.Progress.Value = -2;
            if (!tools.Ready)
            {
                banner.Show("Setup needed", Theme.Warn, PillShape.Hollow, tools.Missing + (tools.Missing.Contains(" and ") ? " are" : " is") +
                    " missing. Install once and the app keeps them up to date.");
                installTools.Visible = true;
            }
            else if (pipeline.AppleMusicProblem != null)
            {
                banner.Show("Apple Music", Theme.Warn, PillShape.Hollow, pipeline.AppleMusicProblem);
                dismiss.Visible = true;
            }
            else if (resent > 0)
            {
                banner.Show("Updated", Theme.Info, PillShape.Circle, (resent == 1 ? "A corrected copy of 1 song is" : "Corrected copies of " + resent + " songs are") +
                    " in Apple Music. Delete the old copies there (Songs \u203A select the old \u201CUnknown Album\u201D rows \u203A Delete from Library).");
                dismiss.Visible = true;
            }
            else if (newGroups.Count > 0)
            {
                string names = String.Join(", ", newGroups.Select(g => "\u201C" + g + "\u201D").ToArray());
                banner.Show(newGroups.Count == 1 ? "New playlist" : newGroups.Count + " new playlists", Theme.Info, PillShape.Circle,
                    (newGroups.Count == 1 ? names + " is a new group. " : "New groups: " + names + ". ") +
                    "In Apple Music select + next to Playlists \u203A New Smart Playlist \u203A Genre is the group name, with Live updating on. " +
                    "Later songs join by themselves.");
                copyGroup.Text = newGroups.Count == 1 ? "Copy group name" : "Copy group names";
                copyGroup.Visible = dismiss.Visible = true;
            }
            else { banner.Visible = false; return; }
            banner.Visible = true;
        }

        // ---- input -------------------------------------------------------------------------------------------------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.V) && !input.Focused && Clipboard.ContainsText() && !(ActiveControl is TextBoxBase))
            {
                input.Text = Clipboard.GetText();
                AddFromInput();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void AddFromInput()
        {
            string text = input.Text.Trim();
            if (text.Length == 0) { input.Focus(); return; }
            bool looksLikeLinks = text.IndexOf("youtu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  text.IndexOf("http", StringComparison.OrdinalIgnoreCase) >= 0 || text.Contains("\n");
            if (!looksLikeLinks) { Search(text); return; }
            int added = pipeline.Add(VideoLink.Parse(text));
            input.Clear();
            Say(added == 0 ? "Those songs are already in the list." : null);
        }

        private async void Search(string query)
        {
            var tools = new DownloadTools();
            if (!tools.Ready) { RefreshBanner(); return; }
            add.Enabled = false;
            Say("Searching YouTube for “" + query + "”…");
            try
            {
                List<SongMatch> results;
                using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
                    results = await Task.Run(() => AudioDownload.Search(query, tools, cancel.Token));
                if (results.Count == 0) { Say("No matches for “" + query + "”. Try the song and the singer."); return; }
                matches.Items.Clear();
                matches.Items.Add(new ToolStripMenuItem("Pick the right song") { Enabled = false });
                foreach (var match in results.Take(8))
                {
                    var pick = match;
                    string label = match.Title + (String.IsNullOrEmpty(match.Channel) ? "" : "  —  " + match.Channel) +
                        (String.IsNullOrEmpty(match.Duration) ? "" : "  ·  " + match.Duration);
                    matches.Items.Add(label, null, delegate
                    {
                        pipeline.Add(new[] { new VideoLink(pick.Link) }, pick.Title);
                        input.Clear();
                        Say(null);
                    });
                }
                Say(null);
                matches.Show(inputField, new Point(Theme.S(8), inputField.Height));
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException) throw;
                Say("Search failed: " + ex.Message);
            }
            finally { add.Enabled = true; }
        }

        private void LoadTextFile()
        {
            using (var picker = new OpenFileDialog { Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*", Title = "Choose a list of YouTube links" })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    int added = pipeline.Add(VideoLink.Parse(File.ReadAllText(picker.FileName)));
                    Say(added == 0 ? "No new links in that file." : null);
                }
                catch (IOException ex) { Say("Couldn't read the file: " + ex.Message); }
                catch (UnauthorizedAccessException ex) { Say("Couldn't read the file: " + ex.Message); }
            }
        }

        private void Say(string text)
        {
            message = text;
            RefreshAll();
        }

        // ---- review, settings, tools ----------------------------------------------------------------------------

        private void OpenReview(SongJob focus)
        {
            var waiting = pipeline.WaitingForReview;
            if (waiting.Count == 0) return;
            using (var dialog = new ReviewDialog(waiting, pipeline.Jobs, focus))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Accepted.Count > 0)
                    pipeline.Send(dialog.Accepted);
            }
            RefreshAll();
        }

        private void OpenSettings()
        {
            using (var window = new SettingsWindow()) window.ShowDialog(this);
            RefreshAll();
            pipeline.Kick();
        }

        private async Task InstallTools()
        {
            installTools.Enabled = false;
            banner.Show("Installing", Theme.Info, PillShape.Circle, "Downloading audio tools…");
            banner.Visible = true;
            IProgress<ToolProgress> progress = new Progress<ToolProgress>(p =>
            {
                banner.Message.Text = p.Message + (p.Percent >= 0 ? " · " + p.Percent + "%" : "…");
                banner.Progress.Value = p.Percent;
            });
            try
            {
                await ToolManager.InstallAsync(false, progress, CancellationToken.None);
                pipeline.RetryFailed();
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException) throw;
                Say("Tool download failed: " + ex.Message + " Check your connection and try again.");
            }
            finally
            {
                installTools.Enabled = true;
                RefreshAll();
            }
        }
    }

    // Settings in their own window, opened from the gear.
    internal sealed class SettingsWindow : Form
    {
        internal SettingsWindow()
        {
            Text = "Settings · " + AppInfo.Name;
            Icon = Theme.AppIcon();
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Background;
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Theme.S(1080), Theme.S(820));
            MinimumSize = new Size(Theme.S(820), Theme.S(560));
            ShowInTaskbar = false;
            KeyPreview = true;
            HandleCreated += delegate { NativeTheme.DarkTitleBar(this); };
            Load += delegate { Theme.FitToScreen(this); };
            Controls.Add(new SettingsPage());
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    // Tool status at the top right; selecting it opens Settings.
    internal sealed class ToolChip : Button
    {
        private string word = "Tools";
        private Color color = Theme.Na;
        private PillShape shape = PillShape.Circle;

        internal ToolChip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Margin = Padding.Empty;
            Height = Theme.S(24);
        }

        internal void Set(string verdict, Color tone, PillShape mark)
        {
            if (word == verdict && color == tone) return;
            word = verdict; color = tone; shape = mark;
            Text = verdict;
            AccessibleName = "Audio tools: " + verdict + ". Open settings.";
            Width = Theme.PillWidth(word) + 2;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Background);
            Theme.DrawPill(e.Graphics, 0, (Height - Theme.S(20)) / 2, word, color, shape, Theme.Background);
        }
    }

    // A round raised button with one glyph, for the settings gear.
    internal sealed class RoundButton : Button
    {
        private bool hover, pressed;

        internal RoundButton(string glyph)
        {
            Text = glyph;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            Size = new Size(Theme.S(50), Theme.S(50));
            Font = new Font("Segoe UI Symbol", 15f);
            Margin = Padding.Empty;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Background);
            var box = new RectangleF(Theme.S(6), Theme.S(5), Width - Theme.S(12), Height - Theme.S(12));
            if (pressed) Theme.Inset(g, box, box.Width / 2, Theme.Input, 3);
            else Theme.Raised(g, box, box.Width / 2, hover ? Theme.S(6) : Theme.S(5), Theme.Background);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Color.FromArgb(170, Theme.Accent), 1.5f))
                    g.DrawEllipse(pen, box.X - 2, box.Y - 2, box.Width + 4, box.Height + 4);
            TextRenderer.DrawText(g, Text, Font, Rectangle.Round(box), hover ? Theme.Accent2 : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
}
