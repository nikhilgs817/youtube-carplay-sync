using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace YouTubeMp3
{
    // The one stop that waits for you: check every waiting song's tags in one table, then send them all at once.
    // Apple Music reads the tags only when it imports a file, so this is the last moment a fix reaches your playlists.
    internal sealed class ReviewDialog : Form
    {
        private readonly DataGridView grid = new DataGridView();
        private readonly PillButton send = Theme.Button("Send to Apple Music", 190, true);
        private readonly PillButton cancel = Theme.Button("Not now", 110);
        private readonly WrapLabel count = Layouts.Note("");
        internal readonly List<SongJob> Accepted = new List<SongJob>();

        private static readonly string[] Languages = { "Hindi", "English", "Punjabi", "Tamil", "Telugu", "Kannada", "Malayalam",
            "Marathi", "Bengali", "Gujarati", "Urdu", "Japanese", "Korean", "Spanish", "Instrumental" };
        private static readonly string[] Genres = { "Pop", "Romantic", "Rock", "Hip-Hop", "Rap", "Devotional", "Classical", "Folk",
            "Sufi", "Ghazal", "Electronic", "Dance", "Indie", "Jazz", "R&B", "Soundtrack", "Lo-fi" };

        internal ReviewDialog(List<SongJob> waiting, IEnumerable<SongJob> everyone, SongJob focus = null)
        {
            Text = "Review tags · " + AppInfo.Name;
            Icon = Theme.AppIcon();
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Theme.S(1240), Theme.S(660));
            MinimumSize = new Size(Theme.S(760), Theme.S(460));
            ShowInTaskbar = false;
            KeyPreview = true;
            HandleCreated += delegate { NativeTheme.DarkTitleBar(this); };
            Load += delegate { Theme.FitToScreen(this); };

            var outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(16)), BackColor = Theme.Background };
            var card = new Card("Check the tags, then send") { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background };
            bool resend = waiting.Any(j => j.Resend);
            var note = new Label { Text = "Title, artist, album and year come from Apple's catalogue where the song was found (see Info from). " +
                "Language and genre become one Genre tag, for example \"Hindi - Pop\", which your Smart Playlists match on. Apple Music reads tags " +
                "only when it imports a song, so fix anything here first. Untick a song to keep it waiting." +
                (resend ? " Some songs are already in Apple Music: delete the old copies there first (Songs \u203A select \u203A Delete from Library), or you'll have two." : ""),
                Dock = DockStyle.Fill, Font = Theme.Small, ForeColor = Theme.Muted, UseMnemonic = false, BackColor = Theme.Background };
            var noteHost = new Panel { Dock = DockStyle.Top, Height = Theme.S(52), BackColor = Theme.Background };
            noteHost.Controls.Add(note);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = Theme.S(62), FlowDirection = FlowDirection.RightToLeft,
                BackColor = Theme.Background, Padding = new Padding(0, Theme.S(10), 0, 0), WrapContents = false };
            buttons.Controls.Add(send);
            buttons.Controls.Add(cancel);
            count.Wrap = false;
            count.AutoSize = true;
            count.Margin = new Padding(0, Theme.S(14), Theme.S(16), 0);
            buttons.Controls.Add(count);

            BuildGrid();
            var known = everyone.ToList();
            var languages = new AutoCompleteStringCollection();
            languages.AddRange(Languages.Concat(known.Select(j => j.Language)).Where(v => !String.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            var genres = new AutoCompleteStringCollection();
            genres.AddRange(Genres.Concat(known.Select(j => j.Genre)).Where(v => !String.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            grid.EditingControlShowing += (sender, e) =>
            {
                var box = e.Control as TextBox;
                if (box == null) return;
                string column = grid.CurrentCell.OwningColumn.Name;
                box.AutoCompleteMode = column == "Language" || column == "Genre" ? AutoCompleteMode.SuggestAppend : AutoCompleteMode.None;
                box.AutoCompleteSource = column == "Language" || column == "Genre" ? AutoCompleteSource.CustomSource : AutoCompleteSource.None;
                if (column == "Language") box.AutoCompleteCustomSource = languages;
                if (column == "Genre") box.AutoCompleteCustomSource = genres;
                // Entering a cell selects its text, so typing replaces a wrong suggestion instead of adding to it.
                grid.BeginInvoke(new Action(() => { if (!box.IsDisposed) box.SelectAll(); }));
                box.KeyDown -= SelectAllOnCtrlA;
                box.KeyDown += SelectAllOnCtrlA;
            };

            foreach (var job in waiting)
            {
                int row = grid.Rows.Add(true, job.Title, job.Artist, job.Album, job.Year, job.Language, job.Genre,
                    String.IsNullOrEmpty(job.InfoSource) ? (job.AiTagged ? "AI" : "Not found") : job.InfoSource);
                grid.Rows[row].Tag = job;
                if (job == focus) grid.CurrentCell = grid.Rows[row].Cells["Language"];
            }

            body.Controls.Add(grid);
            body.Controls.Add(noteHost);
            body.Controls.Add(buttons);
            card.Fill(body);
            outer.Controls.Add(card);
            Controls.Add(outer);
            AcceptButton = send;
            CancelButton = cancel;
            cancel.DialogResult = DialogResult.Cancel;
            send.Click += delegate { Accept(); };
            grid.CellValueChanged += delegate { UpdateCount(); };
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            UpdateCount();
        }

        private void BuildGrid()
        {
            Theme.StyleGrid(grid);
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = grid.AllowUserToResizeRows = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.MultiSelect = false;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.RowTemplate.Height = Theme.S(40);
            grid.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
            grid.DefaultCellStyle.SelectionForeColor = Theme.Text;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Send", HeaderText = "SEND", FillWeight = 8, MinimumWidth = Theme.S(56) });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Title", HeaderText = "TITLE", FillWeight = 22 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Artist", HeaderText = "ARTIST", FillWeight = 22 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Album", HeaderText = "ALBUM / FILM", FillWeight = 20 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Year", HeaderText = "YEAR", FillWeight = 7 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Language", HeaderText = "LANGUAGE", FillWeight = 11 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Genre", HeaderText = "GENRE", FillWeight = 11 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "INFO FROM", FillWeight = 11, ReadOnly = true });
            grid.Columns["Source"].DefaultCellStyle.ForeColor = Theme.Muted;
            grid.CellFormatting += (sender, e) =>
            {
                if (e.RowIndex < 0) return;
                string name = grid.Columns[e.ColumnIndex].Name;
                if ((name == "Language" || name == "Genre") && String.IsNullOrWhiteSpace(Convert.ToString(e.Value)))
                {
                    e.CellStyle.BackColor = Theme.Mix(Theme.Accent, Theme.Background, 0.10f);
                    e.CellStyle.ForeColor = Theme.Faint;
                }
            };
            grid.CellPainting += (sender, e) =>
            {
                if (e.RowIndex < 0 || e.Value != null && !String.IsNullOrWhiteSpace(Convert.ToString(e.Value))) return;
                string name = grid.Columns[e.ColumnIndex].Name;
                if (name != "Language" && name != "Genre") return;
                e.PaintBackground(e.CellBounds, true);
                TextRenderer.DrawText(e.Graphics, "add " + name.ToLowerInvariant(), Theme.Small,
                    new Rectangle(e.CellBounds.X + Theme.S(8), e.CellBounds.Y, e.CellBounds.Width - Theme.S(8), e.CellBounds.Height),
                    Theme.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                e.Handled = true;
            };
        }

        private static void SelectAllOnCtrlA(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A) { ((TextBox)sender).SelectAll(); e.SuppressKeyPress = true; }
        }

        private void UpdateCount()
        {
            int n = grid.Rows.Cast<DataGridViewRow>().Count(r => Convert.ToBoolean(r.Cells["Send"].Value));
            int untagged = grid.Rows.Cast<DataGridViewRow>().Count(r => Convert.ToBoolean(r.Cells["Send"].Value) &&
                (String.IsNullOrWhiteSpace(Convert.ToString(r.Cells["Language"].Value)) || String.IsNullOrWhiteSpace(Convert.ToString(r.Cells["Genre"].Value))));
            send.Text = n == 1 ? "Send 1 song" : "Send " + n + " songs";
            send.Enabled = n > 0;
            count.Text = untagged == 0 ? "" : untagged + (untagged == 1 ? " song is" : " songs are") + " missing a language or genre";
            count.ForeColor = Theme.Accent2;
        }

        private void Accept()
        {
            grid.EndEdit();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!Convert.ToBoolean(row.Cells["Send"].Value)) continue;
                var job = (SongJob)row.Tag;
                string title = Keep(row.Cells["Title"].Value);
                job.Title = title.Length > 0 ? title : job.Title;
                job.Artist = Keep(row.Cells["Artist"].Value);
                job.Language = Clean(row.Cells["Language"].Value);
                job.Genre = Clean(row.Cells["Genre"].Value);
                string album = Convert.ToString(row.Cells["Album"].Value ?? "").Trim();
                if (album != job.Album) { job.Album = album; }
                job.Year = Convert.ToString(row.Cells["Year"].Value ?? "").Trim();
                if (job.AlbumArtist.Length == 0 || job.AlbumArtist == job.Artist) job.AlbumArtist = job.Artist;
                Accepted.Add(job);
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        // Song and artist names keep their own capitals ("AP Dhillon", "iPhone"); only tags are tidied.
        private static string Keep(object value) { return (Convert.ToString(value) ?? "").Trim(); }

        private static string Clean(object value)
        {
            string text = Convert.ToString(value) ?? "";
            text = text.Trim();
            if (text.Length == 0) return "";
            return text.Length == 1 ? text.ToUpperInvariant() : Char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }
}
