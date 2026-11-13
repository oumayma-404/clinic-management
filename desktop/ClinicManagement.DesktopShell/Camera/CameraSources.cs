using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Portable;
using Windows.Storage;

namespace ClinicManagement.DesktopShell.Camera;

/// <summary>
/// Finds the sources a dentist can import from: a removable drive holding <c>DCIM</c>, or a camera on a cable.
/// Every failure is « not a source » — this runs when somebody plugs something in, and nothing about a USB key may
/// interrupt the app.
/// </summary>
public static class CameraSources
{
    /// <summary>A card (or a camera mounting as a drive) at this root, or null when there is no <c>DCIM</c> there.</summary>
    public static CardCameraSource? CardAt(string root)
    {
        try
        {
            if (!Directory.Exists(Path.Combine(root, "DCIM")))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            var letter = drive.Name.TrimEnd('\\');
            string volumeLabel;
            try
            {
                volumeLabel = drive.VolumeLabel?.Trim() ?? string.Empty;
            }
            catch
            {
                volumeLabel = string.Empty;
            }

            var label = volumeLabel.Length == 0 ? $"Carte ({letter})" : $"Carte {volumeLabel} ({letter})";

            // The volume's serial: a card read twice is one card; a reformatted card is a new one, which is right.
            var key = VolumeSerial(root) is { } serial
                ? $"card:{serial:x8}"
                : $"card:{char.ToLowerInvariant(letter[0])}";

            return new CardCameraSource(root, key, label);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// A camera behind this portable-device id, or null: no <c>DCIM</c>, a device with a drive letter (the card path
    /// reads it), or a Windows that will not describe it.
    /// </summary>
    public static async Task<ICameraSource?> FromPortableDeviceAsync(string deviceId, string? name)
    {
        try
        {
            var root = StorageDevice.FromId(deviceId);
            if (root is null || !string.IsNullOrEmpty(root.Path))
            {
                return null;
            }

            var dcims = new List<(string Prefix, StorageFolder Folder)>();
            foreach (var folder in await root.GetFoldersAsync())
            {
                if (folder.Name.Equals("DCIM", StringComparison.OrdinalIgnoreCase))
                {
                    dcims.Add((string.Empty, folder));
                    continue;
                }

                // A camera shows its card slots first (« SD », « Carte 1 »), each with its own DCIM.
                foreach (var inner in await folder.GetFoldersAsync())
                {
                    if (inner.Name.Equals("DCIM", StringComparison.OrdinalIgnoreCase))
                    {
                        dcims.Add((folder.Name, inner));
                    }
                }
            }

            if (dcims.Count == 0)
            {
                return null;
            }

            var key = "wpd:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deviceId)), 0, 8)
                .ToLowerInvariant();
            var label = string.IsNullOrWhiteSpace(name) ? "Appareil photo" : name.Trim();
            return new WpdCameraSource(key, label, dcims);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Everything connected right now — « Chercher l'appareil », and a camera plugged in before the app started.</summary>
    public static async Task<IReadOnlyList<ICameraSource>> FindConnectedAsync()
    {
        var sources = new List<ICameraSource>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType == DriveType.Removable && drive.IsReady
                    && CardAt(drive.RootDirectory.FullName) is { } card)
                {
                    sources.Add(card);
                }
            }
            catch
            {
                // A reader with no card answers « not ready » by throwing on some drivers.
            }
        }

        try
        {
            foreach (var device in await DeviceInformation.FindAllAsync(StorageDevice.GetDeviceSelector()))
            {
                if (await FromPortableDeviceAsync(device.Id, device.Name) is { } camera)
                {
                    sources.Add(camera);
                }
            }
        }
        catch
        {
            // No portable-device layer: the cards above are what there is.
        }

        return sources;
    }

    private static uint? VolumeSerial(string root)
    {
        try
        {
            return GetVolumeInformationW(root, null, 0, out var serial, out _, out _, null, 0) ? serial : null;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(
        string rootPathName,
        StringBuilder? volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder? fileSystemNameBuffer,
        int fileSystemNameSize);
}
