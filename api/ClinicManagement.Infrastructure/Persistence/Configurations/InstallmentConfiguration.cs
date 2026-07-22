using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class InstallmentConfiguration : IEntityTypeConfiguration<Installment>
{
    public void Configure(EntityTypeBuilder<Installment> builder)
    {
        builder.ToTable("Installments");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .ValueGeneratedNever();

        builder.Property(i => i.TreatmentPlanId)
            .IsRequired();

        builder.Property(i => i.DueDate)
            .IsRequired();

        builder.Property(i => i.Amount)
            .HasColumnType("decimal(18,3)");

        builder.Property(i => i.AmountPaid)
            .HasColumnType("decimal(18,3)");

        builder.Property(i => i.LastMethod)
            .HasConversion<int>();

        builder.Property(i => i.LastPaidOn);
    }
}
