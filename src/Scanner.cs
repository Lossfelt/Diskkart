using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Diskoversikt
{
    public sealed class Folder
    {
        public string Path, Name, Note = "";
        public Folder Parent;
        public List<Folder> Children = new List<Folder>();
        public long Bytes, DiskBytes, Files, OwnBytes, OwnDiskBytes, OwnFiles, Unknown, OwnUnknown, Issues;
        public bool NotEnumerated, Incomplete;
    }

    public sealed class Scanner
    {
        public string Current = "";
        public long Files, Folders, Problems;
        public readonly List<string> Messages = new List<string>();
        private CancellationToken cancel;
        private const uint DirectoryFlag = 16, ReparseFlag = 1024;
        private static readonly IntPtr Invalid = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FindData
        {
            public uint Attributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
            public uint SizeHigh, SizeLow, Tag, Reserved;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string Alternate;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileW(string path, out FindData data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool FindNextFileW(IntPtr handle, out FindData data);
        [DllImport("kernel32.dll")] private static extern bool FindClose(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetCompressedFileSizeW(string path, out uint high);
        [DllImport("kernel32.dll")] private static extern void SetLastError(uint code);
        [StructLayout(LayoutKind.Sequential)]
        private struct StandardInfo
        {
            public long AllocationSize, EndOfFile;
            public uint NumberOfLinks;
            public byte DeletePending, Directory;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out StandardInfo info, uint size);
        private static long Allocation(string path, uint attributes)
        {
            // Compressed/sparse files need their actual stored size, not their logical allocation.
            if ((attributes & (0x800u | 0x200u)) != 0)
            {
                uint high;
                SetLastError(0);
                uint low = GetCompressedFileSizeW(Native(path), out high);
                int code = Marshal.GetLastWin32Error();
                if (low == uint.MaxValue && code != 0) throw new Win32Exception(code);
                return ((long)high << 32) | low;
            }
            // Open metadata only, without following a reparse point or reading file contents.
            using (SafeFileHandle file = CreateFileW(Native(path), 0, 7, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero))
            {
                if (file.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
                StandardInfo info;
                if (!GetFileInformationByHandleEx(file, 1, out info, (uint)Marshal.SizeOf(typeof(StandardInfo)))) throw new Win32Exception(Marshal.GetLastWin32Error());
                return info.AllocationSize;
            }
        }

        private static string Native(string path)
        {
            if (path.StartsWith(@"\\?\")) return path;
            return path.StartsWith(@"\\") ? @"\\?\UNC\" + path.Substring(2) : @"\\?\" + path;
        }
        public static bool IsCloud(uint tag) { return (tag & 0xFFFF0FFFu) == 0x9000001Au; }
        private void Problem(Folder folder, string message)
        {
            Interlocked.Increment(ref Problems);
            folder.Issues++;
            if (Messages.Count < 1000) Messages.Add(folder.Path + ": " + message);
        }
        public Folder Scan(string path, CancellationToken token)
        {
            cancel = token;
            cancel.ThrowIfCancellationRequested();
            path = System.IO.Path.GetFullPath(path);
            if (!System.IO.Directory.Exists(path)) throw new IOException("Mappen finnes ikke, eller du mangler tilgang: " + path);
            Folder root = new Folder { Path = path, Name = path };
            Stack<Folder> pending = new Stack<Folder>();
            List<Folder> order = new List<Folder>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                cancel.ThrowIfCancellationRequested();
                Folder folder = pending.Pop();
                order.Add(folder);
                Current = folder.Path;
                Interlocked.Increment(ref Folders);
                FindData data;
                IntPtr handle = FindFirstFileW(Native(folder.Path.TrimEnd('\\') + @"\*"), out data);
                if (handle == Invalid)
                {
                    int code = Marshal.GetLastWin32Error();
                    if (code != 2) { folder.NotEnumerated = folder.Incomplete = true; folder.Note = "Kan ikke leses: størrelse ukjent"; Problem(folder, new Win32Exception(code).Message); }
                    continue;
                }
                try
                {
                    do
                    {
                        cancel.ThrowIfCancellationRequested();
                        if (data.Name == "." || data.Name == "..") continue;
                        string childPath = System.IO.Path.Combine(folder.Path, data.Name);
                        bool reparse = (data.Attributes & ReparseFlag) != 0;
                        if ((data.Attributes & DirectoryFlag) != 0)
                        {
                            Folder child = new Folder { Path = childPath, Name = data.Name, Parent = folder };
                            folder.Children.Add(child);
                            if (reparse && !IsCloud(data.Tag))
                            {
                                child.Note = "Hoppet over: mappekobling";
                                child.NotEnumerated = child.Incomplete = true;
                                Problem(child, "Mappekobling/reparse-punkt følges ikke (unngår løkker og dobbelttelling).");
                            }
                            else pending.Push(child);
                        }
                        else
                        {
                            folder.OwnFiles++;
                            Interlocked.Increment(ref Files);
                            if (reparse && !IsCloud(data.Tag))
                            {
                                folder.OwnUnknown++;
                                folder.Incomplete = true;
                                Problem(folder, "Filkobling hoppet over: " + data.Name);
                                continue;
                            }
                            folder.OwnBytes += ((long)data.SizeHigh << 32) | data.SizeLow;
                            try { folder.OwnDiskBytes += Allocation(childPath, data.Attributes); }
                            catch (Win32Exception ex)
                            {
                                folder.OwnUnknown++;
                                Problem(folder, "Ukjent plass på disk for " + data.Name + ": " + ex.Message);
                            }
                        }
                    } while (FindNextFileW(handle, out data));
                    int last = Marshal.GetLastWin32Error();
                    if (last != 18) { folder.Incomplete = true; folder.Note = "Delvis lest"; Problem(folder, new Win32Exception(last).Message); }
                }
                finally { FindClose(handle); }
            }
            // Children always occur after their parent. Aggregate from leaves to root.
            for (int i = order.Count - 1; i >= 0; i--)
            {
                cancel.ThrowIfCancellationRequested();
                Folder f = order[i];
                f.Bytes += f.OwnBytes; f.DiskBytes += f.OwnDiskBytes;
                f.Files += f.OwnFiles; f.Unknown += f.OwnUnknown;
                // Skipped children were not visited and still need to contribute their issue count.
                foreach (Folder c in f.Children) if (c.Note.StartsWith("Hoppet over")) { f.Issues += c.Issues; f.Incomplete = true; }
                if (f.Parent != null)
                {
                    f.Parent.Bytes += f.Bytes; f.Parent.DiskBytes += f.DiskBytes;
                    f.Parent.Files += f.Files; f.Parent.Unknown += f.Unknown; f.Parent.Issues += f.Issues;
                    f.Parent.Incomplete |= f.Incomplete;
                }
            }
            return root;
        }
    }
}
