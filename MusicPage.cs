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
    internal sealed class MusicPage : UserControl
    {
        private readonly ComboBox provider = Combo(false, "Model provider");
        private readonly TextBox endpoint = TextBox("Model server address");
        private readonly ComboBox model = Combo(true, "Model");
        private readonly TextBox apiKey = TextBox("Optional vLLM API key");
        private readonly Label keyLabel = Theme.Label("API key (optional)");
        private readonly TextBox source = TextBox("Source music folder");
        private readonly TextBox output = TextBox("Organized music folder");
        private readonly ComboBox grouping = Combo(false, "Folder grouping");
        private readonly DataGridView grid = new DataGridView();
        private readonly Label status = Theme.Label("Choose a model and a music folder to create a preview.");
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Button loadModels = Theme.Button("Load models", 108);
        private readonly Button preview = Theme.Button("Preview sorting", 132, true);
        private readonly Button apply = Theme.Button("Copy selected", 126);
        private readonly Button cancel = Theme.Button("Cancel", 78);
        private readonly Button show = Theme.Button("Open output", 112);
        private readonly Button browseSource = Theme.Button("Browse", 82);
        private readonly Button browseOutput = Theme.Button("Browse", 82);
        private readonly Button useDownloads = Theme.Button("Use downloads", 125);
        private CancellationTokenSource active;
        public bool IsBusy { get { return active != null; } }
        public event Action WorkComplete;

        public MusicPage(Func<string> downloadsFolder)
        {
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 10);
            Padding = new Padding(28, 11, 28, 14);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8 };
            foreach (int height in new[] { 53, 30, 123, 125, 37 })
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            Controls.Add(layout);
            layout.Controls.Add(Theme.Label("Organize music", 25, true), 0, 0);
            layout.Controls.Add(Theme.Label("Preview folders from song titles and tags. Edit any category before copying."), 0, 1);

            var server = Section(new[] { 28, 26, 36, 30 });
            server.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            server.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
            server.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
            server.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            Span(server, Theme.Label("MODEL SERVER", 10, true), 0, 0, 4);
            server.Controls.Add(Theme.Label("Provider"), 0, 1);
            Span(server, Theme.Label("Server address"), 1, 1, 2);
            server.Controls.Add(provider, 0, 2);
            Span(server, endpoint, 1, 2, 2);
            server.Controls.Add(loadModels, 3, 2);
            server.Controls.Add(Theme.Label("Model"), 0, 3);
            server.Controls.Add(model, 1, 3);
            apiKey.UseSystemPasswordChar = true;
            server.Controls.Add(keyLabel, 2, 3);
            server.Controls.Add(apiKey, 3, 3);
            layout.Controls.Add(server, 0, 2);

            var paths = Section(new[] { 29, 32, 32, 30 });
            paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 87));
            paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            Span(paths, Theme.Label("FOLDERS", 10, true), 0, 0, 4);
            paths.Controls.Add(Theme.Label("Source folder"), 0, 1);
            paths.Controls.Add(source, 1, 1);
            paths.Controls.Add(browseSource, 2, 1);
            paths.Controls.Add(useDownloads, 3, 1);
            paths.Controls.Add(Theme.Label("Copy to"), 0, 2);
            paths.Controls.Add(output, 1, 2);
            paths.Controls.Add(browseOutput, 2, 2);
            paths.Controls.Add(Theme.Label("Group by"), 0, 3);
            paths.Controls.Add(grouping, 1, 3);
            layout.Controls.Add(paths, 0, 3);

            layout.Controls.Add(Theme.Label("Review categories", 13, true), 0, 4);
            BuildGrid();
            layout.Controls.Add(grid, 0, 5);
            var feedback = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2 };
            feedback.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            feedback.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 111));
            status.AutoSize = false;
            status.AutoEllipsis = true;
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.MiddleLeft;
            feedback.Controls.Add(status, 0, 0);
            progress.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            progress.Height = 11;
            feedback.Controls.Add(progress, 1, 0);
            layout.Controls.Add(feedback, 0, 6);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            foreach (var button in new[] { preview, apply, cancel, show }) actions.Controls.Add(button);
            layout.Controls.Add(actions, 0, 7);

            var settings = Preferences.Load();
            provider.Items.AddRange(new[] { LocalModels.Ollama, LocalModels.Vllm });
            provider.SelectedItem = settings.Provider == LocalModels.Vllm ? LocalModels.Vllm : LocalModels.Ollama;
            endpoint.Text = String.IsNullOrWhiteSpace(settings.Endpoint) ? LocalModels.DefaultEndpoint(provider.Text) : settings.Endpoint;
            model.Text = settings.Model ?? "";
            grouping.Items.AddRange(new[] { MusicOrganizer.Both, MusicOrganizer.Genre, MusicOrganizer.Language });
            grouping.SelectedItem = grouping.Items.Contains(settings.Grouping ?? "") ? settings.Grouping : MusicOrganizer.Both;
            source.Text = downloadsFolder();
            output.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Organized Music");
            var tooltips = new ToolTip();
            tooltips.SetToolTip(apiKey, "Optional vLLM API key. The key is kept for this app session.");
            tooltips.SetToolTip(model, "Load installed models or type the exact model name.");
            Disposed += delegate { tooltips.Dispose(); };
            provider.SelectedIndexChanged += delegate
            {
                endpoint.Text = LocalModels.DefaultEndpoint(provider.Text);
                model.Items.Clear();
                model.Text = "";
                apiKey.Text = "";
                apiKey.Visible = keyLabel.Visible = provider.Text == LocalModels.Vllm;
            };
            apiKey.Visible = keyLabel.Visible = provider.Text == LocalModels.Vllm;
            useDownloads.Click += delegate { source.Text = downloadsFolder(); };
            browseSource.Click += delegate { Browse(source); };
            browseOutput.Click += delegate { Browse(output); };
            show.Click += delegate { OpenOutput(); };
            cancel.Click += delegate { CancelWork(); };
            loadModels.Click += async delegate { await Run(LoadModelsAsync); };
            preview.Click += async delegate { await Run(PreviewAsync); };
            apply.Click += async delegate { await Run(CopyAsync); };
            grouping.SelectedIndexChanged += delegate { RefreshFolders(); };
            source.TextChanged += delegate { if (!IsBusy) { grid.Rows.Clear(); UpdateButtons(); } };
            grid.CellValueChanged += delegate(object _, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && (e.ColumnIndex == 2 || e.ColumnIndex == 3))
                    UpdateFolder(grid.Rows[e.RowIndex]);
            };
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            UpdateButtons();
        }

        private static TableLayoutPanel Section(int[] heights)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 4,
                RowCount = heights.Length };
            foreach (int height in heights) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            return panel;
        }

        private static void Span(TableLayoutPanel panel, Control control, int column, int row, int span)
        {
            panel.Controls.Add(control, column, row);
            panel.SetColumnSpan(control, span);
        }

        private static TextBox TextBox(string name)
        {
            var box = new TextBox { AccessibleName = name, Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 0, 8, 0) };
            Theme.StyleTextBox(box);
            return box;
        }

        private static ComboBox Combo(bool editable, string name)
        {
            return new ComboBox { AccessibleName = name, DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface, ForeColor = Theme.Text,
                Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, 8, 0) };
        }

        private void BuildGrid()
        {
            grid.Dock = DockStyle.Fill;
            grid.Margin = Padding.Empty;
            grid.AccessibleName = "Track category preview";
            grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.BackgroundColor = Theme.Surface;
            grid.GridColor = Theme.Border;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.Sidebar;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.Text;
            grid.DefaultCellStyle.BackColor = Theme.Surface;
            grid.DefaultCellStyle.ForeColor = Theme.Text;
            grid.DefaultCellStyle.SelectionBackColor = Theme.Border;
            grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            grid.DefaultCellStyle.NullValue = "";
            grid.RowTemplate.Height = 27;
            grid.ColumnHeadersHeight = 32;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Copy", Width = 49 });
            Column("Track", 35, true);
            Column("Genre", 16, false);
            Column("Language", 15, false);
            Column("Folder", 19, true);
            Column("Result", 15, true);
        }

        private void Column(string title, int weight, bool readOnly)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = title, ReadOnly = readOnly,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        }

        private void Browse(TextBox box)
        {
            using (var picker = new FolderBrowserDialog())
            {
                if (Directory.Exists(box.Text)) picker.SelectedPath = box.Text;
                if (picker.ShowDialog(this) == DialogResult.OK) box.Text = picker.SelectedPath;
            }
        }

        private void OpenOutput()
        {
            try
            {
                string path = MusicOrganizer.FullFolder(output.Text);
                if (Directory.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                else status.Text = "This folder will be created when you copy tracks.";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        }

        private async Task Run(Func<CancellationToken, Task> work)
        {
            if (active != null) return;
            grid.EndEdit();
            active = new CancellationTokenSource();
            UpdateButtons();
            progress.Style = ProgressBarStyle.Marquee;
            try { await work(active.Token); }
            catch (OperationCanceledException) { status.Text = "Cancelled. Completed tracks remain available."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally
            {
                progress.Style = ProgressBarStyle.Blocks;
                active.Dispose();
                active = null;
                UpdateButtons();
                if (WorkComplete != null) WorkComplete();
            }
        }

        public void CancelWork()
        {
            if (active != null)
            {
                cancel.Enabled = false;
                status.Text = "Cancelling...";
                active.Cancel();
            }
        }

        private void UpdateButtons()
        {
            bool ready = !IsBusy;
            foreach (Control item in new Control[] { provider, endpoint, model, apiKey, source, output, grouping,
                loadModels, preview, show, browseSource, browseOutput, useDownloads })
                item.Enabled = ready;
            apply.Enabled = ready && grid.Rows.Count > 0;
            grid.ReadOnly = !ready;
            cancel.Enabled = !ready;
        }

        private LocalModels Client() { return new LocalModels(provider.Text, endpoint.Text, apiKey.Text); }

        private void SaveSettings()
        {
            try { new Preferences { Provider = provider.Text, Endpoint = endpoint.Text, Model = model.Text,
                Grouping = grouping.Text }.Save(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private async Task LoadModelsAsync(CancellationToken token)
        {
            status.Text = "Connecting to " + provider.Text + "...";
            using (var client = Client())
            {
                var names = await client.ListAsync(token);
                var prior = model.Text;
                model.Items.Clear();
                model.Items.AddRange(names);
                model.Text = names.Contains(prior) ? prior : (names.FirstOrDefault() ?? prior);
                status.Text = names.Length == 0 ? "No compatible local models found on this server." : names.Length + " model(s) available.";
                SaveSettings();
            }
        }

        private async Task PreviewAsync(CancellationToken token)
        {
            string selected = model.Text.Trim();
            if (selected.Length == 0) throw new ArgumentException("Choose a model or enter its name first.");
            string from = MusicOrganizer.FullFolder(source.Text);
            string to = MusicOrganizer.FullFolder(output.Text);
            if (!Directory.Exists(from)) throw new DirectoryNotFoundException("The source music folder does not exist.");
            string scheme = grouping.Text;
            using (var client = Client())
            {
                status.Text = "Checking model and music folder...";
                var names = await client.ListAsync(token);
                if (!names.Contains(selected, StringComparer.Ordinal))
                    throw new InvalidOperationException("That model is not served at this address. Select Load models to refresh.");
                var tracks = await Task.Run(() => MusicOrganizer.Scan(from, to, token), token);
                if (tracks.Length == 0) { status.Text = "No supported audio files found."; return; }
                grid.Rows.Clear();
                SaveSettings();
                int complete = 0;
                int errors = 0;
                foreach (string track in tracks)
                {
                    token.ThrowIfCancellationRequested();
                    status.Text = "Classifying " + (complete + errors + 1) + " of " + tracks.Length + " - " + Path.GetFileName(track);
                    Category category = null;
                    string result;
                    try
                    {
                        var metadata = await Task.Run(() => TrackMetadata.Read(track), token);
                        category = await client.ClassifyAsync(selected, metadata, token);
                        result = "Ready";
                        complete++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { result = ex.Message; errors++; }

                    int index = grid.Rows.Add(category != null, Path.GetFileName(track),
                        category == null ? Category.Unknown : category.Genre,
                        category == null ? Category.Unknown : category.Language,
                        category == null ? "" : MusicOrganizer.RelativeFolder(category, scheme), result);
                    var row = grid.Rows[index];
                    row.Tag = track;
                    row.Cells[1].ToolTipText = track;
                    row.Cells[5].ToolTipText = result;
                    Percent(complete + errors, tracks.Length);
                }
                status.Text = complete + " ready, " + errors + " failed. Review the rows before copying.";
            }
        }

        private void RefreshFolders()
        {
            foreach (DataGridViewRow row in grid.Rows) UpdateFolder(row);
        }

        private void UpdateFolder(DataGridViewRow row)
        {
            var category = RowCategory(row);
            row.Cells[4].Value = MusicOrganizer.RelativeFolder(category, grouping.Text);
        }

        private static Category RowCategory(DataGridViewRow row)
        {
            return new Category(Convert.ToString(row.Cells[2].Value), Convert.ToString(row.Cells[3].Value));
        }

        private async Task CopyAsync(CancellationToken token)
        {
            string to = MusicOrganizer.FullFolder(output.Text);
            var selected = new List<DataGridViewRow>();
            foreach (DataGridViewRow row in grid.Rows)
                if (Object.Equals(row.Cells[0].Value, true)) selected.Add(row);
            if (selected.Count == 0) throw new ArgumentException("Check the tracks you want to copy.");
            int copied = 0;
            int existing = 0;
            int failed = 0;
            string scheme = grouping.Text;
            for (int i = 0; i < selected.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var row = selected[i];
                string path = (string)row.Tag;
                string target = MusicOrganizer.RelativeFolder(RowCategory(row), scheme);
                row.Cells[4].Value = target;
                status.Text = "Copying " + (i + 1) + " of " + selected.Count + " - " + Path.GetFileName(path);
                string result;
                try
                {
                    bool added = await Task.Run(() => MusicOrganizer.Copy(path, to, target, token), token);
                    result = added ? "Copied" : "Already exists";
                    if (added) copied++; else existing++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { result = ex.Message; failed++; }
                row.Cells[5].Value = result;
                row.Cells[5].ToolTipText = result;
                Percent(i + 1, selected.Count);
            }
            SaveSettings();
            status.Text = copied + " copied, " + existing + " already present, " + failed + " failed.";
        }

        private void Percent(int done, int total)
        {
            progress.Style = ProgressBarStyle.Blocks;
            progress.Value = (int)(100L * done / total);
        }
    }
}
