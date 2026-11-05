using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class AttendanceEntryConfiguration : IEntityTypeConfiguration<AttendanceEntry>
{
    public void Configure(EntityTypeBuilder<AttendanceEntry> builder)
    {
        builder.ToTable("AttendanceEntries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ClinicId).IsRequired();
        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(e => e.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        // No FK to Users or to the borne: the lines outlive a removed account and a removed borne.
        builder.Property(e => e.UserId).IsRequired().HasMaxLength(255);
        builder.Property(e => e.LocalDay).IsRequired().HasColumnType("date");
        builder.Property(e => e.TerminalId);
        builder.Property(e => e.TerminalName).HasMaxLength(AttendanceTerminal.MaxNameLength);

        builder.Property(e => e.PunchedArrivalUtc);
        builder.Property(e => e.PunchedDepartureUtc);
        builder.Property(e => e.ArrivalUtc).IsRequired();
        builder.Property(e => e.DepartureUtc);
        builder.Property(e => e.DepartureSource).HasConversion<int?>();

        builder.Property(e => e.CorrectedByUserId).HasMaxLength(255);
        builder.Property(e => e.CorrectionReason).HasMaxLength(AttendanceEntry.MaxReasonLength);
        builder.Property(e => e.CorrectedAt);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt);

        builder.Ignore(e => e.IsOpen);
        builder.Ignore(e => e.IsCorrected);

        builder.HasIndex(e => new { e.ClinicId, e.UserId, e.LocalDay });
        builder.HasIndex(e => new { e.ClinicId, e.LocalDay });
    }
}
