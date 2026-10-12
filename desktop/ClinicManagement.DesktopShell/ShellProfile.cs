using System;
using System.IO;

namespace ClinicManagement.DesktopShell;

/// <summary>
/// Where this app keeps its per-user files: Windows' own folders, unless <c>APEXA_SHELL_PROFILE</c> names another one.
/// The override exists for testing a build beside the installed app — same machine, same Windows account — without
/// either one reading or rewriting the other's address, PC de secours target or WebView2 session. Unset in every
/// installed copy, so nothing changes there.
/// </summary>
public static class ShellProfile
{
    private static string? Override =>
        Environment.GetEnvironmentVariable("APEXA_SHELL_PROFILE") is { Length: > 0 } dir ? dir : null;

    /// <summary>Instead of <c>%AppData%</c>.</summary>
    public static string Roaming => Override is { } dir
        ? Path.Combine(dir, "roaming")
        : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>Instead of <c>%LocalAppData%</c>.</summary>
    public static string Local => Override is { } dir
        ? Path.Combine(dir, "local")
        : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
}
