using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("Appointments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedNever();

        builder.Property(a => a.PatientId)
            .IsRequired();

        builder.Property(a => a.AppointmentDateTime)
            .IsRequired();

        builder.Property(a => a.Duration)
            .IsRequired()
            .HasConversion(
                v => v.Ticks,
                v => TimeSpan.FromTicks(v));

        builder.Property(a => a.DoctorName)
            .HasMaxLength(200);

        builder.Property(a => a.Notes)
            .HasColumnType("text");

        builder.Property(a => a.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(a => a.RecurringAppointmentId);

        builder.Property(a => a.CancellationReason)
            .HasMaxLength(500);

        builder.Property(a => a.CancelledAt);

        builder.Property(a => a.GoogleCalendarEventId)
            .HasMaxLength(500);

        builder.Property(a => a.CreatedAt)
            .IsRequired();

        builder.Property(a => a.UpdatedAt);

        builder.HasOne(a => a.Patient)
            .WithMany(p => p.Appointments)
            .HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}



