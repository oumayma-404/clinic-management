using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DoctorLeaveConfiguration : IEntityTypeConfiguration<DoctorLeave>
{
    public void Configure(EntityTypeBuilder<DoctorLeave> builder)
    {
        builder.ToTable("DoctorLeaves");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .ValueGeneratedNever();

        builder.Property(l => l.ClinicId)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(l => l.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Doctor>()
            .WithMany()
            .HasForeignKey(l => l.DoctorId)
            .OnDelete(DeleteBehavior.Cascade);

        // Calendar days, never instants: a congé is « du 12 au 20 août » in Tunis, whatever the server's zone.
        builder.Property(l => l.StartDate).IsRequired().HasColumnType("date");
        builder.Property(l => l.EndDate).IsRequired().HasColumnType("date");

        builder.Property(l => l.Note)
            .HasMaxLength(DoctorLeave.MaxNoteLength);

        builder.Property(l => l.CreatedAt)
            .IsRequired();

        builder.Property(l => l.UpdatedAt);

        // The booking guard's and the agenda's query shape: one practitioner's congés by start day.
        builder.HasIndex(l => new { l.ClinicId, l.DoctorId, l.StartDate });
    }
}
