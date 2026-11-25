using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace YouTubeMp3
{
    // Every song as a raised card: a vinyl disc that spins while the song is being worked on, the title, a status pill,
    // and a five-step track (Download, Convert, Cover art, AI tags, Apple Music). The running step pulses red, a progress
    // ring shows the download percentage, and the groove leading into it flows. Finished steps fill in slate.
    internal sealed class SongList : Control
    {
        private List<SongJob> items = new List<SongJob>();
        private readonly VScrollBar scroll = new VScrollBar { Dock = DockStyle.Right, Visible = false, SmallChange = 40 };
        private readonly Timer clock = new Timer { Interval = 33 };
        private readonly Dictionary<string, float> spins = new Dictionary<string, float>();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly Stopwatch time = Stopwatch.StartNew();
        private int hover = -1, offset;
        private SongJob menuJob;

        internal event Action<SongJob> RetryRequested, RemoveRequested, OpenRequested, ReviewRequested, CopyRequested, RelookupRequested;

        private static readonly Color Vinyl = Color.FromArgb(52, 63, 84);

        internal SongList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Theme.Background;
            Font = Theme.Body;
            TabStop = true;
            AccessibleRole = AccessibleRole.List;
            AccessibleName = "Songs";
            Controls.Add(scroll);
            scroll.Scroll += delegate { offset = scroll.Value; Invalidate(); };
            clock.Tick += delegate { Tick(); };
            clock.Start();
            Theme.StyleMenu(menu);
            menu.Opening += (sender, e) => BuildMenu(e);
        }

        private int RowHeight { get { return Theme.S(124); } }

        internal void SetItems(List<SongJob> jobs)
        {
            items = jobs;
            UpdateScroll();
            AccessibleDescription = items.Count + " songs";
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { clock.Dispose(); menu.Dispose(); }
            base.Dispose(disposing);
        }

        private void Tick()
        {
            bool animate = false;
            foreach (var job in items)
            {
                if (!job.Working && job.Stage != Stage.Review) continue;
                animate = true;
                if (!job.Working) continue;
                float spin;
                spins.TryGetValue(job.Id, out spin);
                spins[job.Id] = (spin + (job.Stage == Stage.Download ? 9f : 5f)) % 360f;
            }
            if (animate && Visible) Invalidate();
        }

        private void UpdateScroll()
        {
            int content = items.Count * RowHeight + Theme.S(12);
            bool need = content > ClientSize.Height;
            scroll.Visible = need;
            if (!need) { offset = 0; scroll.Value = 0; return; }
            scroll.Minimum = 0;
            scroll.LargeChange = Math.Max(1, ClientSize.Height);
            scroll.Maximum = content;
            offset = Math.Max(0, Math.Min(offset, content - ClientSize.Height));
            scroll.Value = offset;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateScroll(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!scroll.Visible) return;
            int max = scroll.Maximum - scroll.LargeChange + 1;
            offset = Math.Max(0, Math.Min(max, offset - e.Delta / 2));
            scroll.Value = offset;
            Invalidate();
        }

        private int IndexAt(Point p)
        {
            int index = (p.Y + offset) / RowHeight;
            return index >= 0 && index < items.Count && p.X < Width - (scroll.Visible ? scroll.Width : 0) ? index : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = IndexAt(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
            Cursor = h >= 0 && items[h].Stage == Stage.Review ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int index = IndexAt(e.Location);
            if (e.Button == MouseButtons.Right && index >= 0)
            {
                menuJob = items[index];
                menu.Show(this, e.Location);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int index = IndexAt(e.Location);
            if (e.Button == MouseButtons.Left && index >= 0)
            {
                var job = items[index];
                if (job.Stage == Stage.Review && ReviewRequested != null) ReviewRequested(job);
                else if ((job.Stage == Stage.Failed || job.Stage == Stage.Stopped) && ActionBox(index).Contains(e.Location) && RetryRequested != null) RetryRequested(job);
            }
            base.OnMouseClick(e);
        }

        private void BuildMenu(System.ComponentModel.CancelEventArgs e)
        {
            menu.Items.Clear();
            var job = menuJob;
            if (job == null) { e.Cancel = true; return; }
            if (job.Stage == Stage.Review) menu.Items.Add("Review tags…", null, delegate { if (ReviewRequested != null) ReviewRequested(job); });
            if ((job.Stage == Stage.Failed || job.Stage == Stage.Stopped) && job.VideoId != null)
                menu.Items.Add("Retry", null, delegate { if (RetryRequested != null) RetryRequested(job); });
            if ((job.Stage == Stage.Done || job.Stage == Stage.Review) && job.VideoId != null)
                menu.Items.Add(job.Stage == Stage.Done ? "Look up song info again and re-send" : "Look up song info again", null,
                    delegate { if (RelookupRequested != null) RelookupRequested(job); });
            if (!String.IsNullOrEmpty(job.File) && System.IO.File.Exists(job.File))
                menu.Items.Add("Show the MP3 in its folder", null, delegate { if (OpenRequested != null) OpenRequested(job); });
            if (!String.IsNullOrEmpty(job.Error)) menu.Items.Add("Copy error details", null, delegate { if (CopyRequested != null) CopyRequested(job); });
            if (!job.Working || job.Stage == Stage.Importing)
            {
                if (menu.Items.Count > 0) menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Remove from the list", null, delegate { if (RemoveRequested != null) RemoveRequested(job); });
            }
            e.Cancel = menu.Items.Count == 0;
        }

        // ---- painting ----------------------------------------------------------------------------------------------

        private RectangleF CardBox(int index)
        {
            int right = Width - (scroll.Visible ? scroll.Width : 0);
            return new RectangleF(Theme.S(14), index * RowHeight - offset + Theme.S(10), right - Theme.S(28), RowHeight - Theme.S(22));
        }

        private Rectangle ActionBox(int index)
        {
            var card = CardBox(index);
            return new Rectangle((int)card.Right - Theme.S(112), (int)card.Y + Theme.S(12), Theme.S(96), Theme.S(30));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            if (items.Count == 0) { PaintEmpty(g); return; }
            float t = time.ElapsedMilliseconds / 1000f;
            int first = Math.Max(0, offset / RowHeight), last = Math.Min(items.Count - 1, (offset + Height) / RowHeight);
            for (int i = first; i <= last; i++) PaintCard(g, i, items[i], t);
        }

        private void PaintEmpty(Graphics g)
        {
            int size = Theme.S(96);
            var well = new RectangleF((Width - size) / 2f, Height / 2f - size, size, size);
            Theme.Inset(g, well, size / 2f, Theme.Input, 5);
            PaintDisc(g, new PointF(well.X + size / 2f, well.Y + size / 2f), Theme.S(30), 0, false);
            TextRenderer.DrawText(g, "No songs yet", Theme.CardTitle, new Rectangle(0, (int)well.Bottom + Theme.S(18), Width, Theme.S(26)),
                Theme.Text, TextFormatFlags.HorizontalCenter);
            TextRenderer.DrawText(g, "Paste a YouTube link above. The app downloads it, converts it, tags it and waits for you to check the tags.",
                Theme.Body, new Rectangle(Theme.S(40), (int)well.Bottom + Theme.S(48), Width - Theme.S(80), Theme.S(44)),
                Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
        }

        private void PaintCard(Graphics g, int index, SongJob job, float t)
        {
            var card = CardBox(index);
            bool lifted = index == hover;
            Theme.Raised(g, card, Theme.S(20), lifted ? Theme.S(9) : Theme.S(7), Theme.Background);

            // Disc in an inset well, left.
            float discR = Theme.S(30);
            var center = new PointF(card.X + Theme.S(54), card.Y + card.Height / 2f);
            Theme.Inset(g, new RectangleF(center.X - discR - Theme.S(6), center.Y - discR - Theme.S(6), (discR + Theme.S(6)) * 2, (discR + Theme.S(6)) * 2),
                discR + Theme.S(6), Theme.Input, 4);
            float spin;
            spins.TryGetValue(job.Id, out spin);
            PaintDisc(g, center, discR, spin, job.Working);
            if (job.Stage == Stage.Done) Badge(g, center, discR, Theme.Pass, "✓");
            else if (job.Stage == Stage.Failed) Badge(g, center, discR, Theme.Fail, "!");

            // Status pill and action, right.
            string word; Color tone; PillShape shape;
            Status(job, out word, out tone, out shape);
            int pillW = Theme.PillWidth(word);
            float textLeft = card.X + Theme.S(108);
            float textRight = card.Right - Theme.S(24);
            Rectangle action = ActionBox(index);
            bool showAction = job.Stage == Stage.Review || ((job.Stage == Stage.Failed || job.Stage == Stage.Stopped) && job.VideoId != null);
            int pillX = showAction ? action.X - pillW - Theme.S(12) : (int)textRight - pillW;
            Theme.DrawPill(g, pillX, (int)card.Y + Theme.S(17), word, tone, shape, Theme.Background);
            if (showAction)
            {
                var box = new RectangleF(action.X, action.Y, action.Width, action.Height);
                bool review = job.Stage == Stage.Review;
                Theme.Raised(g, box, box.Height / 2, Theme.S(3), review ? Theme.AccentFill : Theme.Background);
                TextRenderer.DrawText(g, review ? "Review" : "Retry", Theme.Strong, action, review ? Theme.AccentInk : Theme.Accent2,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // Title and detail.
            int titleWidth = pillX - (int)textLeft - Theme.S(12);
            TextRenderer.DrawText(g, job.Title, Theme.Strong, new Rectangle((int)textLeft, (int)card.Y + Theme.S(14), titleWidth, Theme.S(22)),
                Theme.Text, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            string detail = Detail(job);
            TextRenderer.DrawText(g, detail, Theme.Small, new Rectangle((int)textLeft, (int)card.Y + Theme.S(36), titleWidth, Theme.S(18)),
                job.Stage == Stage.Failed ? Theme.Fail : Theme.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

            PaintTrack(g, job, new RectangleF(textLeft + Theme.S(10), card.Y + Theme.S(66), textRight - textLeft - Theme.S(20), Theme.S(30)), t, index);
        }

        private static void PaintDisc(Graphics g, PointF c, float r, float angle, bool spinning)
        {
            using (var brush = new SolidBrush(Vinyl)) g.FillEllipse(brush, c.X - r, c.Y - r, r * 2, r * 2);
            using (var groove = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
                for (float k = 0.86f; k > 0.45f; k -= 0.12f) g.DrawEllipse(groove, c.X - r * k, c.Y - r * k, r * k * 2, r * k * 2);
            // A light sheen that sweeps round while the song is being worked on.
            using (var sheen = new Pen(Color.FromArgb(spinning ? 110 : 55, 255, 255, 255), Theme.S(2)))
            {
                g.DrawArc(sheen, c.X - r * 0.75f, c.Y - r * 0.75f, r * 1.5f, r * 1.5f, angle, 50);
                g.DrawArc(sheen, c.X - r * 0.75f, c.Y - r * 0.75f, r * 1.5f, r * 1.5f, angle + 180, 50);
            }
            float label = r * 0.38f;
            // The label is YouTube red #FF0000, like the app icon, whether the disc spins or not.
            using (var brush = new SolidBrush(Theme.Accent))
                g.FillEllipse(brush, c.X - label, c.Y - label, label * 2, label * 2);
            using (var hole = new SolidBrush(Theme.Background)) g.FillEllipse(hole, c.X - 2.5f, c.Y - 2.5f, 5, 5);
        }

        private static void Badge(Graphics g, PointF c, float r, Color color, string glyph)
        {
            float b = Theme.S(18);
            var box = new RectangleF(c.X + r * 0.62f - b / 2, c.Y + r * 0.62f - b / 2, b, b);
            using (var ring = new SolidBrush(Theme.Background)) g.FillEllipse(ring, box.X - 2, box.Y - 2, b + 4, b + 4);
            using (var fill = new SolidBrush(color)) g.FillEllipse(fill, box);
            TextRenderer.DrawText(g, glyph, Theme.Caps, Rectangle.Round(box), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void PaintTrack(Graphics g, SongJob job, RectangleF area, float t, int index)
        {
            int steps = SongJob.StepNames.Length;
            float node = Theme.S(10);
            float y = area.Y + node;
            float gap = (area.Width - node * 2) / (steps - 1);
            int step = job.Step;
            bool working = job.Working;
            bool failed = job.Stage == Stage.Failed, stopped = job.Stage == Stage.Stopped, review = job.Stage == Stage.Review;
            var x = new float[steps];
            for (int i = 0; i < steps; i++) x[i] = area.X + node + i * gap;

            // Grooves between the nodes.
            for (int i = 0; i < steps - 1; i++)
            {
                float a = x[i] + node + Theme.S(4), b = x[i + 1] - node - Theme.S(4);
                if (b <= a) continue;
                Theme.Inset(g, new RectangleF(a, y - Theme.S(3), b - a, Theme.S(6)), Theme.S(3), Theme.Input, 2);
                if (i + 1 < step || step >= steps)
                    using (var pen = new Pen(Theme.Text, Theme.S(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLine(pen, a + 1, y, b - 1, y);
                else if (i + 1 == step && (working || review))
                    using (var pen = new Pen(review ? Theme.Mix(Theme.Accent, Theme.Background, 0.5f) : Theme.Accent, Theme.S(3)))
                    {
                        pen.DashPattern = new[] { 1.6f, 1.6f };
                        pen.DashOffset = review ? 0 : -t * 6f;
                        g.DrawLine(pen, a + 1, y, b - 1, y);
                    }
            }

            // Nodes and their labels.
            float phase = (t * 1.1f + index * 0.37f) % 1f;
            for (int i = 0; i < steps; i++)
            {
                var box = new RectangleF(x[i] - node, y - node, node * 2, node * 2);
                bool done = i < step || step >= steps;
                bool current = i == step && step < steps;
                if (done)
                {
                    Theme.Raised(g, box, node, Theme.S(3), Theme.Text);
                    Check(g, box, Color.White);
                }
                else if (current && working)
                {
                    float pulse = node + phase * Theme.S(12);
                    using (var ring = new Pen(Color.FromArgb((int)(170 * (1 - phase)), Theme.Accent), Theme.S(2)))
                        g.DrawEllipse(ring, x[i] - pulse, y - pulse, pulse * 2, pulse * 2);
                    Theme.Raised(g, box, node, Theme.S(3), Theme.AccentFill);
                    if (i == 0 && job.Percent >= 0)
                    {
                        float ringR = node + Theme.S(5);
                        using (var track = new Pen(Color.FromArgb(60, Theme.Accent), Theme.S(3)))
                            g.DrawEllipse(track, x[i] - ringR, y - ringR, ringR * 2, ringR * 2);
                        using (var arc = new Pen(Theme.Accent, Theme.S(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawArc(arc, x[i] - ringR, y - ringR, ringR * 2, ringR * 2, -90, Math.Max(4, job.Percent * 3.6f));
                    }
                    else
                    {
                        // A dot that orbits inside the node while the step has no percentage.
                        double a = t * 5.0 + index;
                        float dx = (float)Math.Cos(a) * node * 0.45f, dy = (float)Math.Sin(a) * node * 0.45f;
                        using (var dot = new SolidBrush(Color.White)) g.FillEllipse(dot, x[i] + dx - 2.5f, y + dy - 2.5f, 5, 5);
                    }
                }
                else if (current && review)
                {
                    // Waiting for you: a slow breathing red outline.
                    int alpha = (int)(110 + 110 * Math.Sin(t * 3.0));
                    Theme.Raised(g, box, node, Theme.S(3), Theme.Background);
                    using (var pen = new Pen(Color.FromArgb(Math.Max(30, alpha), Theme.Accent), Theme.S(2))) g.DrawEllipse(pen, box);
                    TextRenderer.DrawText(g, "?", Theme.Strong, Rectangle.Round(box), Theme.Accent2, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                else if (current && failed)
                {
                    Theme.Raised(g, box, node, Theme.S(3), Theme.Fail);
                    TextRenderer.DrawText(g, "!", Theme.Strong, Rectangle.Round(box), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                else if (current && stopped)
                {
                    Theme.Raised(g, box, node, Theme.S(3), Theme.Background);
                    using (var square = new SolidBrush(Theme.Muted)) g.FillRectangle(square, x[i] - Theme.S(4), y - Theme.S(4), Theme.S(8), Theme.S(8));
                }
                else
                {
                    Theme.Inset(g, box, node, Theme.Input, 3);
                }
                Color ink = current && (working || review) ? Theme.Accent2 : done ? Theme.Text : Theme.Faint;
                TextRenderer.DrawText(g, SongJob.StepNames[i], current ? Theme.Strong : Theme.Small,
                    new Rectangle((int)(x[i] - gap / 2), (int)(y + node + Theme.S(4)), (int)gap, Theme.S(18)), ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }

        private static void Check(Graphics g, RectangleF box, Color color)
        {
            using (var pen = new Pen(color, Theme.S(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[]
                {
                    new PointF(box.X + box.Width * 0.28f, box.Y + box.Height * 0.52f),
                    new PointF(box.X + box.Width * 0.44f, box.Y + box.Height * 0.68f),
                    new PointF(box.X + box.Width * 0.73f, box.Y + box.Height * 0.36f)
                });
        }

        private static void Status(SongJob job, out string word, out Color tone, out PillShape shape)
        {
            switch (job.Stage)
            {
                case Stage.Queued: word = "Queued"; tone = Theme.Na; shape = PillShape.Hollow; break;
                case Stage.Review: word = "Your turn"; tone = Theme.Accent2; shape = PillShape.Diamond; break;
                case Stage.Done: word = "In Apple Music"; tone = Theme.Pass; shape = PillShape.Circle; break;
                case Stage.Failed: word = "Failed"; tone = Theme.Fail; shape = PillShape.Diamond; break;
                case Stage.Stopped: word = "Stopped"; tone = Theme.Na; shape = PillShape.Dash; break;
                default: word = "Working"; tone = Theme.Info; shape = PillShape.Circle; break;
            }
        }

        private static string AlbumText(SongJob job)
        {
            if (String.IsNullOrEmpty(job.Album)) return "";
            return job.Album + (String.IsNullOrEmpty(job.Year) ? "" : " (" + job.Year + ")");
        }

        private static string Join(params string[] parts)
        {
            return String.Join(" \u00B7 ", parts.Where(p => !String.IsNullOrEmpty(p)).ToArray());
        }

        private static string Detail(SongJob job)
        {
            string tags = SongJob.GroupOf(job.Language, job.Genre);
            switch (job.Stage)
            {
                case Stage.Review:
                    return Join(job.Artist, AlbumText(job), tags ?? "No language or genre yet") + "  \u00B7  " + job.Message;
                case Stage.Done:
                    return Join(job.Artist, AlbumText(job), job.Message);
                case Stage.Failed:
                    return job.Error;
                default:
                    return (String.IsNullOrEmpty(job.Artist) ? "" : job.Artist + " · ") + job.Message;
            }
        }
    }
}
