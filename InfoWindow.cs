using System;
using System.Drawing;
using System.Windows.Forms;

namespace YouTubeMp3
{
    // The "i" page: every feature, how to set up playlists and AI, and what to do when songs don't arrive.
    internal sealed class InfoWindow : Form
    {
        private static InfoWindow open;

        // One info window at a time; opening it again brings the open one forward.
        internal static void Open(IWin32Window owner)
        {
            if (open != null && !open.IsDisposed) { open.Activate(); return; }
            open = new InfoWindow();
            open.FormClosed += delegate { open = null; };
            open.Show(owner);
        }

        private InfoWindow()
        {
            Text = "About · " + AppInfo.Name;
            Icon = Theme.AppIcon();
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Background;
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Theme.S(860), Theme.S(820));
            MinimumSize = new Size(Theme.S(620), Theme.S(520));
            ShowInTaskbar = false;
            KeyPreview = true;
            HandleCreated += delegate { NativeTheme.DarkTitleBar(this); };
            Load += delegate { Theme.FitToScreen(this); };

            var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Background,
                Padding = new Padding(Theme.S(28), Theme.S(4), Theme.S(28), Theme.S(14)) };
            NativeTheme.DarkScrollbars(page);
            var stack = Layouts.Stack();
            stack.Dock = DockStyle.Top;

            Section(stack, "What this app does",
                "Paste a YouTube link and the app does the rest: it downloads the audio, converts it to MP3, adds the official cover art, " +
                "looks up the song's real details and waits for you to check them. When you select Send, the song goes into Apple Music, " +
                "and from there to your iPhone and CarPlay.",
                Step("Download", "yt-dlp fetches the best audio YouTube has."),
                Step("Convert", "FFmpeg makes an MP3 at the quality you chose (Best by default)."),
                Step("Cover art", "The official album cover when the catalogue has one, otherwise the video thumbnail."),
                Step("Song info", "Title, singers, album and year from Apple's catalogue or MusicBrainz; language and genre from AI or the built-in rules."),
                Step("Apple Music", "After your review, the tagged MP3 is handed to Apple Music and the app confirms it arrived."));

            Section(stack, "Adding songs",
                null,
                Step("Paste links", "Paste one or many YouTube links (one per line) and select Add songs. Shorts and YouTube Music links work too."),
                Step("Search by name", "Type a song name and press Enter, then pick the right video from the list."),
                Step("Load .txt", "Load a text file with one link per line, for a long list."),
                Step("Ctrl+V anywhere", "Pasting while the list has focus adds the links straight away."),
                Step("Already sent", "A song already in Apple Music isn't added twice."));

            Section(stack, "Song details and AI",
                "Every song is looked up in Apple's music catalogue and MusicBrainz. AI then picks the right match and adds the language and " +
                "genre. Choose how to connect it in Settings › AI for song details, in three steps: how to connect, what that needs " +
                "(a server address or an API key), then the model.",
                Step("No AI", RuleTagger.LowAccuracy + ". Built-in rules read the script of the title, words like bhajan or remix, " +
                    "well-known labels and the catalogue's genre. Good for friends without a model; check the tags before sending."),
                Step("Local: Ollama or vLLM", "A model on this PC or your own server. Free and private. Suggested: qwen3:30b-a3b or gemma3:27b."),
                Step("Claude", "Anthropic API key. Models: claude-opus-5-5 (most accurate), claude-sonnet-5-5, claude-haiku-5-5 (cheapest)."),
                Step("ChatGPT", "OpenAI API key (billed separately from a ChatGPT subscription). Select Test & load models for the list."),
                Step("Gemini", "Google AI Studio API key. gemini-3.5-flash is quick and cheap; Google has a free tier."),
                Step("Grok", "xAI API key. grok-4.3 is the cheapest; Test & load models shows every model your key can use."),
                Step("What's sent", "Only the YouTube title, channel, a short part of the description and the catalogue matches. Never audio or files."),
                Step("Your keys", "Saved encrypted for your Windows account (DPAPI). Another account or another PC can't read them."),
                Step("Web search", "Optional Ollama web search key: songs the catalogues don't know are also looked up on the web."));

            Section(stack, "Review tags",
                "Nothing goes to Apple Music until you've checked it. Select Review & send (or right-click a song › Review tags).",
                Step("Edit any cell", "Title, singers, album, year, language, genre and playlist. Language and genre suggest names you've used."),
                Step("Playlist for all songs", "Set one playlist for every song at the top, or change it per song in the Playlist column."),
                Step("Send", "Tick the songs to send. Unticked songs keep your edits and wait for next time."),
                Step("Source", "Where the details came from: Apple Music, MusicBrainz, web + AI, AI guess, YouTube or rules."),
                Step("Fix a sent song", "Right-click › Look up song info again and re-send. Then delete the old copy in Apple Music."));

            Section(stack, "Playlists",
                "Apple Music for Windows has no way for other apps to add songs to a playlist, so the app writes the playlist name into each " +
                "song's Comments tag and an Apple Music Smart Playlist collects them. You set this up once per playlist name.",
                Step("1", "Settings › Apple Music › Default playlist: type the name, for example CarPlay."),
                Step("2", "In Apple Music select + next to Playlists › New Smart Playlist."),
                Step("3", "Set the rule Comments is CarPlay (your playlist name), tick Live updating, select OK and name it CarPlay."),
                Step("4", "Every song sent with that playlist name joins it by itself, now and later, and syncs to your iPhone."),
                Step("Another playlist", "Type a new name in Review tags, then make one more Smart Playlist with that name."),
                Step("No playlist", "Leave the name empty and songs go to your library only. Genres still group as “Hindi - Romantic”."));

            Section(stack, "When songs don't reach Apple Music",
                "The app watches every song it hands over. If one doesn't arrive, the banner on the main screen says why and what to do.",
                Step("Apple Music isn't open", "Open it. It adds every waiting song by itself."),
                Step("Apple Devices is busy", "While the Apple Devices app syncs or backs up a phone, Apple Music can't add songs. " +
                    "Let it finish, or close Apple Devices (also from the system tray)."),
                Step("iTunes is open", "Apple Music can't add songs while iTunes runs. Quit iTunes, including from the tray."),
                Step("Hidden dialog", "Apple Music open but nothing added for 3 minutes: switch to it and answer any sign-in, terms or " +
                    "library message. If nothing shows, close Apple Music fully and open it again."),
                Step("File in use", "Antivirus scanning or OneDrive syncing the file. Wait a minute, or pause OneDrive."),
                Step("Windows blocked the folder", "Windows Security's Controlled folder access: allow YouTube CarPlay Sync under " +
                    "Virus & threat protection › Ransomware protection."),
                Step("Folder not found", "Open Apple Music once so it makes its Media folder, or choose the folder in Settings › Apple Music."),
                Step("Disk almost full", "Free some space on the drive Apple Music uses, then send again."),
                Step("Not accepted", "Apple Music moved the file to its Not Added folder. Retry, or add it from Apple Music's menu."));

            Section(stack, "Settings",
                null,
                Step("Audio tools", "Install, repair or update yt-dlp, FFmpeg and Deno. The app keeps its own copies."),
                Step("Downloads", "Where songs are saved, MP3 quality, cover art, and Firefox sign-in for videos YouTube won't play signed out."),
                Step("AI for song details", "How to connect AI, its key or server, the model and how long to wait for an answer."),
                Step("Apple Music", "Default playlist, the import folder and whether to remove the app's copy once Apple Music has the song."));

            Section(stack, "Where things are kept",
                null,
                Step("Settings and keys", "%LOCALAPPDATA%\\YouTubeMP3\\models.json (keys encrypted)."),
                Step("Song list", "%LOCALAPPDATA%\\YouTubeMP3\\songs.json, so waiting songs survive closing the app."),
                Step("Audio tools", DownloadTools.ManagedFolder),
                Step("Version", AppInfo.Name + " " + AppInfo.Version));

            page.Controls.Add(stack);
            Controls.Add(page);
            var close = Theme.Button("Close", 100, true);
            close.Click += delegate { Close(); };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
                BackColor = Theme.Background, Padding = new Padding(Theme.S(28), Theme.S(8), Theme.S(28), Theme.S(16)) };
            footer.Controls.Add(close);
            Controls.Add(footer);
            var top = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = Theme.Background,
                Padding = new Padding(Theme.S(28), Theme.S(22), Theme.S(28), 0) };
            top.Controls.Add(Layouts.Header("How it works", "Every feature, how to set up playlists and AI, and what to do when a song doesn't arrive."));
            Controls.Add(top);
            Controls.SetChildIndex(page, 0);
            AcceptButton = close;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // A feature name in a fixed column and its explanation wrapping beside it. Heights are measured here rather
        // than left to a TableLayoutPanel, which sizes a wrapped label to the whole row and clips the second line.
        private sealed class StepRow : Control
        {
            private const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
            private readonly string name, text;
            private int laidOutWidth = -1;

            internal StepRow(string name, string text)
            {
                this.name = name; this.text = text;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                    | ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
                BackColor = Color.Transparent;
                Margin = Padding.Empty;
                AccessibleRole = AccessibleRole.StaticText;
                AccessibleName = name + ": " + text;
            }

            private static int NameWidth { get { return Theme.S(190); } }
            private static int Gap { get { return Theme.S(10); } }
            private int TextWidth { get { return Width - NameWidth - Gap - Theme.S(6); } }   // a little slack so the last word never clips

            protected override void OnParentChanged(EventArgs e)
            {
                base.OnParentChanged(e);
                if (Parent != null) Parent.SizeChanged += delegate { Measure(); };
                Measure();
            }

            protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); if (Width != laidOutWidth) Measure(); }

            private void Measure()
            {
                if (Parent == null) return;
                int width = Parent.ClientSize.Width - Parent.Padding.Horizontal - Margin.Horizontal;
                if (width < NameWidth + Gap + 60) return;
                int textWidth = width - NameWidth - Gap - Theme.S(6);
                int height = Math.Max(TextRenderer.MeasureText(name, Theme.Strong, new Size(NameWidth, Int32.MaxValue), Flags).Height,
                    TextRenderer.MeasureText(text, Theme.Body, new Size(textWidth, Int32.MaxValue), Flags).Height);
                laidOutWidth = width;
                if (Width != width || Height != height) Size = new Size(width, height);
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                TextRenderer.DrawText(e.Graphics, name, Theme.Strong, new Rectangle(0, 0, NameWidth, Height), Theme.Text, Flags);
                TextRenderer.DrawText(e.Graphics, text, Theme.Body, new Rectangle(NameWidth + Gap, 0, TextWidth, Height),
                    Theme.Ink2, Flags);
            }
        }

        private static string[] Step(string name, string text) { return new[] { name, text }; }

        private static void Section(TableLayoutPanel page, string heading, string intro, params string[][] items)
        {
            var card = new Card(heading);
            var stack = Layouts.Stack();
            if (!String.IsNullOrEmpty(intro))
                Layouts.Add(stack, new WrapLabel { Text = intro, Font = Theme.Body, ForeColor = Theme.Text, UseMnemonic = false,
                    BackColor = Color.Transparent, Margin = Padding.Empty }, true, 12);
            foreach (var item in items) Layouts.Add(stack, new StepRow(item[0], item[1]), true, 8);
            card.Fit(stack);
            Layouts.Add(page, card, true, 14);
        }
    }
}
