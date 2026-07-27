using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Moq;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// [AC-2][AC-3][AC-4][AC-5][AC-6][AC-8] Deleting a patient is refused whenever anything is attached, and the
/// refusal names what actually blocks it.
///
/// This replaces a caught <c>DbUpdateException</c> that was a lie twice over: appointments, tooth states,
/// dental records and files <b>cascaded away</b> instead of blocking, and invoices and treatment plans have no
/// foreign key at all — nothing ever raised for them and they were silently orphaned.
/// </summary>
public class PatientDeletionGuardTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private DeletePatientCommandHandler CreateDeleteHandler() =>
        new(_patients.Object, _clinicResolver.Object, _uow.Object);

    private ArchivePatientCommandHandler CreateArchiveHandler() =>
        new(_patients.Object, _clinicResolver.Object, _uow.Object);

    private GetPatientDeletionCheckQueryHandler CreateCheckHandler() =>
        new(_patients.Object, _clinicResolver.Object);

    private static Patient PatientOf(Guid clinicId) => new(
        PatientId, clinicId, "Sonia", "Bel Hadj",
        new DateTime(1990, 4, 12, 0, 0, 0, DateTimeKind.Utc), "Female",
        new Email("sonia@example.tn"), new PhoneNumber("20123456"));

    private static PatientLinkedDataCounts Nothing => new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private void Arrange(
        Patient patient,
        PatientLinkedDataCounts? counts = null,
        PatientArchiveBlockers? archiveBlockers = null)
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(patient);
        _patients.Setup(r => r.GetLinkedDataCountsAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(counts ?? Nothing);
        _patients.Setup(r => r.GetArchiveBlockersAsync(PatientId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(archiveBlockers ?? new PatientArchiveBlockers(0m, 0m, 0));
    }

    // [AC-5] A patient with genuinely nothing attached still deletes — the created-by-mistake case.
    [Fact]
    public async Task A_Patient_With_Nothing_Attached_Is_Deleted()
    {
        Arrange(PatientOf(ClinicId));

        var result = await CreateDeleteHandler().Handle(new DeletePatientCommand { Id = PatientId }, default);

        Assert.True(result.IsSuccess);
        _patients.Verify(r => r.DeleteAsync(PatientId, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-2] Anything attached refuses the delete, and nothing is written.
    [Fact]
    public async Task An_Attached_Appointment_Refuses_The_Delete()
    {
        Arrange(PatientOf(ClinicId), Nothing with { Appointments = 3 });

        var result = await CreateDeleteHandler().Handle(new DeletePatientCommand { Id = PatientId }, default);

        Assert.True(result.IsFailure);
        Assert.Contains("3 rendez-vous", result.Error);
        _patients.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-3] THE regression that matters: invoices and treatment plans have NO foreign key to Patients, so no
    // database constraint would ever have blocked this. Before the pre-check, such a patient deleted cleanly
    // and orphaned every one of those rows.
    [Fact]
    public async Task Invoices_And_Plans_Alone_Refuse_The_Delete()
    {
        Arrange(PatientOf(ClinicId), Nothing with { Invoices = 2, TreatmentPlans = 1 });

        var result = await CreateDeleteHandler().Handle(new DeletePatientCommand { Id = PatientId }, default);

        Assert.True(result.IsFailure);
        Assert.Contains("2 factures", result.Error);
        Assert.Contains("1 plan de traitement", result.Error);
        _patients.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-2] The message names the actual counts and offers archiving, rather than listing three fixed things
    // of which only one could ever have triggered.
    [Fact]
    public async Task The_Refusal_Enumerates_Every_Blocker_And_Offers_Archiving()
    {
        Arrange(PatientOf(ClinicId), Nothing with { Appointments = 3, Invoices = 2, TreatmentPlans = 1 });

        var result = await CreateDeleteHandler().Handle(new DeletePatientCommand { Id = PatientId }, default);

        Assert.Contains("3 rendez-vous, 2 factures et 1 plan de traitement", result.Error);
        Assert.Contains("Archivez", result.Error);
    }

    // [AC-4] Cancelled and voided artefacts are still records — they block too. The counts do not filter by
    // status, so a patient whose only invoice is cancelled is still undeletable.
    [Fact]
    public async Task A_Cancelled_Record_Still_Blocks()
    {
        Arrange(PatientOf(ClinicId), Nothing with { Invoices = 1 });

        var result = await CreateDeleteHandler().Handle(new DeletePatientCommand { Id = PatientId }, default);

        Assert.True(result.IsFailure);
        Assert.Contains("1 facture", result.Error);
    }

    // [AC-76] Tenant isolation: another clinic's patient reads as not found, and nothing is counted or written.
    [Fact]
    public async Task Deleting_A_Foreign_Clinics_Patient_Is_NotFound()
    {
        Arrange(PatientOf(OtherClinicId));

        var result = await CreateDeleteHandler().Handle(new DeletePatientCommand { Id = PatientId }, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Patient introuvable.", result.Error);
        _patients.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-6] The pre-check query returns the same verdict the delete would, so the dialog cannot promise a
    // deletion the command then refuses.
    [Fact]
    public async Task The_Deletion_Check_Agrees_With_The_Delete()
    {
        Arrange(PatientOf(ClinicId), Nothing with { Invoices = 2 });

        var check = await CreateCheckHandler().Handle(new GetPatientDeletionCheckQuery { PatientId = PatientId }, default);
        var delete = await CreateDeleteHandler().Handle(new DeletePatientCommand { Id = PatientId }, default);

        Assert.False(check.Value!.CanDelete);
        Assert.True(delete.IsFailure);
        Assert.Contains(check.Value.Blockers, b => b.Kind == "invoices" && b.Count == 2 && b.Label == "factures");
    }

    // [AC-6] Each blocker carries the patient-detail tab it lives on, so the dialog can link the user to it
    // instead of being a dead end.
    [Fact]
    public async Task Blockers_Carry_A_Tab_To_Navigate_To()
    {
        Arrange(PatientOf(ClinicId), Nothing with { Invoices = 1 });

        var check = await CreateCheckHandler().Handle(new GetPatientDeletionCheckQuery { PatientId = PatientId }, default);

        Assert.Equal("factures", check.Value!.Blockers.Single().Tab);
    }

    // [AC-8] Archiving is refused while money is owed — hiding a patient must not hide a debt from « Créances ».
    [Fact]
    public async Task Archiving_Is_Refused_While_A_Balance_Is_Due()
    {
        Arrange(PatientOf(ClinicId), archiveBlockers: new PatientArchiveBlockers(150m, 0m, 0));

        var result = await CreateArchiveHandler().Handle(new ArchivePatientCommand { Id = PatientId }, default);

        Assert.True(result.IsFailure);
        Assert.Contains("solde", result.Error);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-8] …and while a visit is booked — archiving must not make an upcoming appointment disappear.
    [Fact]
    public async Task Archiving_Is_Refused_While_A_Future_Appointment_Exists()
    {
        Arrange(PatientOf(ClinicId), archiveBlockers: new PatientArchiveBlockers(0m, 0m, 1));

        var result = await CreateArchiveHandler().Handle(new ArchivePatientCommand { Id = PatientId }, default);

        Assert.True(result.IsFailure);
        Assert.Contains("rendez-vous à venir", result.Error);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-7] A settled patient with no upcoming visit archives, even though records block their deletion —
    // that is the whole point of the escape hatch.
    [Fact]
    public async Task A_Settled_Patient_Archives_Even_When_Undeletable()
    {
        Arrange(PatientOf(ClinicId), Nothing with { Invoices = 5 });

        var result = await CreateArchiveHandler().Handle(new ArchivePatientCommand { Id = PatientId }, default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsArchived);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-76] Tenant isolation on archiving too.
    [Fact]
    public async Task Archiving_A_Foreign_Clinics_Patient_Is_NotFound()
    {
        Arrange(PatientOf(OtherClinicId));

        var result = await CreateArchiveHandler().Handle(new ArchivePatientCommand { Id = PatientId }, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Patient introuvable.", result.Error);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-2] The French enumeration reads naturally for one, two and three blockers.
    [Theory]
    [InlineData(1, 0, "1 rendez-vous")]
    [InlineData(0, 1, "1 facture")]
    [InlineData(0, 2, "2 factures")]
    [InlineData(2, 1, "2 rendez-vous et 1 facture")]
    public void The_Blocker_Enumeration_Is_Plural_Aware(int appointments, int invoices, string expected)
    {
        var counts = Nothing with { Appointments = appointments, Invoices = invoices };

        Assert.Equal(expected, PatientDeletionBlockers.Describe(counts));
    }
}
