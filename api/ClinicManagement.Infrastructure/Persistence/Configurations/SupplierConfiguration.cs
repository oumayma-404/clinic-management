using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .ValueGeneratedNever();

        builder.Property(s => s.ClinicId)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(s => s.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(Supplier.MaxNameLength);

        builder.Property(s => s.Category)
            .HasMaxLength(100);

        builder.Property(s => s.PhoneNumber)
            .HasMaxLength(50);

        builder.Property(s => s.Address)
            .HasMaxLength(500);

        builder.Property(s => s.Notes)
            .HasMaxLength(2000);

        builder.Property(s => s.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(s => s.CreatedAt)
            .IsRequired();

        builder.Property(s => s.UpdatedAt);

        // AC-1's backstop. The handler's refusal is accent- and case-insensitive and produces the French message
        // naming the existing record; this catches the race two simultaneous creates would otherwise win.
        builder.HasIndex(s => new { s.ClinicId, s.Name })
            .IsUnique();

        // The catégorie filter, and the DISTINCT behind the picker's options.
        builder.HasIndex(s => new { s.ClinicId, s.Category });
    }
}
