using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class ClinicReminderSettingsConfiguration : IEntityTypeConfiguration<ClinicReminderSettings>
{
    public void Configure(EntityTypeBuilder<ClinicReminderSettings> builder)
    {
        builder.ToTable("ClinicReminderSettings");

        // Shared primary key with the clinic (1:1): the entity Id IS the clinic id, mapped to the ClinicId
        // column and never store-generated (assigned in the domain ctor).
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("ClinicId")
            .ValueGeneratedNever();

        builder.HasOne<Clinic>()
            .WithOne()
            .HasForeignKey<ClinicReminderSettings>(s => s.Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(s => s.SmsEnabled);
        builder.Property(s => s.WhatsAppEnabled);

        builder.Property(s => s.SmsSenderId).HasMaxLength(100);
        builder.Property(s => s.WhatsAppPhoneNumberId).HasMaxLength(100);
        builder.Property(s => s.WhatsAppTemplateName).HasMaxLength(200);
        builder.Property(s => s.WhatsAppTemplateLanguage).HasMaxLength(20);

        // Per-clinic overrides of previously per-install-only values (reliability-and-polish).
        builder.Property(s => s.SmsApiUrl).HasMaxLength(500);
        builder.Property(s => s.WhatsAppApiUrl).HasMaxLength(500);
        builder.Property(s => s.LeadTimeHours).HasMaxLength(200);
        builder.Property(s => s.MessageTemplateBody).HasColumnType("text");

        // Outbound email (SMTP) — the channel that delivers generated documents. Lengths mirror the sibling
        // channels: identity fields bounded, the URL-ish host at 255, the address at the RFC's 320.
        builder.Property(s => s.SmtpHost).HasMaxLength(255);
        builder.Property(s => s.SmtpPort);
        builder.Property(s => s.SmtpUseTls);
        builder.Property(s => s.SmtpUsername).HasMaxLength(320);
        builder.Property(s => s.SmtpFromAddress).HasMaxLength(320);
        builder.Property(s => s.SmtpFromName).HasMaxLength(200);

        // Data-Protection ciphertext — opaque, variable length.
        builder.Property(s => s.SmsApiKeyEncrypted).HasColumnType("text");
        builder.Property(s => s.WhatsAppAccessTokenEncrypted).HasColumnType("text");
        builder.Property(s => s.SmtpPasswordEncrypted).HasColumnType("text");

        // WhatsApp Embedded-Signup connection metadata (additive, nullable — manual/existing rows default).
        builder.Property(s => s.WhatsAppBusinessAccountId).HasMaxLength(100);
        builder.Property(s => s.WhatsAppConnectionStatus).HasConversion<int>();
        builder.Property(s => s.WhatsAppLastError).HasColumnType("text");
        builder.Property(s => s.WhatsAppConnectedAt);

        // Meta's review of the cabinet's reminder template (vendor-whatsapp-messaging-quota FR-7a/FR-7b). All four
        // nullable: a cabinet connected before Part 4, or one on the install's own pre-approved template, has no
        // template of its own and « unknown » is the honest value — never NotSubmitted, which would read as
        // « en attente de validation » about a cabinet that sends fine.
        builder.Property(s => s.WhatsAppTemplateStatus).HasConversion<int>();
        builder.Property(s => s.WhatsAppTemplateCategory).HasMaxLength(50);
        builder.Property(s => s.WhatsAppTemplateId).HasMaxLength(100);
        builder.Property(s => s.WhatsAppTemplateStatusCheckedAtUtc);

        // The webhook's WABA → cabinet lookup. Filtered on IS NOT NULL because most rows never connect through
        // Embedded Signup at all, and the index exists for exactly one equality read.
        builder.HasIndex(s => s.WhatsAppBusinessAccountId)
            .HasFilter("\"WhatsAppBusinessAccountId\" IS NOT NULL");

        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt);
    }
}
