using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// Fetches the newer client setup and hands it to Windows to install — « Mettre à jour maintenant » in one click.
///
/// <para>
/// ⚠️ <b>What this replaced was not an update mechanism at all.</b> The notice strip's button called
/// <c>Process.Start(url)</c>, i.e. it opened the download in the default browser. From there a member of staff
/// had to find the file in their Downloads folder, run it, answer UAC and click through a wizard — for a product
/// whose users are a dentist and a receptionist between patients. The strip announced an update correctly and
/// then asked the one person least equipped to perform it to perform it manually.
/// </para>
///
/// <para>
/// ⚠️ <b>One UAC prompt is unavoidable, and it is raised deliberately at the moment of the click.</b> The shell
/// installs into <c>{autopf}\APEXA</c> with <c>PrivilegesRequired=admin</c>, so replacing its files requires
/// elevation. The alternatives are a SYSTEM-level helper service (silent, but a permanently elevated component
/// polling the network for something to execute) or moving the whole app to a per-user directory. A prompt the
/// user has just asked for by pressing an update button is the ordinary desktop bargain, and it is honest about
/// what is happening.
/// </para>
///
/// <para>
/// ⚠️ <b>The bytes are verified before anything is executed</b> when the server publishes a hash. An installer
/// is the single most dangerous file this product ever writes to disk — it runs elevated, immediately — so an
/// unverified download is only accepted when the server offers nothing to verify against, and never after a
/// mismatch.
/// </para>
/// </summary>
public static class UpdateInstaller
{
    /// <summary>Where the downloaded setup is staged. Per-user and outside the install dir, so no elevation to write.</summary>
    private static string StagingDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClinicManagement", "updates");

    /// <summary>Progress of the download, 0..1, plus a French line describing what is happening.</summary>
    public sealed record Progress(double Fraction, string Message);

    /// <summary>What happened. <see cref="Launched"/> means the installer is running and the shell should exit.</summary>
    public sealed record Result(bool Launched, string? Error);

    /// <summary>
    /// Downloads <paramref name="url"/>, verifies it against <paramref name="expectedSha256"/> when one is given,
    /// and starts it silently. Returns once the installer has been <b>launched</b> — the caller then shuts the
    /// shell down so its files are not locked.
    /// </summary>
    public static async Task<Result> DownloadAndLaunchAsync(
        string url,
        string version,
        string? expectedSha256,
        IProgress<Progress>? progress,
        CancellationToken cancellationToken = default)
    {
        string file;
        try
        {
            Directory.CreateDirectory(StagingDirectory);
            // Named after the version so a retry of the same update reuses the slot instead of accumulating
            // 50 MB per attempt, and so a half-written file from a previous run is overwritten rather than run.
            file = Path.Combine(StagingDirectory, $"APEXA-Setup-{Sanitise(version)}.exe");
        }
        catch (Exception ex)
        {
            return new Result(false, "Le dossier de téléchargement n'a pas pu être créé." + Environment.NewLine + ex.Message);
        }

        // ⚠️ Written to `.part` and renamed only on a completed stream — `ArchiveCopyService`'s rule, and for a
        // sharper reason here: a truncated .exe is still an .exe, and the whole point of the next step is that
        // Windows executes this file with administrator rights.
        var partial = file + ".part";

        try
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            {
                client.DefaultRequestHeaders.Add("X-Client-Version", ClientRequirements.InstalledVersion);

                using var response = await client
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return new Result(false,
                        $"Le serveur a répondu {(int)response.StatusCode} au téléchargement de la mise à jour.");
                }

                var total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var destination = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    written += read;

                    // A percentage only where the server said how long the file is; otherwise megabytes, which is
                    // still a moving number and is at least true. A fabricated percentage is a measurement that
                    // is not one.
                    progress?.Report(total is > 0
                        ? new Progress((double)written / total.Value,
                            $"Téléchargement… {written * 100 / total.Value} %")
                        : new Progress(0, $"Téléchargement… {written / 1024 / 1024} Mo"));
                }
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                progress?.Report(new Progress(1, "Vérification…"));
                var actual = await ComputeSha256Async(partial, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(partial);
                    return new Result(false,
                        "Le fichier téléchargé ne correspond pas à l'empreinte publiée par le serveur. " +
                        "La mise à jour a été annulée et le fichier supprimé.");
                }
            }

            TryDelete(file);
            File.Move(partial, file);
        }
        // ⚠️ **The `when` filter is load-bearing.** `HttpClient` surfaces its own Timeout as a
        // `TaskCanceledException`, i.e. as an `OperationCanceledException` — so catching the bare type would
        // report a thirty-minute stall the same way it reports a user cancelling: silently. The button would
        // then appear to do nothing at all, which is the one outcome worse than an error message. Only the
        // caller's own token is silent; a timeout falls through to the handler below and gets a sentence.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDelete(partial);
            return new Result(false, null); // The user cancelled; not a failure to report.
        }
        catch (Exception ex)
        {
            TryDelete(partial);
            return new Result(false,
                "La mise à jour n'a pas pu être téléchargée." + Environment.NewLine + Environment.NewLine + ex.Message);
        }

        try
        {
            progress?.Report(new Progress(1, "Installation…"));

            // /SILENT   — a progress window, no wizard, no questions.
            // /NORESTART — never reboot a clinic PC mid-day on our own initiative.
            // /restartapp=1 — read by the client installer's [Run] entry, which relaunches the shell afterwards.
            //
            // ⚠️ UseShellExecute is required for the elevation prompt: the setup's manifest requests admin, and
            // only the shell execute path lets Windows raise UAC. With it false, the launch simply fails.
            var start = new System.Diagnostics.ProcessStartInfo(file)
            {
                Arguments = "/SILENT /NORESTART /restartapp=1",
                UseShellExecute = true,
            };
            System.Diagnostics.Process.Start(start);
            return new Result(true, null);
        }
        catch (Exception ex)
        {
            // The commonest case by far is the user answering « Non » to UAC (ERROR_CANCELLED, 1223). That is a
            // decision, not a fault, and must not be reported as one — the app behind the strip still works.
            if (ex is System.ComponentModel.Win32Exception { NativeErrorCode: 1223 })
            {
                return new Result(false, null);
            }

            return new Result(false,
                "L'installation n'a pas pu être lancée." + Environment.NewLine + Environment.NewLine + ex.Message);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    /// <summary>Keeps a version string usable as a filename — it arrives from the server.</summary>
    private static string Sanitise(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "nouvelle";
        }

        var clean = version.Trim();
        foreach (var bad in Path.GetInvalidFileNameChars())
        {
            clean = clean.Replace(bad, '-');
        }
        return clean;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort: a leftover file in a per-user temp folder is not worth failing an update over.
        }
    }
}
