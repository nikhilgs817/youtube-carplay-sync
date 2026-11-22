using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace YouTubeMp3
{
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(15, 23, 39);
        public static readonly Color Surface = Color.FromArgb(25, 38, 58);
        public static readonly Color Sidebar = Color.FromArgb(19, 30, 47);
        public static readonly Color Border = Color.FromArgb(53, 71, 98);
        public static readonly Color Text = Color.FromArgb(238, 245, 253);
        public static readonly Color Muted = Color.FromArgb(166, 185, 209);
        public static readonly Color Accent = Color.FromArgb(104, 225, 203);
        public static readonly string DefaultMusicFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "YouTube MP3");

        public static Label Label(string value, int size = 10, bool bold = false)
        {
            return new Label { Text = value, AutoSize = true, ForeColor = bold ? Text : Muted,
                Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        }

        public static Button Button(string label, int width, bool primary = false)
        {
            var button = new Button { Text = label, Width = width, Height = 34,
                Font = new Font("Segoe UI Semibold", 10), FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Surface, ForeColor = primary ? Background : Text,
                Margin = new Padding(0, 0, 8, 0), Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = primary ? Accent : Border;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(146, 246, 226) : Border;
            return button;
        }

        public static void StyleTextBox(TextBox box)
        {
            box.BackColor = Surface;
            box.ForeColor = Text;
            box.BorderStyle = BorderStyle.FixedSingle;
        }

        public static Icon AppIcon()
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
        }
    }

    internal sealed class MainWindow : Form
    {
        private readonly DownloadPage downloads;
        private readonly MusicPage organizer;
        private readonly Button downloadNav = Theme.Button("  Download MP3", 150);
        private readonly Button organizeNav = Theme.Button("  Organize music", 150);
        private readonly Image iconImage;
        private bool closing;

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow());
        }

        public MainWindow()
        {
            Text = "YouTube MP3";
            Icon = Theme.AppIcon();
            iconImage = Icon.ToBitmap();
            Font = new Font("Segoe UI", 10);
            ClientSize = new Size(1030, 738);
            MinimumSize = new Size(840, 685);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;

            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(shell);

            var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Sidebar,
                FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty,
                Padding = new Padding(16, 24, 14, 0) };
            nav.Controls.Add(new PictureBox { Image = iconImage, SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(48, 48), Margin = new Padding(2, 0, 0, 9) });
            var brand = Theme.Label("YouTube MP3", 14, true);
            brand.Margin = new Padding(3, 0, 0, 24);
            nav.Controls.Add(brand);
            foreach (Button button in new[] { downloadNav, organizeNav })
            {
                button.Height = 42;
                button.TextAlign = ContentAlignment.MiddleLeft;
                button.Margin = new Padding(0, 0, 0, 8);
                nav.Controls.Add(button);
            }
            shell.Controls.Add(nav, 0, 0);

            downloads = new DownloadPage();
            organizer = new MusicPage(() => downloads.OutputFolder);
            var content = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            content.Controls.Add(downloads);
            content.Controls.Add(organizer);
            shell.Controls.Add(content, 1, 0);
            downloadNav.Click += delegate { ShowOrganizer(false); };
            organizeNav.Click += delegate { ShowOrganizer(true); };
            downloads.DownloadComplete += FinishClosing;
            organizer.WorkComplete += FinishClosing;
            FormClosing += OnClosing;
            ShowOrganizer(false);
        }

        private void ShowOrganizer(bool show)
        {
            organizer.Visible = show;
            downloads.Visible = !show;
            (show ? (Control)organizer : downloads).BringToFront();
            StyleNav(organizeNav, show);
            StyleNav(downloadNav, !show);
        }

        private static void StyleNav(Button button, bool selected)
        {
            button.BackColor = selected ? Theme.Border : Theme.Sidebar;
            button.ForeColor = selected ? Theme.Accent : Theme.Text;
            button.FlatAppearance.BorderColor = button.BackColor;
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!downloads.IsBusy && !organizer.IsBusy) return;
            e.Cancel = true;
            closing = true;
            downloads.CancelBatch();
            organizer.CancelWork();
        }

        private void FinishClosing()
        {
            if (closing && !downloads.IsBusy && !organizer.IsBusy) Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && iconImage != null) iconImage.Dispose();
            base.Dispose(disposing);
        }
    }
}
