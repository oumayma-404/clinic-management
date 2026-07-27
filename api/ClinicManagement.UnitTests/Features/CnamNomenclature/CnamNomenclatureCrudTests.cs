using ClinicManagement.Application.Features.CnamNomenclature.Commands;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.CnamNomenclature;

/// <summary>
/// DB-backed, per-clinic, AdminOnly CNAM catalog CRUD (FR-5.1 + per-clinic #5). The create handler stamps
/// the caller's clinic and uniqueness is per-clinic. Mocks <see cref="ICnamCatalogRepository"/> +
/// <see cref="ICurrentClinicResolver"/> + <see cref="IUnitOfWork"/>.
/// </summary>
public class CnamNomenclatureCrudTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();
    private readonly Mock<ICnamCatalogRepository> _repo = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public CnamNomenclatureCrudTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
    }

    private static CnamNomenclatureEntry Entry(string code = "CONS") =>
        new(Guid.NewGuid(), ClinicId, code, "Consultation dentaire", "CD", 1, "Consultation");

    // CNAM-1
    [Fact]
    public async Task Create_Succeeds_And_Persists_All_Fields() // [FR-5.1]
    {
        _repo.Setup(r => r.CodeActeExistsAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        CnamNomenclatureEntry? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<CnamNomenclatureEntry>(), It.IsAny<CancellationToken>()))
            .Callback<CnamNomenclatureEntry, CancellationToken>((e, _) => captured = e)
            .ReturnsAsync((CnamNomenclatureEntry e, CancellationToken _) => e);

        var handler = new CreateCnamEntryCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<CreateCnamEntryCommandHandler>.Instance);
        var result = await handler.Handle(new CreateCnamEntryCommand
        {
            CodeActe = "DETART", DesignationFr = "Détartrage", LettreCle = "d", Coefficient = 10, Category = "Soins conservateurs",
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal("DETART", captured!.CodeActe);
        Assert.Equal("Détartrage", captured.DesignationFr);
        Assert.Equal("D", captured.LettreCle); // normalized upper-case
        Assert.Equal(10, captured.Coefficient);
        Assert.Equal("Soins conservateurs", captured.Category);
        Assert.True(captured.IsActive);
        _repo.Verify(r => r.AddAsync(It.IsAny<CnamNomenclatureEntry>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // CNAM-2
    [Fact]
    public async Task Create_Duplicate_CodeActe_Is_Rejected_With_French_Message() // [FR-5.1]
    {
        _repo.Setup(r => r.CodeActeExistsAsync("DETART", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var handler = new CreateCnamEntryCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<CreateCnamEntryCommandHandler>.Instance);
        var result = await handler.Handle(new CreateCnamEntryCommand
        {
            CodeActe = "DETART", DesignationFr = "Détartrage", LettreCle = "D", Coefficient = 10, Category = "Soins conservateurs",
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("existe déjà", result.Error);
        _repo.Verify(r => r.AddAsync(It.IsAny<CnamNomenclatureEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // CNAM-3
    [Fact]
    public void Created_Entry_Carries_Provisional_Flag_By_Default() // [FR-5.1]
    {
        var entry = Entry();
        Assert.True(entry.IsProvisional);
    }

    // CNAM-4
    [Fact]
    public async Task Update_Existing_Entry_Succeeds() // [FR-5.1]
    {
        var entry = Entry("DETART");
        _repo.Setup(r => r.GetByIdAsync(entry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(entry);
        _repo.Setup(r => r.CodeActeExistsAsync("DETART", entry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var handler = new UpdateCnamEntryCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<UpdateCnamEntryCommandHandler>.Instance);
        var result = await handler.Handle(new UpdateCnamEntryCommand
        {
            Id = entry.Id, CodeActe = "DETART", DesignationFr = "Détartrage complet", LettreCle = "D", Coefficient = 12, Category = "Soins conservateurs",
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Détartrage complet", entry.DesignationFr);
        Assert.Equal(12, entry.Coefficient);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // CNAM-5
    [Fact]
    public async Task Update_Unknown_Id_Returns_NotFound() // [FR-5.1]
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((CnamNomenclatureEntry?)null);

        var handler = new UpdateCnamEntryCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<UpdateCnamEntryCommandHandler>.Instance);
        var result = await handler.Handle(new UpdateCnamEntryCommand
        {
            Id = Guid.NewGuid(), CodeActe = "X", DesignationFr = "Y", LettreCle = "D", Coefficient = 1, Category = "Consultation",
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // CNAM-5b
    [Fact]
    public async Task Update_To_Duplicate_CodeActe_Is_Rejected() // [edge: duplicate CodeActe]
    {
        var entry = Entry("CONS");
        _repo.Setup(r => r.GetByIdAsync(entry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(entry);
        _repo.Setup(r => r.CodeActeExistsAsync("DETART", entry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var handler = new UpdateCnamEntryCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<UpdateCnamEntryCommandHandler>.Instance);
        var result = await handler.Handle(new UpdateCnamEntryCommand
        {
            Id = entry.Id, CodeActe = "DETART", DesignationFr = "Y", LettreCle = "D", Coefficient = 1, Category = "Consultation",
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // CNAM-6 (#5: catalog is now per-clinic)
    [Fact]
    public void Catalog_Is_Per_Clinic_Scoped() // [#5]
    {
        // Per-clinic (#5): the entity carries a ClinicId (like ProcedureType). The create command has NO
        // clinic parameter — the caller's clinic is resolved server-side and stamped by the handler.
        Assert.NotNull(typeof(CnamNomenclatureEntry).GetProperty("ClinicId"));
        Assert.NotNull(typeof(ProcedureType).GetProperty("ClinicId"));
        Assert.Null(typeof(CreateCnamEntryCommand).GetProperty("ClinicId"));
    }

    // CNAM-7
    [Fact]
    public async Task Deactivate_Sets_Inactive() // [FR-5.1]
    {
        var entry = Entry();
        Assert.True(entry.IsActive);
        _repo.Setup(r => r.GetByIdAsync(entry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(entry);

        var handler = new DeactivateCnamEntryCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<DeactivateCnamEntryCommandHandler>.Instance);
        var result = await handler.Handle(new DeactivateCnamEntryCommand { Id = entry.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(entry.IsActive); // excluded from active-only reads (repo GetAllAsync(includeInactive:false))
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // CNAM-8
    [Fact]
    public async Task Confirm_Clears_Provisional_Flag_On_Entries_And_Vlc() // [FR-5.1]
    {
        var entry = Entry();          // provisional by default
        var vlc = new CnamLetterValue(Guid.NewGuid(), ClinicId, "D", 1.2m); // provisional by default
        Assert.True(entry.IsProvisional);
        Assert.True(vlc.IsProvisional);
        _repo.Setup(r => r.GetAllAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { entry });
        _repo.Setup(r => r.GetAllLetterValuesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { vlc });

        var handler = new ConfirmCnamDataCommandHandler(_repo.Object, _uow.Object, NullLogger<ConfirmCnamDataCommandHandler>.Instance);
        var result = await handler.Handle(new ConfirmCnamDataCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(entry.IsProvisional);
        Assert.False(vlc.IsProvisional);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
