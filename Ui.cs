using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YouTubeMp3
{
    // Neumorphism on a 60/30/10 palette: 60% soft cloud grey for the canvas and every raised surface, 30% slate for
    // text, icons and inset wells, 10% red for the one action and the step that is running now. Surfaces are the same
    // colour as the canvas and read through light (top-left) and dark (bottom-right) shadows.
    // Verdict colours always come with a word and a shape.
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(232, 236, 242);   // 60%: cloud grey #E8ECF2
        public static readonly Color Panel = Background;
        public static readonly Color Panel2 = Color.FromArgb(244, 246, 250);
        public static readonly Color Hover = Color.FromArgb(222, 227, 235);
        public static readonly Color Input = Color.FromArgb(225, 230, 238);        // inset wells
        public static readonly Color Line = Color.FromArgb(208, 215, 226);
        public static readonly Color Line2 = Color.FromArgb(188, 197, 212);
        public static readonly Color Text = Color.FromArgb(61, 75, 99);            // 30%: slate #3D4B63
        public static readonly Color Ink2 = Color.FromArgb(72, 86, 110);
        public static readonly Color Muted = Color.FromArgb(88, 101, 124);
        public static readonly Color Faint = Color.FromArgb(100, 113, 136);
        public static readonly Color Accent = Color.FromArgb(255, 61, 87);         // 10%: red #FF3D57
        public static readonly Color Accent2 = Color.FromArgb(210, 36, 62);        // red text on the light canvas
        public static readonly Color AccentFill = Color.FromArgb(217, 38, 64);     // button fill, white text passes AA
        public static readonly Color AccentHover = Color.FromArgb(232, 46, 72);
        public static readonly Color AccentPressed = Color.FromArgb(190, 30, 54);
        public static readonly Color AccentInk = Color.White;
        public static readonly Color AccentSoft = Mix(Accent, Background, 0.14f);
        public static readonly Color Pass = Color.FromArgb(22, 138, 90);
        public static readonly Color Fail = Color.FromArgb(204, 36, 60);
        public static readonly Color Warn = Color.FromArgb(161, 108, 0);
        public static readonly Color Info = Color.FromArgb(37, 99, 200);
        public static readonly Color Na = Color.FromArgb(110, 123, 145);
        public static readonly Color ShadowDark = Color.FromArgb(166, 172, 200);
        public static readonly Color ShadowLight = Color.White;

        // Names kept from 1.2 so the existing pages keep compiling.
        public static readonly Color Surface = Panel;
        public static readonly Color Sidebar = Panel;
        public static readonly Color Border = Line2;

        public static readonly string DefaultMusicFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "YouTube MP3");

        public static readonly float Dpi = ReadDpi();
        public static int S(int pixels) { return (int)Math.Round(pixels * Dpi); }

        // Keeps a window inside the screen it opens on (a portrait monitor is narrower than our default sizes).
        internal static void FitToScreen(Form form)
        {
            var area = form.Owner != null ? Screen.FromControl(form.Owner).WorkingArea : Screen.FromPoint(Cursor.Position).WorkingArea;
            int margin = S(12), width = area.Width - margin * 2, height = area.Height - margin * 2;
            form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, width), Math.Min(form.MinimumSize.Height, height));
            form.Size = new Size(Math.Min(form.Width, width), Math.Min(form.Height, height));
            int x = area.Left + (area.Width - form.Width) / 2, y = area.Top + (area.Height - form.Height) / 2;
            if (form.Owner != null)
            {
                x = form.Owner.Left + (form.Owner.Width - form.Width) / 2;
                y = form.Owner.Top + (form.Owner.Height - form.Height) / 2;
            }
            x = Math.Max(area.Left + margin, Math.Min(x, area.Right - margin - form.Width));
            y = Math.Max(area.Top + margin, Math.Min(y, area.Bottom - margin - form.Height));
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(x, y);
        }

        public static readonly Font Body = new Font("Segoe UI", 10f);
        public static readonly Font Small = new Font("Segoe UI", 9f);
        public static readonly Font Strong = new Font("Segoe UI Semibold", 10f);
        public static readonly Font CardTitle = new Font("Segoe UI Semibold", 12f);
        public static readonly Font PageTitle = new Font("Segoe UI Semibold", 20f);
        public static readonly Font Caps = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        public static readonly Font Big = new Font("Segoe UI Semibold", 21f);

        public static Color Lighten(Color c, float amount) { return Mix(Color.White, c, amount); }

        // A raised surface: a soft dark shadow down-right, a light one up-left, then the surface with a faint sheen.
        // Keep `depth` pixels of room around `r` inside the control, or the shadow is clipped.
        public static void Raised(Graphics g, RectangleF r, float radius, int depth, Color surface)
        {
            if (r.Width < 2 || r.Height < 2) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int layers = Math.Max(3, Math.Min(10, depth));
            float offset = depth * 0.5f, step = depth / (float)layers;
            int dark = Math.Max(2, 70 / layers), light = Math.Max(3, 170 / layers);
            for (int k = layers; k >= 1; k--)
            {
                float grow = k * step * 0.9f;
                using (var p = Rounded(new RectangleF(r.X + offset - grow, r.Y + offset - grow, r.Width + grow * 2, r.Height + grow * 2), radius + grow))
                using (var b = new SolidBrush(Color.FromArgb(dark, ShadowDark)))
                    g.FillPath(b, p);
                using (var p = Rounded(new RectangleF(r.X - offset - grow, r.Y - offset - grow, r.Width + grow * 2, r.Height + grow * 2), radius + grow))
                using (var b = new SolidBrush(Color.FromArgb(light, ShadowLight)))
                    g.FillPath(b, p);
            }
            using (var path = Rounded(r, radius))
            using (var fill = new LinearGradientBrush(new PointF(r.Left, r.Top), new PointF(r.Right + 1, r.Bottom + 1),
                Lighten(surface, 0.22f), Mix(ShadowDark, surface, 0.06f)))
                g.FillPath(fill, path);
        }

        // An inset well: the surface sinks in, dark along the inner top-left edge and light along the inner bottom-right.
        public static void Inset(Graphics g, RectangleF r, float radius, Color well, int depth)
        {
            if (r.Width < 2 || r.Height < 2) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(r, radius))
            {
                using (var fill = new SolidBrush(well)) g.FillPath(fill, path);
                var saved = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                int layers = Math.Max(2, depth);
                for (int k = 1; k <= layers; k++)
                {
                    float off = k * 0.9f;
                    using (var shifted = Rounded(new RectangleF(r.X + off, r.Y + off, r.Width, r.Height), radius))
                    using (var pen = new Pen(Color.FromArgb(Math.Max(4, 110 / layers), ShadowDark), 2f))
                        g.DrawPath(pen, shifted);
                    using (var shifted = Rounded(new RectangleF(r.X - off, r.Y - off, r.Width, r.Height), radius))
                    using (var pen = new Pen(Color.FromArgb(Math.Max(6, 200 / layers), ShadowLight), 2f))
                        g.DrawPath(pen, shifted);
                }
                g.Clip = saved;
                saved.Dispose();
            }
        }

        private static float ReadDpi()
        {
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f); }
            catch (Exception) { return 1f; }
        }

        public static Color Mix(Color top, Color bottom, float amount)
        {
            return Color.FromArgb(
                (int)(top.R * amount + bottom.R * (1 - amount)),
                (int)(top.G * amount + bottom.G * (1 - amount)),
                (int)(top.B * amount + bottom.B * (1 - amount)));
        }

        public static Label Label(string value, int size = 10, bool bold = false)
        {
            return new Label { Text = value, AutoSize = true, ForeColor = bold ? Text : Muted,
                Font = bold ? new Font("Segoe UI Semibold", size) : new Font("Segoe UI", size),
                Anchor = AnchorStyles.Left, Margin = Padding.Empty, UseMnemonic = false, BackColor = Color.Transparent };
        }

        public static Label CapsLabel(string value)
        {
            return new Label { Text = value.ToUpperInvariant(), AutoSize = true, ForeColor = Faint, Font = Caps,
                Margin = new Padding(0, S(4), 0, S(6)), UseMnemonic = false, BackColor = Color.Transparent };
        }

        public static PillButton Button(string label, int width, bool primary = false)
        {
            var button = new PillButton(primary ? ButtonKind.Primary : ButtonKind.Secondary) { Text = label };
            int text = TextRenderer.MeasureText(label, Strong).Width + S(40);
            button.Size = new Size(Math.Max(S(width), text), S(44));
            button.Margin = new Padding(0, 0, S(8), 0);
            return button;
        }

        public static void StyleTextBox(TextBox box)
        {
            box.BackColor = Input;
            box.ForeColor = Text;
            box.BorderStyle = BorderStyle.FixedSingle;
        }

        public static InputBox Field(TextBox box, string placeholder = null)
        {
            return new InputBox(box, placeholder);
        }

        // The approved icon at a given size, read from the app.ico embedded as a resource (frames 16-256 px).
        // PNG frames are decoded directly, which works on every .NET version.
        public static Bitmap AppImage(int size)
        {
            try
            {
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("appicon"))
                {
                    if (stream != null)
                    {
                        var data = new byte[stream.Length];
                        stream.Read(data, 0, data.Length);
                        int count = BitConverter.ToUInt16(data, 4), best = -1, bestSize = 0;
                        for (int i = 0; i < count; i++)
                        {
                            int side = data[6 + 16 * i] == 0 ? 256 : data[6 + 16 * i];
                            if (best < 0 || (bestSize < size ? side > bestSize : side >= size && side < bestSize)) { best = i; bestSize = side; }
                        }
                        if (best >= 0)
                        {
                            int length = BitConverter.ToInt32(data, 6 + 16 * best + 8), offset = BitConverter.ToInt32(data, 6 + 16 * best + 12);
                            if (length > 8 && data[offset] == 0x89 && data[offset + 1] == (byte)'P')
                                using (var png = new MemoryStream(data, offset, length))
                                using (var frame = new Bitmap(png))
                                    return new Bitmap(frame, size, size);
                            using (var icon = new Icon(new MemoryStream(data), size, size)) return icon.ToBitmap();
                        }
                    }
                }
            }
            catch (Exception ex) { if (!(ex is ArgumentException) && !(ex is IOException) && !(ex is ExternalException)) throw; }
            using (var fallback = AppIcon()) return fallback.ToBitmap();
        }

        public static Icon AppIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone(); }
            catch (Exception) { return (Icon)SystemIcons.Application.Clone(); }
        }

        public static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (d <= 1) { path.AddRectangle(bounds); return path; }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Color ParentColor(Control control)
        {
            for (var parent = control.Parent; parent != null; parent = parent.Parent)
                if (parent.BackColor.A == 255) return parent.BackColor;
            return Background;
        }

        // A verdict pill: a shape, a word and a colour, never colour alone.
        public static int DrawPill(Graphics g, int x, int y, string word, Color color, PillShape shape, Color behind)
        {
            word = word.ToUpperInvariant();
            var size = TextRenderer.MeasureText(g, word, Caps, Size.Empty, TextFormatFlags.NoPadding);
            int height = S(20), dot = S(7), pad = S(8);
            int width = pad + dot + S(6) + size.Width + pad;
            var box = new Rectangle(x, y, width, height);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(box, height / 2f))
            using (var fill = new SolidBrush(Mix(color, behind, 0.15f)))
                g.FillPath(fill, path);
            var mark = new Rectangle(x + pad, y + (height - dot) / 2, dot, dot);
            using (var brush = new SolidBrush(color))
            using (var pen = new Pen(color, Math.Max(1.5f, S(2) * 0.8f)))
            {
                switch (shape)
                {
                    case PillShape.Diamond:
                        g.FillPolygon(brush, new[] { new Point(mark.X + dot / 2, mark.Y - 1), new Point(mark.Right + 1, mark.Y + dot / 2),
                            new Point(mark.X + dot / 2, mark.Bottom + 1), new Point(mark.X - 1, mark.Y + dot / 2) });
                        break;
                    case PillShape.Hollow:
                        g.DrawRectangle(pen, mark.X + 0.5f, mark.Y + 0.5f, dot - 1.5f, dot - 1.5f);
                        break;
                    case PillShape.Dash:
                        g.FillRectangle(brush, mark.X, mark.Y + dot / 2 - 1, dot, Math.Max(2, S(2)));
                        break;
                    default:
                        g.FillEllipse(brush, mark);
                        break;
                }
            }
            TextRenderer.DrawText(g, word, Caps, new Rectangle(mark.Right + S(6), y, size.Width + 2, height), color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            return width;
        }

        public static int PillWidth(string word)
        {
            var size = TextRenderer.MeasureText(word.ToUpperInvariant(), Caps, Size.Empty, TextFormatFlags.NoPadding);
            return S(8) + S(7) + S(6) + size.Width + S(8);
        }

        public static void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor = Background;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Line;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Background;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Faint;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Background;
            grid.ColumnHeadersDefaultCellStyle.Font = Caps;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(S(6), 0, 0, 0);
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = S(36);
            grid.DefaultCellStyle.BackColor = Background;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.Font = Body;
            grid.DefaultCellStyle.Padding = new Padding(S(6), 0, S(4), 0);
            grid.DefaultCellStyle.SelectionBackColor = AccentSoft;
            grid.DefaultCellStyle.SelectionForeColor = Text;
            grid.RowTemplate.Height = S(36);
            grid.RowHeadersVisible = false;
            NativeTheme.DarkScrollbars(grid);
        }

        public static void StyleMenu(ContextMenuStrip menu)
        {
            menu.Renderer = new DarkMenuRenderer();
            menu.BackColor = Panel2;
            menu.ForeColor = Text;
            menu.Font = Body;
            menu.ShowImageMargin = false;
        }
    }

    internal enum PillShape { Circle, Diamond, Hollow, Dash }
    internal enum ButtonKind { Primary, Secondary, Ghost }

    // Soft rounded button. Red marks the one primary action on a screen.
    internal sealed class PillButton : Button
    {
        private bool hover, pressed;
        internal ButtonKind Kind;

        internal PillButton(ButtonKind kind)
        {
            Kind = kind;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = Theme.Strong;
            Cursor = Cursors.Hand;
            UseMnemonic = false;
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color behind = Theme.ParentColor(this);
            g.Clear(behind);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int pad = Theme.S(5);
            var box = new RectangleF(pad, pad - 1, Width - pad * 2, Height - pad * 2);
            float radius = Math.Min(box.Height / 2f, Theme.S(12));
            bool primary = Kind == ButtonKind.Primary;
            Color ink;
            if (!Enabled)
            {
                using (var path = Theme.Rounded(box, radius))
                using (var pen = new Pen(Theme.Line2))
                    g.DrawPath(pen, path);
                ink = Theme.Faint;
            }
            else if (pressed)
            {
                Theme.Inset(g, box, radius, primary ? Theme.AccentPressed : Theme.Input, 3);
                ink = primary ? Theme.AccentInk : Theme.Accent2;
            }
            else
            {
                if (Kind != ButtonKind.Ghost || hover)
                    Theme.Raised(g, box, radius, hover ? Theme.S(6) : Theme.S(4), primary ? (hover ? Theme.AccentHover : Theme.AccentFill) : behind);
                ink = primary ? Theme.AccentInk : hover ? Theme.Accent2 : Theme.Text;
            }
            if (Focused && ShowFocusCues)
                using (var path = Theme.Rounded(new RectangleF(box.X - 2, box.Y - 2, box.Width + 4, box.Height + 4), radius + 2))
                using (var ring = new Pen(Color.FromArgb(170, Theme.Accent), 1.5f))
                    g.DrawPath(ring, path);
            TextRenderer.DrawText(g, Text, Font, Rectangle.Round(box), ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // Opaque rounded container. Children sit inside its padding; a title, if set, is drawn in the header row.
    internal class Card : Panel
    {
        private string title = "";
        private readonly FlowLayoutPanel header;
        internal int TitleIndent { get; set; }

        internal Card(string heading = null)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Margin = new Padding(0, 0, Theme.S(14), Theme.S(14));
            header = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                BackColor = Theme.Panel, Margin = Padding.Empty, Padding = Padding.Empty, FlowDirection = FlowDirection.RightToLeft };
            Controls.Add(header);
            Title = heading;
        }

        internal string Title
        {
            get { return title; }
            set
            {
                title = value ?? "";
                int pad = Theme.S(28);
                Padding = new Padding(pad, title.Length == 0 ? pad : Theme.S(62), pad, pad);
                Invalidate();
            }
        }

        // Sizes the card to its content: the content docks to the top and the card grows with it.
        private Control fitted;

        internal void Fit(Control content)
        {
            fitted = content;
            content.Dock = DockStyle.Top;
            Controls.Add(content);
            content.SizeChanged += delegate { Height = Padding.Vertical + content.Height; };
            Height = Padding.Vertical + content.Height;
        }

        // A fitted card is exactly as tall as its content, whatever the layout engine would guess from child bounds.
        public override Size GetPreferredSize(Size proposedSize)
        {
            if (fitted == null || Dock == DockStyle.Fill) return base.GetPreferredSize(proposedSize);
            return new Size(proposedSize.Width > 0 && proposedSize.Width < Int32.MaxValue ? proposedSize.Width : Width,
                            Padding.Vertical + fitted.Height);
        }

        internal void Fill(Control content)
        {
            content.Dock = DockStyle.Fill;
            Controls.Add(content);
        }

        internal void AddHeaderAction(Control control)
        {
            control.Margin = new Padding(Theme.S(8), 0, 0, 0);
            header.Controls.Add(control);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (fitted != null && Dock != DockStyle.Fill && Height != Padding.Vertical + fitted.Height)
                Height = Padding.Vertical + fitted.Height;
            header.Visible = header.Controls.Count > 0;
            header.Location = new Point(Width - Theme.S(28) - header.Width, Theme.S(20));
            header.BringToFront();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.ParentColor(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Raised(g, new RectangleF(Theme.S(10), Theme.S(9), Width - Theme.S(20), Height - Theme.S(20)), Theme.S(20), Theme.S(9), Theme.Panel);
            if (title.Length > 0)
            {
                int x = Theme.S(28) + TitleIndent;
                using (var dot = new SolidBrush(Theme.Accent)) g.FillEllipse(dot, x, Theme.S(33), Theme.S(7), Theme.S(7));
                TextRenderer.DrawText(g, title, Theme.CardTitle, new Point(x + Theme.S(14), Theme.S(25)), Theme.Text, TextFormatFlags.NoPrefix);
            }
        }
    }

    // A strip of joined count tiles, like a quote board. A count that is not zero takes its colour; a zero is grey,
    // so what needs you is what stands out.
    internal sealed class StatStrip : Control
    {
        private readonly List<string> labels = new List<string>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> values = new List<int>();

        internal StatStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Height = Theme.S(84);
            Margin = new Padding(0, 0, 0, Theme.S(14));
            TabStop = false;
        }

        internal int Add(string label, Color color)
        {
            labels.Add(label.ToUpperInvariant());
            colors.Add(color);
            values.Add(0);
            return labels.Count - 1;
        }

        internal void Set(int index, int value)
        {
            if (values[index] == value) return;
            values[index] = value;
            var parts = new List<string>();
            for (int i = 0; i < labels.Count; i++) parts.Add(labels[i] + " " + values[i]);
            AccessibleDescription = String.Join(", ", parts.ToArray());
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.ParentColor(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (labels.Count == 0) return;
            Theme.Raised(g, new RectangleF(Theme.S(8), Theme.S(7), Width - Theme.S(16), Height - Theme.S(16)), Theme.S(16), Theme.S(7), Theme.Panel);
            using (var edge = new Pen(Theme.Line))
            {
                float width = (Width - 1f) / labels.Count;
                for (int i = 0; i < labels.Count; i++)
                {
                    var cell = new Rectangle((int)(i * width), 0, (int)width, Height);
                    if (i > 0) g.DrawLine(edge, cell.X, Theme.S(12), cell.X, Height - Theme.S(12));
                    bool zero = values[i] == 0;
                    Color c = zero ? Theme.Mix(Theme.Faint, Theme.Panel, 0.7f) : colors[i];
                    TextRenderer.DrawText(g, values[i].ToString(), Theme.Big,
                        new Rectangle(cell.X, Theme.S(10), cell.Width, Theme.S(40)), c,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom);
                    TextRenderer.DrawText(g, labels[i], Theme.Caps, new Rectangle(cell.X, Theme.S(52), cell.Width, Theme.S(18)),
                        Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
                    if (!zero)
                    {
                        using (var glow = new SolidBrush(Color.FromArgb(60, colors[i])))
                            g.FillRectangle(glow, cell.X + cell.Width / 2 - Theme.S(20), Height - Theme.S(6), Theme.S(40), Theme.S(5));
                        using (var bar = new SolidBrush(colors[i]))
                            g.FillRectangle(bar, cell.X + cell.Width / 2 - Theme.S(14), Height - Theme.S(4), Theme.S(28), Theme.S(2));
                    }
                }
            }
        }
    }

    // A text box on the pressed input surface, with a red edge while it has focus.
    internal sealed class InputBox : Panel
    {
        internal readonly TextBox Box;
        private readonly Label hint;

        internal InputBox(TextBox box, string placeholder)
        {
            Box = box;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Input;
            Margin = new Padding(0, 0, Theme.S(8), 0);
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Theme.Input;
            box.ForeColor = Theme.Text;
            box.Font = Theme.Body;
            box.Dock = DockStyle.Fill;
            box.Margin = Padding.Empty;
            Padding = new Padding(Theme.S(16), Theme.S(box.Multiline ? 12 : 11), Theme.S(10), Theme.S(8));
            Height = box.Multiline ? Theme.S(120) : Theme.S(42);
            Controls.Add(box);
            if (!String.IsNullOrEmpty(placeholder))
            {
                hint = new Label { Text = placeholder, ForeColor = Theme.Faint, BackColor = Theme.Input, Font = Theme.Body,
                    AutoSize = false, UseMnemonic = false, Cursor = Cursors.IBeam,
                    Location = new Point(Padding.Left, Padding.Top), Margin = Padding.Empty };
                if (!box.Multiline) hint.TextAlign = ContentAlignment.TopLeft;
                Controls.Add(hint);
                hint.BringToFront();
                hint.Click += delegate { box.Focus(); };
                box.TextChanged += delegate { UpdateHint(); };
                box.GotFocus += delegate { UpdateHint(); };
                box.LostFocus += delegate { UpdateHint(); };
            }
            box.GotFocus += delegate { Invalidate(); };
            box.LostFocus += delegate { Invalidate(); };
            box.EnabledChanged += delegate { Invalidate(); };
            if (box.Multiline) NativeTheme.DarkScrollbars(box);
        }

        private void UpdateHint()
        {
            if (hint != null) hint.Visible = Box.TextLength == 0;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (hint != null)
                hint.Size = new Size(Math.Max(0, Width - Padding.Horizontal), Math.Max(0, Height - Padding.Vertical));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.ParentColor(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var well = new RectangleF(1f, 1f, Width - 2.5f, Height - 2.5f);
            Theme.Inset(g, well, Theme.S(14), Theme.Input, 4);
            if (Box.Focused)
                using (var path = Theme.Rounded(well, Theme.S(14)))
                using (var pen = new Pen(Color.FromArgb(150, Theme.Accent), 1.4f))
                    g.DrawPath(pen, path);
        }
    }

    // Segmented switch: the selected option is white text on red.
    internal sealed class Segmented : Control
    {
        private readonly string[] options;
        private int selected, hover = -1;
        internal event EventHandler SelectedIndexChanged;

        internal Segmented(params string[] choices)
        {
            options = choices;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            TabStop = true;
            Font = Theme.Strong;
            Cursor = Cursors.Hand;
            Height = Theme.S(42);
            int width = Theme.S(12);
            foreach (var option in options) width += TextRenderer.MeasureText(option, Font).Width + Theme.S(26);
            Width = width;
            Margin = new Padding(0, 0, Theme.S(8), 0);
            AccessibleRole = AccessibleRole.PageTabList;
        }

        internal int SelectedIndex
        {
            get { return selected; }
            set
            {
                value = Math.Max(0, Math.Min(options.Length - 1, value));
                if (value == selected) return;
                selected = value;
                AccessibleName = options[selected];
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        internal string SelectedText { get { return options[selected]; } }

        internal void Select(string option)
        {
            int index = Array.IndexOf(options, option);
            if (index >= 0) SelectedIndex = index;
        }

        private Rectangle Segment(int index)
        {
            int inner = Width - Theme.S(12);
            var widths = new int[options.Length];
            int natural = 0;
            for (int i = 0; i < options.Length; i++) { widths[i] = TextRenderer.MeasureText(options[i], Font).Width + Theme.S(22); natural += widths[i]; }
            float extra = (inner - natural) / (float)options.Length;
            float x = Theme.S(6);
            for (int i = 0; i < index; i++) x += widths[i] + extra;
            return new Rectangle((int)x, Theme.S(6), (int)(widths[index] + extra), Height - Theme.S(12));
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < options.Length; i++) if (Segment(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e) { int h = HitTest(e.Location); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { Focus(); int h = HitTest(e.Location); if (h >= 0 && Enabled) SelectedIndex = h; base.OnMouseDown(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) SelectedIndex = selected - 1;
            if (e.KeyCode == Keys.Right) SelectedIndex = selected + 1;
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.ParentColor(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new RectangleF(1f, 1f, Width - 2.5f, Height - 2.5f);
            Theme.Inset(g, track, Theme.S(14), Theme.Input, 3);
            if (Focused && ShowFocusCues)
                using (var path = Theme.Rounded(track, Theme.S(14)))
                using (var pen = new Pen(Color.FromArgb(150, Theme.Accent), 1.4f))
                    g.DrawPath(pen, path);
            for (int i = 0; i < options.Length; i++)
            {
                var r = Segment(i);
                bool on = i == selected;
                if (on) Theme.Raised(g, new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), Theme.S(10), Theme.S(3), Theme.Background);
                Color ink = !Enabled ? Theme.Faint : on ? Theme.Accent2 : i == hover ? Theme.Text : Theme.Muted;
                TextRenderer.DrawText(g, options[i], Font, r, ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // On/off switch with a red knob.
    internal sealed class Toggle : CheckBox
    {
        internal Toggle(string label)
        {
            Text = label;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AutoSize = false;
            Font = Theme.Body;
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
            Height = Theme.S(34);
            Width = Theme.S(70) + TextRenderer.MeasureText(label, Font).Width;
            Margin = new Padding(0, Theme.S(4), 0, Theme.S(4));
        }

        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.ParentColor(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new RectangleF(Theme.S(2), (Height - Theme.S(24)) / 2f, Theme.S(46), Theme.S(24));
            Theme.Inset(g, track, track.Height / 2, Checked ? Theme.Mix(Theme.Accent, Theme.Input, 0.30f) : Theme.Input, 3);
            if (Focused && ShowFocusCues)
                using (var path = Theme.Rounded(track, track.Height / 2))
                using (var pen = new Pen(Color.FromArgb(150, Theme.Accent), 1.4f))
                    g.DrawPath(pen, path);
            float knob = track.Height - Theme.S(6);
            float x = Checked ? track.Right - knob - Theme.S(3) : track.X + Theme.S(3);
            var knobBox = new RectangleF(x, track.Y + Theme.S(3), knob, knob);
            Theme.Raised(g, knobBox, knob / 2, Theme.S(2), Theme.Background);
            if (Checked)
                using (var dot = new SolidBrush(Theme.Accent))
                    g.FillEllipse(dot, knobBox.X + knob / 2 - Theme.S(3), knobBox.Y + knob / 2 - Theme.S(3), Theme.S(6), Theme.S(6));
            TextRenderer.DrawText(g, Text, Font, new Rectangle(Theme.S(60), 0, Width - Theme.S(60), Height),
                Enabled ? ForeColor : Theme.Faint, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    // Thin progress line: accent fill on a dark track, or a sweeping segment while the work has no percentage.
    internal sealed class ProgressLine : Control
    {
        private int value = -2;
        private int sweep;
        private readonly Timer timer = new Timer { Interval = 30 };

        internal ProgressLine()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Height = Theme.S(6);
            TabStop = false;
            timer.Tick += delegate { sweep = (sweep + Theme.S(6)) % Math.Max(1, Width + Theme.S(120)); Invalidate(); };
        }

        // -2 hidden, -1 busy without a percentage, 0..100 percent.
        internal int Value
        {
            get { return value; }
            set
            {
                if (this.value == value) return;
                this.value = value;
                timer.Enabled = value == -1 && !DesignMode;
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.ParentColor(this));
            if (value == -2) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Inset(g, new RectangleF(0, 0, Width - 0.5f, Height - 0.5f), Height / 2f, Theme.Input, 2);
            using (var fill = new LinearGradientBrush(new Point(0, 0), new Point(Math.Max(1, Width), 0), Theme.AccentHover, Theme.Accent))
            {
                if (value == -1)
                {
                    var clip = g.Clip;
                    g.SetClip(new Rectangle(0, 0, Width, Height));
                    using (var path = Theme.Rounded(new RectangleF(sweep - Theme.S(120), 0, Theme.S(120), Height), Height / 2f))
                        g.FillPath(fill, path);
                    g.Clip = clip;
                }
                else if (value > 0)
                    using (var path = Theme.Rounded(new RectangleF(0, 0, Math.Max(Height, Width * value / 100f), Height), Height / 2f))
                        g.FillPath(fill, path);
            }
        }
    }

    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(Theme.Panel2); }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.Line2))
                e.Graphics.DrawRectangle(pen, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var bounds = new Rectangle(Theme.S(3), 0, e.Item.Width - Theme.S(6), e.Item.Height);
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Rounded(bounds, Theme.S(6)))
            using (var brush = new SolidBrush(Theme.Hover))
                e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Faint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.Line2))
                e.Graphics.DrawLine(pen, Theme.S(8), e.Item.Height / 2, e.Item.Width - Theme.S(8), e.Item.Height / 2);
        }
    }

    // Windows 10/11 native polish. Every call is optional and silently skipped where unsupported.
    internal static class NativeTheme
    {
        internal static void DarkTitleBar(Form form)
        {
            try
            {
                // Light title bar in the canvas colour with slate text (Windows 11; silently skipped elsewhere).
                int off = 0;
                if (DwmSetWindowAttribute(form.Handle, 20, ref off, 4) != 0) DwmSetWindowAttribute(form.Handle, 19, ref off, 4);
                int caption = Theme.Background.R | (Theme.Background.G << 8) | (Theme.Background.B << 16);
                DwmSetWindowAttribute(form.Handle, 35, ref caption, 4);
                int text = Theme.Text.R | (Theme.Text.G << 8) | (Theme.Text.B << 16);
                DwmSetWindowAttribute(form.Handle, 36, ref text, 4);
            }
            catch (Exception ex) { if (!IsInteropFailure(ex)) throw; }
        }

        internal static void DarkScrollbars(Control control)
        {
            EventHandler apply = delegate
            {
                try { SetWindowTheme(control.Handle, "Explorer", null); }
                catch (Exception ex) { if (!IsInteropFailure(ex)) throw; }
            };
            if (control.IsHandleCreated) apply(control, EventArgs.Empty);
            control.HandleCreated += apply;
        }

        internal static bool IsInteropFailure(Exception ex)
        {
            return ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is COMException ||
                   ex is InvalidCastException || ex is NotSupportedException || ex is NotImplementedException;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr window, string app, string idList);
    }

    // Shows batch progress on the taskbar button.
    internal static class TaskbarProgress
    {
        private static ITaskbarList3 taskbar;
        private static bool failed;

        internal static void Set(Form form, int percent, bool error = false)
        {
            if (failed || form == null || !form.IsHandleCreated) return;
            try
            {
                if (taskbar == null)
                {
                    taskbar = (ITaskbarList3)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11d0-958A-006097C9A090")));
                    taskbar.HrInit();
                }
                if (percent < -1) taskbar.SetProgressState(form.Handle, 0);
                else if (percent == -1) taskbar.SetProgressState(form.Handle, 1);
                else
                {
                    taskbar.SetProgressState(form.Handle, error ? 4 : 2);
                    taskbar.SetProgressValue(form.Handle, (ulong)Math.Max(0, Math.Min(100, percent)), 100);
                }
            }
            catch (Exception ex)
            {
                if (!NativeTheme.IsInteropFailure(ex) && !(ex is ArgumentException) && !(ex is TypeLoadException)) throw;
                failed = true;
            }
        }

        [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
            void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
            void SetProgressState(IntPtr hwnd, int state);
        }
    }

    internal static class Layouts
    {
        // A single column that grows with its rows; each row fills the width unless told otherwise.
        internal static TableLayoutPanel Stack()
        {
            var stack = new TableLayoutPanel { ColumnCount = 1, RowCount = 0, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return stack;
        }

        internal static T Add<T>(TableLayoutPanel stack, T control, bool fill = true, int gap = 10) where T : Control
        {
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            control.Anchor = fill ? AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top : AnchorStyles.Left | AnchorStyles.Top;
            control.Margin = new Padding(control.Margin.Left, control.Margin.Top, fill ? 0 : control.Margin.Right, Theme.S(gap));
            stack.Controls.Add(control, 0, stack.RowCount++);
            return control;
        }

        // The main control stretches; trailing buttons keep their size.
        internal static TableLayoutPanel Row(Control main, params Control[] trailing)
        {
            var row = new TableLayoutPanel { ColumnCount = 1 + trailing.Length, RowCount = 1, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            main.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            row.Controls.Add(main, 0, 0);
            for (int i = 0; i < trailing.Length; i++)
            {
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                trailing[i].Anchor = AnchorStyles.Left;
                trailing[i].Margin = new Padding(i == 0 ? 0 : 0, 0, i == trailing.Length - 1 ? 0 : Theme.S(8), 0);
                row.Controls.Add(trailing[i], i + 1, 0);
            }
            return row;
        }

        internal static FlowLayoutPanel Buttons(params Control[] buttons)
        {
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true,
                Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent };
            row.Controls.AddRange(buttons);
            foreach (Control button in buttons) button.Margin = new Padding(0, 0, Theme.S(8), Theme.S(4));
            return row;
        }

        internal static WrapLabel Note(string text, Color? color = null)
        {
            return new WrapLabel { Text = text, ForeColor = color ?? Theme.Muted, Font = Theme.Small,
                UseMnemonic = false, BackColor = Color.Transparent, Margin = Padding.Empty };
        }

        internal static Control Header(string title, string subtitle)
        {
            var stack = Stack();
            stack.Dock = DockStyle.Top;
            stack.Padding = new Padding(0, 0, 0, Theme.S(14));
            Add(stack, new Label { Text = title, Font = Theme.PageTitle, ForeColor = Theme.Text, AutoSize = true,
                UseMnemonic = false, Margin = Padding.Empty, BackColor = Color.Transparent }, true, 2);
            Add(stack, new Label { Text = subtitle, Font = Theme.Body, ForeColor = Theme.Muted, AutoSize = true,
                UseMnemonic = false, Margin = Padding.Empty }, true, 0);
            return stack;
        }
    }

    // A one-line notice with a verdict pill, text and an optional action (Phoenix .notice).
    internal sealed class Notice : Panel
    {
        private string word = "Info";
        private Color color = Theme.Info;
        private PillShape shape = PillShape.Circle;
        internal readonly Label Message = new Label { AutoSize = false, Dock = DockStyle.Fill, ForeColor = Theme.Ink2,
            Font = Theme.Body, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, AutoEllipsis = true };
        internal readonly ProgressLine Progress = new ProgressLine { Dock = DockStyle.Bottom };
        private readonly FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true,
            WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, Theme.S(8), 0, 0) };

        internal Notice()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Height = Theme.S(70);
            Margin = new Padding(0, 0, 0, Theme.S(10));
            BackColor = Theme.Panel;
            Controls.Add(Message);
            Controls.Add(actions);
            Controls.Add(Progress);
            Message.BackColor = actions.BackColor = Theme.Panel;
            Progress.Height = Theme.S(3);
            Show("Info", Theme.Info, PillShape.Circle, "");
        }

        internal void AddAction(Control control) { control.Margin = new Padding(Theme.S(8), 0, 0, 0); actions.Controls.Add(control); }

        internal void Show(string verdict, Color tone, PillShape mark, string text)
        {
            word = verdict; color = tone; shape = mark;
            Message.Text = text;
            Padding = new Padding(Theme.S(32) + Theme.PillWidth(word), Theme.S(12), Theme.S(20), Theme.S(12));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.ParentColor(this));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Raised(g, new RectangleF(Theme.S(7), Theme.S(6), Width - Theme.S(14), Height - Theme.S(14)), Theme.S(16), Theme.S(6), Theme.Panel);
            Theme.DrawPill(g, Theme.S(20), (Height - Theme.S(20)) / 2 - 1, word, color, shape, Theme.Panel);
        }
    }

    // A label that wraps to its column and sizes its own height, so cards grow to fit their text.
    // Set Wrap = false for one-line labels that are docked or sized by their container.
    internal sealed class WrapLabel : Label
    {
        private Control host;
        internal bool Wrap = true;

        internal WrapLabel() { AutoSize = false; }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); }

        protected override void OnParentChanged(EventArgs e)
        {
            if (host != null) host.SizeChanged -= HostResized;
            host = Parent;
            if (host != null) host.SizeChanged += HostResized;
            base.OnParentChanged(e);
            Fit();
        }

        private void HostResized(object sender, EventArgs e) { Fit(); }

        private void Fit()
        {
            if (!Wrap || host == null || Dock != DockStyle.None) return;
            int width = host.ClientSize.Width - host.Padding.Horizontal - Margin.Horizontal;
            if (width < 40) return;
            int height = TextRenderer.MeasureText(Text.Length == 0 ? " " : Text, Font, new Size(width, Int32.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height;
            if (Size.Width != width || Size.Height != height) Size = new Size(width, height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (!Wrap) { base.OnPaint(e); return; }
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
        }
    }
}
