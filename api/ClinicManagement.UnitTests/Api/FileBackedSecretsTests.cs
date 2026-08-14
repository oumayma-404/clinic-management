using ClinicManagement.API.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// Secrets supplied as <b>files</b> rather than environment variables (<c>hosted-security-hardening</c> FR-3.10).
///
/// <para>Most of this class is about the <b>refusals</b>, and that is where its value is: a layer that silently
/// yields <c>""</c> for a missing file hands the application an empty connection string, an empty signing key or
/// an empty backup recipient — each of which fails much later, somewhere else, in a message naming neither the
/// file nor the variable. The whole point of moving a secret into a file is that its absence becomes loud.</para>
/// </summary>
public class FileBackedSecretsTests
{
    private static IDictionary<string, string?> Env(params (string Key, string? Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);

    private static IDictionary<string, string?> Read(
        IDictionary<string, string?> environment, params (string Path, string Contents)[] files)
    {
        var byPath = files.ToDictionary(f => f.Path, f => f.Contents, StringComparer.Ordinal);
        return FileBackedSecretsProvider.Read(environment, byPath.ContainsKey, p => byPath[p]);
    }

    [Fact]
    public void A_File_Backed_Variable_Supplies_The_Key_It_Names() // [FR-3.10]
    {
        var data = Read(
            Env(("ConnectionStrings__DefaultConnection_FILE", "/run/secrets/db")),
            ("/run/secrets/db", "Host=postgres;Database=clinic"));

        Assert.Equal("Host=postgres;Database=clinic", data["ConnectionStrings:DefaultConnection"]);
    }

    [Fact]
    public void Double_Underscores_Become_Section_Separators() // [FR-3.10]
    {
        var data = Read(
            Env(("Auth__Local__SigningKey_FILE", "/run/secrets/key")),
            ("/run/secrets/key", "abcdef"));

        Assert.True(data.ContainsKey("Auth:Local:SigningKey"));
    }

    // ⚠️ Every editor and every `echo` adds a trailing newline, and it silently corrupts a password, a key or a
    // connection string — producing an authentication failure that looks like a wrong value rather than a stray byte.
    [Fact]
    public void Surrounding_Whitespace_Is_Trimmed_And_The_Value_Is_Not() // [FR-3.10]
    {
        var data = Read(
            Env(("GoogleCalendar__ClientSecret_FILE", "/run/secrets/gcal")),
            ("/run/secrets/gcal", "  gcs_abc def \r\n"));

        Assert.Equal("gcs_abc def", data["GoogleCalendar:ClientSecret"]);
    }

    [Fact]
    public void A_Variable_Without_The_Suffix_Is_Ignored() // [FR-3.10]
    {
        var data = Read(Env(("GoogleCalendar__ClientSecret", "literal-value")));

        Assert.Empty(data);
    }

    // ---- The refusals ---------------------------------------------------------------------------

    [Fact]
    public void A_Missing_File_Refuses_And_Names_Both_The_Variable_And_The_Path() // [FR-3.10]
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Read(Env(("Console__SigningKey_FILE", "/run/secrets/absent"))));

        Assert.Contains("Console__SigningKey_FILE", ex.Message, StringComparison.Ordinal);
        Assert.Contains("/run/secrets/absent", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_Empty_File_Refuses_Rather_Than_Yielding_An_Empty_Secret() // [FR-3.10]
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Read(Env(("Console__SigningKey_FILE", "/run/secrets/blank")), ("/run/secrets/blank", "\n  \n")));

        Assert.Contains("vide", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_File_Variable_With_No_Path_Refuses() // [FR-3.10]
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Read(Env(("Console__SigningKey_FILE", "   "))));

        Assert.Contains("Console__SigningKey_FILE", ex.Message, StringComparison.Ordinal);
    }

    // ---- The layer, in place --------------------------------------------------------------------

    // ⚠️ The direction is load-bearing. Moving a secret is two edits — write the file, drop the literal — and if
    // the literal won, the state between them would keep reading the OLD value while every sign said it had moved.
    [Fact]
    public void A_File_Backed_Value_Beats_A_Literal_Of_The_Same_Name() // [FR-3.10]
    {
        var path = Path.Combine(Path.GetTempPath(), $"hshb-secret-{Guid.NewGuid():N}");
        File.WriteAllText(path, "from-the-file");
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Some:Secret"] = "from-the-literal" })
                .Add(new FileBackedSecretsSource(
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Some__Secret_FILE"] = path,
                    }))
                .Build();

            Assert.Equal("from-the-file", configuration["Some:Secret"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ⚠️ The whole point of adding it inside AddInstallLayers: the host and every console verb share that one
    // method, so a verb reading one layer fewer would resolve a DIFFERENT connection string from the application
    // it is maintaining — the worst possible way to discover a missing layer.
    [Fact]
    public void The_Layer_Is_Part_Of_The_Stack_Every_Entry_Point_Shares() // [FR-3.10]
    {
        var builder = new ConfigurationBuilder().AddInstallLayers();

        Assert.Contains(builder.Sources, source => source is FileBackedSecretsSource);
    }

    [Fact]
    public void The_Layer_Is_Applied_After_Environment_Variables() // [FR-3.10]
    {
        var builder = new ConfigurationBuilder().AddInstallLayers();

        var environmentIndex = builder.Sources
            .ToList()
            .FindIndex(s => s is EnvironmentVariablesConfigurationSource);
        var fileIndex = builder.Sources.ToList().FindIndex(s => s is FileBackedSecretsSource);

        Assert.True(environmentIndex >= 0 && fileIndex > environmentIndex,
            "The file-backed layer must come after the environment layer, or a leftover literal would win.");
    }
}
