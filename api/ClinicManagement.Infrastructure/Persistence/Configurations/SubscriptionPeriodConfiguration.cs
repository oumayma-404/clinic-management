using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// ⚠️ <see cref="SubscriptionPeriod.Amount"/> carries <b>no</b> <c>HasColumnType</c>/<c>HasPrecision</c>:
/// <c>ConfigureConventions</c> applies <c>(18,3)</c> model-wide, and an explicit annotation would be reported as
/// drift by <c>verify-schema</c> — and would additionally bypass the convention (an explicit store type is
/// returned verbatim), which is the trap AC-P4.37 documents.
/// </summary>
public class SubscriptionPeriodConfiguration : IEntityTypeConfiguration<SubscriptionPeriod>
{
    public void Configure(EntityTypeBuilder<SubscriptionPeriod> builder)
    {
        builder.ToTable("SubscriptionPeriods");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .ValueGeneratedNever();

        builder.Property(p => p.ClinicId)
            .IsRequired();

        builder.Property(p => p.Kind)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(p => p.Method)
            .HasConversion<int>();

        builder.Property(p => p.Reference)
            .HasMaxLength(SubscriptionPeriod.MaxReferenceLength);

        builder.Property(p => p.Note)
            .HasMaxLength(SubscriptionPeriod.MaxNoteLength);

        builder.Property(p => p.RecordedBy)
            .HasMaxLength(SubscriptionPeriod.MaxActorLength);

        builder.Property(p => p.CancelledBy)
            .HasMaxLength(SubscriptionPeriod.MaxActorLength);

        builder.Property(p => p.CancelReason)
            .HasMaxLength(SubscriptionPeriod.MaxCancelReasonLength);

        builder.Property(p => p.RecordedAtUtc)
            .IsRequired();

        builder.Property(p => p.RecordedOnClinicDay)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        // The ledger read's only index, and the order the fold depends on: every entry of one cabinet, oldest
        // first. `Id` is the tie-break the read adds — two grants recorded in the same tick must fold in a
        // stable order or `EndsOn` would depend on which row PostgreSQL returned first.
        builder.HasIndex(p => new { p.ClinicId, p.RecordedAtUtc });

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(p => p.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
