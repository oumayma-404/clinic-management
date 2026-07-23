using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class WaitingListEntryConfiguration : IEntityTypeConfiguration<WaitingListEntry>
{
    public void Configure(EntityTypeBuilder<WaitingListEntry> builder)
    {
        builder.ToTable("WaitingListEntries");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Id)
            .ValueGeneratedNever();

        builder.Property(w => w.ClinicId)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(w => w.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(w => w.PatientId)
            .IsRequired();

        builder.HasOne(w => w.Patient)
            .WithMany()
            .HasForeignKey(w => w.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Clinic + status is the salle d'attente query shape (a clinic's active waiting entries).
        builder.HasIndex(w => new { w.ClinicId, w.Status });
        builder.HasIndex(w => w.PatientId);

        builder.Property(w => w.PreferredDoctorId);

        builder.Property(w => w.Priority)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(w => w.DesiredTimeframe)
            .HasMaxLength(200);

        builder.Property(w => w.Note)
            .HasMaxLength(1000);

        builder.Property(w => w.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(w => w.ResultingAppointmentId);

        builder.Property(w => w.CreatedAt)
            .IsRequired();

        builder.Property(w => w.UpdatedAt);
    }
}
