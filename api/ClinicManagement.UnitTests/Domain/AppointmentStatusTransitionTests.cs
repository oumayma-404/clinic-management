using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// [AC-P1.1–1.9] The appointment status machine. Before this there was no declared transition set at all: each
/// mutator carried its own ad-hoc guard, the command layer carried a second, contradictory copy as a
/// fall-through <c>switch</c>, and the two disagreed — which is how « Terminé » on a Scheduled appointment
/// returned HTTP 200 having changed nothing.
/// <para>
/// There was also **no domain test file for <c>Appointment</c> at all** — the only coverage of the machine was
/// two incidental <c>[Theory]</c>s inside a documents test.
/// </para>
/// </summary>
public class AppointmentStatusTransitionTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime SlotStart = new(2026, 7, 30, 9, 0, 0, DateTimeKind.Utc);

    private static Appointment NewAppointment() => new(
        Guid.NewGuid(),
        ClinicId,
        patientId: Guid.NewGuid(),
        doctorId: Guid.NewGuid(),
        appointmentDateTime: SlotStart,
        duration: TimeSpan.FromMinutes(30));

    /// <summary>
    /// The statuses a fixture can be walked to. <c>Confirmed</c> is absent because nothing can reach it any
    /// more — see <see cref="Nothing_Can_Transition_To_Confirmed"/> — and building one by hand would assert a
    /// state the product can no longer produce. Rows already stored in it are covered by the table, which
    /// still declares their exits.
    /// </summary>
    private static readonly AppointmentStatus[] ReachableStatuses = Enum.GetValues<AppointmentStatus>()
        .Where(s => s != AppointmentStatus.Confirmed)
        .ToArray();

    /// <summary>
    /// Walk an appointment to a state through legal steps only, so no fixture can beg the question by
    /// assigning a status the table would refuse.
    /// </summary>
    private static Appointment AppointmentAt(AppointmentStatus status)
    {
        var appointment = NewAppointment();
        switch (status)
        {
            case AppointmentStatus.Scheduled:
                break;
            case AppointmentStatus.InProgress:
                appointment.Start();
                break;
            case AppointmentStatus.Completed:
                appointment.Start();
                appointment.Complete();
                break;
            case AppointmentStatus.Cancelled:
                appointment.Cancel("motif");
                break;
            case AppointmentStatus.NoShow:
                appointment.MarkAsNoShow();
                break;
            default:
                throw new InvalidOperationException($"No legal path reaches {status}.");
        }

        Assert.Equal(status, appointment.Status);
        return appointment;
    }

    /// <summary>Drive a transition through the mutator that owns it, exactly as the command layer does.</summary>
    private static void MoveTo(Appointment appointment, AppointmentStatus target)
    {
        switch (target)
        {
            case AppointmentStatus.Scheduled:
                if (appointment.Status == AppointmentStatus.Cancelled)
                {
                    appointment.Reactivate(appointment.AppointmentDateTime);
                }
                else
                {
                    appointment.Reschedule(appointment.AppointmentDateTime);
                }
                break;
            case AppointmentStatus.InProgress: appointment.Start(); break;
            case AppointmentStatus.Completed: appointment.Complete(); break;
            case AppointmentStatus.Cancelled: appointment.Cancel("motif"); break;
            case AppointmentStatus.NoShow: appointment.MarkAsNoShow(); break;
        }
    }

    [Fact]
    public void A_New_Appointment_Starts_Scheduled()
    {
        Assert.Equal(AppointmentStatus.Scheduled, NewAppointment().Status);
    }

    // ---- The declared table is the authority (AC-P1.3) -----------------------

    // Every pair the table declares legal must actually be performable through the mutators. Without this the
    // table and the mutators could drift — which is the exact defect being removed, in a new form.
    [Fact]
    public void Every_Declared_Transition_Is_Performable() // [AC-P1.3]
    {
        foreach (var from in ReachableStatuses)
        {
            foreach (var to in Appointment.NextStatusesFrom(from))
            {
                var appointment = AppointmentAt(from);
                MoveTo(appointment, to);
                Assert.Equal(to, appointment.Status);
            }
        }
    }

    // And the mirror: anything the table does NOT declare must be refused, not silently ignored.
    //
    // One documented exclusion. `Reschedule` is a **movement** operation, not a status transition: since the
    // A-2 fix it deliberately *preserves* `Confirmed`/`InProgress`, so calling it on one of those never attempts
    // to reach `Scheduled` and correctly does not throw. `Confirmed/InProgress → Scheduled` is still refused —
    // by the table, enforced at the command layer via `CanTransition` before any mutator runs, which
    // `Undeclared_Transitions_To_Scheduled_Are_Refused_By_The_Table` below asserts directly.
    [Fact]
    public void Every_Undeclared_Transition_Is_Refused() // [AC-P1.2 / AC-P1.3]
    {
        foreach (var from in ReachableStatuses)
        {
            var allowed = Appointment.NextStatusesFrom(from);
            foreach (var to in ReachableStatuses)
            {
                if (to == from || allowed.Contains(to))
                {
                    continue;
                }

                if (to == AppointmentStatus.Scheduled && from == AppointmentStatus.InProgress)
                {
                    continue; // see the note above — asserted by the next test instead
                }

                var appointment = AppointmentAt(from);
                Assert.Throws<InvalidOperationException>(() => MoveTo(appointment, to));
                Assert.Equal(from, appointment.Status);
            }
        }
    }

    // The command layer refuses these before reaching a mutator, so the table is where the rule lives.
    [Theory]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.InProgress)]
    public void Undeclared_Transitions_To_Scheduled_Are_Refused_By_The_Table(AppointmentStatus from)
    {
        Assert.False(Appointment.CanTransition(from, AppointmentStatus.Scheduled));
    }

    // « Confirmé » is retired: it distinguished « the patient said yes » from « we put them in the book » and
    // nothing in the product ever read the difference. The member survives so stored rows still load and still
    // move on, which is what this asserts is the ONLY thing left of it.
    [Fact]
    public void Nothing_Can_Transition_To_Confirmed()
    {
        foreach (var from in Enum.GetValues<AppointmentStatus>())
        {
            Assert.DoesNotContain(AppointmentStatus.Confirmed, Appointment.NextStatusesFrom(from));
        }

        Assert.NotEmpty(Appointment.NextStatusesFrom(AppointmentStatus.Confirmed));
    }

    // ...and rescheduling one keeps its status rather than quietly demoting it to Scheduled, which is the whole
    // point of the A-2 fix and the reason the exclusion above exists.
    [Fact]
    public void Rescheduling_Does_Not_Demote_To_Scheduled()
    {
        var appointment = AppointmentAt(AppointmentStatus.InProgress);

        appointment.Reschedule(SlotStart.AddDays(2));

        Assert.Equal(AppointmentStatus.InProgress, appointment.Status);
    }

    // [AC-P1.2] A refusal names BOTH statuses, in French. The old messages were English and named neither
    // ("Cannot cancel a completed appointment" does not say what it *is*).
    [Fact]
    public void A_Refusal_Names_Both_Statuses_In_French()
    {
        var completed = AppointmentAt(AppointmentStatus.Completed);

        var ex = Assert.Throws<InvalidOperationException>(() => completed.Start());

        Assert.Contains("Terminé", ex.Message);
        Assert.Contains("En cours", ex.Message);
        Assert.DoesNotContain("Cannot", ex.Message);
    }

    // ---- The specific defects the ACs name ----------------------------------

    // [AC-P1.1] « Terminé » is reachable from all three open states. It previously demanded InProgress — which
    // no UI ever sets — so choosing « Terminé » hit the command layer's no-op arm.
    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.InProgress)]
    public void Completing_Is_Allowed_From_Every_Open_State(AppointmentStatus from)
    {
        var appointment = AppointmentAt(from);

        appointment.Complete();

        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
    }

    // [AC-P1.5] The new exit. A visit is auto-completed merely by saving its fiche, so a fiche saved against
    // the wrong appointment used to close it permanently with no way back.
    [Fact]
    public void A_Completed_Appointment_Can_Be_Cancelled()
    {
        var appointment = AppointmentAt(AppointmentStatus.Completed);

        appointment.Cancel("fiche enregistrée sur le mauvais rendez-vous");

        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal("fiche enregistrée sur le mauvais rendez-vous", appointment.CancellationReason);
        Assert.NotNull(appointment.CancelledAt);
    }

    // [AC-P1.5] ...and it is the ONLY exit. A closed visit is voided, never reopened.
    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.InProgress)]
    [InlineData(AppointmentStatus.NoShow)]
    public void Cancelled_Is_The_Only_Exit_From_Completed(AppointmentStatus target)
    {
        Assert.DoesNotContain(target, Appointment.NextStatusesFrom(AppointmentStatus.Completed));
    }

    // [AC-P1.9 / A-2] Reschedule() force-set Scheduled, so moving a visit that had already started silently
    // discarded that fact. (It preserved « Confirmé » for the same reason, before that status was retired.)
    [Fact]
    public void Rescheduling_Preserves_InProgress()
    {
        var appointment = AppointmentAt(AppointmentStatus.InProgress);
        var moved = SlotStart.AddDays(3);

        appointment.Reschedule(moved);

        Assert.Equal(AppointmentStatus.InProgress, appointment.Status);
        Assert.Equal(moved, appointment.AppointmentDateTime);
    }

    // [AC-P1.9] NoShow is deliberately NOT preserved: rebooking is how a no-show is resolved, and carrying the
    // absence forward would mark the patient absent from a visit that has not happened yet.
    [Fact]
    public void Rescheduling_A_NoShow_Returns_It_To_Scheduled()
    {
        var appointment = AppointmentAt(AppointmentStatus.NoShow);
        var moved = SlotStart.AddDays(7);

        appointment.Reschedule(moved);

        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
        Assert.Equal(moved, appointment.AppointmentDateTime);
    }

    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public void Rescheduling_Is_Refused_For_A_Finished_Or_Void_Appointment(AppointmentStatus from)
    {
        var appointment = AppointmentAt(from);
        var original = appointment.AppointmentDateTime;

        var ex = Assert.Throws<InvalidOperationException>(() => appointment.Reschedule(original.AddDays(1)));

        Assert.Equal(original, appointment.AppointmentDateTime);
        // French, per the § 2 sweep's standing rule.
        Assert.DoesNotContain("Cannot", ex.Message);
    }

    // Re-emitting the current status is a no-op, not a refusal — a UI select can send it back unchanged.
    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.InProgress)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public void Re_Emitting_The_Current_Status_Is_Legal(AppointmentStatus status)
    {
        Assert.True(Appointment.CanTransition(status, status));
    }

    // ---- MarkVisitCompleted's three outcomes (AC-P1.12) ---------------------

    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.InProgress)]
    public void MarkVisitCompleted_Closes_An_Open_Visit(AppointmentStatus from) // [AC-P1.12]
    {
        var appointment = AppointmentAt(from);

        var outcome = appointment.MarkVisitCompleted();

        Assert.Equal(VisitCompletionOutcome.Completed, outcome);
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
    }

    // Idempotent — a second staff member filing a record is harmless, and the caller must still clear the
    // post-visit review. Distinguished from Contradicted so the caller can tell them apart.
    [Fact]
    public void MarkVisitCompleted_On_An_Already_Completed_Visit_Is_Idempotent() // [AC-P1.12]
    {
        var appointment = AppointmentAt(AppointmentStatus.Completed);

        var outcome = appointment.MarkVisitCompleted();

        Assert.Equal(VisitCompletionOutcome.AlreadyCompleted, outcome);
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
    }

    // [AC-P1.12] The case that used to be swallowed as an identical silent no-op: a fiche filed against a visit
    // the schedule says did not happen. The status is deliberately left alone — a cancelled visit is never
    // silently reopened — but the caller is told, so it can be logged rather than lost.
    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public void MarkVisitCompleted_Reports_A_Contradiction_Without_Reopening(AppointmentStatus from)
    {
        var appointment = AppointmentAt(from);

        var outcome = appointment.MarkVisitCompleted();

        Assert.Equal(VisitCompletionOutcome.Contradicted, outcome);
        Assert.Equal(from, appointment.Status);
    }

    // MarkVisitCompleted never throws — both callers are post-commit best-effort helpers whose fiche has
    // already committed, and a throw would jump over CancelPostVisitReviewAsync, leaving the post-visit prompt
    // nagging forever. That is the whole reason it returns an outcome instead.
    [Fact]
    public void MarkVisitCompleted_Never_Throws() // [AC-P1.12]
    {
        foreach (var status in ReachableStatuses)
        {
            var appointment = AppointmentAt(status);
            appointment.MarkVisitCompleted();
        }
    }

    // ---- The set the UI reads (AC-P1.6) ------------------------------------

    // The status control and the « Annuler » button derive from this, so it must agree with what the mutators
    // enforce — otherwise the UI offers a transition the server then refuses.
    [Fact]
    public void NextStatusesFrom_Is_Declared_For_Every_Status() // [AC-P1.6]
    {
        foreach (var status in Enum.GetValues<AppointmentStatus>())
        {
            Assert.NotEmpty(Appointment.NextStatusesFrom(status));
        }
    }

    // Every status has a French label — no raw enum name can reach the screen through a refusal message.
    [Fact]
    public void Every_Status_Has_A_French_Label() // [AC-P1.44]
    {
        foreach (var status in Enum.GetValues<AppointmentStatus>())
        {
            var label = Appointment.FrenchLabel(status);
            Assert.NotEqual(status.ToString(), label);
            Assert.False(string.IsNullOrWhiteSpace(label));
        }
    }
}
