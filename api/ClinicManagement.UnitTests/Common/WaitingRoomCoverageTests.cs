using System.Text.RegularExpressions;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// `salle-attente` — two derived guards for the two places a correct change could be wired to one call site out of
/// several (`fixes-dont-propagate`).
/// </summary>
public class WaitingRoomCoverageTests
{
    private static string Read(params string[] relative) =>
        File.ReadAllText(Path.Combine(new[] { SolutionSources.Root().FullName }.Concat(relative).ToArray()));

    /// <summary>
    /// Every site that builds an <c>AppointmentDto</c> fills its board state. Four sites build one by hand (create,
    /// update, the single read, the list read) and none is checked by the compiler: a fifth that forgets
    /// <c>WaitingRoom</c> would serve a visit with no lane, and the agenda's « Arrivé » would read every patient as
    /// not arrived. Derived from the source, so a new site is covered the day it is written.
    /// </summary>
    [Fact]
    public void Every_AppointmentDto_Built_In_The_Application_Carries_Its_Waiting_Room_State()
    {
        var application = Path.Combine(SolutionSources.Root().FullName, "ClinicManagement.Application");
        var sites = Directory.EnumerateFiles(application, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(file => Initializers(File.ReadAllText(file)).Select(body => (file, body)))
            .ToList();

        // Non-vacuity: the four known sites must be found, or the scan is reading nothing.
        Assert.True(sites.Count >= 4, $"Expected at least 4 « new AppointmentDto » sites, found {sites.Count}.");

        var missing = sites
            .Where(s => !s.body.Contains("WaitingRoom =", StringComparison.Ordinal))
            .Select(s => Path.GetFileName(s.file))
            .ToList();
        Assert.True(missing.Count == 0, "AppointmentDto built without WaitingRoom in: " + string.Join(", ", missing));
    }

    /// <summary>The red proof: the scan sees an initializer that omits the field.</summary>
    [Fact]
    public void The_Guard_Sees_An_Initializer_That_Forgets_The_Field()
    {
        const string offender = "var dto = new AppointmentDto\n{\n    Id = a.Id,\n    Status = \"x\",\n};";
        var body = Assert.Single(Initializers(offender));
        Assert.DoesNotContain("WaitingRoom =", body);
    }

    /// <summary>
    /// « L'encaissement libère le fauteuil » is an interceptor, and its place in the chain is the whole guarantee:
    /// registered after the audit interceptor, the appointment it closes would be missing from the audit rows of the
    /// save that closed it.
    /// </summary>
    [Fact]
    public void The_Payment_Interceptor_Runs_Before_The_Audit_And_Automatic_Write_Interceptors()
    {
        var source = Read("ClinicManagement.Infrastructure", "Extensions.cs");
        var chain = source[source.IndexOf(".AddInterceptors(", StringComparison.Ordinal)..];
        chain = chain[..chain.IndexOf(");", StringComparison.Ordinal)];

        var payment = chain.IndexOf("WaitingRoomPaymentInterceptor", StringComparison.Ordinal);
        var audit = chain.IndexOf("AuditSaveChangesInterceptor", StringComparison.Ordinal);
        var automatic = chain.IndexOf("AutomaticWriteInterceptor", StringComparison.Ordinal);

        Assert.True(payment >= 0, "WaitingRoomPaymentInterceptor is not in the DbContext's interceptor chain.");
        Assert.True(payment < audit && payment < automatic, "WaitingRoomPaymentInterceptor must be registered first.");
    }

    private static IEnumerable<string> Initializers(string source)
    {
        foreach (Match match in Regex.Matches(source, @"new\s+AppointmentDto\s*(\(\s*\))?\s*\{"))
        {
            var depth = 0;
            for (var i = match.Index + match.Length - 1; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0)
                {
                    yield return source[(match.Index + match.Length)..i];
                    break;
                }
            }
        }
    }
}
