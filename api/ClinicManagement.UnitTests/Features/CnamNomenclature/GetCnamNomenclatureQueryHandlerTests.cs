using ClinicManagement.UnitTests.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Features.CnamNomenclature.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.CnamNomenclature;

/// <summary>
/// CNAM nomenclature query handler. Since `list-pagination` it is a **pass-through**: the free-text term, the
/// category, the page and <c>IncludeInactive</c> go to <see cref="ICnamCatalogRepository"/> and the returned page
/// is mapped — so that is what these cases hold.
///
/// <para>⚠️ **The matching is SQL and outside this project's reach** (no database here): case-insensitivity,
/// accent folding, and matching a term against code acte / désignation / lettre clé all live in the repository.
/// Seven cases used to assert them by handing the mock the whole catalogue and checking the handler narrowed it,
/// which stopped meaning anything once the filter moved — a mocked repository applies no predicate, so they were
/// testing a capability the handler had correctly lost, and the one thing they *could* still catch (an argument
/// silently dropped on the way to the repository) they did not check at all.</para>
/// </summary>
public class GetCnamNomenclatureQueryHandlerTests
{
    private readonly Mock<ICnamCatalogRepository> _repository = new();

    private static List<CnamNomenclatureEntry> Sample() => new()
    {
        new CnamNomenclatureEntry(Guid.NewGuid(), Guid.NewGuid(), "CONS", "Consultation dentaire", "CD", 1, "Consultation"),
        new CnamNomenclatureEntry(Guid.NewGuid(), Guid.NewGuid(), "DETART", "Détartrage", "D", 10, "Soins conservateurs"),
        new CnamNomenclatureEntry(Guid.NewGuid(), Guid.NewGuid(), "EXT-SIMPLE", "Extraction d'une dent permanente", "D", 10, "Chirurgie/Extraction"),
        new CnamNomenclatureEntry(Guid.NewGuid(), Guid.NewGuid(), "PANO", "Radiographie panoramique", "RD", 5, "Radiologie"),
    };

    private GetCnamNomenclatureQueryHandler Handler()
    {
        _repository.Setup(r => r.GetAllAsync(It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Sample()).AsPage());
        return new GetCnamNomenclatureQueryHandler(_repository.Object, NullLogger<GetCnamNomenclatureQueryHandler>.Instance);
    }

    private static List<Application.DTOs.CnamNomenclatureEntryDto> ToList(
        ClinicManagement.Application.Common.Models.Result<PagedResult<Application.DTOs.CnamNomenclatureEntryDto>> result)
    {
        Assert.True(result.IsSuccess);
        return result.Value!.Items.ToList();
    }

    [Fact]
    public async Task Handle_Returns_Full_Catalogue_When_No_Filter() // [FR-5.1]
    {
        var result = await Handler().Handle(new GetCnamNomenclatureQuery(), CancellationToken.None);
        Assert.Equal(4, ToList(result).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_Treats_Blank_Query_And_Category_As_No_Filter(string blank) // [FR-5.1]
    {
        var result = await Handler().Handle(
            new GetCnamNomenclatureQuery { Q = blank, Category = blank }, CancellationToken.None);
        Assert.Equal(4, ToList(result).Count);
    }

    // [FR-5.1] The two filters reach the repository verbatim and **independently** — the category is not folded
    // into the free-text term, and neither is trimmed here (normalisation is `SearchTerm`'s job, inside the
    // repository, so a handler that trimmed would be a second authority on what the user typed).
    [Theory]
    [InlineData("detart", null)]                            // code acte
    [InlineData("panoramique", null)]                       // French designation
    [InlineData("rd", null)]                                // lettre clé
    [InlineData("  detart  ", null)]                        // untrimmed, forwarded as typed
    [InlineData(null, "radiologie")]                        // category alone
    [InlineData(null, "Prothèse")]                          // a category with no rows is still just an argument
    [InlineData("extraction", "Chirurgie/Extraction")]      // both at once
    public async Task Handle_Forwards_Query_And_Category_To_The_Repository(string? q, string? category)
    {
        var handler = Handler();

        await handler.Handle(new GetCnamNomenclatureQuery { Q = q, Category = category }, CancellationToken.None);

        _repository.Verify(r => r.GetAllAsync(It.IsAny<bool>(), category, q, It.IsAny<PageRequest?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Forwards_The_Requested_Page() // [FR-5.1]
    {
        var handler = Handler();

        await handler.Handle(new GetCnamNomenclatureQuery { Page = 2, PageSize = 50 }, CancellationToken.None);

        _repository.Verify(r => r.GetAllAsync(It.IsAny<bool>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.Is<PageRequest?>(p => p != null && p.Value.Page == 2 && p.Value.PageSize == 50),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_Maps_The_Returned_Page() // [FR-5.1]
    {
        var result = await Handler().Handle(new GetCnamNomenclatureQuery(), CancellationToken.None);

        var pano = Assert.Single(ToList(result), e => e.CodeActe == "PANO");
        Assert.Equal("Radiographie panoramique", pano.DesignationFr);
        Assert.Equal("RD", pano.LettreCle);
    }

    [Fact]
    public async Task Handle_Passes_IncludeInactive_To_Repository() // [FR-5.1] admin screen sees inactive rows
    {
        var handler = Handler();
        await handler.Handle(new GetCnamNomenclatureQuery { IncludeInactive = true }, CancellationToken.None);
        _repository.Verify(r => r.GetAllAsync(true, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }
}
