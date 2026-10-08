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
