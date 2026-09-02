using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class RecurringExpenseConfiguration : IEntityTypeConfiguration<RecurringExpense>
{
    public void Configure(EntityTypeBuilder<RecurringExpense> builder)
    {
        builder.ToTable("RecurringExpenses");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedNever();

        builder.Property(r => r.ClinicId)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(r => r.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        // The posting pass's own read: every clinic's ACTIVE series. Filtered so the index stays the size of the
        // live set rather than of every commitment a practice has ever ended.
        builder.HasIndex(r => new { r.ClinicId, r.CancelledAt })
            .HasFilter("\"CancelledAt\" IS NULL");

        builder.Property(r => r.Category)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.Amount)
            .IsRequired();

        builder.Property(r => r.Method)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(r => r.Description)
            .HasMaxLength(1000);

        builder.Property(r => r.DayOfMonth)
            .IsRequired();

        // `AAAA-MM`. A string rather than a date because it is a MONTH, and a date column invites a reader to
        // compare it against a day — `ClinicClock`'s month key is the vocabulary the rest of the product uses.
        builder.Property(r => r.LastPostedMonth)
            .IsRequired()
            .HasMaxLength(7);

        builder.Property(r => r.CancelledAt);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.UpdatedAt);
    }
}
