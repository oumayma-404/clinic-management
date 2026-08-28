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
    /// The legacy Inno client setup, whose filename carries its own version — so it can be read back without a
    /// manifest to keep in step. Still recognised because an offline-LAN clinic uses that installer for a FIRST
    /// install (it imports the LAN certificate authority and bootstraps the WebView2 runtime, neither of which a
    /// per-user Velopack setup can do without elevation).
    /// </summary>
    private static readonly Regex NamePattern = new(
        @"^ClinicManagementClientSetup-(?<version>\d+\.\d+\.\d+(\.\d+)?)\.exe$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Velopack's own setup, and its version comes from the feed manifest rather than from its name — <c>vpk</c>
    /// emits a single unversioned <c>&lt;id&gt;-win-Setup.exe</c> that is replaced on every release.
    /// </summary>
    private static readonly Regex VelopackSetupPattern = new(
        @"^[A-Za-z0-9._-]+-win-Setup\.exe$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The feed manifest <c>vpk</c> writes beside the packages. Read only for the VERSION of the setup above; the
    /// update mechanism itself never comes through here — the shell talks to <c>client-feed</c> directly and
    /// Velopack parses this file properly.
    /// </summary>
    private const string VelopackManifest = "releases.win.json";

    /// <summary>
    /// Hashing 50 MB on every probe would make a launch-time read cost half a second of CPU per client, so the
    /// answer is cached against the file's identity (path, length, write time). A replaced file therefore
    /// re-hashes on the next read without anything having to invalidate anything.
    /// </summary>
    private static readonly object CacheGate = new();
    private static string _cacheKey = string.Empty;
    private static ClientUpdatePackage? _cached;

    /// <summary>
    /// The folder both this and the Velopack feed route read, or <c>null</c> when there is none. Shared so the
    /// installer download and the feed cannot end up looking in two different places.
    /// </summary>
    public static string? ResolveFolder(IConfiguration configuration, string baseDirectory)
    {
        try
        {
            var configured = configuration["Clients:UpdateDirectory"];
            var folder = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(baseDirectory, DefaultFolder)
                : configured;

            return Directory.Exists(folder) ? folder : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The newest installer present, or <c>null</c>. Never throws: an unreadable folder, a permissions refusal
    /// and a malformed filename all mean « nothing to offer », because the alternative is a server that will not
    /// answer <c>client-requirements</c> at all — the one route a refused client depends on.
    /// </summary>
    public static ClientUpdatePackage? Resolve(IConfiguration configuration, string baseDirectory)
    {
        try
        {
            var folder = ResolveFolder(configuration, baseDirectory);
            if (folder is null)
            {
                return null;
            }

            // ⚠️ **A Velopack feed wins over a legacy Inno setup when both are present**, and that ordering is
            // the migration: a folder holding both is a clinic mid-move, and the answer it should be given is the
            // self-updating one. The Inno path stays only for a first install on an offline LAN.
            var velopack = ResolveVelopackSetup(folder);
            if (velopack is not null)
            {
                return velopack;
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

    /// <summary>
    /// The Velopack setup in <paramref name="folder"/> plus the version its manifest names, or <c>null</c>.
    ///
    /// <para>⚠️ The version is taken from the manifest's <b>highest</b> asset rather than from the first entry:
    /// the file lists full and delta packages for a release and may retain older ones, and the setup on disk is
    /// always the newest release's. A wrong version here would be published as <c>currentShellVersion</c> and
    /// either hide a real update or advertise one that does not exist.</para>
    /// </summary>
    private static ClientUpdatePackage? ResolveVelopackSetup(string folder)
    {
        var setup = Directory
            .EnumerateFiles(folder, "*-win-Setup.exe")
            .FirstOrDefault(path => VelopackSetupPattern.IsMatch(Path.GetFileName(path)));

        if (setup is null)
        {
            return null;
        }

        var manifest = Path.Combine(folder, VelopackManifest);
        if (!File.Exists(manifest))
        {
            return null; // A setup with no manifest is a half-published feed; say nothing rather than guess.
        }

        System.Version? highest = null;
        using (var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifest)))
        {
            if (!document.RootElement.TryGetProperty("Assets", out var assets)
                || assets.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return null;
            }

            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.TryGetProperty("Version", out var v)
                    && System.Version.TryParse(v.GetString(), out var parsed)
                    && (highest is null || parsed > highest))
                {
                    highest = parsed;
                }
            }
        }

        if (highest is null)
        {
            return null;
        }

        var info = new FileInfo(setup);
        var key = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc:O}|{highest}";

        lock (CacheGate)
        {
            if (_cacheKey == key && _cached is not null)
            {
                return _cached;
            }
        }

        var package = new ClientUpdatePackage(
            $"{highest.Major}.{highest.Minor}.{Math.Max(highest.Build, 0)}",
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

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }
}
