using System.Security.Cryptography;
using System.Text.Json;

namespace ClinicManagement.API.Models;

/// <summary>
/// The server installer a PC de secours is set up — and later updated — from (<c>clinic-pc-copy</c> D10), served by
/// <c>GET /api/relay/installer</c>.
///
/// <para>⚠️ <b>Only the installer of THIS build is ever served (lockstep).</b> A PC on another build is refused every
/// copy call (<c>relay_version_mismatch</c>), so an installer one deploy behind would pair a PC that can never copy.
/// The folder therefore holds one manifest per published build — <c>{ "build": "…", "file": "…" }</c>, written by CI
/// from the installer's own <c>relay-build</c> verb — and this picks the one naming <see cref="Find"/>'s build. Keeping
/// the last few (CI prunes to three) is what makes a rollback keep its installer.</para>
///
/// <para>⚠️ <b>The hash is computed off the bytes, never configured</b> — <see cref="ClientUpdatePackage"/>'s reason: a
/// typed hash beside a copied file is two things that can disagree, and the Windows app runs this file elevated.</para>
/// </summary>
public sealed record RelayInstallerPackage(string Build, string FileName, long Length, string Sha256, string FullPath)
{
    /// <summary>Beside the Windows client's feed: <c>deploy/updates/relay/</c> on the server, <c>/app/updates/relay</c> in the container.</summary>
    private const string DefaultFolder = "updates/relay";

    private static readonly object CacheGate = new();
    private static string _cacheKey = string.Empty;
    private static string _cachedSha256 = string.Empty;

    public static string ResolveFolder(IConfiguration configuration, string baseDirectory)
    {
        var configured = configuration["Relay:InstallerDirectory"];
        return string.IsNullOrWhiteSpace(configured) ? Path.Combine(baseDirectory, DefaultFolder) : configured;
    }

    /// <summary>
    /// The installer published for <paramref name="build"/>, or <c>null</c> — none published yet, only older ones, or a
    /// manifest naming a file that is not there. Never throws: an unreadable folder is « nothing to serve ».
    /// </summary>
    public static RelayInstallerPackage? Find(string folder, string build)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(build) || !Directory.Exists(folder))
            {
                return null;
            }

            var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
            foreach (var manifest in Directory.EnumerateFiles(folder, "*.json"))
            {
                var (manifestBuild, file) = ReadManifest(manifest);
                if (!string.Equals(manifestBuild, build, StringComparison.Ordinal) || !IsBareFileName(file))
                {
                    continue;
                }

                var full = Path.GetFullPath(Path.Combine(folder, file!));
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                {
                    continue;
                }

                var info = new FileInfo(full);
                return new RelayInstallerPackage(build, info.Name, info.Length, Sha256Of(info), full);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static (string? Build, string? File) ReadManifest(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return (
                root.TryGetProperty("build", out var build) && build.ValueKind == JsonValueKind.String ? build.GetString() : null,
                root.TryGetProperty("file", out var file) && file.ValueKind == JsonValueKind.String ? file.GetString() : null);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>A manifest names a file in its own folder, an <c>.exe</c>, and nothing else.</summary>
    private static bool IsBareFileName(string? file) =>
        !string.IsNullOrWhiteSpace(file)
        && file.Length <= 200
        && file.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !file.Contains("..", StringComparison.Ordinal)
        && file == Path.GetFileName(file)
        && file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>~150 MB hashed once per file: cached on the file's identity, so a replaced file re-hashes by itself.</summary>
    private static string Sha256Of(FileInfo info)
    {
        var key = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        lock (CacheGate)
        {
            if (key == _cacheKey)
            {
                return _cachedSha256;
            }
        }

        using var stream = info.OpenRead();
        var sha = Convert.ToHexString(SHA256.HashData(stream));
        lock (CacheGate)
        {
            _cacheKey = key;
            _cachedSha256 = sha;
        }

        return sha;
    }
}
