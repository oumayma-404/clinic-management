using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class PlatformNewsReadMarkerConfiguration : IEntityTypeConfiguration<PlatformNewsReadMarker>
{
    public void Configure(EntityTypeBuilder<PlatformNewsReadMarker> builder)
    {
        builder.ToTable("PlatformNewsReadMarkers");

        builder.HasKey(m => new { m.ClinicId, m.UserId });
        builder.HasIndex(m => m.UserId).IsUnique();

        builder.Property(m => m.UserId)
            .IsRequired()
            .HasMaxLength(PlatformNewsReadMarker.UserIdMaxLength);

        builder.Property(m => m.ReadUpToUtc)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(m => m.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
