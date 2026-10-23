using System;
using System.IO;
using System.Linq;
using System.Threading;
using Diskoversikt;

class ScannerTests
{
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
    static int Main(string[] args)
    {
        try
        {
            string fixture = args[0];
            Scanner scanner = new Scanner();
            Folder root = scanner.Scan(fixture, CancellationToken.None);
            Check(root.Bytes == 1234704L, "Recursive byte totals, including long Unicode paths");
            Check(root.Files == 3, "Recursive file count");
            Check(root.OwnBytes == 137 && root.OwnFiles == 1, "Files directly in root");
            Folder child = root.Children.Single(x => x.Name == "Undermappe æøå");
            Check(child.Bytes == 1234567 && child.Files == 2, "Nested aggregation");
            Check(root.Children.Single(x => x.Name == "Tom mappe").Bytes == 0, "Empty folder");
            Check(root.Children.Single(x => x.Name == "Tilbake til rot").Note.StartsWith("Hoppet over"), "Junction cycle skipped");
            Check(root.Issues == 1 && scanner.Problems == 1, "Skipped link propagates incomplete status");
            Check(root.Unknown == 0 && root.DiskBytes >= 0, "Allocation metadata readable");
            // 137-byte NTFS resident files may allocate zero data clusters; a large file must allocate storage.
            Check(child.DiskBytes > 0 && child.DiskBytes != child.Bytes, "Disk allocation differs from logical size");
            Check(Scanner.IsCloud(0x9000001A) && Scanner.IsCloud(0x9000F01A) && !Scanner.IsCloud(0xA0000003), "Cloud tags distinguished from junctions");
            var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            bool didCancel = false;
            try { new Scanner().Scan(fixture, cancelled.Token); } catch (OperationCanceledException) { didCancel = true; }
            Check(didCancel, "Cancellation honored");
            bool missing = false;
            try { new Scanner().Scan(Path.Combine(fixture, "missing"), CancellationToken.None); } catch (IOException) { missing = true; }
            Check(missing, "Missing folder rejected");
            Console.WriteLine("TOTAL bytes=" + root.Bytes + " allocated=" + root.DiskBytes + " files=" + root.Files);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
