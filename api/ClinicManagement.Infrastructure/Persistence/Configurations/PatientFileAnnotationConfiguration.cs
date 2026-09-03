using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class PatientFileAnnotationConfiguration : IEntityTypeConfiguration<PatientFileAnnotation>
{
    public void Configure(EntityTypeBuilder<PatientFileAnnotation> builder)
    {
        builder.ToTable("PatientFileAnnotations");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedNever();

        builder.Property(a => a.PatientFileId)
            .IsRequired();

        // Denormalised from the file so this clinical child carries a global query filter of its own — see
        // ApplicationDbContext.OnModelCreating. The two must agree.
        builder.Property(a => a.ClinicId)
            .IsRequired();

        builder.HasIndex(a => a.ClinicId);

        // The one read this table has is « every marker on this file », so that is the index it gets.
        builder.HasIndex(a => a.PatientFileId);

        // ⚠️ `double`, not `decimal`. These are geometry read out of a float32 mesh, not money: the file itself
        // only ever held single precision, so a decimal column would store spurious exactness and cost the
        // arithmetic that a viewer does on every frame anyway.
        builder.Property(a => a.X).IsRequired();
        builder.Property(a => a.Y).IsRequired();
        builder.Property(a => a.Z).IsRequired();
        builder.Property(a => a.NormalX).IsRequired();
        builder.Property(a => a.NormalY).IsRequired();
        builder.Property(a => a.NormalZ).IsRequired();

        builder.Property(a => a.Label)
            .IsRequired()
            .HasMaxLength(PatientFileAnnotation.MaxLabelLength);

        builder.Property(a => a.CreatedAt)
            .IsRequired();

        builder.Property(a => a.UpdatedAt);

        builder.Property(a => a.CreatedBy)
            .HasMaxLength(256);

        // ⚠️ Cascade, and it is the right answer here rather than the lazy one: a marker points at a surface,
        // so when the file goes the marker is not orphaned data to preserve — it is a pin with nothing behind
        // it. `Restrict` would make deleting an annotated model fail with a foreign-key error a dentist can
        // neither read nor act on.
        builder.HasOne(a => a.File)
            .WithMany()
            .HasForeignKey(a => a.PatientFileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
