using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// What the PC de secours offer needs to know about THIS machine (<c>clinic-pc-copy</c> AC-1.6–1.8): its name, whether
/// it is a laptop, whether its disk is encrypted, and how much room the install drive has. The page owns every word
/// the offer says; the shell owns only these facts, which no web page can read.
/// </summary>
public static class RelayHost
{
    public sealed record Facts(string MachineName, bool HasBattery, bool? DiskEncrypted, long? FreeBytes);

    /// <summary>
    /// The drive the installer writes to — <c>{autopf}</c>, Program Files, for an elevated install. Where the copy's
    /// rows and files will live, so where the room has to be.
    /// </summary>
    public static string InstallDrive =>
        Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)) ?? "C:\\";

    /// <summary>Never throws: a fact that cannot be read is absent, and the offer says less rather than something false.</summary>
    public static Facts Read()
    {
        var drive = InstallDrive;
        return new Facts(Environment.MachineName, HasSystemBattery(), DriveEncryption.IsEncrypted(drive), FreeBytes(drive));
    }

    private static long? FreeBytes(string drive)
    {
        try
        {
            return new DriveInfo(drive).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// <c>BatteryFlag</c> 128 is « no system battery » and 255 « unknown status »; anything else is a battery. A desktop
    /// on a UPS reports 128 — the UPS is not the system's battery — which is what AC-1.6 needs: a laptop warning, not
    /// an onduleur one.
    /// </summary>
    public static bool IsBatteryFlag(byte batteryFlag) => batteryFlag is not (128 or 255);

    private static bool HasSystemBattery()
    {
        try
        {
            return GetSystemPowerStatus(out var status) && IsBatteryFlag(status.BatteryFlag);
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
