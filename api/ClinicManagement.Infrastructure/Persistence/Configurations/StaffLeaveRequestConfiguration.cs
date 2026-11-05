using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class StaffLeaveRequestConfiguration : IEntityTypeConfiguration<StaffLeaveRequest>
{
    public void Configure(EntityTypeBuilder<StaffLeaveRequest> builder)
    {
        builder.ToTable("StaffLeaveRequests");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.ClinicId).IsRequired();
        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(l => l.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        // 255 like every other User.Id column, and no FK: a congé history outlives the account it was asked for.
        builder.Property(l => l.UserId).IsRequired().HasMaxLength(255);

        builder.Property(l => l.Type).IsRequired().HasConversion<int>();
        builder.Property(l => l.Status).IsRequired().HasConversion<int>();

        // Calendar days, never instants: « du 10 au 12 novembre » in Tunis, whatever the server's zone.
        builder.Property(l => l.StartDate).IsRequired().HasColumnType("date");
        builder.Property(l => l.EndDate).IsRequired().HasColumnType("date");

        builder.Property(l => l.HalfDay).IsRequired();
        builder.Property(l => l.Reason).IsRequired().HasMaxLength(StaffLeaveRequest.MaxReasonLength);
        builder.Property(l => l.RequestedDays).IsRequired();
        builder.Property(l => l.CountedDays);

        builder.Property(l => l.DecidedByUserId).HasMaxLength(255);
        builder.Property(l => l.DecidedAt);
        builder.Property(l => l.RejectionReason).HasMaxLength(StaffLeaveRequest.MaxReasonLength);
        builder.Property(l => l.CancelledAt);
        builder.Property(l => l.CancelledByUserId).HasMaxLength(255);

        builder.Property(l => l.EnteredByAdmin).IsRequired();
        builder.Property(l => l.CreatedAt).IsRequired();
        builder.Property(l => l.UpdatedAt);

        builder.Ignore(l => l.HoldsItsDays);

        builder.HasIndex(l => new { l.ClinicId, l.Status });
        builder.HasIndex(l => new { l.ClinicId, l.UserId, l.StartDate });
    }
}
