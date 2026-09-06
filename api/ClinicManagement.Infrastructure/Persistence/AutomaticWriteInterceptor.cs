using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Keeps <see cref="Appointment.VersionBeforeAutoAdvance"/> true, so that a write <b>no person made</b> stops
/// spending the concurrency token a person is holding.
///
/// <para><b>The defect this closes.</b> <c>AppointmentProgressJob</c> runs minutely and writes an appointment's
/// status at its slot's start minute and again at its end minute. The token is <c>xmin</c>, which is per-row, so
/// those writes age the version every open edit form is holding — and the refusal they produce says « cet
/// enregistrement a été modifié par quelqu'un d'autre », which is false and unactionable. Production, 2026-09-05:
/// the form read the row at 15:50:04, the job wrote it at 15:50:05, the user pressed Enregistrer at 15:50:32.</para>
///
/// <para><b>Why an interceptor and not a call at each writer.</b> <see cref="AuditSaveChangesInterceptor"/>'s
/// argument exactly, and this repository's most expensive recurring defect is the other choice: a rule wired to
/// one call site out of several, staying correct where it was written while every other writer quietly disagrees.
/// A save-changes interceptor sees every save by construction. Adding a new automatic writer of an appointment
/// needs no knowledge of this file, and adding a new human one cannot forget to invalidate the marker.</para>
///
/// <para><b>The discriminator is the audit actor</b>, not a flag a caller passes: a background job declares itself
/// with <c>RunAs</c> and is recorded as <c>job|&lt;name&gt;</c>, which is the same fact « was anybody looking at a
/// form when this happened? » needs. Reusing it means the ledger and this marker can never disagree about who
/// wrote a row. The vendor's console and an archive restore are deliberately <b>not</b> automatic — both have a
/// person behind them (see <c>AuditActor.ConsolePrefix</c>), and forgiving their writes would let a stale form
/// overwrite one.</para>
/// </summary>
public class AutomaticWriteInterceptor : SaveChangesInterceptor
{
    private readonly IAuditActorProvider _actorProvider;

    public AutomaticWriteInterceptor(IAuditActorProvider actorProvider)
    {
        _actorProvider = actorProvider;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Runs before the UPDATE is generated, so a value written here is part of the same statement and the same
    /// transaction as the change that justified it — there is no window in which the row and its marker disagree.
    /// </summary>
    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var automatic = _actorProvider.Current.IsProcess;

        foreach (var entry in context.ChangeTracker.Entries<Appointment>())
        {
            // `Added` is left alone: a new row has no version anybody could be holding, and its marker is null.
            // `Deleted` likewise — there is nothing left to save against.
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var marker = entry.Property(a => a.VersionBeforeAutoAdvance);
            var next = MarkerAfterWrite(automatic, marker.CurrentValue, entry.Entity.Version);

            // Compared before assigning rather than written unconditionally: an assignment marks the property
            // modified, which would put this column into the UPDATE — and into the audit row's changed-field
            // summary — on every ordinary save of an appointment, saying a field changed that did not.
            if (next != marker.CurrentValue)
            {
                marker.CurrentValue = next;
            }
        }
    }

    /// <summary>
    /// The marker a row should carry after this write. The whole rule, as one expression, so the tests exercise
    /// the production decision rather than a restatement of it.
    /// </summary>
    /// <param name="automatic">Was the writer a process rather than a person?</param>
    /// <param name="current">The marker the row carries now.</param>
    /// <param name="version">The row's version — what a person could be holding right now.</param>
    public static uint? MarkerAfterWrite(bool automatic, uint? current, uint version) =>
        automatic
            // `current ??`, so a RUN of automatic writes keeps pointing at the version the last person saw. Two in
            // a row is the routine case and not an edge one: a visit advances Scheduled → InProgress at its start
            // minute and InProgress → AwaitingClosure at its end, and a form open across both has to survive both.
            // Recording the second would forgive only the last minute of a stale form's life.
            ? current ?? version
            // A person wrote the row, so every version older than this one is now genuinely somebody's work.
            : null;
}
