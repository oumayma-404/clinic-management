using ClinicManagement.UnitTests.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.CnamNomenclature.Commands;
using ClinicManagement.Application.Features.DentalActs.Commands;
using ClinicManagement.Application.Features.Medications.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Catalogs;

/// <summary>
/// Tenant isolation for the per-clinic reference catalogs (security-hardening US-9, audit § 2 finding 10).
///
/// <para><b>This is the test the finding is actually about.</b> Before this, the catalog mutators carried no
/// <c>ClinicId</c> check of their own and relied entirely on the EF Core global query filter — which is
/// <b>fail-open</b>: <c>ICurrentClinicProvider</c> reads the JWT <c>clinic_id</c> claim, and when no clinic is
/// in scope the filter is simply inactive. Since the Auth0 <c>app_metadata</c> push is best-effort and its
/// failure is swallowed, a token minted without that claim let an admin reach another clinic's catalog rows
/// by id.</para>
///
/// <para><b>Why these tests are meaningful where the CRUD tests are not.</b> The existing
/// <c>CnamNomenclatureCrudTests</c> / <c>MedicationCrudTests</c> pass a row that belongs to the caller's own
/// clinic, so they would pass with or without the guard. Here the repository deliberately returns a row owned
/// by <b>another</b> clinic — which is precisely what "the query filter is inactive" looks like from the
/// handler's point of view, because a mocked repository applies no filter at all. So these assertions fail
/// unless the handler performs its own DB-resolved check.</para>
///
/// <para>Every case asserts the same three things: the operation <b>fails</b>, it reads as "not found" rather
/// than "forbidden" (no existence disclosure — the convention everywhere else in this codebase), and
/// <b>nothing is saved</b>.</para>
/// </summary>
public class CatalogTenantIsolationTests
{
    private static readonly Guid CallerClinic = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinic = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    public CatalogTenantIsolationTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(CallerClinic));
    }

    private void AssertRefusedAndNothingSaved(bool isFailure, string? error)
    {
        Assert.True(isFailure);
        Assert.Contains("introuvable", error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Dental acts ----

    private static DentalActCode ForeignAct() =>
        new(Guid.NewGuid(), OtherClinic, "EXT-01", "Extraction", "Chirurgie");

    [Fact]
    public async Task UpdateDentalAct_Refuses_Another_Clinics_Row() // [AC-9.1][AC-9.3]
    {
        var act = ForeignAct();
        var repo = new Mock<IDentalActCodeRepository>();
        repo.Setup(r => r.GetByIdAsync(act.Id, It.IsAny<CancellationToken>())).ReturnsAsync(act);

        var handler = new UpdateDentalActCommandHandler(
            repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<UpdateDentalActCommandHandler>.Instance);

        var result = await handler.Handle(
            new UpdateDentalActCommand { Id = act.Id, CodeActe = "EXT-01", DesignationFr = "Piraté", Category = "Chirurgie" },
            CancellationToken.None);

        AssertRefusedAndNothingSaved(result.IsFailure, result.Error);
    }

    [Fact]
    public async Task DeactivateDentalAct_Refuses_Another_Clinics_Row() // [AC-9.1][AC-9.3]
    {
        var act = ForeignAct();
        var repo = new Mock<IDentalActCodeRepository>();
        repo.Setup(r => r.GetByIdAsync(act.Id, It.IsAny<CancellationToken>())).ReturnsAsync(act);

        var handler = new DeactivateDentalActCommandHandler(
            repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<DeactivateDentalActCommandHandler>.Instance);

        var result = await handler.Handle(new DeactivateDentalActCommand { Id = act.Id }, CancellationToken.None);

        AssertRefusedAndNothingSaved(result.IsFailure, result.Error);
        Assert.True(act.IsActive); // the aggregate was not mutated either
    }

    [Fact]
    public async Task ConfirmDentalActs_Confirms_Only_The_Callers_Rows() // [AC-9.4] the bulk-write path
    {
        // The worst case in the finding: no id to guard, so an unfiltered read confirmed EVERY clinic's rows.
        var mine = new DentalActCode(Guid.NewGuid(), CallerClinic, "MINE", "À moi", "Soins");
        var theirs = new DentalActCode(Guid.NewGuid(), OtherClinic, "THEIRS", "À eux", "Soins");

        var repo = new Mock<IDentalActCodeRepository>();
        repo.Setup(r => r.GetProvisionalAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { mine, theirs });

        var handler = new ConfirmDentalActsCommandHandler(
            repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<ConfirmDentalActsCommandHandler>.Instance);

        var result = await handler.Handle(new ConfirmDentalActsCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.UpdateAsync(mine, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.UpdateAsync(theirs, It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- CNAM nomenclature ----

    private static CnamNomenclatureEntry ForeignEntry() =>
        new(Guid.NewGuid(), OtherClinic, "CONS", "Consultation", "CD", 1, "Consultation");

    [Fact]
    public async Task UpdateCnamEntry_Refuses_Another_Clinics_Row() // [AC-9.1][AC-9.3]
    {
        var entry = ForeignEntry();
        var repo = new Mock<ICnamCatalogRepository>();
        repo.Setup(r => r.GetByIdAsync(entry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(entry);

        var handler = new UpdateCnamEntryCommandHandler(
            repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<UpdateCnamEntryCommandHandler>.Instance);

        var result = await handler.Handle(
            new UpdateCnamEntryCommand
            {
                Id = entry.Id,
                CodeActe = "CONS",
                DesignationFr = "Piraté",
                LettreCle = "CD",
                Coefficient = 1,
                Category = "Consultation"
            },
            CancellationToken.None);

        AssertRefusedAndNothingSaved(result.IsFailure, result.Error);
    }

    [Fact]
    public async Task DeactivateCnamEntry_Refuses_Another_Clinics_Row() // [AC-9.1][AC-9.3]
    {
        var entry = ForeignEntry();
        var repo = new Mock<ICnamCatalogRepository>();
        repo.Setup(r => r.GetByIdAsync(entry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(entry);

        var handler = new DeactivateCnamEntryCommandHandler(
            repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<DeactivateCnamEntryCommandHandler>.Instance);

        var result = await handler.Handle(new DeactivateCnamEntryCommand { Id = entry.Id }, CancellationToken.None);

        AssertRefusedAndNothingSaved(result.IsFailure, result.Error);
    }

    [Fact]
    public async Task UpdateCnamLetterValue_Refuses_Another_Clinics_Row() // [AC-9.1][AC-9.3]
    {
        // The VLC drives every reimbursement estimate, so a cross-clinic write here would silently change
        // what another clinic tells its patients they will be reimbursed.
        var value = new CnamLetterValue(Guid.NewGuid(), OtherClinic, "D", 1.2m);
        var repo = new Mock<ICnamCatalogRepository>();
        repo.Setup(r => r.GetLetterValueByIdAsync(value.Id, It.IsAny<CancellationToken>())).ReturnsAsync(value);

        var handler = new UpdateCnamLetterValueCommandHandler(
            repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<UpdateCnamLetterValueCommandHandler>.Instance);

        var result = await handler.Handle(
            new UpdateCnamLetterValueCommand { Id = value.Id, Value = 99m }, CancellationToken.None);

        AssertRefusedAndNothingSaved(result.IsFailure, result.Error);
        Assert.Equal(1.2m, value.Value); // untouched
    }

    [Fact]
    public async Task ConfirmCnamData_Confirms_Only_The_Callers_Rows() // [AC-9.4] the bulk-write path
    {
        var mine = new CnamNomenclatureEntry(Guid.NewGuid(), CallerClinic, "A", "À moi", "CD", 1, "Consultation");
        var theirs = new CnamNomenclatureEntry(Guid.NewGuid(), OtherClinic, "B", "À eux", "CD", 1, "Consultation");
        var myValue = new CnamLetterValue(Guid.NewGuid(), CallerClinic, "D", 1.2m);
        var theirValue = new CnamLetterValue(Guid.NewGuid(), OtherClinic, "D", 1.2m);

        var repo = new Mock<ICnamCatalogRepository>();
        repo.Setup(r => r.GetAllAsync(true, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { mine, theirs }.AsPage());
        repo.Setup(r => r.GetAllLetterValuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { myValue, theirValue });

        var handler = new ConfirmCnamDataCommandHandler(
            repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<ConfirmCnamDataCommandHandler>.Instance);

        var result = await handler.Handle(new ConfirmCnamDataCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.UpdateAsync(mine, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.UpdateAsync(theirs, It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.UpdateLetterValueAsync(myValue, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.UpdateLetterValueAsync(theirValue, It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Medications ----

    private static Medication ForeignMedication() =>
        new(Guid.NewGuid(), OtherClinic, "Augmentin", "Comprimé", "1 g", new[] { "Amoxicilline" });

    [Fact]
    public async Task UpdateMedication_Refuses_Another_Clinics_Row() // [AC-9.1][AC-9.3]
    {
        var medication = ForeignMedication();
        var repo = new Mock<IMedicationCatalogRepository>();
        repo.Setup(r => r.GetByIdAsync(medication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(medication);

        var handler = new UpdateMedicationCommandHandler(
            repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<UpdateMedicationCommandHandler>.Instance);

        var result = await handler.Handle(
            new UpdateMedicationCommand
            {
                Id = medication.Id,
                BrandName = "Piraté",
                Form = "Comprimé",
                Strength = "1 g",
                Dcis = new List<string> { "Amoxicilline" }
            },
            CancellationToken.None);

        AssertRefusedAndNothingSaved(result.IsFailure, result.Error);
    }

    [Fact]
    public async Task DeactivateMedication_Refuses_Another_Clinics_Row() // [AC-9.1][AC-9.3]
    {
        var medication = ForeignMedication();
        var repo = new Mock<IMedicationCatalogRepository>();
        repo.Setup(r => r.GetByIdAsync(medication.Id, It.IsAny<CancellationToken>())).ReturnsAsync(medication);

        var handler = new DeactivateMedicationCommandHandler(
            repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<DeactivateMedicationCommandHandler>.Instance);

        var result = await handler.Handle(
            new DeactivateMedicationCommand { Id = medication.Id }, CancellationToken.None);

        AssertRefusedAndNothingSaved(result.IsFailure, result.Error);
    }

    [Fact]
    public async Task ConfirmMedicationData_Confirms_Only_The_Callers_Rows() // [AC-9.4] the bulk-write path
    {
        var mine = new Medication(Guid.NewGuid(), CallerClinic, "À moi", "Comprimé", "1 g", new[] { "X" });
        var theirs = new Medication(Guid.NewGuid(), OtherClinic, "À eux", "Comprimé", "1 g", new[] { "Y" });

        var repo = new Mock<IMedicationCatalogRepository>();
        repo.Setup(r => r.GetAllAsync(true, It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { mine, theirs }.AsPage());

        var handler = new ConfirmMedicationDataCommandHandler(
            repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<ConfirmMedicationDataCommandHandler>.Instance);

        var result = await handler.Handle(new ConfirmMedicationDataCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.UpdateAsync(mine, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.UpdateAsync(theirs, It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- The fail-open resolver itself ----

    [Fact]
    public async Task An_unresolvable_clinic_refuses_rather_than_falling_through() // [AC-9.3]
    {
        // The original defect in one line: with no clinic in scope the EF filter is inactive. The handler must
        // therefore refuse outright rather than proceed against an unfiltered read.
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure("Cabinet introuvable."));

        var act = ForeignAct();
        var repo = new Mock<IDentalActCodeRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(act);

        var handler = new DeactivateDentalActCommandHandler(
            repo.Object, _clinicResolver.Object, _uow.Object, NullLogger<DeactivateDentalActCommandHandler>.Instance);

        var result = await handler.Handle(new DeactivateDentalActCommand { Id = act.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        // It must not even reach the row.
        repo.Verify(r => r.UpdateAsync(It.IsAny<DentalActCode>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
