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
    // Settings: audio tools, download options, how AI is connected, the default playlist and Apple Music.
    internal sealed class SettingsPage : UserControl
    {
        private readonly ToolStatusView toolList = new ToolStatusView();
        private readonly PillButton install = Theme.Button("Install tools", 120, true);
        private readonly PillButton update = Theme.Button("Update yt-dlp", 120);
        private readonly PillButton openTools = Theme.Button("Open tools folder", 140);
        private readonly Toggle autoUpdate = new Toggle("Keep yt-dlp up to date automatically");
        private readonly ProgressLine toolProgress = new ProgressLine();
        private readonly Label toolStatus = Layouts.Note("");

        private readonly Toggle artwork = new Toggle("Embed cover art in each MP3");
        private readonly Segmented cookies = new Segmented("Off", "Firefox");

        // AI, in three steps: how to connect, what that connection needs, then which model.
        private readonly ComboBox aiWay = new ComboBox { AccessibleName = "How to connect AI", DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox endpoint = new TextBox { AccessibleName = "Model server address" };
        private readonly TextBox apiKey = new TextBox { AccessibleName = "API key", UseSystemPasswordChar = true };
        private readonly ComboBox model = new ComboBox { AccessibleName = "Model", DropDownStyle = ComboBoxStyle.DropDown };
        private readonly Segmented timeout = new Segmented("60 s", "120 s", "300 s");
        private readonly PillButton loadModels = Theme.Button("Test & load models", 150);
        private readonly Label aiStatus = Layouts.Note("Not tested yet.");
        private readonly Label endpointLabel = Theme.CapsLabel("Server address");
        private readonly Label keyLabel = Theme.CapsLabel("API key");
        private readonly Label keyHelp = Layouts.Note("");
        private readonly Label modelLabel = Theme.CapsLabel("Model");
        private readonly Label timeoutLabel = Theme.CapsLabel("Wait for an answer up to");
        private readonly Label aiNote = Layouts.Note("");
        private Control endpointRow, keyRow, modelRow;
        private string shownProvider;

        private readonly TextBox defaultPlaylist = new TextBox { AccessibleName = "Default Apple Music playlist" };
        private readonly PillButton copyPlaylist = Theme.Button("Copy name", 110);
        private readonly Label playlistNote = Layouts.Note("");

        private readonly TextBox musicFolder = new TextBox { AccessibleName = "Folder where downloaded songs are saved" };
        private readonly PillButton browseMusic = Theme.Button("Browse…", 100);
        private readonly Segmented quality = new Segmented(AudioDownload.Qualities);
        private readonly TextBox appleFolder = new TextBox { AccessibleName = "Apple Music Automatically Add folder" };
        private readonly PillButton browseApple = Theme.Button("Browse…", 100);
        private readonly PillButton findApple = Theme.Button("Find it", 100);
        private readonly Label appleStatus = Layouts.Note("");
        private readonly Toggle removeCopy = new Toggle("Remove the app's copy once Apple Music has the song");
        private readonly TextBox webKey = new TextBox { AccessibleName = "Ollama web search key (optional)", UseSystemPasswordChar = true };
        private readonly Label webStatus = Layouts.Note("");
        private bool loading;
        internal event Action ToolsChanged;

        internal SettingsPage()
        {
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            AutoScroll = true;
            Padding = new Padding(Theme.S(28), Theme.S(22), Theme.S(28), Theme.S(14));
            NativeTheme.DarkScrollbars(this);

            var columns = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            var left = Layouts.Stack();
            var right = Layouts.Stack();
            left.Dock = right.Dock = DockStyle.Fill;
            left.Padding = new Padding(0, 0, Theme.S(7), 0);
            right.Padding = new Padding(Theme.S(7), 0, 0, 0);
            left.Anchor = right.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            columns.Controls.Add(left, 0, 0);
            columns.Controls.Add(right, 1, 0);
            Layouts.Add(left, ToolsCard(), true, 14);
            Layouts.Add(left, DownloadsCard(), true, 14);
            Layouts.Add(right, AiCard(), true, 14);
            Layouts.Add(right, AppleMusicCard(), true, 14);
            Controls.Add(columns);
            var header = Layouts.Header("Settings", "Saved on this PC. Your music never leaves it; with a cloud AI, only song names go to that service.");
            var info = RoundButton.Info();
            info.Click += delegate { InfoWindow.Open(FindForm()); };
            header.Dock = DockStyle.None;
            var top = Layouts.Row(header, info);
            top.Dock = DockStyle.Top;
            info.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            Controls.Add(top);
            endpointRow = endpoint.Parent;
            keyRow = apiKey.Parent;

            LoadValues();
            install.Click += async delegate { await RunInstall(!new DownloadTools().Ready ? false : true); };
            update.Click += async delegate { await RunUpdate(); };
            openTools.Click += delegate
            {
                Directory.CreateDirectory(DownloadTools.ManagedFolder);
                try { Process.Start(new ProcessStartInfo(DownloadTools.ManagedFolder) { UseShellExecute = true }); }
                catch (Exception ex) { toolStatus.Text = ex.Message; }
            };
            autoUpdate.CheckedChanged += delegate { if (!loading) Preferences.Update(p => p.AutoUpdate = autoUpdate.Checked); };
            artwork.CheckedChanged += delegate { if (!loading) Preferences.Update(p => p.Artwork = artwork.Checked); };
            cookies.SelectedIndexChanged += delegate
            {
                if (!loading) Preferences.Update(p => p.CookiesBrowser = cookies.SelectedIndex == 1 ? "firefox" : "");
            };
            aiWay.SelectedIndexChanged += delegate
            {
                if (loading) return;
                string chosen = Chosen;
                Preferences.Update(p => { p.AiProvider = chosen; p.AiEnabled = chosen != AiProviders.None; });
                ShowProvider(Preferences.Load());
                aiStatus.Text = "Not tested yet.";
                aiStatus.ForeColor = Theme.Muted;
            };
            endpoint.LostFocus += delegate { SaveAi(); };
            model.LostFocus += delegate { SaveAi(); };
            model.SelectedIndexChanged += delegate { SaveAi(); };
            // A key is saved, encrypted, as soon as it's pasted, so closing Settings straight away keeps it.
            apiKey.TextChanged += delegate
            {
                if (loading || shownProvider == null) return;
                string which = shownProvider, key = apiKey.Text.Trim();
                if (key == Preferences.Load().GetKey(which)) return;
                Preferences.Update(p => p.SetKey(which, key));
                ShowKeyStatus();
            };
            timeout.SelectedIndexChanged += delegate { if (!loading) SaveAi(); };
            defaultPlaylist.TextChanged += delegate
            {
                if (loading) return;
                string name = defaultPlaylist.Text.Trim();
                Preferences.Update(p => p.DefaultPlaylist = name);
                ShowPlaylistNote();
            };
            defaultPlaylist.LostFocus += delegate { string name = defaultPlaylist.Text.Trim(); Preferences.Update(p => p.RememberPlaylist(name)); };
            copyPlaylist.Click += delegate { if (defaultPlaylist.Text.Trim().Length > 0) Clipboard.SetText(defaultPlaylist.Text.Trim()); };
            loadModels.Click += async delegate { await TestModels(); };
            browseMusic.Click += delegate
            {
                using (var picker = new FolderBrowserDialog { Description = "Choose where downloaded songs are saved" })
                {
                    if (Directory.Exists(musicFolder.Text)) picker.SelectedPath = musicFolder.Text;
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    musicFolder.Text = picker.SelectedPath;
                    SaveFolders();
                }
            };
            musicFolder.LostFocus += delegate { SaveFolders(); };
            quality.SelectedIndexChanged += delegate { if (!loading) Preferences.Update(p => p.Quality = quality.SelectedText); };
            browseApple.Click += delegate
            {
                using (var picker = new FolderBrowserDialog { Description = "Choose Apple Music's \"Automatically Add to Apple Music\" folder" })
                {
                    if (Directory.Exists(appleFolder.Text)) picker.SelectedPath = appleFolder.Text;
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    appleFolder.Text = picker.SelectedPath;
                    SaveFolders();
                    ShowAppleStatus();
                }
            };
            appleFolder.LostFocus += delegate { SaveFolders(); ShowAppleStatus(); };
            removeCopy.CheckedChanged += delegate { if (!loading) Preferences.Update(p => p.RemoveAfterImport = removeCopy.Checked); };
            // Saved on every change, so a pasted key is kept even if Settings is closed straight away.
            EventHandler saveKey = delegate
            {
                if (loading) return;
                string key = webKey.Text.Trim();
                if (key == Preferences.Load().WebKey) return;
                Preferences.Update(p => p.WebKey = key);
                ShowWebStatus();
            };
            webKey.TextChanged += saveKey;
            webKey.LostFocus += saveKey;
            findApple.Click += delegate
            {
                Preferences.Update(p => p.AppleMusicFolder = "");
                appleFolder.Text = "";
                ShowAppleStatus();
            };
            VisibleChanged += async delegate { if (Visible) await RefreshTools(); };
        }

        private void LoadValues()
        {
            loading = true;
            var prefs = Preferences.Load();
            autoUpdate.Checked = prefs.AutoUpdateTools;
            artwork.Checked = prefs.UseArtwork;
            cookies.SelectedIndex = String.Equals(prefs.CookiesBrowser, "firefox", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            aiWay.SelectedIndex = Math.Max(0, Array.IndexOf(AiProviders.All, prefs.ActiveProvider));
            timeout.SelectedIndex = prefs.Timeout <= 60 ? 0 : prefs.Timeout <= 120 ? 1 : 2;
            defaultPlaylist.Text = prefs.Playlist;
            musicFolder.Text = String.IsNullOrWhiteSpace(prefs.DownloadFolder) ? Theme.DefaultMusicFolder : prefs.DownloadFolder;
            quality.Select(String.IsNullOrEmpty(prefs.Quality) ? "Best" : prefs.Quality);
            appleFolder.Text = prefs.AppleMusicFolder ?? "";
            removeCopy.Checked = prefs.RemoveCopy;
            webKey.Text = prefs.WebKey;
            loading = false;
            ShowProvider(prefs);
            ShowPlaylistNote();
            ShowAppleStatus();
            ShowWebStatus();
        }

        private void ShowWebStatus()
        {
            bool set = webKey.Text.Trim().Length > 0;
            webStatus.Text = set ? "Key saved, encrypted for your Windows account. Used only for songs the music catalogues don't find."
                : "Optional. With a free Ollama account key, songs the catalogues don't find are also looked up on the web.";
            webStatus.ForeColor = set ? Theme.Pass : Theme.Muted;
        }

        private void ShowAppleStatus()
        {
            string found = AppleMusic.AutoAddFolder();
            bool chosen = appleFolder.Text.Trim().Length > 0;
            if (found == null)
            {
                appleStatus.Text = chosen ? "That folder doesn't exist." : "Not found yet. Open Apple Music once so it creates its Media folder, then select Find it.";
                appleStatus.ForeColor = Theme.Fail;
            }
            else
            {
                appleStatus.Text = (chosen ? "Using: " : "Found: ") + found;
                appleStatus.ForeColor = Theme.Pass;
            }
        }

        private string Chosen { get { return AiProviders.All[Math.Max(0, aiWay.SelectedIndex)]; } }

        private void SaveAi()
        {
            if (loading || shownProvider == null) return;
            string p = shownProvider, e = endpoint.Text.Trim(), m = model.Text.Trim();
            int seconds = timeout.SelectedIndex == 0 ? 60 : timeout.SelectedIndex == 1 ? 120 : 300;
            Preferences.Update(x =>
            {
                x.AiTimeout = seconds;
                if (p == AiProviders.None) return;
                x.SetModel(p, m);
                if (AiProviders.IsLocal(p)) { x.SetEndpoint(p, e); x.Provider = p; x.Endpoint = e; x.Model = m; }
            });
        }

        // Fill the second and third steps for the chosen way to connect, and show only what it needs.
        private void ShowProvider(Preferences prefs)
        {
            loading = true;
            string p = Chosen;
            shownProvider = p;
            bool none = p == AiProviders.None, local = AiProviders.IsLocal(p), cloud = AiProviders.IsCloud(p);
            endpoint.Text = local ? prefs.EndpointFor(p) : "";
            apiKey.Text = cloud || p == AiProviders.Vllm ? prefs.GetKey(p) : "";
            model.Items.Clear();
            model.Items.AddRange(AiProviders.SuggestedModels(p));
            string saved = prefs.ModelFor(p);
            model.Text = saved.Length > 0 ? saved : (cloud && model.Items.Count > 0 ? Convert.ToString(model.Items[0]) : "");
            loading = false;
            if (cloud && saved.Length == 0 && model.Text.Length > 0) SaveAi();

            endpointLabel.Visible = endpointRow.Visible = local;
            keyLabel.Visible = keyRow.Visible = keyHelp.Visible = cloud || p == AiProviders.Vllm;
            // Numbered as the three steps: how to connect, what that needs, the model.
            endpointLabel.Text = "2 · Server address".ToUpperInvariant();
            keyLabel.Text = (p == AiProviders.Vllm ? "API key (optional)" : "2 · " + p + " API key").ToUpperInvariant();
            apiKey.AccessibleName = p == AiProviders.Vllm ? "vLLM API key (optional)" : p + " API key";
            modelLabel.Text = "3 · Model".ToUpperInvariant();
            keyHelp.Text = AiProviders.KeyHelp(p);
            modelLabel.Visible = modelRow.Visible = aiStatus.Visible = timeoutLabel.Visible = timeout.Visible = !none;
            if (cloud)
                aiNote.Text = "For each song, " + p + " gets only its YouTube title, channel, a short part of the description and the catalogue " +
                    "matches. No audio, files or anything else from this PC. Each song is one small request on your " + p + " account." +
                    (p == AiProviders.Claude ? " Claude Opus and Sonnet retry a declined answer on a fallback model automatically." : "");
            else if (local)
                aiNote.Text = "Runs on this PC or your own server. Good models for Indian-language titles: qwen3:30b-a3b or gemma3:27b.";
            else
                aiNote.Text = RuleTagger.LowAccuracy + ". Language and genre come from the script of the title, words like bhajan " +
                    "or remix, well-known labels and the catalogue's genre. Check them in Review tags.";
            aiNote.ForeColor = none ? Theme.Warn : Theme.Muted;
            ShowKeyStatus();
        }

        private void ShowKeyStatus()
        {
            if (!AiProviders.IsCloud(shownProvider)) return;
            bool set = apiKey.Text.Trim().Length > 0;
            aiStatus.ForeColor = set ? Theme.Pass : Theme.Warn;
            aiStatus.Text = set ? "Key saved, encrypted for your Windows account. Select Test & load models to check it."
                : "Paste your " + shownProvider + " API key to use it.";
        }

        private void ShowPlaylistNote()
        {
            string name = defaultPlaylist.Text.Trim();
            playlistNote.Text = name.Length == 0
                ? "No default playlist: songs go to your library only. You can still choose a playlist per song in Review tags."
                : "Songs go to \u201C" + name + "\u201D unless you change it in Review tags. Once per playlist name, in Apple Music select + next to " +
                  "Playlists \u203A New Smart Playlist, set the rule Comments is " + name + ", tick Live updating and name it " + name + ". " +
                  "Every song sent with that name then joins it by itself.";
        }

        private void SaveFolders()
        {
            if (loading) return;
            string music = musicFolder.Text.Trim(), apple = appleFolder.Text.Trim();
            Preferences.Update(x => { x.DownloadFolder = music; x.AppleMusicFolder = apple; });
        }

        // ---- cards --------------------------------------------------------------------------------------------

        private Card ToolsCard()
        {
            var card = new Card("Audio tools");
            var stack = Layouts.Stack();
            Layouts.Add(stack, Layouts.Note("yt-dlp downloads the audio, FFmpeg converts it to MP3, and Deno helps yt-dlp read YouTube pages. " +
                "The app keeps its own copies, separate from anything else on this PC."), true, 12);
            Layouts.Add(stack, toolList, true, 8);
            Layouts.Add(stack, autoUpdate, false, 8);
            Layouts.Add(stack, Layouts.Buttons(install, update, openTools), true, 6);
            toolProgress.Value = -2;
            Layouts.Add(stack, toolProgress, true, 6);
            Layouts.Add(stack, toolStatus, true, 0);
            card.Fit(stack);
            return card;
        }

        private Card DownloadsCard()
        {
            var card = new Card("Downloads");
            var stack = Layouts.Stack();
            Layouts.Add(stack, Theme.CapsLabel("Save songs to"), true, 0);
            Layouts.Add(stack, Layouts.Row(Theme.Field(musicFolder), browseMusic), true, 10);
            Layouts.Add(stack, Theme.CapsLabel("MP3 quality"), true, 0);
            Layouts.Add(stack, quality, false, 10);
            Layouts.Add(stack, artwork, false, 10);
            Layouts.Add(stack, Theme.CapsLabel("Use YouTube sign-in from"), true, 0);
            Layouts.Add(stack, cookies, false, 6);
            Layouts.Add(stack, Layouts.Note("Only needed when YouTube asks you to sign in or to confirm you're not a bot. " +
                "Sign in to YouTube in Firefox first. Your cookies are read locally by yt-dlp and never stored by this app."), true, 0);
            card.Fit(stack);
            return card;
        }

        private Card AiCard()
        {
            var card = new Card("AI for song details");
            var stack = Layouts.Stack();
            Layouts.Add(stack, Layouts.Note("Each song is looked up in Apple's music catalogue and MusicBrainz. AI picks the right match and adds " +
                "language and genre. Every song still waits for you in Review tags before it goes to Apple Music."), true, 12);
            Layouts.Add(stack, Theme.CapsLabel("1 · How to connect AI"), true, 0);
            StyleCombo(aiWay);
            foreach (var p in AiProviders.All) aiWay.Items.Add(AiProviders.Label(p));
            Layouts.Add(stack, aiWay, true, 10);
            Layouts.Add(stack, endpointLabel, true, 0);
            Layouts.Add(stack, Theme.Field(endpoint), true, 10);
            Layouts.Add(stack, keyLabel, true, 0);
            Layouts.Add(stack, Theme.Field(apiKey), true, 4);
            Layouts.Add(stack, keyHelp, true, 10);
            Layouts.Add(stack, modelLabel, true, 0);
            StyleCombo(model);
            modelRow = Layouts.Row(model, loadModels);
            Layouts.Add(stack, modelRow, true, 6);
            Layouts.Add(stack, aiStatus, true, 10);
            Layouts.Add(stack, timeoutLabel, true, 0);
            Layouts.Add(stack, timeout, false, 10);
            Layouts.Add(stack, aiNote, true, 12);
            Layouts.Add(stack, Theme.CapsLabel("Ollama web search key (optional)"), true, 0);
            Layouts.Add(stack, Theme.Field(webKey), true, 6);
            Layouts.Add(stack, webStatus, true, 0);
            card.Fit(stack);
            return card;
        }

        private Card AppleMusicCard()
        {
            var card = new Card("Apple Music");
            var stack = Layouts.Stack();
            Layouts.Add(stack, Theme.CapsLabel("Default playlist"), true, 0);
            Layouts.Add(stack, Layouts.Row(Theme.Field(defaultPlaylist, "For example CarPlay"), copyPlaylist), true, 6);
            Layouts.Add(stack, playlistNote, true, 14);
            Layouts.Add(stack, Layouts.Note("When you send songs, the app copies each tagged MP3 into Apple Music's \"Automatically Add to Apple Music\" " +
                "folder. Apple Music imports it from there and the app confirms it arrived. Leave the box empty to find the folder automatically."), true, 12);
            Layouts.Add(stack, Theme.CapsLabel("Automatically Add folder"), true, 0);
            Layouts.Add(stack, Layouts.Row(Theme.Field(appleFolder), browseApple, findApple), true, 8);
            Layouts.Add(stack, appleStatus, true, 12);
            Layouts.Add(stack, removeCopy, false, 4);
            Layouts.Add(stack, Layouts.Note("Apple Music keeps its own copy in its Media folder, sorted by artist and album. With this on, the app's " +
                "copy in the save folder is removed after Apple Music confirms the song, so each song is stored once."), true, 0);
            card.Fit(stack);
            return card;
        }

        private static void StyleCombo(ComboBox combo)
        {
            combo.FlatStyle = FlatStyle.Flat;
            combo.BackColor = Theme.Input;
            combo.ForeColor = Theme.Text;
            combo.Font = Theme.Body;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = Theme.S(26);
            combo.DrawItem += delegate(object sender, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                bool chosen = (e.State & DrawItemState.Selected) != 0;
                using (var back = new SolidBrush(chosen ? Theme.Hover : Theme.Panel2)) e.Graphics.FillRectangle(back, e.Bounds);
                TextRenderer.DrawText(e.Graphics, Convert.ToString(combo.Items[e.Index]), Theme.Body,
                    new Rectangle(e.Bounds.X + Theme.S(6), e.Bounds.Y, e.Bounds.Width - Theme.S(6), e.Bounds.Height),
                    Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            };
        }

        // ---- actions ------------------------------------------------------------------------------------------

        internal async Task RefreshTools()
        {
            var tools = new DownloadTools();
            var rows = await Task.Run(() => new[]
            {
                Describe("yt-dlp", tools.Downloader, ToolManager.Version(tools.Downloader, "--version"), true),
                Describe("FFmpeg", tools.Encoder, ToolManager.Version(tools.Encoder, "-version"), true),
                Describe("Deno", tools.JsRuntime == null ? null : tools.JsRuntime.Substring(tools.JsRuntime.IndexOf(':') + 1),
                    tools.JsRuntime == null ? null : ToolManager.Version(tools.JsRuntime.Substring(tools.JsRuntime.IndexOf(':') + 1), "--version"), false)
            });
            if (IsDisposed) return;
            toolList.SetRows(rows);
            bool ready = tools.Ready;
            install.Text = ready ? "Repair tools" : "Install tools";
            install.Kind = ready ? ButtonKind.Secondary : ButtonKind.Primary;
            update.Enabled = tools.Downloader != null && !ToolManager.IsBusy;
            install.Enabled = !ToolManager.IsBusy;
            install.Invalidate();
            if (toolStatus.Text.Length == 0 || toolStatus.Text.StartsWith("Managed", StringComparison.Ordinal))
                toolStatus.Text = "Managed copies live in " + DownloadTools.ManagedFolder;
        }

        private static ToolRow Describe(string name, string path, string version, bool required)
        {
            var row = new ToolRow { Name = name };
            if (path == null)
            {
                row.Detail = required ? "Not installed — downloads can't run without it" : "Not installed — optional, helps with some videos";
                row.Word = required ? "Missing" : "Optional";
                row.Color = required ? Theme.Fail : Theme.Na;
                row.Shape = required ? PillShape.Diamond : PillShape.Dash;
                return row;
            }
            bool managed = String.Equals(Path.GetDirectoryName(path), DownloadTools.ManagedFolder, StringComparison.OrdinalIgnoreCase);
            row.Detail = (version == null ? "Version unknown" : version) + " · " + (managed ? "app copy" : path);
            row.Word = version == null ? "Check" : "Ready";
            row.Color = version == null ? Theme.Warn : Theme.Pass;
            row.Shape = version == null ? PillShape.Hollow : PillShape.Circle;
            return row;
        }

        private IProgress<ToolProgress> ToolProgressReporter()
        {
            return new Progress<ToolProgress>(p =>
            {
                toolStatus.Text = p.Message + (p.Percent >= 0 ? " · " + p.Percent + "%" : "…");
                toolProgress.Value = p.Percent;
            });
        }

        private async Task RunInstall(bool refresh)
        {
            install.Enabled = update.Enabled = false;
            try
            {
                toolStatus.ForeColor = Theme.Muted;
                toolStatus.Text = await ToolManager.InstallAsync(refresh, ToolProgressReporter(), CancellationToken.None);
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException) throw;
                toolStatus.ForeColor = Theme.Fail;
                toolStatus.Text = "Couldn't install the tools: " + ex.Message;
            }
            // C# 5 cannot await inside a finally block; every other path above reaches here.
            toolProgress.Value = -2;
            await RefreshTools();
            if (ToolsChanged != null) ToolsChanged();
        }

        private async Task RunUpdate()
        {
            install.Enabled = update.Enabled = false;
            try
            {
                toolStatus.ForeColor = Theme.Muted;
                string version = await ToolManager.UpdateDownloaderAsync(ToolProgressReporter(), CancellationToken.None);
                toolStatus.Text = version == null ? "yt-dlp could not be updated. Select Repair tools." : "yt-dlp " + version + ".";
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException) throw;
                toolStatus.ForeColor = Theme.Fail;
                toolStatus.Text = "Update failed: " + ex.Message;
            }
            toolProgress.Value = -2;
            await RefreshTools();
            if (ToolsChanged != null) ToolsChanged();
        }

        private async Task TestModels()
        {
            SaveAi();
            string p = shownProvider;
            if (p == null || p == AiProviders.None) return;
            loadModels.Enabled = false;
            aiStatus.ForeColor = Theme.Muted;
            aiStatus.Text = "Connecting to " + p + "…";
            try
            {
                using (ISongModel client = AiProviders.IsLocal(p) ? (ISongModel)new LocalModels(p, endpoint.Text, apiKey.Text)
                    : new CloudModels(p, apiKey.Text))
                using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                {
                    var names = await client.ListAsync(cancel.Token);
                    string prior = model.Text;
                    model.Items.Clear();
                    model.Items.AddRange(names);
                    model.Text = names.Contains(prior) ? prior : (names.FirstOrDefault() ?? prior);
                    SaveAi();
                    aiStatus.ForeColor = names.Length == 0 ? Theme.Warn : Theme.Pass;
                    aiStatus.Text = names.Length == 0 ? (AiProviders.IsLocal(p) ? "Connected, but no models are installed on this server."
                        : "Connected, but this key can't use any text models.") :
                        "Connected · " + names.Length + " model" + (names.Length == 1 ? "" : "s") + " available.";
                }
            }
            catch (Exception ex)
            {
                if (ex is OutOfMemoryException) throw;
                aiStatus.ForeColor = Theme.Fail;
                aiStatus.Text = ex is OperationCanceledException ? "The server did not answer within 20 seconds." : ex.Message;
            }
            finally { loadModels.Enabled = true; }
        }
    }

    internal sealed class ToolRow
    {
        internal string Name, Detail, Word;
        internal Color Color;
        internal PillShape Shape;
    }

    internal sealed class ToolStatusView : Control
    {
        private ToolRow[] rows = new ToolRow[0];

        internal ToolStatusView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Height = Theme.S(3 * 48);
            TabStop = false;
            AccessibleRole = AccessibleRole.List;
            AccessibleName = "Audio tool status";
        }

        internal void SetRows(ToolRow[] value)
        {
            rows = value;
            Height = Theme.S(48) * Math.Max(1, rows.Length);
            AccessibleDescription = String.Join("; ", rows.Select(r => r.Name + ": " + r.Word + ", " + r.Detail).ToArray());
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.ParentColor(this));
            int h = Theme.S(48);
            for (int i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                int y = i * h;
                if (i > 0) using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, y, Width, y);
                int pill = Theme.PillWidth(r.Word);
                TextRenderer.DrawText(g, r.Name, Theme.Strong, new Rectangle(0, y + Theme.S(5), Width - pill - Theme.S(10), Theme.S(20)),
                    Theme.Text, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, r.Detail, Theme.Small, new Rectangle(0, y + Theme.S(25), Width - pill - Theme.S(10), Theme.S(18)),
                    Theme.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis);
                Theme.DrawPill(g, Width - pill, y + Theme.S(14), r.Word, r.Color, r.Shape, Theme.ParentColor(this));
            }
        }
    }
}
