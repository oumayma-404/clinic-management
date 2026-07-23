using ClinicManagement.Application.Features.CnamNomenclature.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.CnamNomenclature;

/// <summary>
/// CNAM nomenclature query handler. The read moved from the in-code provider to the DB-backed
/// <see cref="ICnamCatalogRepository"/> (FR-5.1); these tests re-assert the filter logic (q / category /
/// lettre-clé, case-insensitive, blank = no filter) against a mocked repository. The handler is still NOT
/// clinic-scoped — its only dependencies are the (global) repository and a logger.
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
        _repository.Setup(r => r.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Sample());
        return new GetCnamNomenclatureQueryHandler(_repository.Object, NullLogger<GetCnamNomenclatureQueryHandler>.Instance);
    }

    private static List<Application.DTOs.CnamNomenclatureEntryDto> ToList(
        ClinicManagement.Application.Common.Models.Result<IEnumerable<Application.DTOs.CnamNomenclatureEntryDto>> result)
    {
        Assert.True(result.IsSuccess);
        return result.Value!.ToList();
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

    [Fact]
    public async Task Handle_Filters_By_Category_Case_Insensitively() // [FR-5.1]
    {
        var result = await Handler().Handle(
            new GetCnamNomenclatureQuery { Category = "radiologie" }, CancellationToken.None);
        var entries = ToList(result);
        Assert.Single(entries);
        Assert.Equal("PANO", entries[0].CodeActe);
    }

    [Fact]
    public async Task Handle_Unknown_Category_Returns_Empty_Not_Error() // [FR-5.1]
    {
        var result = await Handler().Handle(
            new GetCnamNomenclatureQuery { Category = "Prothèse" }, CancellationToken.None);
        Assert.Empty(ToList(result));
    }

    [Theory]
    [InlineData("detart", "DETART")]     // matches code acte
    [InlineData("panoramique", "PANO")]  // matches French designation
    public async Task Handle_Filters_By_Free_Text_On_Code_Or_Designation(string q, string expectedCode) // [FR-5.1]
    {
        var result = await Handler().Handle(new GetCnamNomenclatureQuery { Q = q }, CancellationToken.None);
        var entries = ToList(result);
        Assert.Single(entries);
        Assert.Equal(expectedCode, entries[0].CodeActe);
    }

    [Fact]
    public async Task Handle_Free_Text_Matches_Lettre_Cle() // [FR-5.1]
    {
        var result = await Handler().Handle(new GetCnamNomenclatureQuery { Q = "rd" }, CancellationToken.None);
        var entries = ToList(result);
        Assert.Single(entries);
        Assert.Equal("PANO", entries[0].CodeActe);
    }

    [Fact]
    public async Task Handle_Combines_Query_And_Category() // [FR-5.1]
    {
        var result = await Handler().Handle(
            new GetCnamNomenclatureQuery { Q = "extraction", Category = "Chirurgie/Extraction" },
            CancellationToken.None);
        var entries = ToList(result);
        Assert.Single(entries);
        Assert.Equal("EXT-SIMPLE", entries[0].CodeActe);
    }

    [Fact]
    public async Task Handle_Trims_Query_Before_Matching() // [FR-5.1]
    {
        var result = await Handler().Handle(new GetCnamNomenclatureQuery { Q = "  detart  " }, CancellationToken.None);
        Assert.Single(ToList(result));
    }

    [Fact]
    public async Task Handle_Passes_IncludeInactive_To_Repository() // [FR-5.1] admin screen sees inactive rows
    {
        var handler = Handler();
        await handler.Handle(new GetCnamNomenclatureQuery { IncludeInactive = true }, CancellationToken.None);
        _repository.Verify(r => r.GetAllAsync(true, It.IsAny<CancellationToken>()), Times.Once);
    }
}
