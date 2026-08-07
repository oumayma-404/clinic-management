using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// The backup ledger's schema (L4d).
/// </summary>
public class BackupRunConfiguration : IEntityTypeConfiguration<BackupRun>
{
    public void Configure(EntityTypeBuilder<BackupRun> builder)
    {
        builder.ToTable("BackupRuns");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        // A real FK with a cascade, unlike AuditEntries': the value of a backup row is entirely about a clinic
        // that still exists — « quand ai-je sauvegardé pour la dernière fois ? » is meaningless once the clinic
        // is gone, and evidence-outlives-its-subject (the audit ledger's rule) does not apply to an operational
        // log of a machine task.
        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(b => b.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(b => b.StartedAt).IsRequired();
        builder.Property(b => b.CompletedAt);
        builder.Property(b => b.Outcome).IsRequired().HasConversion<int>();

        // Windows' own MAX_PATH is 260, but a UNC destination on a NAS legitimately runs longer, and this is a
        // recorded fact rather than an input to validate.
        builder.Property(b => b.DestinationPath).HasMaxLength(1000);

        builder.Property(b => b.SizeBytes);
        builder.Property(b => b.VerifiedObjectCount);

        // Unbounded: the reason is a pg_dump/pg_restore stderr tail plus a French sentence, and truncating the
        // one field that explains a failure is how a failure becomes unactionable.
        builder.Property(b => b.Error);

        builder.Property(b => b.Trigger).IsRequired().HasMaxLength(20);

        // The three reads' one shape: a clinic's runs, newest first — the history page, « dernière sauvegarde
        // réussie » (with an Outcome predicate) and the staleness check all come off this.
        builder.HasIndex(b => new { b.ClinicId, b.StartedAt });
    }
}
