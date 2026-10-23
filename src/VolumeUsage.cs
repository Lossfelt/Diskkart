using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Diskoversikt
{
    public sealed class VolumeUsage
    {
        public string Root, Error;
        public long Capacity, Free;
        public bool WholeVolume;
        public long Used { get { return Capacity - Free; } }
        public long Difference(long measured) { return Used - measured; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetVolumePathNameW(string path, StringBuilder volume, uint length);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool GetDiskFreeSpaceExW(string path, out ulong available, out ulong total, out ulong free);

        public static VolumeUsage Read(string path)
        {
            var usage = new VolumeUsage();
            try
            {
                string full = Path.GetFullPath(path);
                var volume = new StringBuilder(32768);
                if (!GetVolumePathNameW(full, volume, (uint)volume.Capacity)) throw new Win32Exception(Marshal.GetLastWin32Error());
                usage.Root = volume.ToString();
                ulong available, total, free;
                if (!GetDiskFreeSpaceExW(usage.Root, out available, out total, out free)) throw new Win32Exception(Marshal.GetLastWin32Error());
                // Values are the capacity/free space exposed by Windows to this account.
                usage.Capacity = checked((long)total); usage.Free = checked((long)free);
                if (usage.Free > usage.Capacity) throw new IOException("Diskens tall kan ikke sammenlignes (for eksempel på grunn av en lagringskvote).");
                usage.WholeVolume = string.Equals(full.TrimEnd('\\'), usage.Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            { usage.Error = ex.Message; }
            return usage;
        }
    }
}
