using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class StaffLeaveAllowanceEntryConfiguration : IEntityTypeConfiguration<StaffLeaveAllowanceEntry>
{
    public void Configure(EntityTypeBuilder<StaffLeaveAllowanceEntry> builder)
    {
        builder.ToTable("StaffLeaveAllowanceEntries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ClinicId).IsRequired();
        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(e => e.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        // No FK, like StaffLeaveRequest.UserId: the history outlives the account.
        builder.Property(e => e.UserId).IsRequired().HasMaxLength(255);

        builder.Property(e => e.Kind).IsRequired().HasConversion<int>();
        // The solution's decimal convention (18,3); verify-schema flags any narrower scale.
        builder.Property(e => e.Days).IsRequired();
        builder.Property(e => e.Accrual).HasConversion<int>();
        builder.Property(e => e.CarryOver).IsRequired();
        builder.Property(e => e.OnDay).IsRequired().HasColumnType("date");
        builder.Property(e => e.Reason).HasMaxLength(StaffLeaveAllowanceEntry.MaxReasonLength);

        builder.Property(e => e.CreatedByUserId).IsRequired().HasMaxLength(255);
        builder.Property(e => e.CreatedByName).IsRequired().HasMaxLength(StaffLeaveAllowanceEntry.MaxNameLength);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.CancelledAt);
        builder.Property(e => e.CancelledByName).HasMaxLength(StaffLeaveAllowanceEntry.MaxNameLength);
        builder.Property(e => e.CancelReason).HasMaxLength(StaffLeaveAllowanceEntry.MaxReasonLength);

        builder.Ignore(e => e.IsCancelled);

        builder.HasIndex(e => new { e.ClinicId, e.UserId, e.OnDay });
    }
}
