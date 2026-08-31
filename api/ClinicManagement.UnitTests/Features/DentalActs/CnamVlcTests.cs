using ClinicManagement.Application.Features.DentalActs.Commands;
using ClinicManagement.Application.Features.DentalActs.Queries;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.DentalActs;

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

    // ===================== K10 — the DTO carries what the convention says =====================
    //
    // The startup pass corrects only rows untouched since seeding (DEV-4), so a value an admin edited is
    // deliberately left alone — which means the divergence has to be VISIBLE or it is simply lost. These three
    // fields are what let `/cnam-nomenclature` offer the correction instead of applying it behind their back.

    // ⚠️ Exercised through the QUERY HANDLER, not `CnamEntryMapper` directly — the mapper is `internal` and this
    // project has no `InternalsVisibleTo` (the same reason `CnamBs1BulletinRendererTests` uses reflection). Going
    // through the public seam is the better test anyway: it is the shape `/cnam-nomenclature` actually receives.
    private async Task<IReadOnlyList<ClinicManagement.Application.DTOs.CnamLetterValueDto>> ReadLetterValues(
        params CnamLetterValue[] stored)
    {
        _repo.Setup(r => r.GetAllLetterValuesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(stored);

        var handler = new GetCnamLetterValuesQueryHandler(
            _repo.Object, NullLogger<GetCnamLetterValuesQueryHandler>.Instance);
        var result = await handler.Handle(new GetCnamLetterValuesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value!.ToList();
    }

    [Theory] // [K10] For a lettre clé the convention settles, the DTO states the figure, the source and the cadence.
    [InlineData("CD", 30.000)]
    [InlineData("CDS", 45.000)]
    [InlineData("D", 3.000)]
    public async Task Dto_Carries_The_Convention_Value_For_A_Settled_Lettre_Cle(string lettreCle, double expected)
    {
        // A stored value deliberately DIFFERENT from the convention's — i.e. the case the prompt exists for.
        var dto = (await ReadLetterValues(new CnamLetterValue(Guid.NewGuid(), ClinicId, lettreCle, 1m))).Single();

        Assert.Equal((decimal)expected, dto.ConventionValue);
        Assert.False(string.IsNullOrWhiteSpace(dto.ConventionSource));
        Assert.Equal(ClinicManagement.Domain.Services.CnamConventionTariffs.RevisionIntervalYears,
            dto.ConventionRevisionIntervalYears);
        // The stored value is untouched — the DTO reports what the convention says, it does not overwrite.
        Assert.Equal(1m, dto.Value);
    }

    [Theory] // [K10] A lettre clé the convention does not settle carries all three fields NULL, together.
    [InlineData("RD")]
    [InlineData("ZZ")]
    public async Task Dto_Convention_Fields_Are_Null_Together_For_An_Unsettled_Lettre_Cle(string lettreCle)
    {
        var dto = (await ReadLetterValues(new CnamLetterValue(Guid.NewGuid(), ClinicId, lettreCle, 10m))).Single();

        // Null together and not merely null-ish: a source with no value to attribute would read on screen as
        // provenance for the clinic's OWN figure, which is the opposite of what it says.
        Assert.Null(dto.ConventionValue);
        Assert.Null(dto.ConventionSource);
        Assert.Null(dto.ConventionRevisionIntervalYears);
    }

    [Fact] // [K10] The update command's response carries them too — it is what the screen re-renders on « Appliquer ».
    public async Task Update_Response_Carries_The_Convention_Fields()
    {
        var vlc = new CnamLetterValue(Guid.NewGuid(), ClinicId, "CD", 7m);
        _repo.Setup(r => r.GetLetterValueByIdAsync(vlc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(vlc);

        var handler = new UpdateCnamLetterValueCommandHandler(
            _repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<UpdateCnamLetterValueCommandHandler>.Instance);
        var result = await handler.Handle(
            new UpdateCnamLetterValueCommand { Id = vlc.Id, Value = 30.000m }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Projected in the mapper rather than at each call site, so the read and this response cannot disagree
        // about what the convention says — and after applying it, the prompt must stop showing.
        Assert.Equal(30.000m, result.Value!.ConventionValue);
        Assert.Equal(30.000m, result.Value!.Value);
    }

    [Fact]
    public async Task Confirming_The_Catalogue_Clears_The_Provisional_Flag_On_The_Vlc_Rows()
    {
        // The dental confirm absorbed ConfirmCnamDataCommand's VLC half, so it is the only writer of
        // CnamLetterValue.Confirm() — without this the « à vérifier » badge could never be cleared.
        var value = new CnamLetterValue(Guid.NewGuid(), ClinicId, "D", 3m);
        Assert.True(value.IsProvisional);

        _repo.Setup(r => r.GetAllLetterValuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { value });

        var acts = new Mock<IDentalActCodeRepository>();
        acts.Setup(r => r.GetProvisionalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DentalActCode>());

        var result = await new ConfirmDentalActsCommandHandler(
                acts.Object, _repo.Object, _clinicResolver.Object, _uow.Object,
                NullLogger<ConfirmDentalActsCommandHandler>.Instance)
            .Handle(new ConfirmDentalActsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(value.IsProvisional);
        _repo.Verify(r => r.UpdateLetterValueAsync(value, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Confirming_Leaves_A_Vlc_Row_Somebody_Already_Vouched_For_Untouched()
    {
        var value = new CnamLetterValue(Guid.NewGuid(), ClinicId, "D", 3m);
        value.Confirm();

        _repo.Setup(r => r.GetAllLetterValuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { value });

        var acts = new Mock<IDentalActCodeRepository>();
        acts.Setup(r => r.GetProvisionalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DentalActCode>());

        var result = await new ConfirmDentalActsCommandHandler(
                acts.Object, _repo.Object, _clinicResolver.Object, _uow.Object,
                NullLogger<ConfirmDentalActsCommandHandler>.Instance)
            .Handle(new ConfirmDentalActsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _repo.Verify(r => r.UpdateLetterValueAsync(It.IsAny<CnamLetterValue>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
