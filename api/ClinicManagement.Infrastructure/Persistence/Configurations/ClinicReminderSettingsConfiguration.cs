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

        // Data-Protection ciphertext — opaque, variable length.
        builder.Property(s => s.SmsApiKeyEncrypted).HasColumnType("text");
        builder.Property(s => s.WhatsAppAccessTokenEncrypted).HasColumnType("text");

        // WhatsApp Embedded-Signup connection metadata (additive, nullable — manual/existing rows default).
        builder.Property(s => s.WhatsAppBusinessAccountId).HasMaxLength(100);
        builder.Property(s => s.WhatsAppConnectionStatus).HasConversion<int>();
        builder.Property(s => s.WhatsAppLastError).HasColumnType("text");
        builder.Property(s => s.WhatsAppConnectedAt);

        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt);
    }
}
