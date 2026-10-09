using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>Which launcher the PC de secours uses (<c>Relay:UpdateLauncher</c>).</summary>
public static class RelayUpdateLaunchers
{
    /// <summary>
    /// The scheduled task unless the setting says <c>direct</c> — a test rig, where the API is not a service and its
    /// user cannot register a SYSTEM task. The installer never writes the setting.
    /// </summary>
    public static IRelayUpdateLauncher For(string? setting, string workFolder, ILogger logger) =>
        string.Equals(setting?.Trim(), "direct", StringComparison.OrdinalIgnoreCase)
            ? new DirectProcessUpdateLauncher()
            : new ScheduledTaskUpdateLauncher(workFolder, logger);
}

/// <summary>
/// Runs the installer from a one-shot scheduled task as SYSTEM (D10b).
///
/// <para>⚠️ <b>Why not a child process:</b> the installer's first act is to stop <c>ClinicManagementApi</c> — the very
/// service that would be its parent. A task belongs to the Task Scheduler service, outside that service's tree by
/// construction, so nothing about how Windows tears a stopping service down can take the installer with it. The task
/// has no trigger (it runs only when asked) and is deleted once the new build is up.</para>
///
/// <para>The definition goes through <c>schtasks /xml</c> rather than <c>/tr</c>: <c>/tr</c> caps the command at 261
/// characters, which an install path under « Program Files » plus the installer's arguments passes easily.</para>
/// </summary>
public sealed class ScheduledTaskUpdateLauncher : IRelayUpdateLauncher
{
    public const string TaskName = @"\APEXA\PC de secours - mise a jour";
    public const string SystemSid = "S-1-5-18";

    private static readonly TimeSpan SchtasksTimeout = TimeSpan.FromSeconds(30);
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private readonly string _workFolder;
    private readonly ILogger _logger;

    public ScheduledTaskUpdateLauncher(string workFolder, ILogger logger)
    {
        _workFolder = workFolder;
        _logger = logger;
    }

    public RelayLaunch Launch(string installerPath, string arguments, string workingDirectory)
    {
        var definition = Path.Combine(_workFolder, "tache-mise-a-jour.xml");
        try
        {
            Directory.CreateDirectory(_workFolder);
            // schtasks reads a task definition as UTF-16, which is what the XML declares.
            File.WriteAllText(definition, TaskXml(installerPath, arguments, workingDirectory), Encoding.Unicode);

            var created = Schtasks("/create", "/tn", TaskName, "/xml", definition, "/f");
            if (created.ExitCode != 0)
            {
                return new RelayLaunch(false, $"schtasks /create: {created.ExitCode} {created.Output}");
            }

            var run = Schtasks("/run", "/tn", TaskName);
            return run.ExitCode == 0 ? new RelayLaunch(true) : new RelayLaunch(false, $"schtasks /run: {run.ExitCode} {run.Output}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return new RelayLaunch(false, ex.Message);
        }
        finally
        {
            try
            {
                File.Delete(definition);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public void Forget()
    {
        try
        {
            var deleted = Schtasks("/delete", "/tn", TaskName, "/f");
            if (deleted.ExitCode != 0)
            {
                _logger.LogDebug("PC de secours: no update task to remove ({Output}).", deleted.Output);
            }
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            _logger.LogDebug(ex, "PC de secours: the update task could not be removed.");
        }
    }

    /// <summary>The task: run once on demand, as <paramref name="userSid"/> with its highest rights, on mains or battery.</summary>
    public static string TaskXml(string command, string arguments, string workingDirectory, string userSid = SystemSid)
    {
        var task = new XElement(Ns + "Task", new XAttribute("version", "1.2"),
            new XElement(Ns + "RegistrationInfo",
                new XElement(Ns + "Description", "APEXA : mise à jour du PC de secours, lancée une fois puis supprimée.")),
            new XElement(Ns + "Principals",
                new XElement(Ns + "Principal", new XAttribute("id", "Author"),
                    new XElement(Ns + "UserId", userSid),
                    new XElement(Ns + "RunLevel", "HighestAvailable"))),
            new XElement(Ns + "Settings",
                new XElement(Ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(Ns + "DisallowStartIfOnBatteries", "false"),
                new XElement(Ns + "StopIfGoingOnBatteries", "false"),
                new XElement(Ns + "AllowHardTerminate", "true"),
                new XElement(Ns + "StartWhenAvailable", "false"),
                new XElement(Ns + "RunOnlyIfNetworkAvailable", "false"),
                new XElement(Ns + "AllowStartOnDemand", "true"),
                new XElement(Ns + "Enabled", "true"),
                new XElement(Ns + "Hidden", "false"),
                new XElement(Ns + "RunOnlyIfIdle", "false"),
                new XElement(Ns + "WakeToRun", "false"),
                new XElement(Ns + "ExecutionTimeLimit", "PT2H"),
                new XElement(Ns + "Priority", "7")),
            new XElement(Ns + "Actions", new XAttribute("Context", "Author"),
                new XElement(Ns + "Exec",
                    new XElement(Ns + "Command", command),
                    new XElement(Ns + "Arguments", arguments),
                    new XElement(Ns + "WorkingDirectory", workingDirectory))));

        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" + Environment.NewLine + task;
    }

    private static (int ExitCode, string Output) Schtasks(params string[] arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)
                            ?? throw new System.ComponentModel.Win32Exception("schtasks.exe could not be started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(SchtasksTimeout))
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
            }

            return (-1, "timeout");
        }

        return (process.ExitCode, (output.Result + " " + error.Result).Trim());
    }
}

/// <summary>Starts the installer as a plain child process — a test rig's launcher, never the installed PC's.</summary>
public sealed class DirectProcessUpdateLauncher : IRelayUpdateLauncher
{
    public RelayLaunch Launch(string installerPath, string arguments, string workingDirectory)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(installerPath, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory,
            });
            return process is null ? new RelayLaunch(false, "no process") : new RelayLaunch(true);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            return new RelayLaunch(false, ex.Message);
        }
    }

    public void Forget()
    {
    }
}
