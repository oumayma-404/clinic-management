using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// The recovery-point ledger's schema (<c>clinic-recovery-points</c>), on
/// <see cref="BackupRunConfiguration"/>'s shape.
/// </summary>
public class ClinicRecoveryPointConfiguration : IEntityTypeConfiguration<ClinicRecoveryPoint>
{
    public void Configure(EntityTypeBuilder<ClinicRecoveryPoint> builder)
    {
        builder.ToTable("ClinicRecoveryPoints");

        builder.HasKey(p => p.Id);

        // ValueGeneratedNever, and it is load-bearing beyond the usual convention: without it EF marks the key
        // ValueGenerated.OnAdd, and `ClinicArchiveStore.ArchivedProperties` excludes such properties — the trap that
        // once made three tables silently unrestorable. This table is excluded from the archive anyway, so it cannot
        // bite here; stating it keeps the convention true of every configuration rather than of most of them.
        builder.Property(p => p.Id).ValueGeneratedNever();

        // A real FK with a cascade, as the backup ledger has: « quand ai-je pu restaurer pour la dernière fois ? » is
        // meaningless once the cabinet is gone, and these rows are an operational log of a machine task rather than
        // evidence that must outlive its subject (the audit ledger's rule, which is why that one has no FK).
        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(p => p.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(p => p.StartedAt).IsRequired();
        builder.Property(p => p.CompletedAt);
        builder.Property(p => p.Outcome).IsRequired().HasConversion<int>();
        builder.Property(p => p.Contents).IsRequired().HasConversion<int>();

        // A tenant-prefixed storage key (`clinics/{id}/recovery-points/…`), not a filesystem path — bounded well
        // inside what the object store accepts, and long enough that a future prefix change needs no migration.
        builder.Property(p => p.StorageKey).HasMaxLength(512);

        builder.Property(p => p.SizeBytes);
        builder.Property(p => p.TableCount);
        builder.Property(p => p.RowCount);

        // Unbounded, for BackupRunConfiguration's reason: truncating the one field that explains a failure is how a
        // failure becomes unactionable.
        builder.Property(p => p.Error);

        // The one shape every read here has: a cabinet's points, newest first — the list, the due-check, the last
        // success (with an Outcome predicate) and the prune all come off this.
        builder.HasIndex(p => new { p.ClinicId, p.StartedAt });
    }
}
