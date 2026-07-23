using ClinicManagement.Application.Features.Medications.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Medications;

/// <summary>
/// Medication catalog read query. GLOBAL reference data (not clinic-scoped); the handler filters the
/// mocked repository's rows by free text over brand / form / strength / DCI (case-insensitive, blank = no
/// filter) and forwards IncludeInactive for the admin screen.
/// </summary>
public class GetMedicationsQueryHandlerTests
{
    private readonly Mock<IMedicationCatalogRepository> _repository = new();

    private static List<Medication> Sample() => new()
    {
        new Medication(Guid.NewGuid(), Guid.NewGuid(), "Doliprane", "Comprimé", "1000 mg", new[] { "Paracétamol" }),
        new Medication(Guid.NewGuid(), Guid.NewGuid(), "Augmentin", "Comprimé", "1 g", new[] { "Amoxicilline", "Acide clavulanique" }),
        new Medication(Guid.NewGuid(), Guid.NewGuid(), "Ventoline", "Solution", "100 µg", new[] { "Salbutamol" }),
        new Medication(Guid.NewGuid(), Guid.NewGuid(), "Amlor", "Gélule", "5 mg", new[] { "Amlodipine" }),
    };

    private GetMedicationsQueryHandler Handler()
    {
        _repository.Setup(r => r.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(Sample());
        return new GetMedicationsQueryHandler(_repository.Object, NullLogger<GetMedicationsQueryHandler>.Instance);
    }

    private static List<Application.DTOs.MedicationDto> ToList(
        ClinicManagement.Application.Common.Models.Result<IEnumerable<Application.DTOs.MedicationDto>> result)
    {
        Assert.True(result.IsSuccess);
        return result.Value!.ToList();
    }

    [Fact]
    public async Task Handle_Returns_Full_Catalogue_When_No_Filter() // [AC-2]
    {
        var result = await Handler().Handle(new GetMedicationsQuery(), CancellationToken.None);
        Assert.Equal(4, ToList(result).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_Treats_Blank_Query_As_No_Filter(string blank) // [AC-2]
    {
        var result = await Handler().Handle(new GetMedicationsQuery { Q = blank }, CancellationToken.None);
        Assert.Equal(4, ToList(result).Count);
    }

    [Fact]
    public async Task Handle_Filters_By_Brand_Case_Insensitively() // [AC-2]
    {
        var result = await Handler().Handle(new GetMedicationsQuery { Q = "augmentin" }, CancellationToken.None);
        var meds = ToList(result);
        Assert.Single(meds);
        Assert.Equal("Augmentin", meds[0].BrandName);
    }

    [Fact]
    public async Task Handle_Filters_By_Dci() // [AC-2] molecule search
    {
        var result = await Handler().Handle(new GetMedicationsQuery { Q = "salbutamol" }, CancellationToken.None);
        var meds = ToList(result);
        Assert.Single(meds);
        Assert.Equal("Ventoline", meds[0].BrandName);
    }

    [Fact]
    public async Task Handle_Filters_By_Form() // [AC-2]
    {
        var result = await Handler().Handle(new GetMedicationsQuery { Q = "gélule" }, CancellationToken.None);
        var meds = ToList(result);
        Assert.Single(meds);
        Assert.Equal("Amlor", meds[0].BrandName);
    }

    [Fact]
    public async Task Handle_Filters_By_Strength() // [AC-2]
    {
        var result = await Handler().Handle(new GetMedicationsQuery { Q = "5 mg" }, CancellationToken.None);
        var meds = ToList(result);
        Assert.Single(meds);
        Assert.Equal("Amlor", meds[0].BrandName);
    }

    [Fact]
    public async Task Handle_Unknown_Query_Returns_Empty_Not_Error() // [AC-2]
    {
        var result = await Handler().Handle(new GetMedicationsQuery { Q = "zzznotadrug" }, CancellationToken.None);
        Assert.Empty(ToList(result));
    }

    [Fact]
    public async Task Handle_Trims_Query_Before_Matching() // [AC-2]
    {
        var result = await Handler().Handle(new GetMedicationsQuery { Q = "  augmentin  " }, CancellationToken.None);
        Assert.Single(ToList(result));
    }

    [Fact]
    public async Task Handle_Maps_All_Dcis_Onto_The_Dto() // [AC-2] combination drug carries both molecules
    {
        var result = await Handler().Handle(new GetMedicationsQuery { Q = "augmentin" }, CancellationToken.None);
        var augmentin = Assert.Single(ToList(result));
        Assert.Equal(2, augmentin.Dcis.Count);
        Assert.Contains("Amoxicilline", augmentin.Dcis);
        Assert.Contains("Acide clavulanique", augmentin.Dcis);
    }

    [Fact]
    public async Task Handle_Passes_IncludeInactive_To_Repository() // [AC-2] admin screen sees inactive rows
    {
        var handler = Handler();
        await handler.Handle(new GetMedicationsQuery { IncludeInactive = true }, CancellationToken.None);
        _repository.Verify(r => r.GetAllAsync(true, It.IsAny<CancellationToken>()), Times.Once);
    }
}
