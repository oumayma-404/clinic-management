using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class LabWorkOrderConfiguration : IEntityTypeConfiguration<LabWorkOrder>
{
    public void Configure(EntityTypeBuilder<LabWorkOrder> builder)
    {
        builder.ToTable("LabWorkOrders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .ValueGeneratedNever();

        builder.Property(o => o.ClinicId)
            .IsRequired();

        builder.Property(o => o.PatientId)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(o => o.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.Patient)
            .WithMany()
            .HasForeignKey(o => o.PatientId)
            .OnDelete(DeleteBehavior.Cascade);

        // Clinic + status is the board query shape (a clinic's orders grouped by lab stage).
        builder.HasIndex(o => new { o.ClinicId, o.Status });
        builder.HasIndex(o => o.PatientId);

        builder.Property(o => o.ToothNumber);

        builder.Property(o => o.Prosthetist)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(o => o.WorkDescription)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(o => o.SentDate);
        builder.Property(o => o.ExpectedDate);
        builder.Property(o => o.ReceivedDate);

        builder.Property(o => o.Cost);

        builder.Property(o => o.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(o => o.Notes)
            .HasMaxLength(1000);

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        builder.Property(o => o.UpdatedAt);
    }
}
