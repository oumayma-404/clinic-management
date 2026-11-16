using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ClinicManagement.DesktopShell.Camera;

/// <summary>
/// This PC's camera settings (<c>features/camera-photo-import</c>, phase 2), per Windows account.
///
/// <para>⚠️ <b><see cref="AutoImport"/> is off unless switched on</b>, and an absent file reads as off. A
/// <c>desktop/**</c> merge reaches every clinic's PC silently through Velopack; a USB key must not start raising
/// « Appareil photo détecté » in a practice that never asked for it.</para>
///
/// <para><see cref="IgnoredSources"/>: a device somebody chose « Ignorer cet appareil » for — the phone a colleague
/// charges on this PC every morning.</para>
/// </summary>
public sealed class CameraSettings
{
    public bool AutoImport { get; set; }

    public List<string> IgnoredSources { get; set; } = new();
}

public static class CameraSettingsStore
{
    private const string FileName = "camera.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static string FilePath => Path.Combine(ShellProfile.Roaming, "ClinicManagement", FileName);

    /// <summary>Never throws: unreadable reads as « off », which is the safe direction.</summary>
    public static CameraSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<CameraSettings>(File.ReadAllText(FilePath)) ?? new CameraSettings();
            }
        }
        catch
        {
            // Corrupt: off.
        }

        return new CameraSettings();
    }

    public static bool TrySave(CameraSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Json));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
