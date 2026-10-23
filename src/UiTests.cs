using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Diskoversikt;

class UiTests
{
    static object Field(object form, string name) { return form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); }
    static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); }
    static async Task Wait(MainWindow form)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while ((bool)Field(form, "busy") && watch.Elapsed.TotalSeconds < 30) await Task.Delay(20);
        Check(!(bool)Field(form, "busy"), "UI scan completes without hanging");
    }
    static async Task RunChecks(MainWindow form, string[] args)
    {
        ((TextBox)Field(form, "path")).Text = args[0];
        ((Button)Field(form, "scan")).PerformClick(); await Wait(form);
        TreeView tree = (TreeView)Field(form, "tree");
        Check(tree.Nodes.Count == 1 && tree.Nodes[0].Nodes.Count == 4, "Root and direct-file row displayed");
        VolumeUsage live = (VolumeUsage)Field(form, "volumeUsage");
        Check(live.Error == null && live.Capacity > 0 && live.Free >= 0 && !live.WholeVolume, "Live disk totals loaded for folder scan");
        Check(((Label)Field(form, "capacityValue")).Text.Contains("GiB") && ((Label)Field(form, "usedValue")).Text.Contains("GiB") && ((Label)Field(form, "freeValue")).Text.Contains("GiB"), "Capacity, used and free space cards visible");
        Check(form.Text == "Diskkart" && form.Icon != null && tree.ImageList != null, "App identity and folder icons loaded");
        int[] beforeWidths = (int[])((int[])Field(form, "columnWidths")).Clone();
        form.ResizeColumn(2, 10);
        Check(((int[])Field(form, "columnWidths"))[1] == beforeWidths[1] + 10, "Column boundary can be resized");
        Check(((Label)Field(form, "coverage")).Text.Contains("Undermappe:"), "Folder scan does not imply unaccounted volume space");
        Check(((Button)Field(form, "scanWhole")).Enabled, "Whole disk scan shortcut enabled");
        var drive = new DriveInfo(live.Root);
        Check(live.Capacity == drive.TotalSize && Math.Abs(live.Free - drive.TotalFreeSpace) < 64 * 1024 * 1024, "Disk API agrees with DriveInfo (allow concurrent writes)");
        VolumeUsage rootUsage = VolumeUsage.Read(live.Root);
        Check(rootUsage.WholeVolume && rootUsage.Error == null, "Actual volume root recognized");
        const long GiB = 1024L * 1024 * 1024;
        var sample = new VolumeUsage { Root = @"C:\", Capacity = 235 * GiB, Free = 52 * GiB, WholeVolume = true };
        Check(sample.Used == 183 * GiB && sample.Difference(100 * GiB) == 83 * GiB, "183 GiB used minus 100 GiB measured equals 83 GiB");
        string text = MainWindow.CoverageText(sample, 100 * GiB);
        Check(text.Contains("Ikke gjort rede for: 83") && text.Contains("utilgjengelige filer"), "Difference labeled without claiming all space is inaccessible");
        Check(MainWindow.CoverageText(sample, 200 * GiB).Contains("overstiger"), "Overcount explained rather than reporting negative inaccessible space");
        Check(!MainWindow.CoverageText(new VolumeUsage { WholeVolume = false }, 0).Contains("Ikke gjort rede for:"), "Subfolder has no misleading difference");
        Check(!MainWindow.CoverageText(new VolumeUsage { WholeVolume = true }, 0).Contains("NaN"), "Empty volume avoids division by zero");
        Folder skipped = (Folder)tree.Nodes[0].Nodes[2].Tag;
        Check(skipped.NotEnumerated && MainWindow.FolderSize(skipped, true) == "Ukjent" && MainWindow.FolderSize(skipped, false) == "Ukjent", "Skipped folder size is unknown, not zero");
        Check(MainWindow.FolderSize((Folder)tree.Nodes[0].Tag, true).EndsWith(" + ?"), "Parent total explicitly indicates missing sizes");
        TreeNode first = tree.Nodes[0].Nodes[0];
        Check(first.Text == "Undermappe æøå", "Largest folder sorts first");
        first.Expand();
        Check(first.Nodes.Count == 2 && first.Nodes[0].Text == "[Filer direkte i mappen]", "Lazy expansion populates nested rows");
        tree.SelectedNode = first;
        Check(((Label)Field(form, "details")).Text.Contains("Undermappe æøå"), "Selection shows full path");
        ((CheckBox)Field(form, "disk")).Checked = false;
        Check(tree.Nodes[0].Nodes[0].Text == "Undermappe æøå", "Logical-size sort rebuilds tree");
        tree.Nodes[0].Nodes[0].Expand(); tree.SelectedNode = tree.Nodes[0];
        await Task.Delay(50);
        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(args[1]); }
        // Exercise the whole-volume summary without scanning unrelated user files.
        form.GetType().GetField("volumeUsage", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, sample);
        form.GetType().GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
        Check(((Label)Field(form, "coverage")).Text.Contains("Ikke gjort rede for:"), "Whole-volume summary rendered");
        Check(tree.Nodes[0].Name == "volume" && tree.Nodes[0].Text.Contains("brukt ifølge Windows"), "Whole-volume root represents Windows used space");
        TreeNode rest = tree.Nodes[0].Nodes[tree.Nodes[0].Nodes.Count - 1];
        Check(rest.Name == "unaccounted" && ((Folder)rest.Tag).DiskBytes == sample.Difference(((Folder)tree.Nodes[0].Tag).DiskBytes), "Unaccounted disk space appears as separate summary row");
        tree.SelectedNode = rest;
        Check(!((Button)Field(form, "open")).Enabled && ((Label)Field(form, "details")).Text.Contains("Kan ikke fordeles"), "Summary row cannot be mistaken for an openable folder");
        Check(Math.Abs(MainWindow.Fraction(112, 190) - 0.5894736842) < 0.00000001, "112 of 190 corresponds to 58.9 percent");
        Check(MainWindow.Fraction(0, 0) == 0 && MainWindow.Fraction(200, 190) > 1, "Zero denominator and overcount handled");
        ((CheckBox)Field(form, "disk")).Checked = true;
        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(args[1] + ".whole.png"); }
        // Illustration with clearly labeled example values from the user's scenario.
        Folder original = (Folder)Field(form, "result");
        var demo = new Folder { Name = @"C:\", Path = @"C:\", Bytes = 132 * GiB, DiskBytes = 112 * GiB, Files = 20000, Incomplete = true };
        demo.Children.Add(new Folder { Name = "Users", Path = @"C:\Users", Bytes = 80 * GiB, DiskBytes = 60 * GiB, Files = 10000 });
        demo.Children.Add(new Folder { Name = "Windows", Path = @"C:\Windows", Bytes = 40 * GiB, DiskBytes = 40 * GiB, Files = 8000 });
        demo.Children.Add(new Folder { Name = "Program Files", Path = @"C:\Program Files", Bytes = 12 * GiB, DiskBytes = 12 * GiB, Files = 2000 });
        var demoUsage = new VolumeUsage { Root = @"C:\", Capacity = 235 * GiB, Free = 45 * GiB, WholeVolume = true };
        form.GetType().GetField("result", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, demo);
        form.GetType().GetField("volumeUsage", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, demoUsage);
        form.GetType().GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
        ((TextBox)Field(form, "path")).Text = @"C:\";
        ((Label)Field(form, "status")).Text = "Eksempeldata: 235 GiB kapasitet, 190 GiB brukt, 112 GiB målt. Dette er ikke en ny skanning av PC-en.";
        form.Text = "Diskkart | Eksempeldata";
        Check(((Folder)tree.Nodes[0].Nodes[3].Tag).DiskBytes == 78 * GiB, "190 GiB used is decomposed into 112 GiB measured and 78 GiB unaccounted");
        Check(((Label)Field(form, "coverage")).Text.Contains("112") && ((Label)Field(form, "coverage")).Text.Contains("78"), "Measured and unaccounted amounts visible together");
        Check(Math.Abs(form.PercentValue(tree.Nodes[0]) - 112.0 / 190) < 0.00000001 && form.PercentLabel(tree.Nodes[0]).EndsWith(" målt"), "Disk root shows 58.9 percent measured instead of 100 percent");
        Check(Math.Abs(form.PercentValue(tree.Nodes[0].Nodes[0]) - 60.0 / 190) < 0.00000001, "Subfolder still shows share of used disk space");
        Check(Math.Abs(form.PercentValue(tree.Nodes[0].Nodes[3]) - 78.0 / 190) < 0.00000001, "Unaccounted row shows complementary 41.1 percent");
        ((CheckBox)Field(form, "disk")).Checked = false;
        Check(Math.Abs(form.PercentValue(tree.Nodes[0]) - 112.0 / 190) < 0.00000001, "Root coverage remains based on physical disk usage in logical-size mode");
        Check(Math.Abs(form.PercentValue(tree.Nodes[0].Nodes[0]) - 80.0 / 132) < 0.00000001, "Logical-size mode still uses measured logical root for subfolders");
        ((CheckBox)Field(form, "disk")).Checked = true;
        form.Text = "Diskkart | Eksempeldata";
        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
        { form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height)); bitmap.Save(args[1] + ".example.png"); }
        form.GetType().GetField("result", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, original);
        form.GetType().GetField("volumeUsage", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, live);
        form.GetType().GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
        ((TextBox)Field(form, "path")).Text = @"C:\";
        ((Button)Field(form, "scan")).PerformClick();
        Check(((Label)Field(form, "status")).Text.Contains("forrige resultat"), "Rescan clearly identifies stale displayed result");
        ((Button)Field(form, "stop")).PerformClick(); await Wait(form);
        Check(((Label)Field(form, "status")).Text.StartsWith("Skanning avbrutt"), "Stop cancels background scan");
        Check(((Folder)tree.Nodes[0].Tag).Path == args[0], "Cancellation preserves previous complete result");
        await Task.Delay(50);
    }
    [STAThread] static int Main(string[] args)
    {
        int exit = 1;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // Settings are isolated by Test.ps1, and must survive a save/reload.
        var settings = new UiSettings { LastPath = args[0], Width = 1200, Height = 850, Columns = new int[] { 145, 145, 150, 90, 115 } };
        Check(settings.Save() && UiSettings.Load().LastPath == args[0], "Settings round trip");
        File.WriteAllText(UiSettings.FilePath, "<broken settings");
        Check(UiSettings.Load().Width == 1220, "Damaged settings recover to defaults");
        using (MainWindow form = new MainWindow(null))
        {
            form.Shown += async delegate
            {
                try { await RunChecks(form, args); exit = 0; }
                catch (Exception ex) { Console.Error.WriteLine(ex); }
                finally { form.Close(); }
            };
            Application.Run(form);
        }
        var restored = UiSettings.Load();
        Check(restored.Columns[1] == 155, "Resized column width saved on close");
        return exit;
    }
}
