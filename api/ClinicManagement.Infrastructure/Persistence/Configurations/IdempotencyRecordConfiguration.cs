using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>The replay keys of a cabinet's saves (D17) — read and written by <see cref="IdempotencyStore"/> in raw SQL.</summary>
public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");

        // The claim is « INSERT … ON CONFLICT DO NOTHING » on this key: two presses of one save meet here.
        builder.HasKey(r => new { r.ClinicId, r.Key });
        builder.Property(r => r.Key).HasMaxLength(IdempotencyRecord.MaxKeyLength);
        builder.Property(r => r.Fingerprint).IsRequired().HasMaxLength(IdempotencyRecord.MaxFingerprintLength);
        builder.Property(r => r.State).HasConversion<int>();
        builder.Property(r => r.ContentType).HasMaxLength(100);

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(r => r.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        // The 48 h purge, run beside each claim.
        builder.HasIndex(r => new { r.ClinicId, r.CreatedAtUtc });
    }
}

/// <summary>The numbers the cloud promised a PC de secours (D16) — written by <see cref="RelayNumberPromiseStore"/>.</summary>
public class RelayNumberPromiseConfiguration : IEntityTypeConfiguration<RelayNumberPromise>
{
    public void Configure(EntityTypeBuilder<RelayNumberPromise> builder)
    {
        builder.ToTable("RelayNumberPromises");

        // A number is promised once per sequence: a repeat (a retry, the same key) only refreshes it.
        builder.HasKey(p => new { p.ClinicId, p.Sequence, p.Number });
        builder.Property(p => p.Sequence).HasMaxLength(20);
        builder.Property(p => p.Number).HasMaxLength(20);
        builder.Property(p => p.IdempotencyKey).HasMaxLength(IdempotencyRecord.MaxKeyLength);

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(p => p.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.PromisedAtUtc);
    }
}
