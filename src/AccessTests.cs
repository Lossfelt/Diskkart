using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using Diskoversikt;

class AccessTests
{
    static int Main(string[] args)
    {
        string root = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(root);
        string denied = Path.Combine(root, "Uten lesetilgang");
        Directory.CreateDirectory(denied);
        File.WriteAllText(Path.Combine(denied, "skjult.txt"), "This fixture must not be read by the scanner");
        byte[] original = Directory.GetAccessControl(denied).GetSecurityDescriptorBinaryForm();
        try
        {
            DirectorySecurity changed = Directory.GetAccessControl(denied);
            changed.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.ListDirectory, AccessControlType.Deny));
            Directory.SetAccessControl(denied, changed);
            Scanner scanner = new Scanner();
            Folder scanned = scanner.Scan(root, CancellationToken.None);
            Folder child = scanned.Children[0];
            if (!child.NotEnumerated || !child.Incomplete || !scanned.Incomplete || child.Issues != 1) throw new Exception("Denied directory not marked as unknown/incomplete");
            if (MainWindow.FolderSize(child, true) != "Ukjent" || MainWindow.FolderSize(child, false) != "Ukjent") throw new Exception("Denied folder rendered as numeric size");
            if (MainWindow.FolderSize(scanned, true) != "0 B + ?") throw new Exception("Unknown size not propagated to parent");
            Console.WriteLine("PASS: Real access-denied folder displays unknown and propagates incomplete totals");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            // SetSecurityDescriptorBinaryForm marks AccessRulesModified, so restoring
            // a snapshot actually writes its DACL back to disk.
            var restore = new DirectorySecurity();
            restore.SetSecurityDescriptorBinaryForm(original, AccessControlSections.Access);
            Directory.SetAccessControl(denied, restore);
            Directory.GetFiles(denied); // The temporary denial must be gone.
        }
    }
}
