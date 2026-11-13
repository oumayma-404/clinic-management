using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// The cabinet's paired cameras (<c>features/camera-photo-import</c>). A real <c>ClinicId</c> with a cascading
/// foreign key, so the tenant filter covers it and a deleted cabinet takes its cameras with it.
///
/// <para>⚠️ <b><c>DoctorId</c> carries no foreign key</b>, deliberately — the <c>MedicalDocument.DentalRecordId</c>
/// shape: a shared camera belongs to nobody, and a retired doctor must not take the camera's history with them.</para>
/// </summary>
public class ClinicCameraConfiguration : IEntityTypeConfiguration<ClinicCamera>
{
    public void Configure(EntityTypeBuilder<ClinicCamera> builder)
    {
        builder.ToTable("ClinicCameras");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .ValueGeneratedNever();

        builder.Property(c => c.ClinicId)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(c => c.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(c => c.Fingerprint)
            .IsRequired()
            .HasMaxLength(ClinicCamera.FingerprintLength);

        builder.Property(c => c.Make)
            .IsRequired()
            .HasMaxLength(ClinicCamera.MaxMakeLength);

        builder.Property(c => c.Model)
            .IsRequired()
            .HasMaxLength(ClinicCamera.MaxModelLength);

        builder.Property(c => c.SerialSuffix)
            .HasMaxLength(ClinicCamera.MaxSerialSuffixLength);

        builder.Property(c => c.Label)
            .IsRequired()
            .HasMaxLength(ClinicCamera.MaxLabelLength);

        builder.Property(c => c.DoctorId);

        builder.Property(c => c.ClockOffsetMinutes)
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .IsRequired();

        // One registry entry per body per cabinet: two PCs pairing the same camera at once must converge on one row.
        builder.HasIndex(c => new { c.ClinicId, c.Fingerprint })
            .IsUnique();

        builder.HasIndex(c => c.DoctorId);
    }
}
