using ClinicManagement.Domain.Entities;
using Xunit;

namespace ClinicManagement.UnitTests.Domain.Entities;

/// <summary>
/// Doctor cachet/ordre domain invariants (Part B — FR-2.5 / FR-3.1). The cachet key and its content type
/// travel together (both required); the ordre number is trimmed and blank clears it.
/// </summary>
public class DoctorEntityTests
{
    private static Doctor NewDoctor() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Amine", "Khelifi", "Chirurgien-dentiste");

    // [DOC-1] SetCachet stores key + content type and bumps UpdatedAt.
    [Fact]
    public void SetCachet_Sets_Key_And_ContentType_And_Bumps_UpdatedAt()
    {
        var doctor = NewDoctor();

        doctor.SetCachet("clinic/doctors/x/cachet", "image/jpeg");

        Assert.Equal("clinic/doctors/x/cachet", doctor.CachetStorageKey);
        Assert.Equal("image/jpeg", doctor.CachetContentType);
        Assert.NotNull(doctor.UpdatedAt);
    }

    // [DOC-1] Both the key and the content type are required (guards the logo-bug class of mistake).
    [Theory]
    [InlineData("", "image/png")]
    [InlineData("   ", "image/png")]
    [InlineData("key", "")]
    [InlineData("key", "   ")]
    public void SetCachet_Requires_Key_And_ContentType(string key, string contentType)
    {
        Assert.Throws<ArgumentException>(() => NewDoctor().SetCachet(key, contentType));
    }

    // [DOC-2] The CNOMDT order number is trimmed on set.
    [Fact]
    public void SetOrdreNumber_Trims_And_Persists()
    {
        var doctor = NewDoctor();

        doctor.SetOrdreNumber("  D-04-1287  ");

        Assert.Equal("D-04-1287", doctor.OrdreNumberCnomdt);
    }

    // [DOC-2] Blank/null clears the order number.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetOrdreNumber_Blank_Clears(string? value)
    {
        var doctor = NewDoctor();
        doctor.SetOrdreNumber("D-1");

        doctor.SetOrdreNumber(value);

        Assert.Null(doctor.OrdreNumberCnomdt);
    }

    // [DOC-3] RemoveCachet clears both cachet fields.
    [Fact]
    public void RemoveCachet_Clears_Both_Fields()
    {
        var doctor = NewDoctor();
        doctor.SetCachet("key", "image/png");

        doctor.RemoveCachet();

        Assert.Null(doctor.CachetStorageKey);
        Assert.Null(doctor.CachetContentType);
    }
}
