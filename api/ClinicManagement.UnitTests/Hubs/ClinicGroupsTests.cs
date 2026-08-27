using ClinicManagement.API.Hubs;
using Xunit;

namespace ClinicManagement.UnitTests.Hubs;

/// <summary>
/// The clinic group name is the single source of truth shared by the hub (adds connections) and the
/// notifier (broadcasts). If the two ever derived it differently a broadcast would miss its clients,
/// so these lock the format and per-clinic distinctness (AC-2 multi-tenant isolation).
/// </summary>
public class ClinicGroupsTests
{
    private static readonly Guid ClinicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    // [AC-2] Group name is derived from the clinic id in one place.
    [Fact]
    public void Name_Returns_Clinic_Scoped_Group_Name()
    {
        Assert.Equal($"clinic-{ClinicId}", ClinicGroups.Name(ClinicId));
    }

    // [AC-2] Two clinics never share a group, so a broadcast can't cross tenants.
    [Fact]
    public void Name_Is_Distinct_Per_Clinic()
    {
        Assert.NotEqual(ClinicGroups.Name(Guid.NewGuid()), ClinicGroups.Name(Guid.NewGuid()));
    }
}
