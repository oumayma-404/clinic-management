using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ClinicManagement.API.Models;

/// <summary>
/// The newest Android shell APK sitting in this server's updates folder — what the phone downloads when it offers
/// « Mettre à jour » (<c>android-in-app-update</c>). The Android twin of <see cref="ClientUpdatePackage"/>, and it
/// reads the same folder through <see cref="ClientUpdatePackage.ResolveFolder"/> so the two cannot look in two places.
///
/// <para>
/// ⚠️ <b>Served by the cabinet's own server, never by a fixed public URL.</b> An offline-LAN install has no
/// internet, and the address the phone already reaches is the only one it is known to be able to reach — the
/// desktop shell's Velopack feed comes from the same place for the same reason.
/// </para>
///
/// <para>
/// ⚠️ <b>The version is the file NAME</b> (<c>APEXA-1.2.0.apk</c>): reading <c>versionName</c> out of an APK means
/// parsing a binary manifest, and the release workflow writes the name from the very version it built. Newest by
/// version, not by write time, so a re-copied older file cannot win. A <c>.part</c> upload matches nothing, which
/// is what keeps a half-copied file from ever being offered.
/// </para>
///
/// <para>⚠️ <b>Absent is normal</b> — a server with no APK simply offers no Android update. Never throws: the route
/// that publishes it is the one a refused client depends on.</para>
/// </summary>
public sealed record AndroidUpdatePackage(string Version, string FileName, long Length, string Sha256, string FullPath)
{
    private static readonly Regex NamePattern = new(
        @"^APEXA-(?<version>\d+\.\d+\.\d+(\.\d+)?)\.apk$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Hashed once per file identity (path, length, write time); a replaced file re-hashes on the next read.</summary>
    private static readonly object CacheGate = new();
    private static string _cacheKey = string.Empty;
    private static AndroidUpdatePackage? _cached;

    public static AndroidUpdatePackage? Resolve(IConfiguration configuration, string baseDirectory)
    {
        try
        {
            var folder = ClientUpdatePackage.ResolveFolder(configuration, baseDirectory);
            if (folder is null)
            {
                return null;
            }

            var best = Directory
                .EnumerateFiles(folder, "APEXA-*.apk")
                .Select(path => (path, match: NamePattern.Match(Path.GetFileName(path))))
                .Where(x => x.match.Success)
                // `System.Version` qualified: this record has its own `Version` property, which shadows the type.
                .Select(x => (x.path, version: System.Version.Parse(x.match.Groups["version"].Value)))
                .OrderByDescending(x => x.version)
                .FirstOrDefault();

            if (best.path is null)
            {
                return null;
            }

            var info = new FileInfo(best.path);
            var key = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc:O}";

            lock (CacheGate)
            {
                if (_cacheKey == key && _cached is not null)
                {
                    return _cached;
                }
            }

            var package = new AndroidUpdatePackage(
                // Three components, matching the shell's `versionName` — a four-part name compared against a
                // three-part one would never read as equal.
                $"{best.version.Major}.{best.version.Minor}.{Math.Max(best.version.Build, 0)}",
                info.Name,
                info.Length,
                ComputeSha256(info.FullName),
                info.FullName);

            lock (CacheGate)
            {
                _cacheKey = key;
                _cached = package;
            }

            return package;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }
}
