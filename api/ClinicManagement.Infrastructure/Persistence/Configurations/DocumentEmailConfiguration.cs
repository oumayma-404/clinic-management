using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class DocumentEmailConfiguration : IEntityTypeConfiguration<DocumentEmail>
{
    public void Configure(EntityTypeBuilder<DocumentEmail> builder)
    {
        builder.ToTable("DocumentEmails");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.ClinicId).IsRequired();
        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(e => e.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        // The kind is a stored token from DocumentEmail.AllowedKinds, not an enum, so the wire value and the
        // stored value are the same string (the DocumentTypes precedent).
        builder.Property(e => e.DocumentKind).IsRequired().HasMaxLength(50);
        builder.Property(e => e.DocumentId).IsRequired();
        builder.Property(e => e.InstallmentId);
        builder.Property(e => e.PaymentId);

        builder.Property(e => e.RecipientEmail).IsRequired().HasMaxLength(320);
        builder.Property(e => e.Subject).IsRequired().HasMaxLength(500);
        builder.Property(e => e.Body).HasColumnType("text");

        builder.Property(e => e.AttachmentStorageKey).HasMaxLength(500);
        builder.Property(e => e.AttachmentFileName).HasMaxLength(300);

        builder.Property(e => e.Status).HasConversion<int>().IsRequired();
        builder.Property(e => e.Attempts).IsRequired();
        builder.Property(e => e.QueuedAt).IsRequired();
        builder.Property(e => e.SentAt);
        builder.Property(e => e.FailureReason).HasColumnType("text");

        // User ids are strings in this model (the Auth0 sub / local|{guid}).
        builder.Property(e => e.RequestedByUserId).HasMaxLength(200);

        // Serves the dispatcher's oldest-first scan of queued rows.
        builder.HasIndex(e => new { e.Status, e.QueuedAt })
            .HasDatabaseName("IX_DocumentEmails_Status_QueuedAt");

        // Serves the per-document send history.
        builder.HasIndex(e => new { e.ClinicId, e.DocumentKind, e.DocumentId })
            .HasDatabaseName("IX_DocumentEmails_Clinic_Kind_Document");
    }
}
