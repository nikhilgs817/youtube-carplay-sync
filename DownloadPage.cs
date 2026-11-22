using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace YouTubeMp3
{
    internal sealed class DownloadPage : UserControl
    {
        private static readonly Color Ink = Theme.Text;
        private static readonly Color Muted = Theme.Muted;
        private static readonly Color Paper = Theme.Background;
        private readonly TextBox links = new TextBox();
        private readonly TextBox folder = new TextBox();
        private readonly ListView results = new ListView();
        private readonly Label status = TextLabel("", 10, FontStyle.Regular, Muted);
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Button load = ActionButton("Load .txt", 105);
        private readonly Button browse = ActionButton("Browse…", 105);
        private readonly Button setup = ActionButton("Setup tools", 112);
        private readonly Button open = ActionButton("Open folder", 115);
        private readonly Button cancel = ActionButton("Cancel", 90);
        private readonly Button start = ActionButton("Save MP3", 118, true);
        private CancellationTokenSource active;
        private Process installer;
        public event Action DownloadComplete;
        public bool IsBusy { get { return active != null; } }
        public string OutputFolder { get { return folder.Text; } }

        public DownloadPage()
        {
            Dock = DockStyle.Fill;
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Paper;
            ForeColor = Ink;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            Controls.Add(layout);

            var hero = new Panel { Dock = DockStyle.Fill, BackColor = Paper, Margin = Padding.Empty };
            var title = TextLabel("Download audio", 25, FontStyle.Bold, Ink);
            title.Location = new Point(27, 12);
            hero.Controls.Add(title);
            var subtitle = TextLabel("Save a video or a list of links as MP3 files.", 10, FontStyle.Regular, Muted);
            subtitle.Location = new Point(30, 61);
            hero.Controls.Add(subtitle);
            layout.Controls.Add(hero, 0, 0);

            var input = Section(3);
            input.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
            input.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            input.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            input.Controls.Add(TextLabel("YouTube links", 13, FontStyle.Bold, Ink), 0, 0);
            input.Controls.Add(load, 1, 0);
            var hint = TextLabel("Paste a URL or multiple links, one per line.", 10, FontStyle.Regular, Muted);
            input.SetColumnSpan(hint, 2);
            input.Controls.Add(hint, 0, 1);
            links.Multiline = true;
            links.AcceptsReturn = true;
            links.WordWrap = false;
            links.ScrollBars = ScrollBars.Both;
            links.MaxLength = 0;
            links.Dock = DockStyle.Fill;
            links.Margin = Padding.Empty;
            links.AccessibleName = "YouTube links, one URL per line";
            Theme.StyleTextBox(links);
            input.SetColumnSpan(links, 2);
            input.Controls.Add(links, 0, 2);
            layout.Controls.Add(input, 0, 1);

            var destination = Section(2);
            destination.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            destination.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            destination.Controls.Add(TextLabel("Save to", 13, FontStyle.Bold, Ink), 0, 0);
            destination.SetColumnSpan(destination.GetControlFromPosition(0, 0), 2);
            folder.Text = Theme.DefaultMusicFolder;
            folder.AccessibleName = "Output folder";
            Theme.StyleTextBox(folder);
            folder.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            folder.Margin = new Padding(0, 0, 9, 0);
            destination.Controls.Add(folder, 0, 1);
            destination.Controls.Add(browse, 1, 1);
            layout.Controls.Add(destination, 0, 2);

            var queue = Section(2);
            queue.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            queue.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            queue.Controls.Add(TextLabel("Results", 13, FontStyle.Bold, Ink), 0, 0);
            queue.SetColumnSpan(queue.GetControlFromPosition(0, 0), 2);
            results.View = View.Details;
            results.FullRowSelect = true;
            results.MultiSelect = false;
            results.Dock = DockStyle.Fill;
            results.Margin = Padding.Empty;
            results.AccessibleName = "Download results";
            results.BackColor = Theme.Surface;
            results.ForeColor = Ink;
            results.ShowItemToolTips = true;
            results.Columns.Add("Link or file", 380);
            results.Columns.Add("Status", 200);
            results.SizeChanged += delegate { ResizeColumns(); };
            queue.SetColumnSpan(results, 2);
            queue.Controls.Add(results, 0, 1);
            layout.Controls.Add(queue, 0, 3);

            var footer = Section(2);
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            status.AutoEllipsis = true;
            status.AutoSize = false;
            status.Dock = DockStyle.Fill;
            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(0, 6, 0, 9);
            footer.Controls.Add(status, 0, 0);
            footer.Controls.Add(progress, 1, 0);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            foreach (Button button in new[] { start, cancel, open, setup }) actions.Controls.Add(button);
            footer.SetColumnSpan(actions, 2);
            footer.Controls.Add(actions, 0, 1);
            layout.Controls.Add(footer, 0, 4);

            load.Click += delegate { LoadFile(); };
            browse.Click += delegate { ChooseFolder(); };
            setup.Click += delegate { SetupTools(); };
            open.Click += delegate { OpenFolder(); };
            start.Click += async delegate { await SaveBatch(); };
            cancel.Click += delegate { CancelBatch(); };
            SetBusy(false);
            ResizeColumns();
            CheckTools();
        }

        private static TableLayoutPanel Section(int rows)
        {
            var section = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = rows,
                Margin = new Padding(28, 9, 28, 0)
            };
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            return section;
        }

        private static Label TextLabel(string text, float size, FontStyle style, Color color)
        {
            return new Label
            {
                Text = text, AutoSize = true, ForeColor = color,
                Font = new Font("Segoe UI", size, style), Margin = Padding.Empty,
                Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Button ActionButton(string text, int width, bool primary = false)
        {
            var button = Theme.Button(text, width, primary);
            button.Anchor = AnchorStyles.Right;
            return button;
        }

        private void ResizeColumns()
        {
            if (results.Columns.Count != 2 || results.ClientSize.Width < 60) return;
            int width = results.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            results.Columns[0].Width = width * 52 / 100;
            results.Columns[1].Width = width - results.Columns[0].Width;
        }

        private void LoadFile()
        {
            using (var picker = new OpenFileDialog { Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*" })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    links.Text = File.ReadAllText(picker.FileName);
                    status.Text = "Loaded " + Path.GetFileName(picker.FileName);
                }
                catch (IOException ex) { status.Text = ex.Message; }
                catch (UnauthorizedAccessException ex) { status.Text = ex.Message; }
            }
        }

        private void ChooseFolder()
        {
            using (var picker = new FolderBrowserDialog { Description = "Choose where to save your MP3 files" })
            {
                if (Directory.Exists(folder.Text)) picker.SelectedPath = folder.Text;
                if (picker.ShowDialog(this) == DialogResult.OK) folder.Text = picker.SelectedPath;
            }
        }

        private void OpenFolder()
        {
            try
            {
                string path = Destination();
                if (Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                else status.Text = "The folder will be created when you save an MP3.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        }

        private string Destination()
        {
            string path = Environment.ExpandEnvironmentVariables(folder.Text.Trim().Trim('"'));
            if (path.Length == 0 || !Path.IsPathRooted(path) ||
                (!path.StartsWith(@"\\", StringComparison.Ordinal) && (path.Length < 3 || path[1] != ':' || path[2] != '\\')))
                throw new ArgumentException("Choose a full Windows folder path.");
            return Path.GetFullPath(path);
        }

        private void SetBusy(bool busy)
        {
            links.ReadOnly = folder.ReadOnly = busy;
            load.Enabled = browse.Enabled = setup.Enabled = start.Enabled = !busy && installer == null;
            open.Enabled = !busy;
            cancel.Enabled = busy;
        }

        private static bool Ready(DownloadTools tools)
        {
            return tools.Downloader != null && tools.Encoder != null && tools.JsRuntime != null;
        }

        private void CheckTools()
        {
            status.Text = Ready(new DownloadTools()) ? "Ready" : "First run? Select Setup tools to install the audio utilities.";
        }

        private async void SetupTools()
        {
            string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "setup-tools.ps1");
            if (!File.Exists(script))
            {
                status.Text = "Extract the full app ZIP, then select Setup tools.";
                return;
            }
            try
            {
                string powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                installer = Process.Start(new ProcessStartInfo(powershell,
                    "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"") { UseShellExecute = false });
                SetBusy(false);
                status.Text = "Setup is running in a separate window…";
                await Task.Run(() => installer.WaitForExit());
                if (!IsDisposed) CheckTools();
            }
            catch (Exception ex) { if (!IsDisposed) status.Text = ex.Message; }
            finally
            {
                if (installer != null) installer.Dispose();
                installer = null;
                if (!IsDisposed) SetBusy(false);
            }
        }

        private async Task SaveBatch()
        {
            var batch = VideoLink.Parse(links.Text);
            if (batch.Count == 0)
            {
                status.Text = "Paste a YouTube link or load a .txt file first.";
                links.Focus();
                return;
            }
            var tools = new DownloadTools();
            bool hasValidLinks = batch.Exists(link => link.Id != null);
            if (hasValidLinks && !Ready(tools))
            {
                CheckTools();
                return;
            }
            string path = null;
            if (hasValidLinks)
            {
                try { path = Destination(); Directory.CreateDirectory(path); }
                catch (Exception ex) { status.Text = ex.Message; return; }
            }

            active = new CancellationTokenSource();
            var token = active.Token;
            results.Items.Clear();
            foreach (VideoLink link in batch) results.Items.Add(new ListViewItem(new[] { link.Original, "Waiting" }));
            SetBusy(true);
            int saved = 0;
            int failed = 0;
            int cancelled = 0;
            try
            {
                for (int i = 0; i < batch.Count; i++)
                {
                    ListViewItem row = results.Items[i];
                    if (token.IsCancellationRequested)
                    {
                        row.SubItems[1].Text = "Cancelled";
                        cancelled++;
                        continue;
                    }
                    VideoLink link = batch[i];
                    if (link.Id == null)
                    {
                        row.SubItems[1].Text = "Enter a YouTube video URL";
                        failed++;
                        continue;
                    }
                    string step = (i + 1) + " of " + batch.Count + " · ";
                    row.SubItems[1].Text = "Connecting…";
                    row.EnsureVisible();
                    status.Text = step + "Connecting…";
                    progress.Style = ProgressBarStyle.Marquee;
                    bool downloading = true;
                    IProgress<Tuple<int, string>> onProgress = new Progress<Tuple<int, string>>(value =>
                    {
                        if (!downloading || token.IsCancellationRequested) return;
                        progress.Style = value.Item1 == 100 ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
                        progress.Value = value.Item1;
                        status.Text = step + value.Item2;
                        row.SubItems[1].Text = value.Item2;
                    });
                    try
                    {
                        string output = await Task.Run(() => AudioDownload.Save(link, path, tools,
                            (percent, message) => onProgress.Report(Tuple.Create(percent, message)), token));
                        row.Text = Path.GetFileName(output);
                        row.SubItems[1].Text = "Saved MP3";
                        saved++;
                    }
                    catch (OperationCanceledException)
                    {
                        row.SubItems[1].Text = "Cancelled";
                        cancelled++;
                    }
                    catch (Exception ex)
                    {
                        row.SubItems[1].Text = ex.Message;
                        row.ToolTipText = ex.Message;
                        failed++;
                    }
                    finally { downloading = false; }
                }
                status.Text = saved + " saved  ·  " + failed + " failed" + (cancelled == 0 ? "" : "  ·  " + cancelled + " cancelled");
            }
            finally
            {
                active.Dispose();
                active = null;
                progress.Style = ProgressBarStyle.Blocks;
                progress.Value = saved > 0 ? 100 : 0;
                SetBusy(false);
                if (DownloadComplete != null) DownloadComplete();
            }
        }

        public void CancelBatch()
        {
            if (active == null || !cancel.Enabled) return;
            cancel.Enabled = false;
            status.Text = "Cancelling…";
            active.Cancel();
        }

    }
}
