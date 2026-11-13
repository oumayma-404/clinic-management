using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// One row per clinic PC holding camera originals (<c>features/camera-photo-import</c>): a real <c>ClinicId</c> with a
/// cascading foreign key, so the tenant filter covers it and a deleted cabinet takes its reports with it.
/// </summary>
public class PhotoOriginalsCopyConfiguration : IEntityTypeConfiguration<PhotoOriginalsCopy>
{
    public void Configure(EntityTypeBuilder<PhotoOriginalsCopy> builder)
    {
        builder.ToTable("PhotoOriginalsCopies");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .ValueGeneratedNever();

        builder.Property(c => c.ClinicId)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(c => c.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(c => c.HeldBy)
            .IsRequired()
            .HasMaxLength(PatientFile.MaxOriginalHolderLength);

        builder.HasIndex(c => new { c.ClinicId, c.HeldBy })
            .IsUnique();
    }
}
