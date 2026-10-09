using System;
using System.Diagnostics;
using System.IO;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// Whether a drive is protected by BitLocker — <b>true</b>, <b>false</b>, or <b>null for « je ne sais pas »</b>. Shared
/// by the archive-copy window and the PC de secours offer (AC-1.7), so the two cannot answer the same disk differently.
///
/// <para>⚠️ <b>Null is the honest answer far more often than either</b>, and asserting « non chiffré » unverified is the
/// confident-wrong-answer class this repo keeps deleting. The Shell's own volume property answers without elevation;
/// <c>manage-bde</c> is the fallback and needs elevation, so it is usually null too.</para>
/// </summary>
public static class DriveEncryption
{
    public static bool? IsEncrypted(string pathOnDrive)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(pathOnDrive));
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            return FromShellProperty(root) ?? FromManageBde(root.TrimEnd('\\'));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// <c>System.Volume.BitLockerProtection</c>, as Explorer reads it: 1 on · 3 encrypting · 6 on and locked are
    /// protected; 2 off · 4 decrypting · 5 suspended (key in the clear) · 8 waiting for activation are not; anything
    /// else (0, absent — a Home edition, a network drive) is not an answer.
    /// </summary>
    public static bool? FromBitLockerProtection(int? value) => value switch
    {
        1 or 3 or 6 => true,
        2 or 4 or 5 or 8 => false,
        _ => null,
    };

    private static bool? FromShellProperty(string root)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
            {
                return null;
            }

            dynamic? shell = Activator.CreateInstance(shellType);
            dynamic? folder = shell?.NameSpace(root);
            object? value = folder?.Self.ExtendedProperty("System.Volume.BitLockerProtection");
            return value is null ? null : FromBitLockerProtection(Convert.ToInt32(value));
        }
        catch
        {
            return null;
        }
    }

    private static bool? FromManageBde(string root)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("manage-bde", $"-status {root}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process == null || !process.WaitForExit(5000) || process.ExitCode != 0)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (output.Contains("Protection On", StringComparison.OrdinalIgnoreCase)
                || output.Contains("Protection activée", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return output.Contains("Protection Off", StringComparison.OrdinalIgnoreCase)
                   || output.Contains("Protection désactivée", StringComparison.OrdinalIgnoreCase)
                ? false
                : null;
        }
        catch
        {
            return null;
        }
    }
}
