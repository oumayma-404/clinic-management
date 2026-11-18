using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// A reopened fiche reads its « Payé » from its note (`seance-au-fauteuil`). The desk takes the money on the note now,
/// and nothing writes it back onto the fiche — so before this, a séance settled at reception reopened reading
/// « Payé 0 · Reste 120 ». The figure is the billing guard's own, so a re-save compares the same number.
/// </summary>
public class DentalRecordCollectedOnNoteTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Seance = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IDentalRecordRepository> _records = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IMedicalDocumentRepository> _documents = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<ICurrentClinicResolver> _resolver = new();
    private readonly List<Invoice> _notes = new();

    private readonly Patient _patient = new(Guid.NewGuid(), ClinicId, "Amal", "Ben Salah", new DateTime(1990, 4, 3), "Femme");
    private readonly DentalRecord _record;

    public DentalRecordCollectedOnNoteTests()
    {
        _record = new DentalRecord(Guid.NewGuid(), _patient.Id, ClinicId, Seance, 0m, true);
        _record.SetActs(new[]
        {
            new DentalRecordActInput(null, "Soin", 150m, null, false, Array.Empty<int>(), null, null, null),
        });

        _resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _patients.Setup(r => r.GetByIdAsync(_patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_patient);
        _records.Setup(r => r.GetByPatientIdAsync(_patient.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { _record });
        _plans.Setup(r => r.GetCollectedByDentalRecordAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DentalRecordCollectedRow>());
        _plans.Setup(r => r.GetPlanLinksByDentalRecordAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DentalRecordPlanLinkRow>());
        _documents.Setup(r => r.GetFicheOrdonnancesForDentalRecordsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MedicalDocument>());
        _invoices.Setup(r => r.GetByPatientIdsAsync(
                ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _notes);
    }

    private Invoice NoteForTheFiche(bool issued = true)
    {
        var note = new Invoice(Guid.NewGuid(), ClinicId, _patient.Id, dentalRecordId: _record.Id);
        note.SetLines(new[] { ("Soin", 1, 150m, (Guid?)_record.Id) });
        if (issued)
        {
            note.Issue("2026-0050");
        }
        _notes.Add(note);
        return note;
    }

    private async Task<DentalRecordDto> ReadTheFiche()
    {
        var handler = new GetDentalRecordsQueryHandler(
            _records.Object, _patients.Object, _plans.Object, _documents.Object, _invoices.Object, _resolver.Object);
        var result = await handler.Handle(new GetDentalRecordsQuery { PatientId = _patient.Id }, CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return Assert.Single(result.Value!);
    }

    // The headline: the chair took 100, the desk 20 more — the reopened fiche reads 120.
    [Fact]
    public async Task A_Reopened_Fiche_Reads_The_Desk_Money_From_Its_Note()
    {
        var note = NoteForTheFiche();
        note.RecordPayment(100m, PaymentMethod.Cash, Seance);
        note.RecordPayment(20m, PaymentMethod.Cash, Seance.AddDays(1));

        Assert.Equal(120m, (await ReadTheFiche()).CollectedOnNote);
    }

    // Money a later séance of the treatment collected on this note is not this fiche's « Payé ».
    [Fact]
    public async Task A_Later_Seances_Money_On_The_Note_Is_Not_This_Fiches()
    {
        var note = NoteForTheFiche();
        note.RecordPayment(100m, PaymentMethod.Cash, Seance);
        note.RecordPayment(30m, PaymentMethod.Cash, Seance.AddDays(7), dentalRecordId: Guid.NewGuid());

        Assert.Equal(100m, (await ReadTheFiche()).CollectedOnNote);
    }

    // No issued note: nothing to read, and the fiche keeps its own « Payé ».
    [Fact]
    public async Task A_Draft_Or_Missing_Note_Gives_No_Figure()
    {
        Assert.Null((await ReadTheFiche()).CollectedOnNote);

        NoteForTheFiche(issued: false);
        Assert.Null((await ReadTheFiche()).CollectedOnNote);
    }
}
