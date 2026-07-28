using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// An act's material list (AC-P4.9). A child of <see cref="ProcedureType"/>, so it inherits the parent's clinic
/// scoping and needs no <c>ClinicId</c> or query filter of its own.
/// </summary>
public class ProcedureTypeMaterialConfiguration : IEntityTypeConfiguration<ProcedureTypeMaterial>
{
    public void Configure(EntityTypeBuilder<ProcedureTypeMaterial> builder)
    {
        builder.ToTable("ProcedureTypeMaterials");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.ProcedureTypeId).IsRequired();
        builder.Property(m => m.StockItemId).IsRequired();
        builder.Property(m => m.QuantityPerAct).IsRequired();
        builder.Property(m => m.CreatedAt).IsRequired();

        builder.HasOne<ProcedureType>()
            .WithMany(p => p.Materials)
            .HasForeignKey(m => m.ProcedureTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not Cascade: deleting a stock item must not silently strip it out of the acts that consume
        // it — the act's list would quietly become wrong and nobody would be told. The delete is refused so the
        // admin removes it from the lists first.
        builder.HasOne<StockItem>()
            .WithMany()
            .HasForeignKey(m => m.StockItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // One line per (act, item) — the aggregate refuses a duplicate; this is the database saying the same.
        builder.HasIndex(m => new { m.ProcedureTypeId, m.StockItemId }).IsUnique();
    }
}
