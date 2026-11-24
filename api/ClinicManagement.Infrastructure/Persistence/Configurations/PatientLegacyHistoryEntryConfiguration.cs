using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class PatientLegacyHistoryEntryConfiguration : IEntityTypeConfiguration<PatientLegacyHistoryEntry>
{
    public void Configure(EntityTypeBuilder<PatientLegacyHistoryEntry> builder)
    {
        builder.ToTable("PatientLegacyHistoryEntries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.PatientId).IsRequired();

        // Denormalised from the patient for the tenant filter; verify-schema's clinic-matches-patient holds it.
        builder.Property(e => e.ClinicId).IsRequired();
        builder.HasIndex(e => e.ClinicId);

        builder.Property(e => e.Date).HasColumnType("date");
        builder.Property(e => e.Category).IsRequired().HasMaxLength(PatientLegacyHistoryEntry.MaxCategoryLength);
        builder.Property(e => e.Title).IsRequired().HasMaxLength(PatientLegacyHistoryEntry.MaxTitleLength);
        builder.Property(e => e.Details).HasMaxLength(PatientLegacyHistoryEntry.MaxDetailsLength);
        builder.Property(e => e.Position).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();

        // The one read: a patient's lines, newest first.
        builder.HasIndex(e => new { e.PatientId, e.Date });

        // No navigation on Patient: these lines are never part of the aggregate's loads. A patient holding any is
        // refused deletion and merge (PatientLinkedDataCounts), so the cascade only ever runs under ClinicPurge.
        builder.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(e => e.PatientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
