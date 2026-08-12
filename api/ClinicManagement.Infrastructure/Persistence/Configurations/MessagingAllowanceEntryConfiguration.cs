using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// ⚠️ <see cref="MessagingAllowanceEntry.Amount"/> carries <b>no</b> <c>HasColumnType</c>/<c>HasPrecision</c>:
/// <c>ConfigureConventions</c> applies <c>(18,3)</c> model-wide, and an explicit annotation would be reported as drift
/// by <c>verify-schema</c> — and would additionally bypass the convention, which is the trap AC-P4.37 documents.
/// </summary>
public class MessagingAllowanceEntryConfiguration : IEntityTypeConfiguration<MessagingAllowanceEntry>
{
    public void Configure(EntityTypeBuilder<MessagingAllowanceEntry> builder)
    {
        builder.ToTable("MessagingAllowanceEntries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.ClinicId)
            .IsRequired();

        builder.Property(e => e.Kind)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.Messages)
            .IsRequired();

        // Fixed-length rather than free text: the key is `AAAA-MM` and nothing else, and the fixed width is what
        // makes the ordinal comparisons the fold and the SQL predicates rely on chronological (D-7).
        builder.Property(e => e.EffectiveMonth)
            .HasMaxLength(MessagingAllowanceEntry.MonthKeyLength)
            .IsRequired();

        builder.Property(e => e.Method)
            .HasConversion<int>();

        builder.Property(e => e.Reference)
            .HasMaxLength(MessagingAllowanceEntry.MaxReferenceLength);

        builder.Property(e => e.Note)
            .HasMaxLength(MessagingAllowanceEntry.MaxNoteLength);

        builder.Property(e => e.RecordedBy)
            .HasMaxLength(MessagingAllowanceEntry.MaxActorLength);

        builder.Property(e => e.CancelledBy)
            .HasMaxLength(MessagingAllowanceEntry.MaxActorLength);

        builder.Property(e => e.CancelReason)
            .HasMaxLength(MessagingAllowanceEntry.MaxCancelReasonLength);

        builder.Property(e => e.RecordedAtUtc)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        // The console's history read: one cabinet's entries newest-first.
        builder.HasIndex(e => new { e.ClinicId, e.RecordedAtUtc });

        // The fold's own read: « every entry of this cabinet effective on or before month M ». Separate from the
        // index above because the two answer different questions — one orders by when it was recorded, the other
        // narrows by which month it reaches, and the fold's ordering tie-break is `RecordedAtUtc` *within* a month.
        builder.HasIndex(e => new { e.ClinicId, e.EffectiveMonth });

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(e => e.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
