using Microsoft.Extensions.Configuration;

namespace ClinicManagement.API.Startup;

/// <summary>
/// Reads secrets from <b>files</b> named by <c>*_FILE</c> environment variables, the Docker-secrets convention
/// (<c>hosted-security-hardening</c> FR-3.10).
///
/// <para><b>What it buys.</b> An environment variable is visible to anything that can inspect the container —
/// <c>docker inspect</c>, <c>/proc/&lt;pid&gt;/environ</c>, a crash dump, and every child process the app ever
/// spawns — and it is what an ops tool prints by accident. A file is readable by the process and named, not
/// carried, everywhere else. So <c>ConnectionStrings__DefaultConnection_FILE=/run/secrets/db_connection</c>
/// supplies <c>ConnectionStrings:DefaultConnection</c> without the value ever entering the environment.</para>
///
/// <para>⚠️ <b>A <c>*_FILE</c> variable WINS over a literal of the same name, and that direction is
/// load-bearing.</b> Migrating a deployment means setting the file and removing the literal, and those are two
/// separate edits — if the literal won, the intermediate state would read the old value while every log line
/// said the secret had moved, i.e. the change would appear to work and do nothing.</para>
///
/// <para>⚠️ <b>An unreadable file is a startup failure, never an empty value.</b> Silently yielding "" would
/// hand the application an empty connection string, an empty signing key or an empty backup recipient — each of
/// which fails much later, somewhere else, in a message naming neither the file nor the variable.</para>
///
/// <para>⚠️ <b>It is added inside <see cref="InstallConfiguration.AddInstallLayers"/> and nowhere else</b>, so
/// the host and every console verb read the identical layer stack. A verb reading one layer fewer would resolve
/// a <i>different connection string</i> from the application it is maintaining — which is the worst possible way
/// to discover a missing layer, and why <c>FileBackedSecretsTests</c> asserts it off that one method.</para>
/// </summary>
public sealed class FileBackedSecretsSource : IConfigurationSource
{
    /// <summary>Suffix marking a variable as naming a file rather than holding a value.</summary>
    public const string Suffix = "_FILE";

    private readonly IDictionary<string, string?> _environment;

    /// <param name="environment">
    /// The environment to read, injectable so the behaviour is testable without mutating the process's own.
    /// </param>
    public FileBackedSecretsSource(IDictionary<string, string?>? environment = null)
    {
        _environment = environment ?? ReadProcessEnvironment();
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder) =>
        new FileBackedSecretsProvider(_environment);

    private static IDictionary<string, string?> ReadProcessEnvironment()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            result[(string)entry.Key] = entry.Value as string;
        }

        return result;
    }
}

/// <summary>Loads each <c>*_FILE</c> variable's file into the key it names. See <see cref="FileBackedSecretsSource"/>.</summary>
public sealed class FileBackedSecretsProvider : ConfigurationProvider
{
    private readonly IDictionary<string, string?> _environment;

    public FileBackedSecretsProvider(IDictionary<string, string?> environment)
    {
        _environment = environment;
    }

    public override void Load()
    {
        Data = Read(_environment, File.Exists, File.ReadAllText);
    }

    /// <summary>
    /// The rule, separated from the filesystem so it is testable: which keys a set of variables produces, and
    /// what each refuses.
    /// </summary>
    /// <exception cref="InvalidOperationException">A named file is missing, unreadable or empty.</exception>
    public static IDictionary<string, string?> Read(
        IDictionary<string, string?> environment,
        Func<string, bool> exists,
        Func<string, string> readAllText)
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (variable, path) in environment)
        {
            if (!variable.EndsWith(FileBackedSecretsSource.Suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = ToConfigurationKey(variable);
            if (key.Length == 0)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(
                    $"{variable} est défini mais vide. Il doit nommer le fichier contenant la valeur de "
                    + $"« {key} » — supprimez la variable ou indiquez un chemin.");
            }

            if (!exists(path))
            {
                throw new InvalidOperationException(
                    $"{variable} désigne « {path} », qui n'existe pas. Le secret « {key} » ne peut pas être lu ; "
                    + "vérifiez le bloc `secrets:` du fichier compose et le montage du conteneur.");
            }

            string contents;
            try
            {
                contents = readAllText(path);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"{variable} désigne « {path} », illisible : {ex.Message}. Le secret « {key} » ne peut pas "
                    + "être chargé.", ex);
            }

            // A trailing newline is what every editor and `echo` adds, and it silently corrupts a password, a
            // key or a connection string. Only the outer whitespace goes — the value itself is untouched.
            contents = contents.Trim('\r', '\n', ' ', '\t');

            if (contents.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Le fichier « {path} » désigné par {variable} est vide. Une valeur vide pour « {key} » "
                    + "échouerait bien plus tard, ailleurs, dans un message ne nommant ni le fichier ni la "
                    + "variable — le refus a lieu ici.");
            }

            data[key] = contents;
        }

        return data;
    }

    /// <summary>
    /// <c>ConnectionStrings__DefaultConnection_FILE</c> → <c>ConnectionStrings:DefaultConnection</c>, the same
    /// <c>__</c> → <c>:</c> rule the environment-variable provider beside it uses.
    /// </summary>
    public static string ToConfigurationKey(string variable) =>
        variable[..^FileBackedSecretsSource.Suffix.Length].Replace("__", ":");
}
