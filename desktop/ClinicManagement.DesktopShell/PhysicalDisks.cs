using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// Whether two folders live on the same physical disk — the question a « second copy » has to answer before it can
/// claim to protect anything (<see cref="AddOnlyFolderCopy.SameDiskRefusal"/>).
///
/// <para>⚠️ <b>A drive letter is not a disk.</b> <c>C:</c> and <c>D:</c> are very often two partitions of the one disk
/// a PC shipped with, so comparing roots alone would wave through exactly the copy that dies with the original. The
/// volume's disk extents (<c>IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS</c>) are what name the disk, and they need no
/// elevation.</para>
///
/// <para>⚠️ <b>Every unknown answers « not shared »</b> — a share, a disk Windows will not describe, a call that fails.
/// The caller refuses on « shared », so a guess there would remove a copy that may be protecting something.</para>
/// </summary>
public static class PhysicalDisks
{
    public static bool ShareADisk(string first, string second)
    {
        try
        {
            var rootA = VolumeRoot(first);
            var rootB = VolumeRoot(second);
            if (rootA is null || rootB is null)
            {
                return false;
            }

            // One volume is one disk, whatever it is — a share included: the same share twice is the same remote disk.
            if (string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var disksA = DisksOf(rootA);
            var disksB = DisksOf(rootB);
            return disksA is not null && disksB is not null && disksA.Overlaps(disksB);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The mount point holding <paramref name="path"/> (« C:\ », or a folder a volume is mounted on), or null.</summary>
    private static string? VolumeRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var full = Path.GetFullPath(path);
        var buffer = new StringBuilder(1024);
        if (GetVolumePathNameW(full, buffer, buffer.Capacity))
        {
            return Normalize(buffer.ToString());
        }

        var root = Path.GetPathRoot(full);
        return string.IsNullOrEmpty(root) ? null : Normalize(root);
    }

    private static string Normalize(string root) =>
        root.EndsWith('\\') ? root : root + "\\";

    /// <summary>The disk numbers a volume spans, or null when Windows will not say (a share, an odd driver).</summary>
    private static HashSet<int>? DisksOf(string root)
    {
        if (root.StartsWith(@"\\", StringComparison.Ordinal) && !root.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return null; // A UNC share has no local disk.
        }

        var volumeName = new StringBuilder(64);
        if (!GetVolumeNameForVolumeMountPointW(root, volumeName, volumeName.Capacity))
        {
            return null;
        }

        // « \\?\Volume{guid}\ » names the volume's root directory; without the slash it names the device itself.
        var device = volumeName.ToString().TrimEnd('\\');

        using var handle = CreateFileW(
            device, 0, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        // Room for 32 extents — a volume spanning more is a storage pool, and « je ne sais pas » is right there.
        const int extentSize = 24;
        var buffer = new byte[8 + (extentSize * 32)];
        if (!DeviceIoControl(
                handle, IoctlVolumeGetVolumeDiskExtents, IntPtr.Zero, 0, buffer, buffer.Length, out _, IntPtr.Zero))
        {
            return null;
        }

        var count = BitConverter.ToInt32(buffer, 0);
        if (count <= 0 || count > 32)
        {
            return null;
        }

        var disks = new HashSet<int>();
        for (var i = 0; i < count; i++)
        {
            // DISK_EXTENT { DWORD DiskNumber; LARGE_INTEGER StartingOffset; LARGE_INTEGER ExtentLength; }, 8-aligned.
            disks.Add(BitConverter.ToInt32(buffer, 8 + (i * extentSize)));
        }

        return disks;
    }

    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint OpenExisting = 3;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(string fileName, StringBuilder volumePathName, int bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(
        string volumeMountPoint, StringBuilder volumeName, int bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        IntPtr inBuffer,
        int inBufferSize,
        [Out] byte[] outBuffer,
        int outBufferSize,
        out int bytesReturned,
        IntPtr overlapped);
}
