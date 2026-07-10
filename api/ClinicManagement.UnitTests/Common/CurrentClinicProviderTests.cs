using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// Hardening pass — the EF global-query-filter backstop (§1 / AC-3). The provider must surface the
/// caller's clinic id when one is in scope and <c>null</c> when none is (so the filter goes inactive
/// for background jobs / CLI / anonymous contexts rather than filtering everything to empty).
/// </summary>
public class CurrentClinicProviderTests
{
    [Fact]
    public void ClinicId_Returns_Context_Value_When_In_Scope() // [AC-3]
    {
        var clinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetClinicId()).Returns(clinicId);

        var provider = new CurrentClinicProvider(context.Object);

        Assert.Equal(clinicId, provider.ClinicId);
    }

    [Fact]
    public void ClinicId_Is_Null_When_No_Clinic_In_Scope() // [AC-3] filter stays inactive
    {
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetClinicId()).Returns((Guid?)null);

        var provider = new CurrentClinicProvider(context.Object);

        Assert.Null(provider.ClinicId);
    }
}
