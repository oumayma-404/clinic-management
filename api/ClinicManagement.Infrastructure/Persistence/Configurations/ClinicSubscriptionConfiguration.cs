using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// ⚠️ <b>No money annotation.</b> There is no <c>decimal</c> here at all, and if one is ever added it must carry
/// no <c>HasColumnType</c>/<c>HasPrecision</c>: <c>ConfigureConventions</c> applies <c>(18,3)</c> model-wide and an
/// explicit annotation is reported as drift by <c>verify-schema</c>.
///
/// <para><see cref="ClinicSubscription.EndsOn"/> is a calendar day and is mapped as <c>timestamp with time
/// zone</c> like every other calendar-day column in this model (<c>Payment.ChequeDueDate</c>,
/// <c>Installment.DueDate</c>, <c>Payment.PaidOn</c>) — see the migration for why <c>date</c> was not introduced
/// here.</para>
/// </summary>
public class ClinicSubscriptionConfiguration : IEntityTypeConfiguration<ClinicSubscription>
{
    public void Configure(EntityTypeBuilder<ClinicSubscription> builder)
    {
        builder.ToTable("ClinicSubscriptions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .ValueGeneratedNever();

        builder.Property(s => s.ClinicId)
            .IsRequired();

        builder.Property(s => s.Plan)
            .HasConversion<int>();

        builder.Property(s => s.SuspensionReason)
            .HasMaxLength(ClinicSubscription.MaxSuspensionReasonLength);

        builder.Property(s => s.SuspendedBy)
            .HasMaxLength(ClinicSubscription.MaxActorLength);

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        // UNIQUE: « one entitlement per cabinet » held by the database rather than by whichever door happens to
        // check first. Two rows would make the gate's answer depend on which one it read.
        builder.HasIndex(s => s.ClinicId)
            .IsUnique();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(s => s.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
