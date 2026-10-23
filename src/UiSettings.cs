using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;

namespace Diskoversikt
{
    public sealed class UiSettings
    {
        public string LastPath = "";
        public int Left = -1, Top = -1, Width = 1220, Height = 900;
        public bool Maximized, SortDisk = true;
        public int[] Columns = { 145, 145, 150, 90, 115 };
        public static string FilePath
        {
            get { return Environment.GetEnvironmentVariable("DISKKART_SETTINGS_PATH") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Diskkart", "settings.xml"); }
        }
        public static UiSettings Load()
        {
            try
            {
                var options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536 };
                using (var reader = XmlReader.Create(FilePath, options)) return (UiSettings)new XmlSerializer(typeof(UiSettings)).Deserialize(reader);
            }
            catch (Exception ex) { if (!(ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is XmlException || ex is System.Security.SecurityException)) throw; return new UiSettings(); }
        }
        public bool Save()
        {
            try
            {
                string file = Path.GetFullPath(FilePath); Directory.CreateDirectory(Path.GetDirectoryName(file));
                string temp = file + ".tmp";
                using (var writer = new StreamWriter(temp, false, System.Text.Encoding.UTF8)) new XmlSerializer(typeof(UiSettings)).Serialize(writer, this);
                if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
                return true;
            }
            catch (Exception ex) { if (!(ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is System.Security.SecurityException)) throw; return false; }
        }
    }

    public sealed partial class MainWindow
    {
        readonly UiSettings preferences = UiSettings.Load();
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400, ReshowDelay = 100 };
        readonly Label capacityValue = new Label(), usedValue = new Label(), freeValue = new Label();
        int[] columnWidths = { 145, 145, 150, 90, 115 };
        int dragColumn = -1, dragX;
        bool restoring;

        void InitializePolish()
        {
            restoring = true;
            try
            {
                using (Stream source = typeof(MainWindow).Assembly.GetManifestResourceStream("Diskkart.ico"))
                    if (source != null) { using (var icon = new Icon(source)) Icon = (Icon)icon.Clone(); }
            }
            catch (ArgumentException) { }
            if (preferences.Columns != null && preferences.Columns.Length == 5)
                for (int i = 0; i < 5; i++) columnWidths[i] = Math.Max(MinColumn(i), Math.Min(240, preferences.Columns[i]));
            Size = new Size(Math.Max(MinimumSize.Width, Math.Min(2000, preferences.Width)), Math.Max(MinimumSize.Height, Math.Min(1400, preferences.Height)));
            Rectangle saved = new Rectangle(preferences.Left, preferences.Top, Width, Height);
            if (preferences.Left >= 0 && Screen.AllScreens.Any(s => Rectangle.Intersect(s.WorkingArea, saved).Width > 180 && Rectangle.Intersect(s.WorkingArea, saved).Height > 120))
            { StartPosition = FormStartPosition.Manual; Location = saved.Location; }
            if (preferences.Maximized) WindowState = FormWindowState.Maximized;
            disk.Checked = preferences.SortDisk;
            tips.SetToolTip(header, "Dra skillelinjene for å endre kolonnebredder. På diskroten betyr prosenten hvor mye som er målt; på andre rader vises andelen.");
            tips.SetToolTip(coverage, "Målt er filene appen har kartlagt. Resten kan omfatte utilgjengelige filer, systemdata og måleavvik. Se Merknader for forklaring.");
            tips.SetToolTip(usageBar, "Blå: målt plass. Oransje: ikke gjort rede for. Grå: ledig plass. Ved undermappeskanning vises bare diskens brukte og ledige plass.");
            tips.SetToolTip(disk, "Bytt mellom plass brukt lokalt på disken og filenes logiske størrelse. Rotens målte prosent bruker alltid plass på disk.");
            header.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragColumn = HeaderBoundary(e.X); dragX = e.X; header.Capture = dragColumn >= 0; } };
            header.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (dragColumn >= 0 && header.Capture) { ResizeColumn(dragColumn, e.X - dragX); dragX = e.X; }
                else header.Cursor = HeaderBoundary(e.X) >= 0 ? Cursors.VSplit : Cursors.Default;
            };
            header.MouseUp += delegate { dragColumn = -1; header.Capture = false; };
            var images = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
            images.Images.Add(FolderImage());
            images.Images.Add(DiskImage());
            var unknown = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(unknown)) using (Brush fill = new SolidBrush(Color.FromArgb(238, 176, 92))) { g.FillEllipse(fill, 2, 2, 12, 12); }
            images.Images.Add(unknown);
            tree.ImageList = images;
            Shown += delegate { FitColumns(); };
            FormClosed += delegate
            {
                Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                preferences.Left = bounds.Left; preferences.Top = bounds.Top; preferences.Width = bounds.Width; preferences.Height = bounds.Height;
                preferences.Maximized = WindowState == FormWindowState.Maximized; preferences.SortDisk = disk.Checked;
                preferences.Columns = (int[])columnWidths.Clone(); preferences.Save();
                tips.Dispose(); images.Dispose(); timer.Dispose();
            };
            restoring = false;
        }
        static int MinColumn(int i) { return new int[] { 105, 105, 125, 65, 85 }[i]; }
        void FitColumns()
        {
            if (!IsHandleCreated) return;
            int excess = columnWidths.Sum() - (tree.ClientSize.Width - 220);
            for (int i = 0; excess > 0 && i < columnWidths.Length; i++) { int remove = Math.Min(excess, columnWidths[i] - MinColumn(i)); columnWidths[i] -= remove; excess -= remove; }
            header.Invalidate(); tree.Invalidate();
        }
        int HeaderBoundary(int x)
        {
            int edge = NameEnd;
            for (int i = 0; i < 5; i++) { if (Math.Abs(x - edge) <= 5) return i; edge += columnWidths[i]; }
            return -1;
        }
        internal void ResizeColumn(int boundary, int delta)
        {
            if (boundary < 0 || boundary >= 5) return;
            if (boundary == 0)
            {
                delta = Math.Max(220 - NameEnd, Math.Min(columnWidths[0] - MinColumn(0), delta));
                columnWidths[0] -= delta;
            }
            else
            {
                delta = Math.Max(MinColumn(boundary - 1) - columnWidths[boundary - 1], Math.Min(columnWidths[boundary] - MinColumn(boundary), delta));
                columnWidths[boundary - 1] += delta; columnWidths[boundary] -= delta;
            }
            header.Invalidate(); tree.Invalidate();
        }
        Rectangle Column(int index, int y, int height)
        {
            int x = NameEnd; for (int i = 0; i < index; i++) x += columnWidths[i];
            return new Rectangle(x + 3, y, Math.Max(1, columnWidths[index] - 6), height);
        }
        static Bitmap FolderImage()
        {
            var bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Brush fill = new SolidBrush(Color.FromArgb(225, 173, 78)))
            using (Brush front = new SolidBrush(Color.FromArgb(248, 209, 123)))
            { g.FillRectangle(fill, 1, 3, 7, 4); g.FillRectangle(fill, 1, 5, 14, 9); g.FillRectangle(front, 2, 7, 13, 7); }
            return bitmap;
        }
        static Bitmap DiskImage()
        {
            var bitmap = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Brush dark = new SolidBrush(Color.FromArgb(27, 53, 75)))
            using (Brush light = new SolidBrush(Color.FromArgb(237, 245, 250)))
            using (Brush orange = new SolidBrush(Color.FromArgb(245, 179, 93)))
            using (var line = new Pen(Color.FromArgb(112, 191, 229), 2))
            {
                g.FillRectangle(dark, 1, 1, 14, 14); g.DrawLines(line, new Point[] { new Point(5, 3), new Point(5, 8), new Point(11, 8) });
                g.DrawLine(line, 5, 4, 11, 4); g.FillRectangle(orange, 10, 7, 3, 3); g.FillRectangle(light, 4, 11, 8, 2);
            }
            return bitmap;
        }
        Control StatCard(string label, Label value)
        {
            var card = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = Color.White, Margin = new Padding(0, 0, 10, 0), Padding = new Padding(10, 5, 10, 3) };
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 19)); card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            card.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, ForeColor = Color.DimGray }, 0, 0);
            value.Dock = DockStyle.Fill; value.Font = new Font("Segoe UI", 16, FontStyle.Bold); value.Text = "...";
            card.Controls.Add(value, 0, 1); return card;
        }
        void ShowAbout()
        {
            using (var about = new Form { Text = "Om Diskkart", Icon = Icon, Width = 470, Height = 280, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = Font, BackColor = Color.White, Padding = new Padding(24) })
            {
                var text = new Label { Dock = DockStyle.Fill, Text = "Diskkart 0.1.0\r\n\r\nEt kart over lagringsplassen din.\r\n\r\nLokal skanning av mapper og diskplass.\r\nAppen endrer ikke filene som skannes.\r\nInnstillinger lagres lokalt per brukerkonto." };
                var close = new Button { Text = "Lukk", Dock = DockStyle.Bottom, Height = 32, DialogResult = DialogResult.OK };
                about.Controls.Add(text); about.Controls.Add(close); about.AcceptButton = close; about.CancelButton = close; about.ShowDialog(this);
            }
        }
    }
}
