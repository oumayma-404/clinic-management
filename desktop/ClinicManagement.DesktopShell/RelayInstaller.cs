using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// « Oui » on the PC de secours offer (<c>clinic-pc-copy</c> AC-1.4): fetch the server installer from the cloud this
/// shell already uses, run it elevated once with the one-time code, and say how it ended. Installation, pairing and
/// the first copy then need nobody.
///
/// <para>⚠️ <b>The code never goes on the command line</b> — every process can read another's arguments. It is
/// written to a file under <c>%LocalAppData%</c>, whose inherited ACL already admits only this user, the
/// Administrators and SYSTEM (so an over-the-shoulder elevation, which runs the installer as a different admin, can
/// still read it), and <c>pair-relay</c> deletes it either way; this class deletes it too if it is still there.</para>
///
/// <para>⚠️ <b>The downloaded file is held open from its hash check until the installer exits</b>, sharing read and
/// nothing else, so no other process of this user can swap it between the check and an elevated run — the window
/// in which a standard user's writable folder would otherwise be a way to admin.</para>
///
/// <para>⚠️ <b>Never throws</b>: the bridge's contract is that every failure is an outcome, worded here in French
/// because only the shell knows which step failed. The installer's own outcomes carry the installer's own sentence
/// (<c>/RESULTFILE=</c>), never a console verb's — those print in the console's code page.</para>
/// </summary>
public static class RelayInstaller
{
    /// <summary>Where the cloud serves the server installer (D10). Same origin as the app, so no address is configured.</summary>
    public const string InstallerPath = "/api/relay/installer";

    /// <summary>The installer's SHA-256 (hex), sent by the cloud beside the bytes. No header, no elevated run.</summary>
    public const string Sha256Header = "X-Content-SHA256";

    /// <summary><c>ERROR_CANCELLED</c>: the person answered « Non » to Windows' permission prompt (AC-1.11).</summary>
    public const int PermissionRefused = 1223;

    public const string Installed = "installed";
    public const string Declined = "declined";
    public const string Refused = "refused";
    public const string Failed = "failed";

    public sealed record Outcome(string Kind, string Sentence);

    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(60);
    private static int _running;

    /// <summary>
    /// The bridge's own request ids (<c>'r' + counter</c>). Checked before one is placed in a script, since anything on
    /// the page can post a message.
    /// </summary>
    public static bool IsRequestId(string? id) =>
        id is { Length: >= 2 and <= 12 } && id[0] == 'r' && id.Skip(1).All(char.IsAsciiDigit);

    /// <summary>
    /// Whether this server's clinics can have a PC de secours (<c>/api/auth/mode</c>: a change feed, and not itself a
    /// PC de secours). False on any failure — the menu item is then simply absent.
    /// </summary>
    public static async Task<bool> ServerOffersRelayAsync(string baseUrl)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.Add("X-Client-Version", ClientRequirements.InstalledVersion);
            var body = await client.GetStringAsync(baseUrl + "/api/auth/mode").ConfigureAwait(false);
            using var mode = System.Text.Json.JsonDocument.Parse(body);
            var root = mode.RootElement;
            return root.TryGetProperty("relayFeedEnabled", out var feed) && feed.ValueKind == System.Text.Json.JsonValueKind.True
                   && !(root.TryGetProperty("isClinicRelay", out var relay) && relay.ValueKind == System.Text.Json.JsonValueKind.True);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The cloud's codes are 43 base64url characters; anything else is not worth an elevated run.</summary>
    public static bool IsPlausibleCode(string? code) =>
        !string.IsNullOrEmpty(code)
        && code.Length is >= 16 and <= 128
        && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>
    /// The installer's silent relay role (<c>clinic-setup.iss</c>). Each <c>/NAME=value</c> is one quoted argument, so
    /// a path with spaces survives Windows' command-line parsing; no value here can end in a backslash.
    /// </summary>
    public static string Arguments(string pairFile, string cloudUrl, string label, long needBytes, string resultFile) =>
        string.Join(' ',
            "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/RELAY",
            Quoted("/PAIRFILE=" + pairFile),
            Quoted("/CLOUD=" + cloudUrl),
            Quoted("/LABEL=" + label.Replace("\"", string.Empty, StringComparison.Ordinal)),
            "/NEEDBYTES=" + Math.Max(0, needBytes),
            Quoted("/RESULTFILE=" + resultFile));

    /// <summary>
    /// The installer's exit code as the page reads it: 0 paired and running · 7 refused before copying anything
    /// (Inno's <c>PrepareToInstall</c>: too little room, already the cabinet's server…) · 20 pairing refused (the code
    /// lapsed) · anything else stopped part-way. The installer's sentence wins whenever it wrote one.
    /// </summary>
    public static Outcome OutcomeFor(int exitCode, string? installerSentence)
    {
        var sentence = string.IsNullOrWhiteSpace(installerSentence) ? null : installerSentence.Trim();
        return exitCode switch
        {
            0 => new Outcome(Installed, sentence ?? "Ce PC est maintenant le PC de secours du cabinet. La première copie commence."),
            7 or 20 => new Outcome(Refused, sentence ?? "L'installation du PC de secours a été refusée."),
            _ => new Outcome(Failed, sentence ?? "L'installation du PC de secours n'a pas pu se terminer."),
        };
    }

    public static async Task<Outcome> InstallAsync(ServerConfig config, string code, long needBytes)
    {
        if (!IsPlausibleCode(code) || !config.IsConfigured)
        {
            return new Outcome(Failed, "Le code d'installation est invalide. Recommencez depuis l'offre.");
        }

        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            return new Outcome(Refused, "Une installation du PC de secours est déjà en cours sur ce PC.");
        }

        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClinicManagement", "relay");
            Directory.CreateDirectory(folder);
            var setupPath = Path.Combine(folder, "APEXA-PC-de-secours-setup.exe");

            var (expectedSha256, failure) = await DownloadAsync(config.BaseUrl, setupPath).ConfigureAwait(false);
            if (failure is not null)
            {
                return failure;
            }

            await using var hold = new FileStream(setupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(hold).ConfigureAwait(false));
            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new Outcome(Failed, "Le fichier d'installation reçu est incomplet ou modifié : il n'a pas été lancé. Réessayez.");
            }

            var pairFile = Path.Combine(folder, $"pair-{Guid.NewGuid():N}.txt");
            var resultFile = Path.Combine(folder, $"result-{Guid.NewGuid():N}.txt");
            try
            {
                await File.WriteAllTextAsync(pairFile, code, new UTF8Encoding(false)).ConfigureAwait(false);
                return await RunElevatedAsync(setupPath, folder,
                    Arguments(pairFile, config.BaseUrl, Environment.MachineName, needBytes, resultFile), resultFile)
                    .ConfigureAwait(false);
            }
            finally
            {
                TryDelete(pairFile);
                TryDelete(resultFile);
            }
        }
        catch (Exception)
        {
            return new Outcome(Failed, "L'installation du PC de secours n'a pas pu démarrer sur ce PC. Réessayez.");
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private static async Task<(string Sha256, Outcome? Failure)> DownloadAsync(string baseUrl, string setupPath)
    {
        try
        {
            using var client = new HttpClient { Timeout = DownloadTimeout };
            client.DefaultRequestHeaders.Add("X-Client-Version", ClientRequirements.InstalledVersion);

            using var response = await client
                .GetAsync(baseUrl + InstallerPath, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return (string.Empty, new Outcome(Failed, "Le cloud ne propose pas encore l'installation du PC de secours."));
            }

            if (!response.IsSuccessStatusCode)
            {
                return (string.Empty, new Outcome(Failed,
                    "Le fichier d'installation n'a pas pu être téléchargé depuis le cloud. Vérifiez votre connexion, puis réessayez."));
            }

            var sha256 = response.Headers.TryGetValues(Sha256Header, out var values) ? values.FirstOrDefault()?.Trim() : null;
            if (string.IsNullOrEmpty(sha256) || sha256.Length != 64)
            {
                return (string.Empty, new Outcome(Failed,
                    "Le cloud n'a pas fourni l'empreinte du fichier d'installation : il n'a pas été lancé."));
            }

            var part = setupPath + ".part";
            await using (var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(target).ConfigureAwait(false);
            }

            File.Move(part, setupPath, overwrite: true);
            return (sha256, null);
        }
        catch (Exception)
        {
            return (string.Empty, new Outcome(Failed,
                "Le fichier d'installation n'a pas pu être téléchargé depuis le cloud. Vérifiez votre connexion, puis réessayez."));
        }
    }

    private static async Task<Outcome> RunElevatedAsync(string setupPath, string folder, string arguments, string resultFile)
    {
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(setupPath, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = folder,
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == PermissionRefused)
        {
            return new Outcome(Declined, "Windows n'a pas donné l'autorisation : rien n'a été installé.");
        }

        if (process is null)
        {
            return new Outcome(Failed, "L'installation du PC de secours n'a pas pu démarrer sur ce PC. Réessayez.");
        }

        using (process)
        {
            using var timeout = new CancellationTokenSource(InstallTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new Outcome(Failed,
                    "L'installation du PC de secours dure anormalement longtemps. « Paramètres → PC de secours » dira où elle en est.");
            }

            return OutcomeFor(process.ExitCode, ReadSentence(resultFile));
        }
    }

    private static string? ReadSentence(string resultFile)
    {
        try
        {
            return File.Exists(resultFile)
                ? File.ReadAllLines(resultFile, Encoding.UTF8).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Quoted(string argument) => "\"" + argument + "\"";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // The pairing code lapses in ten minutes and is single-use; the result sentence holds nothing secret.
        }
    }
}
