using ClinicManagement.UnitTests.Common;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// [cabinet-de-groupe US-1, R-3] Every production file that builds an <c>Appointment</c> asks
/// <c>AppointmentPractitioner</c> which dentist it is for.
///
/// <para>The way the rule breaks is not an edit to one of today's two booking commands — it is a <i>third</i> door
/// added later (an online-booking request, a waiting-list promotion that books server-side) that saves a patient RDV
/// with no dentist in a cabinet of three. So the doors are <b>derived by reading the sources</b>, on
/// <c>ClinicCreationEntitlementTests</c>' shape, and scoped to Application + API because test fixtures build
/// appointments freely.</para>
/// </summary>
public class AppointmentPractitionerCoverageTests
{
    private static readonly string[] ProductionProjects = { "ClinicManagement.Application", "ClinicManagement.API" };

    private static string Needle() => "new Appointment" + "(";

    private static IReadOnlyList<string> Unguarded(IEnumerable<(string Path, string Source)> files) =>
        files
            .Where(f => SolutionSources.WithoutComments(f.Source).Contains(Needle(), StringComparison.Ordinal))
            .Where(f => !f.Source.Contains("AppointmentPractitioner.ResolveAsync", StringComparison.Ordinal))
            .Select(f => f.Path)
            .ToList();

    private static IReadOnlyList<(string Path, string Source)> ProductionSources()
    {
        var root = SolutionSources.Root();
        return SolutionSources.CsFiles(root)
            .Where(path => ProductionProjects.Any(project =>
                Path.GetRelativePath(root.FullName, path)
                    .StartsWith(project + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            .Select(path => (Path.GetRelativePath(root.FullName, path), File.ReadAllText(path)))
            .ToList();
    }

    [Fact]
    public void Every_Appointment_Construction_Asks_Which_Dentist_It_Is_For()
    {
        var sources = ProductionSources();
        var doors = sources.Where(f => SolutionSources.WithoutComments(f.Source).Contains(Needle(), StringComparison.Ordinal)).ToList();

        // Non-vacuity: a scan that found nothing would pass for ever while checking nothing.
        Assert.True(doors.Count >= 2, $"Expected at least the two booking commands, found {doors.Count}.");
        Assert.Empty(Unguarded(sources));
    }

    [Fact]
    public void The_Guard_Rejects_A_Door_That_Does_Not_Ask()
    {
        var violation = ("Fake/OnlineBookingCommand.cs",
            "class OnlineBooking { void Book() { var a = " + Needle() + "id, clinic, patient, null, at, d); } }");

        Assert.Equal(new[] { "Fake/OnlineBookingCommand.cs" }, Unguarded(new[] { violation }));
    }
}
