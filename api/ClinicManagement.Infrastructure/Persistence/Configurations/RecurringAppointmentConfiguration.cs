using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class RecurringAppointmentConfiguration : IEntityTypeConfiguration<RecurringAppointment>
{
    public void Configure(EntityTypeBuilder<RecurringAppointment> builder)
    {
        builder.ToTable("RecurringAppointments");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedNever();

        builder.Property(r => r.ClinicId)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(r => r.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.ClinicId);
        builder.HasIndex(r => r.PatientId);

        builder.HasOne(r => r.Patient)
            .WithMany()
            .HasForeignKey(r => r.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(r => r.StartDate)
            .IsRequired();

        builder.Property(r => r.EndDate);

        builder.Property(r => r.OccurrenceCount);

        builder.Property(r => r.Duration)
            .IsRequired()
            .HasConversion(
                v => v.Ticks,
                v => TimeSpan.FromTicks(v));

        builder.Property(r => r.RecurrencePattern)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(r => r.Interval)
            .IsRequired();

        builder.Property(r => r.DoctorName)
            .HasMaxLength(200);

        builder.Property(r => r.Notes)
            .HasColumnType("text");

        builder.Property(r => r.IsActive)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .IsRequired();
    }
}
