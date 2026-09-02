using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// « À clôturer › séances retirées » lists a séance somebody set aside <b>whatever else is true of it</b> — and
/// that is a recovery guarantee, not a listing preference.
///
/// <para>It is the only screen in the product that shows the mark, so it is also the only way to undo one, and
/// « Supprimer (créé par erreur) » on the appointment now writes that mark from the agenda. The reader used to
/// apply two worklist gates before the partition: <c>IsClosable</c> (the slot must have ended) and
/// <c>IsOpen</c> (the séance must still owe something). Both are right for the worklist and wrong for the
/// recovery list — under them a séance mis-typed onto <i>next</i> Tuesday left the agenda, left the dashboard's
/// figures, and appeared nowhere, while the dialog that removed it promised it could be brought back.</para>
///
/// <para>The other half of the guarantee is a SQL predicate (<c>GetClosureCandidatesAsync</c> must return the row
/// at all) and no unit test in this solution reaches a database — so these cases hold the in-memory half, and the
/// query half is verified against a real database with <c>verify-schema</c>'s sibling, a read-only probe.</para>
/// </summary>
public class DisregardedVisitsStayRecoverableTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Now = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);
    private const string UserId = "local|11111111-1111-1111-1111-111111111111";

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IDentalRecordRepository> _dentalRecords = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();

    private static Appointment VisitAt(DateTime startUtc)
        => new(Guid.NewGuid(), ClinicId, Guid.NewGuid(), doctorId: null, startUtc, TimeSpan.FromHours(1));

    // The case the feature exists for: a séance typed onto the wrong day, caught before anyone sat in the chair.
    [Fact]
    public async Task A_Retired_Séance_In_The_Future_Is_Listed_Among_The_Retired()
    {
        var nextTuesday = VisitAt(Now.AddDays(5));
        nextTuesday.Disregard(UserId, Now);

        var worklist = await Read(nextTuesday);

        Assert.Equal(new[] { nextTuesday.Id }, worklist.Disregarded.Select(v => v.Appointment.Id));
        Assert.Empty(worklist.Open);
    }

    // `IsOpen` is the second gate, and it hid a different set: a séance that owes nothing has no reason to be on
    // the worklist and every reason to be recoverable.
    [Fact]
    public async Task A_Retired_Séance_That_Owes_Nothing_Is_Still_Listed()
    {
        var settled = VisitAt(Now.AddHours(-3));
        settled.MarkVisitCompleted();
        settled.MarkNothingToBill("Contrôle offert", UserId, Now);
        settled.Disregard(UserId, Now);

        var worklist = await Read(settled);

        Assert.Equal(new[] { settled.Id }, worklist.Disregarded.Select(v => v.Appointment.Id));
    }

    // A row can be annulée and then retirée; the second mark must not be swallowed by the first.
    [Fact]
    public async Task A_Retired_Séance_That_Was_Cancelled_Is_Still_Listed()
    {
        var cancelled = VisitAt(Now.AddHours(-3));
        cancelled.Cancel();
        cancelled.Disregard(UserId, Now);

        var worklist = await Read(cancelled);

        Assert.Equal(new[] { cancelled.Id }, worklist.Disregarded.Select(v => v.Appointment.Id));
    }

    // ⚠️ The worklist itself must be untouched by all of the above — the partition moved above the two gates, the
    // gates did not go away. A future séance nobody retired owes nothing yet and belongs on neither list.
    [Fact]
    public async Task A_Future_Séance_Nobody_Retired_Is_On_Neither_List()
    {
        var worklist = await Read(VisitAt(Now.AddDays(5)));

        Assert.Empty(worklist.Open);
        Assert.Empty(worklist.Disregarded);
    }

    [Fact]
    public async Task An_Elapsed_Séance_Nobody_Retired_Is_Still_On_The_Worklist()
    {
        var yesterday = VisitAt(Now.AddHours(-3));

        var worklist = await Read(yesterday);

        Assert.Equal(new[] { yesterday.Id }, worklist.Open.Select(v => v.Appointment.Id));
        Assert.Empty(worklist.Disregarded);
    }

    private async Task<VisitClosureWorklist> Read(params Appointment[] candidates)
    {
        _appointments.Setup(r => r.GetClosureCandidatesAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);

        _dentalRecords.Setup(r => r.GetAppointmentLinksAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, decimal)>());

        _invoices.Setup(r => r.GetAppointmentLinksAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());

        _invoices.Setup(r => r.GetDentalRecordLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());

        _plans.Setup(r => r.GetDebtBearingItemIdsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        return await VisitClosureReader.ReadAsync(
            ClinicId, days: null, doctorId: null, Now,
            _appointments.Object, _dentalRecords.Object, _invoices.Object, _plans.Object);
    }
}
