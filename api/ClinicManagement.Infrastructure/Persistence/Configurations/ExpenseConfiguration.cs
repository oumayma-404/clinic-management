using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> builder)
    {
        builder.ToTable("Expenses");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.ClinicId)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(e => e.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        // Clinic + date is the caisse query shape (a clinic's expenses within a day/range).
        builder.HasIndex(e => new { e.ClinicId, e.ExpenseDate });

        builder.Property(e => e.ExpenseDate)
            .IsRequired();

        builder.Property(e => e.Category)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.Amount)
            .IsRequired();

        builder.Property(e => e.Method)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        // ⚠️ `SetNull`, never `Cascade`. This column says where the row CAME FROM; the money is the row's own.
        // A cascade would let removing a monthly commitment delete every dépense it ever posted — silently
        // raising the reported Net of every period the series ran through.
        builder.HasOne<RecurringExpense>()
            .WithMany()
            .HasForeignKey(e => e.RecurringExpenseId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt);
    }
}
