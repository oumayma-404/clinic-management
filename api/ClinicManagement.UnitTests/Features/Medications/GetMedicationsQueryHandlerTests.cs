using ClinicManagement.UnitTests.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Features.Medications.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Medications;

/// <summary>
/// Medication catalog read query. Since `list-pagination` the handler is a **pass-through**: the free-text term,
/// the page and <c>IncludeInactive</c> go to <c>IMedicationCatalogRepository</c> and the returned page is mapped.
/// So that is what these cases hold — that each argument arrives verbatim, and that the mapping is right.
///
/// <para>⚠️ **The matching itself is SQL and is therefore outside this project's reach entirely** (no database
/// here). Case-insensitivity, accent folding and the molecule search — an <c>EXISTS</c> over the ingredient child
/// rows — live in the repository. Six cases used to assert them by handing the mock the whole catalogue and
/// checking the handler narrowed it, which stopped meaning anything the moment the filter moved: a mocked
/// repository applies no predicate, so they were testing a capability the handler had correctly lost.</para>
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
        _repository.Setup(r => r.GetAllAsync(It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>())).ReturnsAsync((Sample()).AsPage());
        return new GetMedicationsQueryHandler(_repository.Object, NullLogger<GetMedicationsQueryHandler>.Instance);
    }

    private static List<Application.DTOs.MedicationDto> ToList(
        ClinicManagement.Application.Common.Models.Result<PagedResult<Application.DTOs.MedicationDto>> result)
    {
        Assert.True(result.IsSuccess);
        return result.Value!.Items.ToList();
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

    // [AC-2] The search term reaches the repository **verbatim** — brand, molecule, form and strength are one
    // argument, matched in SQL. Six cases here used to hand the mock the whole catalogue and assert the handler
    // narrowed it; that filtering is `MedicationCatalogRepository`'s now, so those cases were asserting a
    // capability the handler had correctly stopped having.
    [Theory]
    [InlineData("augmentin")]
    [InlineData("salbutamol")]
    [InlineData("gélule")]
    [InlineData("5 mg")]
    [InlineData("  augmentin  ")]
    [InlineData("zzznotadrug")]
    public async Task Handle_Forwards_The_Search_Term_To_The_Repository(string term)
    {
        var handler = Handler();

        await handler.Handle(new GetMedicationsQuery { Q = term }, CancellationToken.None);

        // Verbatim, including the untrimmed form: normalisation belongs to SearchTerm inside the repository, so a
        // handler that "helpfully" trimmed here would be a second place deciding what the user typed.
        _repository.Verify(r => r.GetAllAsync(It.IsAny<bool>(), term, It.IsAny<PageRequest?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Forwards_The_Requested_Page() // [AC-2]
    {
        var handler = Handler();

        await handler.Handle(new GetMedicationsQuery { Page = 3, PageSize = 25 }, CancellationToken.None);

        _repository.Verify(r => r.GetAllAsync(It.IsAny<bool>(), It.IsAny<string?>(),
            It.Is<PageRequest?>(p => p != null && p.Value.Page == 3 && p.Value.PageSize == 25),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Maps_All_Dcis_Onto_The_Dto() // [AC-2] combination drug carries both molecules
    {
        var result = await Handler().Handle(new GetMedicationsQuery(), CancellationToken.None);

        var augmentin = Assert.Single(ToList(result), m => m.BrandName == "Augmentin");
        Assert.Equal(2, augmentin.Dcis.Count);
        Assert.Contains("Amoxicilline", augmentin.Dcis);
        Assert.Contains("Acide clavulanique", augmentin.Dcis);
    }

    [Fact]
    public async Task Handle_Passes_IncludeInactive_To_Repository() // [AC-2] admin screen sees inactive rows
    {
        var handler = Handler();
        await handler.Handle(new GetMedicationsQuery { IncludeInactive = true }, CancellationToken.None);
        _repository.Verify(r => r.GetAllAsync(true, It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }
}
