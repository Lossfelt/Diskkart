using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Diskoversikt
{
    static class Program
    {
        [STAThread] static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow(args.Length > 0 ? args[0] : null));
        }
    }
    public sealed partial class MainWindow : Form
    {
        readonly TextBox path = new TextBox();
        readonly Button browse = new Button(), scan = new Button(), stop = new Button(), open = new Button(), log = new Button(), scanWhole = new Button();
        readonly CheckBox disk = new CheckBox();
        readonly Label status = new Label(), details = new Label();
        readonly Label volumeSummary = new Label(), coverage = new Label();
        readonly Panel usageBar = new Panel();
        readonly TreeView tree = new TreeView();
        readonly Panel header = new Panel();
        readonly ProgressBar progress = new ProgressBar();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        CancellationTokenSource cancellation;
        Scanner scanner, resultScanner;
        Folder result;
        VolumeUsage volumeUsage;
        bool busy, closeAfterStop;
        public MainWindow(string initial)
        {
            Text = "Diskkart"; Width = 1220; Height = 900; MinimumSize = new Size(1080, 700);
            Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(247, 249, 251);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(16) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 182));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(layout);
            var heading = new Panel { Dock = DockStyle.Fill };
            heading.Controls.Add(new Label { Text = "Diskkart", Font = new Font("Segoe UI", 21, FontStyle.Bold), AutoSize = true, Location = new Point(0, 0), ForeColor = Color.FromArgb(27, 53, 75) });
            heading.Controls.Add(new Label { Text = "Se hvor lagringsplassen blir av", AutoSize = true, Location = new Point(2, 38), ForeColor = Color.DimGray });
            var about = new Button { Text = "Om Diskkart", Width = 110, Height = 30, Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(1030, 6) };
            about.Click += delegate { ShowAbout(); }; heading.Controls.Add(about);
            heading.Resize += delegate { about.Left = heading.ClientSize.Width - about.Width; };
            layout.Controls.Add(heading, 0, 0);
            var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            path.Text = initial ?? (Directory.Exists(preferences.LastPath) ? preferences.LastPath : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            path.Dock = DockStyle.Fill; path.Margin = new Padding(0, 5, 8, 0);
            SetButton(browse, "Velg mappe", delegate { using (var d = new FolderBrowserDialog { SelectedPath = path.Text, ShowNewFolderButton = false }) if (d.ShowDialog(this) == DialogResult.OK) path.Text = d.SelectedPath; });
            SetButton(scan, "Skann", async delegate { await StartScan(); });
            scan.BackColor = Color.FromArgb(27, 78, 108); scan.ForeColor = Color.White;
            SetButton(stop, "Stopp", delegate { StopScan(); }); stop.Enabled = false;
            bar.Controls.Add(path, 0, 0); bar.Controls.Add(browse, 1, 0); bar.Controls.Add(scan, 2, 0); bar.Controls.Add(stop, 3, 0); layout.Controls.Add(bar, 0, 1);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            disk.Text = "Sorter etter plass på disk"; disk.Checked = true; disk.AutoSize = true;
            disk.Margin = new Padding(0, 5, 20, 0); disk.CheckedChanged += delegate { if (!restoring) Render(); };
            SetButton(open, "Åpne mappe", delegate { OpenSelected(); }); open.Enabled = false; open.Width = 125;
            SetButton(log, "Vis merknader", delegate { ShowLog(); }); log.Enabled = false; log.Width = 140;
            SetButton(scanWhole, "Skann hele disken", async delegate { if (volumeUsage != null && volumeUsage.Error == null) { path.Text = volumeUsage.Root; await StartScan(); } });
            scanWhole.Enabled = false; scanWhole.Width = 155;
            actions.Controls.Add(disk); actions.Controls.Add(open); actions.Controls.Add(log); actions.Controls.Add(scanWhole); layout.Controls.Add(actions, 0, 2);
            var summary = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, BackColor = Color.FromArgb(236, 241, 246), Padding = new Padding(10, 6, 10, 4), Margin = new Padding(0, 0, 0, 10) };
            summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 25)); summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 65)); summary.RowStyles.Add(new RowStyle(SizeType.Absolute, 29)); summary.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
            for (int i = 0; i < 3; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            cards.Controls.Add(StatCard("KAPASITET", capacityValue), 0, 0); cards.Controls.Add(StatCard("BRUKT", usedValue), 1, 0); cards.Controls.Add(StatCard("LEDIG", freeValue), 2, 0);
            volumeSummary.Dock = DockStyle.Fill; volumeSummary.Text = "Diskinformasjon vises etter skanning.";
            coverage.Dock = DockStyle.Fill; coverage.Text = "Skann hele disken for å sammenligne diskens brukte plass med appens målinger.";
            usageBar.Dock = DockStyle.Fill; usageBar.Paint += DrawUsageBar; usageBar.Resize += delegate { usageBar.Invalidate(); };
            summary.Controls.Add(volumeSummary, 0, 0); summary.Controls.Add(cards, 0, 1); summary.Controls.Add(usageBar, 0, 2); summary.Controls.Add(coverage, 0, 3); layout.Controls.Add(summary, 0, 3);
            header.Dock = DockStyle.Fill; header.BackColor = Color.FromArgb(236, 240, 244); header.Paint += DrawHeader; layout.Controls.Add(header, 0, 4);
            tree.Dock = DockStyle.Fill; tree.BorderStyle = BorderStyle.FixedSingle; tree.ItemHeight = 28;
            tree.DrawMode = TreeViewDrawMode.OwnerDrawText; tree.DrawNode += DrawNode; tree.HideSelection = false;
            tree.ShowNodeToolTips = true; tree.FullRowSelect = true;
            tree.BeforeExpand += delegate(object sender, TreeViewCancelEventArgs e) { Populate(e.Node); };
            tree.AfterSelect += delegate { UpdateDetails(); };
            tree.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) OpenSelected(); };
            tree.Resize += delegate { FitColumns(); tree.Invalidate(); header.Invalidate(); };
            layout.Controls.Add(tree, 0, 5);
            progress.Dock = DockStyle.Fill; progress.Visible = false; layout.Controls.Add(progress, 0, 6);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            status.Dock = DockStyle.Fill; status.AutoEllipsis = true; status.Text = "Velg en mappe eller skriv en disksti, for eksempel C:\\, og trykk Skann.";
            details.Dock = DockStyle.Fill; details.AutoEllipsis = true; details.ForeColor = Color.DimGray;
            footer.Controls.Add(status, 0, 0); footer.Controls.Add(details, 0, 1); layout.Controls.Add(footer, 0, 7);
            timer.Interval = 250; timer.Tick += delegate { if (scanner != null && busy) status.Text = string.Format("Skanner: {0:N0} mapper, {1:N0} filer | {2}{3}", Interlocked.Read(ref scanner.Folders), Interlocked.Read(ref scanner.Files), scanner.Current, result == null ? "" : " | Viser forrige resultat"); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; closeAfterStop = true; StopScan(); } };
            InitializePolish();
            if (initial != null) Shown += async delegate { await StartScan(); };
        }
        void SetButton(Button b, string text, EventHandler action) { b.Text = text; b.Height = 30; b.Dock = DockStyle.Top; b.BackColor = Color.White; b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderColor = Color.FromArgb(200, 210, 220); b.Click += action; }
        void StopScan() { if (cancellation != null) { cancellation.Cancel(); stop.Enabled = false; status.Text = "Stopper skanning ..."; } }
        async Task StartScan()
        {
            if (busy) return;
            string target = path.Text.Trim().Trim('"');
            if (!Directory.Exists(target)) { MessageBox.Show(this, "Mappen finnes ikke, eller du mangler tilgang.", "Kan ikke skanne"); return; }
            busy = true; cancellation = new CancellationTokenSource(); scanner = new Scanner();
            status.Text = result == null ? "Starter skanning ..." : "Skanner på nytt. Mappetreet viser forrige resultat frem til skanningen er ferdig.";
            preferences.LastPath = Path.GetFullPath(target);
            browse.Enabled = scan.Enabled = path.Enabled = false; stop.Enabled = true;
            scanWhole.Enabled = false;
            progress.Style = ProgressBarStyle.Marquee; progress.Visible = true; timer.Start();
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                VolumeUsage usage = null;
                Folder scanned = await Task.Factory.StartNew(() => { Folder found = scanner.Scan(target, cancellation.Token); usage = VolumeUsage.Read(found.Path); cancellation.Token.ThrowIfCancellationRequested(); return found; }, cancellation.Token, TaskCreationOptions.None, TaskScheduler.Default);
                result = scanned; resultScanner = scanner; volumeUsage = usage; Render(); log.Enabled = true;
                status.Text = string.Format("Ferdig på {0:N1} s | {1:N0} mapper | {2:N0} filer | {3:N0} merknader{4}", watch.Elapsed.TotalSeconds, scanner.Folders, scanner.Files, scanner.Problems, scanner.Problems > 0 ? " | Oversikten er ufullstendig, se merknader." : "");
            }
            catch (OperationCanceledException) { status.Text = "Skanning avbrutt. Eventuell tidligere oversikt er beholdt."; }
            catch (Exception ex) { status.Text = "Skanningen mislyktes."; MessageBox.Show(this, ex.Message, "Skanningsfeil", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally
            {
                timer.Stop(); busy = false; progress.Visible = false; stop.Enabled = false;
                browse.Enabled = scan.Enabled = path.Enabled = true; cancellation.Dispose(); cancellation = null;
                scanWhole.Enabled = volumeUsage != null && volumeUsage.Error == null;
                if (closeAfterStop) Close();
            }
        }
        static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KiB", "MiB", "GiB", "TiB", "PiB" }; double value = bytes; int i = 0;
            while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
            return value.ToString(i == 0 ? "N0" : "N2") + " " + units[i];
        }
        long Metric(Folder f) { return disk.Checked ? f.DiskBytes : f.Bytes; }
        internal static string FolderSize(Folder f, bool onDisk)
        {
            if (f.NotEnumerated) return "Ukjent";
            return FormatBytes(onDisk ? f.DiskBytes : f.Bytes) + (f.Incomplete || (onDisk && f.Unknown > 0) ? " + ?" : "");
        }
        internal static string CoverageText(VolumeUsage usage, long measured)
        {
            string known = "Målt plass på disk: " + FormatBytes(measured);
            if (!usage.WholeVolume) return known + " i valgt mappe.\r\nUndermappe: skann hele disken for å beregne plass som ikke er gjort rede for.";
            long difference = usage.Difference(measured);
            string explanation = "Differansen kan omfatte utilgjengelige filer, systemdata og måleavvik. Størrelse per utilgjengelig mappe er ukjent.";
            if (difference < 0) return known + " | Målt sum overstiger diskens bruk med " + FormatBytes(-difference) + ".\r\nDette kan skyldes hardlenker, endringer under skanning eller måleavvik.";
            string percent = usage.Used > 0 ? " (" + ((double)difference / usage.Used).ToString("P1") + " av brukt plass)" : "";
            string measuredPercent = usage.Used > 0 ? " (" + ((double)measured / usage.Used).ToString("P1") + " av brukt plass)" : "";
            return known + measuredPercent + " | Ikke gjort rede for: " + FormatBytes(difference) + percent + "\r\n" + explanation;
        }
        void UpdateSummary()
        {
            usageBar.Invalidate();
            if (volumeUsage == null) return;
            if (volumeUsage.Error != null)
            {
                capacityValue.Text = usedValue.Text = freeValue.Text = "Ukjent";
                volumeSummary.Text = "Diskinformasjon kunne ikke hentes: " + volumeUsage.Error;
                coverage.Text = "Målt plass på disk: " + FormatBytes(result.DiskBytes) + ". Differanse kan ikke beregnes.";
                return;
            }
            volumeSummary.Text = "Disk " + volumeUsage.Root;
            capacityValue.Text = FormatBytes(volumeUsage.Capacity); usedValue.Text = FormatBytes(volumeUsage.Used); freeValue.Text = FormatBytes(volumeUsage.Free);
            string full = CoverageText(volumeUsage, result.DiskBytes);
            tips.SetToolTip(coverage, full);
            if (WholeVolumeView && volumeUsage.Difference(result.DiskBytes) >= 0)
                coverage.Text = "Målt " + FormatBytes(result.DiskBytes) + " av " + FormatBytes(volumeUsage.Used) + " brukt (" + Fraction(result.DiskBytes, volumeUsage.Used).ToString("P1") + ")  |  Ikke gjort rede for: " + FormatBytes(volumeUsage.Difference(result.DiskBytes)) + "\r\nBlå: målt   ·   Oransje: ikke gjort rede for   ·   Grå: ledig";
            else coverage.Text = full;
        }
        TreeNode MakeNode(Folder f)
        {
            var n = new TreeNode(f.Name) { Tag = f, ImageIndex = 0, SelectedImageIndex = 0, ToolTipText = f.Path + (f.Note.Length > 0 ? "\n" + f.Note : "") + (f.Issues > 0 ? "\nUfullstendig: se merknader." : "") };
            if (f.Children.Count > 0 || f.OwnFiles > 0) n.Nodes.Add(new TreeNode("..."));
            return n;
        }
        void Populate(TreeNode n)
        {
            if (n.Nodes.Count != 1 || n.Nodes[0].Tag != null) return;
            Folder f = (Folder)n.Tag; n.Nodes.Clear();
            var rows = f.Children.ToList();
            if (f.OwnFiles > 0) rows.Add(new Folder { Name = "[Filer direkte i mappen]", Path = f.Path, Bytes = f.OwnBytes, DiskBytes = f.OwnDiskBytes, Files = f.OwnFiles, Unknown = f.OwnUnknown, Note = "Samlet størrelse på filene direkte i denne mappen" });
            foreach (Folder child in rows.OrderByDescending(x => Metric(x)).ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)) n.Nodes.Add(MakeNode(child));
            if (n.Name == "volume" && volumeUsage.Difference(result.DiskBytes) > 0)
            {
                Folder missing = new Folder { Name = "Ikke gjort rede for", Path = result.Path, DiskBytes = volumeUsage.Difference(result.DiskBytes), Note = "Differanse mot Windows' brukstall. Kan ikke fordeles på mapper uten flere målinger." };
                TreeNode unknown = MakeNode(missing); unknown.Name = "unaccounted"; unknown.ImageIndex = unknown.SelectedImageIndex = 2;
                unknown.ToolTipText = "Brukt ifølge Windows minus målt plass på disk. Kan omfatte utilgjengelige filer, systemdata og måleavvik. Dette er ikke en mappe.";
                // Keep this summary row separate from the measured folders.
                n.Nodes.Add(unknown);
            }
        }
        bool WholeVolumeView { get { return volumeUsage != null && volumeUsage.Error == null && volumeUsage.WholeVolume; } }
        long PercentTotal { get { return result == null ? 0 : WholeVolumeView && disk.Checked ? volumeUsage.Used : Metric(result); } }
        internal static double Fraction(long value, long total) { return total <= 0 ? 0 : (double)value / total; }
        internal double PercentValue(TreeNode node)
        {
            if (node.Name == "volume") return Fraction(result.DiskBytes, volumeUsage.Used);
            return Fraction(Metric((Folder)node.Tag), PercentTotal);
        }
        internal string PercentLabel(TreeNode node)
        {
            Folder f = (Folder)node.Tag;
            if (node.Name == "volume") return volumeUsage.Used <= 0 ? "" : PercentValue(node).ToString("P1") + " målt";
            if (f.NotEnumerated || node.Name == "unaccounted" && !disk.Checked) return "Ukjent";
            return PercentTotal <= 0 ? "" : PercentValue(node).ToString("P1");
        }
        void Render()
        {
            if (result == null) return;
            tree.BeginUpdate(); tree.Nodes.Clear(); var root = MakeNode(result);
            if (WholeVolumeView)
            {
                root.Name = "volume"; root.Text = result.Path + " (brukt ifølge Windows)"; root.ImageIndex = root.SelectedImageIndex = 1;
                root.ToolTipText = "Plass på disk viser hele diskens bruk ifølge Windows. Prosenten på denne raden viser hvor mye av den brukte plassen appen har målt. Undermappenes prosent viser deres andel, etter valgt størrelsestype.";
            }
            tree.Nodes.Add(root);
            // Even an unreadable/empty root needs the summary of unaccounted space.
            if (WholeVolumeView && root.Nodes.Count == 0 && volumeUsage.Difference(result.DiskBytes) > 0) root.Nodes.Add(new TreeNode("..."));
            root.Expand(); tree.SelectedNode = root; tree.EndUpdate();
            header.Invalidate();
            UpdateSummary();
        }
        int NameEnd { get { return tree.ClientSize.Width - columnWidths.Sum(); } }
        void Cell(Graphics g, string text, Rectangle rect, Color color, bool right)
        {
            TextRenderer.DrawText(g, text, Font, rect, color, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | (right ? TextFormatFlags.Right : TextFormatFlags.Left));
        }
        void DrawHeader(object sender, PaintEventArgs e)
        {
            int x = NameEnd; Cell(e.Graphics, "Mappe (inkludert undermapper)", new Rectangle(8, 0, x - 8, 29), Color.Black, false);
            string[] titles = { "Plass på disk", "Filstørrelse", WholeVolumeView ? "Målt / andel" : "% av målt rot", "Filer", "Status" };
            for (int i = 0; i < titles.Length; i++)
            {
                Rectangle cell = Column(i, 0, header.Height); Cell(e.Graphics, titles[i], cell, Color.FromArgb(45, 65, 80), i != 4);
                e.Graphics.DrawLine(Pens.LightGray, cell.Left - 3, 5, cell.Left - 3, header.Height - 5);
            }
        }
        void DrawNode(object sender, DrawTreeNodeEventArgs e)
        {
            Folder f = e.Node.Tag as Folder; if (f == null) { e.DrawDefault = true; return; }
            bool selected = e.Node == tree.SelectedNode;
            Color back = selected ? Color.FromArgb(218, 234, 250) : Color.White;
            using (Brush b = new SolidBrush(back)) e.Graphics.FillRectangle(b, new Rectangle(e.Bounds.X, e.Bounds.Y, Math.Max(0, tree.ClientSize.Width - e.Bounds.X), e.Bounds.Height));
            int x = NameEnd, y = e.Bounds.Y, h = e.Bounds.Height;
            Color ink = f.Note.StartsWith("Hoppet over") ? Color.DimGray : Color.FromArgb(30, 40, 50);
            bool volume = e.Node.Name == "volume", missing = e.Node.Name == "unaccounted";
            Cell(e.Graphics, e.Node.Text, new Rectangle(e.Bounds.X, y, Math.Max(1, x - e.Bounds.X - 8), h), ink, false);
            Cell(e.Graphics, volume ? FormatBytes(volumeUsage.Used) : FolderSize(f, true), Column(0, y, h), ink, true);
            Cell(e.Graphics, missing ? "Ukjent" : FolderSize(f, false), Column(1, y, h), ink, true);
            long total = volume ? volumeUsage.Used : PercentTotal;
            bool unknownPercent = !volume && (missing && !disk.Checked || f.NotEnumerated);
            double fraction = PercentValue(e.Node);
            Rectangle percent = Column(2, y + 4, Math.Max(1, h - 8));
            if (!unknownPercent && total > 0)
            {
                using (Brush track = new SolidBrush(Color.FromArgb(236, 240, 244))) e.Graphics.FillRectangle(track, percent);
                int width = (int)Math.Round(percent.Width * Math.Max(0, Math.Min(1, fraction)));
                using (Brush fill = new SolidBrush(missing ? Color.FromArgb(244, 198, 135) : Color.FromArgb(139, 195, 226))) e.Graphics.FillRectangle(fill, new Rectangle(percent.X, percent.Y, width, percent.Height));
            }
            Cell(e.Graphics, PercentLabel(e.Node), Column(2, y, h), ink, true);
            Cell(e.Graphics, missing || f.NotEnumerated ? "Ukjent" : f.Files.ToString("N0") + (f.Incomplete ? " + ?" : ""), Column(3, y, h), ink, true);
            Cell(e.Graphics, missing ? "Differanse" : volume ? "Windows" : f.Note.StartsWith("Hoppet over") ? "Hoppet over" : f.NotEnumerated ? "Kan ikke leses" : f.Issues > 0 || f.Unknown > 0 ? "Delvis" : "", Column(4, y, h), Color.FromArgb(150, 80, 0), false);
        }
        void DrawUsageBar(object sender, PaintEventArgs e)
        {
            if (result == null || volumeUsage == null || volumeUsage.Error != null || volumeUsage.Capacity <= 0) return;
            Rectangle rect = new Rectangle(0, 3, Math.Max(1, usageBar.ClientSize.Width - 1), Math.Max(1, usageBar.ClientSize.Height - 7));
            long known = WholeVolumeView ? Math.Min(result.DiskBytes, volumeUsage.Used) : 0;
            int measuredWidth = (int)Math.Round(rect.Width * Fraction(known, volumeUsage.Capacity));
            int usedWidth = (int)Math.Round(rect.Width * Fraction(volumeUsage.Used, volumeUsage.Capacity));
            using (Brush free = new SolidBrush(Color.FromArgb(225, 230, 235))) e.Graphics.FillRectangle(free, rect);
            using (Brush rest = new SolidBrush(WholeVolumeView ? Color.FromArgb(244, 198, 135) : Color.FromArgb(150, 170, 190))) e.Graphics.FillRectangle(rest, new Rectangle(rect.X, rect.Y, usedWidth, rect.Height));
            using (Brush measured = new SolidBrush(Color.FromArgb(139, 195, 226))) e.Graphics.FillRectangle(measured, new Rectangle(rect.X, rect.Y, measuredWidth, rect.Height));
            if (measuredWidth > 90) Cell(e.Graphics, "Målt", new Rectangle(0, rect.Y, measuredWidth, rect.Height), Color.Black, false);
            if (usedWidth - measuredWidth > 150) Cell(e.Graphics, WholeVolumeView ? "Ikke gjort rede for" : "Brukt ifølge Windows", new Rectangle(measuredWidth + 4, rect.Y, usedWidth - measuredWidth - 4, rect.Height), Color.Black, false);
            if (rect.Width - usedWidth > 70) Cell(e.Graphics, "Ledig", new Rectangle(usedWidth + 4, rect.Y, rect.Width - usedWidth - 4, rect.Height), Color.Black, false);
        }
        void UpdateDetails()
        {
            Folder f = tree.SelectedNode == null ? null : tree.SelectedNode.Tag as Folder;
            bool missing = tree.SelectedNode != null && tree.SelectedNode.Name == "unaccounted";
            open.Enabled = f != null && !missing;
            if (missing) { details.Text = "Differanse mellom Windows' brukstall og målt plass på disk. Kan ikke fordeles på enkeltmapper."; return; }
            if (tree.SelectedNode != null && tree.SelectedNode.Name == "volume") { details.Text = f.Path + " | Brukt ifølge Windows: " + FormatBytes(volumeUsage.Used) + " | Målt av appen: " + FormatBytes(result.DiskBytes) + " | " + PercentLabel(tree.SelectedNode); return; }
            details.Text = f == null ? "" : f.Path + " | " + (f.NotEnumerated ? "Størrelse ukjent" : f.Bytes.ToString("N0") + " byte målt filstørrelse") + (f.Note.Length > 0 ? " | " + f.Note : "");
        }
        void OpenSelected()
        {
            if (tree.SelectedNode != null && tree.SelectedNode.Name == "unaccounted") return;
            Folder f = tree.SelectedNode == null ? null : tree.SelectedNode.Tag as Folder; if (f == null) return;
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + f.Path + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Kunne ikke åpne mappen"); }
        }
        void ShowLog()
        {
            string text = "Tallene inkluderer undermapper. Plass på disk er summen Windows rapporterer for filene.\r\n" +
                "Diskens kapasitet/brukt/ledig hentes ved fullført skanning. Differanse beregnes bare for en hel disk, alltid mot målt plass på disk.\r\n" +
                "Ved hel diskskanning viser roten Windows' brukstall. Blå stolper viser målte andeler, oransje viser differansen.\r\n" +
                "Ved hel diskskanning viser rotens prosent hvor stor del av brukt plass appen har målt (alltid plass på disk).\r\n" +
                "Undermappenes prosent viser andel av brukt diskplass når plass på disk er valgt for hele disken; ellers andel av målt rot etter valgt størrelsestype.\r\n" +
                "Ikke gjort rede for er en differanse, ikke en måling av mapper uten tilgang. Deres størrelse er ukjent.\r\n" +
                "Hardlenker telles per filnavn. Filsystemmetadata og utilgjengelige filer er ikke med. Summen kan derfor avvike fra diskens totale bruk.\r\n" +
                "Mappekoblinger følges ikke. Sky-mapper fra OneDrive kan gjennomgås; filinnhold leses ikke av appen.\r\n" +
                "Ukjent betyr at mappen ikke er lest. \"+ ?\" betyr at tallet er ufullstendig. Delvis betyr at noe er hoppet over eller ikke kunne måles.\r\n\r\n" +
                (resultScanner == null ? "" : string.Format("{0:N0} merknader (viser maksimalt 1 000):\r\n", resultScanner.Problems) + string.Join("\r\n", resultScanner.Messages));
            using (var dialog = new Form { Text = "Merknader og begrensninger", Width = 900, Height = 550, StartPosition = FormStartPosition.CenterParent })
            { dialog.Controls.Add(new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Text = text, Font = Font }); dialog.ShowDialog(this); }
        }
    }
}
