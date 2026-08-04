using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedNever();

        // Nullable, and with **no foreign key to Clinics** — the two go together. An audit row must outlive what
        // it describes: a cascade from `Clinics` would delete the record of a clinic's own history along with it,
        // and a restrict would make deleting a clinic impossible. Evidence does not hang off its subject.
        builder.Property(a => a.ClinicId);

        builder.Property(a => a.UserId)
            .IsRequired()
            .HasMaxLength(200);

        // Denormalised, not an FK to `Users`, for the same reason: the ledger's most valuable rows are the ones
        // about people whose accounts are gone.
        builder.Property(a => a.UserEmail)
            .HasMaxLength(320);

        builder.Property(a => a.EntityType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(a => a.EntityId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.Action)
            .IsRequired()
            .HasConversion<int>();

        // Unbounded text; `AuditEntry` caps the value it accepts, so the column never needs to refuse one.
        builder.Property(a => a.ChangedFields);

        builder.Property(a => a.OccurredAt)
            .IsRequired();

        // The screen's query shape: one clinic's ledger, newest first, optionally within a window.
        builder.HasIndex(a => new { a.ClinicId, a.OccurredAt });

        // « Tout ce qui est arrivé à CE dossier » — the second question the ledger is opened for, and the one a
        // clinic-and-date index cannot serve without scanning the clinic's whole history.
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
    }
}
