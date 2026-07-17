using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.CnamNomenclature.Queries;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.CnamNomenclature;

/// <summary>
/// CNAM nomenclature query handler (cnam-nomenclature-lookup, AC-1). The handler filters the provider's
/// static catalogue by free-text <c>q</c> and/or <c>category</c>. It is NOT clinic-scoped — its only
/// dependencies are the reference-data provider and a logger (no <c>IClinicContext</c>/resolver), which
/// the constructor signature exercised here proves. A deterministic mocked provider keeps the
/// filter-logic assertions independent of the real curated data (that data is covered separately by
/// <see cref="ClinicManagement.UnitTests.Infrastructure.Services.CnamNomenclatureProviderTests"/>).
/// </summary>
public class GetCnamNomenclatureQueryHandlerTests
{
    private readonly Mock<ICnamNomenclatureProvider> _provider = new();

    private static readonly List<CnamNomenclatureEntryDto> Sample = new()
    {
        new CnamNomenclatureEntryDto
        {
            CodeActe = "CONS", DesignationFr = "Consultation dentaire",
            LettreCle = "CD", Coefficient = 1, Category = "Consultation",
        },
        new CnamNomenclatureEntryDto
        {
            CodeActe = "DETART", DesignationFr = "Détartrage",
            LettreCle = "D", Coefficient = 10, Category = "Soins conservateurs",
        },
        new CnamNomenclatureEntryDto
        {
            CodeActe = "EXT-SIMPLE", DesignationFr = "Extraction d'une dent permanente",
            LettreCle = "D", Coefficient = 10, Category = "Chirurgie/Extraction",
        },
        new CnamNomenclatureEntryDto
        {
            CodeActe = "PANO", DesignationFr = "Radiographie panoramique",
            LettreCle = "RD", Coefficient = 5, Category = "Radiologie",
        },
    };

    private GetCnamNomenclatureQueryHandler Handler()
    {
        _provider.Setup(p => p.GetAll()).Returns(Sample);
        return new GetCnamNomenclatureQueryHandler(
            _provider.Object, new Mock<ILogger<GetCnamNomenclatureQueryHandler>>().Object);
    }

    private static List<CnamNomenclatureEntryDto> ToList(
        ClinicManagement.Application.Common.Models.Result<IEnumerable<CnamNomenclatureEntryDto>> result)
    {
        Assert.True(result.IsSuccess);
        return result.Value!.ToList();
    }

    [Fact]
    public async Task Handle_Returns_Full_Catalogue_When_No_Filter() // [AC-1]
    {
        var result = await Handler().Handle(new GetCnamNomenclatureQuery(), CancellationToken.None);

        Assert.Equal(Sample.Count, ToList(result).Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Handle_Treats_Blank_Query_And_Category_As_No_Filter(string blank) // [AC-1]
    {
        var result = await Handler().Handle(
            new GetCnamNomenclatureQuery { Q = blank, Category = blank }, CancellationToken.None);

        Assert.Equal(Sample.Count, ToList(result).Count);
    }

    [Fact]
    public async Task Handle_Filters_By_Category_Case_Insensitively() // [AC-1]
    {
        var result = await Handler().Handle(
            new GetCnamNomenclatureQuery { Category = "radiologie" }, CancellationToken.None);

        var entries = ToList(result);
        Assert.Single(entries);
        Assert.Equal("PANO", entries[0].CodeActe);
    }

    [Fact]
    public async Task Handle_Unknown_Category_Returns_Empty_Not_Error() // [AC-1]
    {
        var result = await Handler().Handle(
            new GetCnamNomenclatureQuery { Category = "Prothèse" }, CancellationToken.None);

        Assert.Empty(ToList(result));
    }

    [Theory]
    [InlineData("detart", "DETART")]     // matches code acte
    [InlineData("panoramique", "PANO")]  // matches French designation
    public async Task Handle_Filters_By_Free_Text_On_Code_Or_Designation(string q, string expectedCode) // [AC-1]
    {
        var result = await Handler().Handle(
            new GetCnamNomenclatureQuery { Q = q }, CancellationToken.None);

        var entries = ToList(result);
        Assert.Single(entries);
        Assert.Equal(expectedCode, entries[0].CodeActe);
    }

    [Fact]
    public async Task Handle_Free_Text_Matches_Lettre_Cle() // [AC-1]
    {
        var result = await Handler().Handle(
            new GetCnamNomenclatureQuery { Q = "rd" }, CancellationToken.None);

        var entries = ToList(result);
        Assert.Single(entries);
        Assert.Equal("PANO", entries[0].CodeActe);
    }

    [Fact]
    public async Task Handle_Combines_Query_And_Category() // [AC-1]
    {
        // "d" matches several designations/codes/clés; the category narrows it to the single extraction act.
        var result = await Handler().Handle(
            new GetCnamNomenclatureQuery { Q = "extraction", Category = "Chirurgie/Extraction" },
            CancellationToken.None);

        var entries = ToList(result);
        Assert.Single(entries);
        Assert.Equal("EXT-SIMPLE", entries[0].CodeActe);
    }

    [Fact]
    public async Task Handle_Trims_Query_Before_Matching() // [AC-1]
    {
        var result = await Handler().Handle(
            new GetCnamNomenclatureQuery { Q = "  detart  " }, CancellationToken.None);

        Assert.Single(ToList(result));
    }
}
