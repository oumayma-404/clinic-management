using ClinicManagement.Infrastructure;

namespace ClinicManagement.API.Startup;

/// <summary>
/// The config layers a Local install reads, in one place (L4e).
///
/// <para><b>Why there are two files and not one.</b> The installer used to write
/// <c>appsettings.Production.json</c> with <c>SaveStringToFile(..., False)</c> — truncate — unconditionally from
/// <c>ssPostInstall</c>, with no « if not FileExists » guard, <i>although the author used that exact idiom
/// twenty-five lines away to gate initdb</i>. So every upgrade silently destroyed every value an operator had
/// hand-edited: <c>Cors:AllowedOrigins</c>, <c>Hosting:TrustPort</c>, <c>Security:EnableHsts</c>, the reminder
/// gateway keys — all of them documented in <c>packaging/README.md</c> as things to hand-edit.</para>
///
/// <para>The fix splits the file by <b>ownership</b> rather than trying to merge JSON in Inno Setup's Pascal:</para>
/// <list type="bullet">
///   <item><b><c>appsettings.Install.json</c></b> — machine-derived, <b>installer-owned</b>: the connection
///     string, the bundled tool paths, the ports. Rewritten on every install, because those values are *about*
///     this machine and a stale one is a broken install.</item>
///   <item><b><c>appsettings.Production.json</c></b> — <b>operator-owned</b>: written once when absent, never
///     truncated again, and backed up before it is touched at all. It is loaded <i>after</i> the install layer,
///     so an operator's value always wins.</item>
/// </list>
///
/// <para>A structural split beats a textual merge here for a reason worth stating: a merge has to decide what to
/// do about a key the operator <b>deliberately removed</b>, and every answer is wrong — re-adding it overrides
/// their decision, skipping it means a genuinely new key never arrives. Two files make the question disappear.</para>
///
/// <para>⚠️ Every entry point that builds its own <see cref="IConfiguration"/> must use this method — the four
/// console verbs as well as the host. A verb that read one layer fewer would resolve a different connection
/// string from the app it is maintaining, which is the worst possible way to find out about a missing layer.</para>
/// </summary>
public static class InstallConfiguration
{
    /// <summary>Installer-owned, machine-derived. Regenerated on every install.</summary>
    public const string InstallLayerFileName = "appsettings.Install.json";

    /// <summary>
    /// Adds <c>appsettings.json</c> → <c>appsettings.Install.json</c> → <c>appsettings.{Environment}.json</c> →
    /// environment variables → <b>file-backed secrets</b>, based at the <b>install directory</b> and not the CWD
    /// (a Windows service's CWD is <c>System32</c>).
    ///
    /// <para>⚠️ <see cref="FileBackedSecretsSource"/> is <b>last</b>, so a <c>*_FILE</c> variable beats a literal
    /// of the same name (FR-3.10). Moving a secret to a file and removing its literal are two separate edits; if
    /// the literal won, the state between them would keep reading the old value while appearing to have moved.</para>
    /// </summary>
    public static IConfigurationBuilder AddInstallLayers(
        this IConfigurationBuilder builder, bool baseSettingsOptional = true)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

        return builder
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: baseSettingsOptional)
            // Installer-owned, before the operator layer so the operator can override anything in it.
            .AddJsonFile(InstallLayerFileName, optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Add(new FileBackedSecretsSource());
    }

    /// <summary>
    /// A ready-built configuration for the console verbs, which have no host to borrow one from.
    /// </summary>
    public static IConfiguration BuildForConsoleVerb() =>
        new ConfigurationBuilder().AddInstallLayers(baseSettingsOptional: false).Build();
}
