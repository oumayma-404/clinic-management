using ClinicManagement.Application.Features.CnamNomenclature.Commands;
using ClinicManagement.Application.Features.CnamNomenclature.Queries;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.CnamNomenclature;

/// <summary>
/// Valeur de la lettre clé (VLC) — global, admin-managed set keyed by lettre clé (FR-5.2/5.3).
/// </summary>
public class CnamVlcTests
{
    private readonly Mock<ICnamCatalogRepository> _repo = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // Catalogs are per-clinic, so the update command now resolves the caller's clinic from the DB and
    // refuses a row belonging to another one (security-hardening P4.4, audit § 2 finding 10).
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    public CnamVlcTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ClinicManagement.Application.Common.Models.Result<Guid>.Success(ClinicId));
    }

    // VLC-1
    [Fact]
    public async Task Read_Returns_Seeded_Values_For_Any_Authenticated_User() // [FR-5.2, FR-5.3]
    {
        var values = new[]
        {
            new CnamLetterValue(Guid.NewGuid(), Guid.NewGuid(), "CD", 7m),
            new CnamLetterValue(Guid.NewGuid(), Guid.NewGuid(), "D", 1.2m),
            new CnamLetterValue(Guid.NewGuid(), Guid.NewGuid(), "RD", 2m),
        };
        _repo.Setup(r => r.GetAllLetterValuesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(values);

        var handler = new GetCnamLetterValuesQueryHandler(_repo.Object, NullLogger<GetCnamLetterValuesQueryHandler>.Instance);
        var result = await handler.Handle(new GetCnamLetterValuesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var list = result.Value!.ToList();
        Assert.Equal(3, list.Count);
        Assert.All(list, v => Assert.True(v.IsProvisional)); // seeded provisional
    }

    // VLC-2
    [Fact]
    public async Task Update_By_Admin_Persists_New_Value() // [FR-5.2]
    {
        var vlc = new CnamLetterValue(Guid.NewGuid(), ClinicId, "D", 1.2m);
        _repo.Setup(r => r.GetLetterValueByIdAsync(vlc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(vlc);

        var handler = new UpdateCnamLetterValueCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<UpdateCnamLetterValueCommandHandler>.Instance);
        var result = await handler.Handle(new UpdateCnamLetterValueCommand { Id = vlc.Id, Value = 1.5m }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1.5m, vlc.Value);
        Assert.Equal(1.5m, result.Value!.Value);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // VLC-2b — unknown id → not found, no save.
    [Fact]
    public async Task Update_Unknown_Vlc_Returns_NotFound() // [FR-5.2]
    {
        _repo.Setup(r => r.GetLetterValueByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((CnamLetterValue?)null);

        var handler = new UpdateCnamLetterValueCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<UpdateCnamLetterValueCommandHandler>.Instance);
        var result = await handler.Handle(new UpdateCnamLetterValueCommand { Id = Guid.NewGuid(), Value = 2m }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // VLC-3
    [Fact]
    public void Seeded_Vlc_Values_Are_Provisional() // [FR-5.2]
    {
        // The seed inserts every VLC with the provisional flag; the domain default encodes that contract.
        var vlc = new CnamLetterValue(Guid.NewGuid(), Guid.NewGuid(), "CD", 7m);
        Assert.True(vlc.IsProvisional);
    }
}
