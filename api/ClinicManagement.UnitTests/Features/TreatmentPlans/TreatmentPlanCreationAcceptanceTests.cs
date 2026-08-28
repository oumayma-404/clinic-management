using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// A devis is <b>accepted at creation</b> — there is no draft stage and no « Accepter le devis » step.
/// <para>
/// The draft stage was a second confirmation of a decision the dentist had already made with the patient in the
/// chair, and it silently held the plan out of « Solde patient » and « Créances » until someone remembered to
/// press a button. These tests pin the consequences that acceptance carries with it, because each one is
/// invisible from the create call itself: the plan is numbered, it is live, it is payable, and it is refused
/// outright when it has no act.
/// </para>
/// </summary>
public class TreatmentPlanCreationAcceptanceTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IProcedureTypeRepository> _procedureTypes = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    // L9 — the attribution dependencies, arranged to reproduce this test's ORIGINAL behaviour: an empty roster and
    // no caller doctor means `PractitionerAttribution.Resolve` finds no candidate and the aggregate stays
    // unattributed, exactly as before the column existed. Attribution has its own tests; these are not repurposed.
    private readonly Mock<IDoctorRepository> _doctors = new();
    private readonly Mock<IClinicContext> _clinicContext = new();

    /// <summary>The plan the handler staged, captured so the tests can assert on the aggregate itself.</summary>
    private TreatmentPlan? _saved;

    public TreatmentPlanCreationAcceptanceTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Patient(
                PatientId, ClinicId, "Jean", "Dupont", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M"));
        _plans.Setup(r => r.AddAsync(It.IsAny<TreatmentPlan>(), It.IsAny<CancellationToken>()))
            .Callback<TreatmentPlan, CancellationToken>((p, _) => _saved = p)
            .ReturnsAsync((TreatmentPlan p, CancellationToken _) => p);
        // 4 devis already numbered this year, so the next one must be 0005 — a sequence that starts from the max
        // rather than from a count is what keeps it gapless when an early plan is cancelled.
        _plans.Setup(r => r.GetMaxSequenceForYearAsync(ClinicId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
        _doctors.Setup(r => r.GetByClinicIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Doctor>());
        _clinicContext.Setup(c => c.GetUserId()).Returns((string?)null);
    }

    private CreateTreatmentPlanCommandHandler Handler() => new(
        _plans.Object, _patients.Object, _procedureTypes.Object, _clinicResolver.Object, _doctors.Object,
        _clinicContext.Object, _uow.Object, NullLogger<CreateTreatmentPlanCommandHandler>.Instance);

    private static CreateTreatmentPlanCommand Command(
        List<TreatmentPlanItemRequest>? items = null,
        List<InstallmentRequest>? installments = null) => new()
        {
            PatientId = PatientId,
            Title = "Réhabilitation",
            Items = items ?? new List<TreatmentPlanItemRequest>
            {
                new() { DesignationFr = "Couronne", PlannedCost = 500m, ToothNumbers = new List<int> { 11 } },
            },
            Installments = installments ?? new List<InstallmentRequest>(),
        };

    [Fact]
    public async Task A_Created_Plan_Is_Accepted_And_Numbered()
    {
        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(_saved);
        Assert.Equal(TreatmentPlanStatus.Accepted, _saved!.Status);
        Assert.Equal($"{ClinicManagement.Application.Common.ClinicClock.ClinicYear()}-0005", _saved.Number);
        Assert.NotNull(_saved.AcceptedDate);
    }

    /// <summary>
    /// The DTO must carry the number back, because the UI names it in the success toast — that is the only
    /// evidence on screen that the plan is already live and no acceptance step is missing.
    /// </summary>
    [Fact]
    public async Task The_Response_Carries_The_Devis_Number()
    {
        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.Equal(_saved!.Number, result.Value!.Number);
        Assert.Equal("Accepted", result.Value.Status);
    }

    /// <summary>
    /// `Accept` back-fills a single lump-sum échéance when none was supplied. Without it `Outstanding` — derived
    /// from the installments — would sit at the full total forever with no row to record a payment against.
    /// </summary>
    [Fact]
    public async Task A_Plan_Created_Without_A_Schedule_Gets_A_Lump_Sum_Echeance()
    {
        await Handler().Handle(Command(), CancellationToken.None);

        var installment = Assert.Single(_saved!.Installments);
        Assert.Equal(500m, installment.Amount);
        Assert.Equal(500m, _saved.Outstanding);
    }

    [Fact]
    public async Task A_Supplied_Schedule_Is_Kept_As_Is()
    {
        var due = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        await Handler().Handle(
            Command(installments: new List<InstallmentRequest>
            {
                new() { DueDate = due, Amount = 200m },
                new() { DueDate = due.AddMonths(1), Amount = 300m },
            }),
            CancellationToken.None);

        Assert.Equal(2, _saved!.Installments.Count);
        Assert.Equal(500m, _saved.Installments.Sum(i => i.Amount));
    }

    /// <summary>
    /// Acceptance requires an act, so an empty plan is now refused at creation instead of being saved and left
    /// unusable. The message is the domain's own French one — the client never sees exception text.
    /// </summary>
    [Fact]
    public async Task A_Plan_With_No_Act_Is_Refused()
    {
        var result = await Handler().Handle(
            Command(items: new List<TreatmentPlanItemRequest>()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("au moins un acte", result.Error);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// One commit, not two. The insert and the acceptance share a single `SaveChanges`, so there is no window in
    /// which a plan exists un-numbered — which a create-then-accept pair would have.
    /// </summary>
    [Fact]
    public async Task Creation_And_Acceptance_Commit_Together()
    {
        await Handler().Handle(Command(), CancellationToken.None);

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A cross-clinic patient id is refused before anything is numbered — a devis number is a gapless sequence,
    /// so consuming one on a rejected request would leave a permanent hole.
    /// </summary>
    [Fact]
    public async Task A_Foreign_Patient_Is_Refused_Without_Consuming_A_Number()
    {
        var foreignPatient = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        _patients.Setup(r => r.GetByIdAsync(foreignPatient, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Patient(
                foreignPatient, Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                "Autre", "Cabinet", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "F"));

        var command = Command();
        command.PatientId = foreignPatient;

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(_saved);
        _plans.Verify(
            r => r.GetMaxSequenceForYearAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
