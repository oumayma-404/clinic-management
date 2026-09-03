using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// ⚠️ <b>A real <c>ClinicId</c> with a cascading foreign key</b>, unlike <c>ClinicSignup</c>: an upload in flight
/// belongs to a cabinet, so it takes the tenant query filter like everything else that cabinet owns — and a
/// deleted clinic takes its half-finished uploads with it rather than leaving rows nothing can reach.
/// </summary>
public class FileUploadSessionConfiguration : IEntityTypeConfiguration<FileUploadSession>
{
    public void Configure(EntityTypeBuilder<FileUploadSession> builder)
    {
        builder.ToTable("FileUploadSessions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .ValueGeneratedNever();

        builder.Property(s => s.ClinicId)
            .IsRequired();

        builder.Property(s => s.PatientId)
            .IsRequired();

        builder.Property(s => s.FileName)
            .IsRequired()
            .HasMaxLength(FileUploadSession.MaxFileNameLength);

        builder.Property(s => s.ContentType)
            .IsRequired()
            .HasMaxLength(FileUploadSession.MaxContentTypeLength);

        builder.Property(s => s.StorageReference)
            .IsRequired()
            .HasMaxLength(FileUploadSession.MaxStorageReferenceLength);

        builder.Property(s => s.Description)
            .HasMaxLength(FileUploadSession.MaxDescriptionLength);

        builder.Property(s => s.UploadedBy)
            .HasMaxLength(FileUploadSession.MaxUploadedByLength);

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(s => s.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        // ⚠️ Deliberately NOT a foreign key to Patient. A patient deleted mid-upload should abandon the session,
        // not cascade-delete a row the sweep is about to reclaim anyway — and the completion re-checks the
        // patient against the caller's clinic before anything is stored, which is the authoritative check.
        builder.HasIndex(s => new { s.ClinicId, s.PatientId });

        // The sweep's own read: expired first, bounded.
        builder.HasIndex(s => s.ExpiresAtUtc);
    }
}
