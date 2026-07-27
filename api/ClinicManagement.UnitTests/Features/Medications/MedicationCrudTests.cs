using ClinicManagement.Application.Features.Medications.Commands;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Medications;

/// <summary>
/// DB-backed, per-clinic, AdminOnly medication catalog CRUD (#5). Mirrors the CNAM catalog CRUD pattern but
/// keyed on brand + form + strength (rejects a duplicate presentation) and requires at least one DCI
/// molecule. Mocks <see cref="IMedicationCatalogRepository"/> + <see cref="ICurrentClinicResolver"/> +
/// <see cref="IUnitOfWork"/>.
/// </summary>
public class MedicationCrudTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();
    private readonly Mock<IMedicationCatalogRepository> _repo = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public MedicationCrudTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
    }

    private static Medication Med(string brand = "Doliprane") =>
        new(Guid.NewGuid(), ClinicId, brand, "Comprimé", "1000 mg", new[] { "Paracétamol" });

    [Fact]
    public async Task Create_Succeeds_And_Persists_All_Fields() // [AC-1]
    {
        _repo.Setup(r => r.BrandExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Medication? captured = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<Medication>(), It.IsAny<CancellationToken>()))
            .Callback<Medication, CancellationToken>((m, _) => captured = m)
            .ReturnsAsync((Medication m, CancellationToken _) => m);

        var handler = new CreateMedicationCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<CreateMedicationCommandHandler>.Instance);
        var result = await handler.Handle(new CreateMedicationCommand
        {
            BrandName = "Augmentin", Form = "Comprimé", Strength = "1 g",
            Dcis = new List<string> { "Amoxicilline", "Acide clavulanique" },
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal("Augmentin", captured!.BrandName);
        Assert.Equal("Comprimé", captured.Form);
        Assert.Equal("1 g", captured.Strength);
        Assert.Equal(2, captured.ActiveIngredients.Count);
        Assert.Contains(captured.ActiveIngredients, i => i.Dci == "Amoxicilline");
        Assert.Contains(captured.ActiveIngredients, i => i.Dci == "Acide clavulanique");
        Assert.True(captured.IsActive);
        Assert.True(captured.IsProvisional);
        _repo.Verify(r => r.AddAsync(It.IsAny<Medication>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Create_Duplicate_Brand_Form_Strength_Is_Rejected_With_French_Message() // [AC-8]
    {
        _repo.Setup(r => r.BrandExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var handler = new CreateMedicationCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<CreateMedicationCommandHandler>.Instance);
        var result = await handler.Handle(new CreateMedicationCommand
        {
            BrandName = "Doliprane", Form = "Comprimé", Strength = "1000 mg", Dcis = new List<string> { "Paracétamol" },
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("existe déjà", result.Error);
        _repo.Verify(r => r.AddAsync(It.IsAny<Medication>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Without_Any_Dci_Is_Rejected() // [AC-1] a medication must carry >= 1 molecule
    {
        var handler = new CreateMedicationCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<CreateMedicationCommandHandler>.Instance);
        var result = await handler.Handle(new CreateMedicationCommand
        {
            BrandName = "Doliprane", Form = "Comprimé", Strength = "1000 mg", Dcis = new List<string>(),
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("DCI", result.Error);
        _repo.Verify(r => r.AddAsync(It.IsAny<Medication>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_With_Only_Blank_Dcis_Is_Rejected() // [AC-1] whitespace molecules are dropped
    {
        var handler = new CreateMedicationCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<CreateMedicationCommandHandler>.Instance);
        var result = await handler.Handle(new CreateMedicationCommand
        {
            BrandName = "Doliprane", Form = "Comprimé", Strength = "1000 mg", Dcis = new List<string> { "  ", "" },
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _repo.Verify(r => r.AddAsync(It.IsAny<Medication>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_Without_BrandName_Is_Rejected()
    {
        var handler = new CreateMedicationCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<CreateMedicationCommandHandler>.Instance);
        var result = await handler.Handle(new CreateMedicationCommand
        {
            BrandName = "   ", Form = "Comprimé", Strength = "1000 mg", Dcis = new List<string> { "Paracétamol" },
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("nom commercial", result.Error);
    }

    [Fact]
    public void Created_Entry_Is_Provisional_And_Active_By_Default() // [AC-1]
    {
        var med = Med();
        Assert.True(med.IsProvisional);
        Assert.True(med.IsActive);
    }

    [Fact]
    public async Task Update_Existing_Replaces_Fields_And_Dcis()
    {
        var med = Med("Augmentin");
        _repo.Setup(r => r.GetByIdAsync(med.Id, It.IsAny<CancellationToken>())).ReturnsAsync(med);
        _repo.Setup(r => r.BrandExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var handler = new UpdateMedicationCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<UpdateMedicationCommandHandler>.Instance);
        var result = await handler.Handle(new UpdateMedicationCommand
        {
            Id = med.Id, BrandName = "Augmentin", Form = "Sachet", Strength = "500 mg/62,5 mg",
            Dcis = new List<string> { "Amoxicilline", "Acide clavulanique" },
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Sachet", med.Form);
        Assert.Equal("500 mg/62,5 mg", med.Strength);
        Assert.Equal(2, med.ActiveIngredients.Count);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_Unknown_Id_Returns_NotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Medication?)null);

        var handler = new UpdateMedicationCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<UpdateMedicationCommandHandler>.Instance);
        var result = await handler.Handle(new UpdateMedicationCommand
        {
            Id = Guid.NewGuid(), BrandName = "X", Form = "Y", Strength = "Z", Dcis = new List<string> { "M" },
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_To_Duplicate_Is_Rejected()
    {
        var med = Med("Doliprane");
        _repo.Setup(r => r.GetByIdAsync(med.Id, It.IsAny<CancellationToken>())).ReturnsAsync(med);
        _repo.Setup(r => r.BrandExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var handler = new UpdateMedicationCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<UpdateMedicationCommandHandler>.Instance);
        var result = await handler.Handle(new UpdateMedicationCommand
        {
            Id = med.Id, BrandName = "Efferalgan", Form = "Comprimé", Strength = "1000 mg", Dcis = new List<string> { "Paracétamol" },
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Deactivate_Sets_Inactive()
    {
        var med = Med();
        Assert.True(med.IsActive);
        _repo.Setup(r => r.GetByIdAsync(med.Id, It.IsAny<CancellationToken>())).ReturnsAsync(med);

        var handler = new DeactivateMedicationCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<DeactivateMedicationCommandHandler>.Instance);
        var result = await handler.Handle(new DeactivateMedicationCommand { Id = med.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(med.IsActive); // excluded from active-only reads / the picker
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Deactivate_Unknown_Id_Returns_NotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Medication?)null);

        var handler = new DeactivateMedicationCommandHandler(_repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<DeactivateMedicationCommandHandler>.Instance);
        var result = await handler.Handle(new DeactivateMedicationCommand { Id = Guid.NewGuid() }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirm_Clears_Provisional_On_All_Entries() // [AC-1]
    {
        var med = Med(); // provisional by default
        Assert.True(med.IsProvisional);
        _repo.Setup(r => r.GetAllAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { med });

        var handler = new ConfirmMedicationDataCommandHandler(_repo.Object, _uow.Object, NullLogger<ConfirmMedicationDataCommandHandler>.Instance);
        var result = await handler.Handle(new ConfirmMedicationDataCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(med.IsProvisional);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Catalog_Is_Per_Clinic_Scoped() // [#5]
    {
        // Per-clinic (#5): the entity carries a ClinicId (like ProcedureType). The create command has NO
        // clinic parameter — the caller's clinic is resolved server-side and stamped by the handler.
        Assert.NotNull(typeof(Medication).GetProperty("ClinicId"));
        Assert.NotNull(typeof(ProcedureType).GetProperty("ClinicId"));
        Assert.Null(typeof(CreateMedicationCommand).GetProperty("ClinicId"));
    }
}
