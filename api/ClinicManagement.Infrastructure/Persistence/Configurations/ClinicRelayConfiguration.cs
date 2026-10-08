using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>The PC de secours rows and the clinic change log (<c>clinic-pc-copy</c>).</summary>
public class ClinicRelayConfiguration : IEntityTypeConfiguration<ClinicRelay>
{
    public void Configure(EntityTypeBuilder<ClinicRelay> builder)
    {
        builder.ToTable("ClinicRelays");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(r => r.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(r => r.Label).IsRequired().HasMaxLength(120);
        builder.Property(r => r.Status).HasConversion<int>();
        builder.Property(r => r.RetiredReason).HasConversion<int?>();
        builder.Property(r => r.CreatedByUserId).IsRequired().HasMaxLength(128);
        builder.Property(r => r.RetiredByUserId).HasMaxLength(128);
        builder.Property(r => r.ReclaimedByUserId).HasMaxLength(128);
        builder.Property(r => r.PairingCodeHash).HasMaxLength(64);
        builder.Property(r => r.SecretHash).HasMaxLength(64);
        builder.Property(r => r.PublicKey).HasMaxLength(2048);
        builder.Property(r => r.CertificateFingerprint).HasMaxLength(64);
        builder.Property(r => r.LanAddresses).HasMaxLength(600);
        builder.Property(r => r.GatewayAddress).HasMaxLength(64);
        builder.Property(r => r.PublicAddress).HasMaxLength(64);
        builder.Property(r => r.Build).HasMaxLength(ClinicRelay.MaxBuildLength);
        builder.Property(r => r.MismatchTables).HasMaxLength(ClinicRelay.MaxMismatchLength);
        builder.Property(r => r.LastError).HasMaxLength(ClinicRelay.MaxErrorLength);

        // One PC de secours per clinic (AC-1.10, EC-8): a second concurrent setup collides here.
        builder.HasIndex(r => r.ClinicId)
            .IsUnique()
            .HasFilter($"\"Status\" <> {(int)ClinicRelayStatus.Retired}");

        builder.HasIndex(r => r.PairingCodeHash).IsUnique().HasFilter("\"PairingCodeHash\" IS NOT NULL");
        builder.HasIndex(r => r.SecretHash).IsUnique().HasFilter("\"SecretHash\" IS NOT NULL");
    }
}

public class ClinicChangeConfiguration : IEntityTypeConfiguration<ClinicChange>
{
    public void Configure(EntityTypeBuilder<ClinicChange> builder)
    {
        builder.ToTable("ClinicChanges");

        builder.HasKey(c => new { c.ClinicId, c.Seq });
        builder.Property(c => c.Seq).ValueGeneratedNever();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(c => c.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Property(c => c.Table).IsRequired().HasMaxLength(128);
        builder.Property(c => c.EntityKey).IsRequired().HasMaxLength(300);
        builder.Property(c => c.Op).HasConversion<int>();
        builder.Property(c => c.Origin).HasConversion<int>();
        builder.Property(c => c.IdempotencyKey).HasMaxLength(100);

        // « Which keys did the cloud change after seq N? » (handback) and the per-key latest read of the feed.
        builder.HasIndex(c => new { c.ClinicId, c.Table, c.EntityKey });
    }
}

public class RelayIncidentConfiguration : IEntityTypeConfiguration<RelayIncident>
{
    public void Configure(EntityTypeBuilder<RelayIncident> builder)
    {
        builder.ToTable("RelayIncidents");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Kind).HasConversion<int>();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(i => i.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ClinicRelay>()
            .WithMany()
            .HasForeignKey(i => i.RelayId)
            .OnDelete(DeleteBehavior.Cascade);

        // One open episode per (PC, problem): the database, not the watcher, is what makes « e-mailed once » hold.
        builder.HasIndex(i => new { i.RelayId, i.Kind })
            .IsUnique()
            .HasFilter("\"EndedAtUtc\" IS NULL");

        builder.HasIndex(i => new { i.ClinicId, i.StartedAtUtc });
    }
}

public class RelayReviewItemConfiguration : IEntityTypeConfiguration<RelayReviewItem>
{
    public void Configure(EntityTypeBuilder<RelayReviewItem> builder)
    {
        builder.ToTable("RelayReviewItems");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Kind).HasConversion<int>();
        builder.Property(i => i.Table).IsRequired().HasMaxLength(RelayReviewItem.MaxTableLength);
        builder.Property(i => i.EntityKey).IsRequired().HasMaxLength(RelayReviewItem.MaxKeyLength);
        builder.Property(i => i.CloudEntityKey).HasMaxLength(RelayReviewItem.MaxKeyLength);
        builder.Property(i => i.CloudChangedBy).HasMaxLength(RelayReviewItem.MaxAuthorLength);
        builder.Property(i => i.CabinetChangedBy).HasMaxLength(RelayReviewItem.MaxAuthorLength);
        builder.Property(i => i.ReviewedByUserId).HasMaxLength(128);

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(i => i.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ClinicRelay>()
            .WithMany()
            .HasForeignKey(i => i.RelayId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => new { i.ClinicId, i.ReviewedAtUtc });
        builder.HasIndex(i => new { i.RelayId, i.CutSinceUtc });
    }
}

public class ClinicChangeCursorConfiguration : IEntityTypeConfiguration<ClinicChangeCursor>
{
    public void Configure(EntityTypeBuilder<ClinicChangeCursor> builder)
    {
        builder.ToTable("ClinicChangeCursors");

        builder.HasKey(c => c.ClinicId);
        builder.Property(c => c.ClinicId).ValueGeneratedNever();
        // AC-9.4: system identifier, timeline and database OID — three integers and two dashes.
        builder.Property(c => c.Epoch).HasMaxLength(100);

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(c => c.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
