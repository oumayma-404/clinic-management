using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ClinicManagement.API.Models;

/// <summary>
/// The Windows client installer this server was shipped with, ready to be handed to a shell that asks for it.
///
/// <para>
/// ⚠️ <b>This exists so an offline clinic can update itself.</b> Before it, the desktop shell's « Mettre à jour
/// maintenant » could only download from <c>Clients:StoreUrls:Windows</c> — a URL somebody outside the product
/// had to host and keep reachable. On a cabinet's own LAN, with no internet, there is frequently nowhere to put
/// it: the operator upgrades the server, every PC keeps announcing an update, and none can fetch one. The server
/// installer now carries the matching client setup into <c>{app}\updates</c>, and this reads it from there — so
/// upgrading the server is the only act, and the clients follow.
/// </para>
///
/// <para>
/// ⚠️ <b>The hash is computed here, not configured.</b> An operator-typed SHA-256 beside an operator-copied file
/// is two things that can disagree, and the failure mode of a stale hash is « every client refuses the update »,
/// which looks exactly like a broken download. Reading it off the very bytes that will be served cannot drift —
/// and it is still worth publishing, because the shell runs that file <b>elevated</b>.
/// </para>
///
/// <para>
/// ⚠️ <b>Absent is normal, not an error.</b> A hosted deployment has no offline-LAN client to ship, and a
/// server installed before this change has an empty folder. Both mean « no local package », which leaves the
/// configured <c>StoreUrls:Windows</c> exactly as it was.
/// </para>
/// </summary>
public sealed record ClientUpdatePackage(string Version, string FileName, long Length, string Sha256, string FullPath)
{
    /// <summary>Where the server installer stages it, relative to the API's own directory.</summary>
    private const string DefaultFolder = "updates";

    /// <summary>
    /// What the server installer names it — <c>publish-server.ps1</c> compiles the client setup first and copies
    /// it in under exactly this shape, so the version can be read back without a manifest file to keep in step.
    /// </summary>
    private static readonly Regex NamePattern = new(
        @"^ClinicManagementClientSetup-(?<version>\d+\.\d+\.\d+(\.\d+)?)\.exe$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Hashing 50 MB on every probe would make a launch-time read cost half a second of CPU per client, so the
    /// answer is cached against the file's identity (path, length, write time). A replaced file therefore
    /// re-hashes on the next read without anything having to invalidate anything.
    /// </summary>
    private static readonly object CacheGate = new();
    private static string _cacheKey = string.Empty;
    private static ClientUpdatePackage? _cached;

    /// <summary>
    /// The newest installer present, or <c>null</c>. Never throws: an unreadable folder, a permissions refusal
    /// and a malformed filename all mean « nothing to offer », because the alternative is a server that will not
    /// answer <c>client-requirements</c> at all — the one route a refused client depends on.
    /// </summary>
    public static ClientUpdatePackage? Resolve(IConfiguration configuration, string baseDirectory)
    {
        try
        {
            var configured = configuration["Clients:UpdateDirectory"];
            var folder = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(baseDirectory, DefaultFolder)
                : configured;

            if (!Directory.Exists(folder))
            {
                return null;
            }

            // Newest by VERSION, not by write time: a re-copied older file must not win, and the operator may
            // legitimately leave a previous release in place.
            var best = Directory
                .EnumerateFiles(folder, "ClinicManagementClientSetup-*.exe")
                .Select(path => (path, match: NamePattern.Match(Path.GetFileName(path))))
                .Where(x => x.match.Success)
                // `System.Version` qualified: this record has its own `Version` property, which shadows the type.
                .Select(x => (x.path, version: System.Version.Parse(x.match.Groups["version"].Value)))
                .OrderByDescending(x => x.version)
                .Select(x => (x.path, x.version))
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

            var sha = ComputeSha256(best.path);

            var package = new ClientUpdatePackage(
                // Three components, matching what the shell reports as X-Client-Version — a four-part
                // AssemblyVersion compared against a three-part filename would never be equal.
                $"{best.version.Major}.{best.version.Minor}.{Math.Max(best.version.Build, 0)}",
                info.Name,
                info.Length,
                sha,
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
            // Stated on the type: nothing here may take down the route a refused client reads.
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
