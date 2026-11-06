using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class PlatformNewsBroadcastConfiguration : IEntityTypeConfiguration<PlatformNewsBroadcast>
{
    public void Configure(EntityTypeBuilder<PlatformNewsBroadcast> builder)
    {
        builder.ToTable("PlatformNewsBroadcasts");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();

        builder.Property(b => b.RequestId).IsRequired();
        builder.HasIndex(b => b.RequestId).IsUnique();

        builder.Property(b => b.Subject).IsRequired().HasMaxLength(PlatformNewsBroadcast.SubjectMaxLength);
        builder.Property(b => b.SentByAccountId).IsRequired();
        builder.Property(b => b.SentByName).IsRequired().HasMaxLength(PlatformNewsBroadcast.SentByNameMaxLength);
        builder.Property(b => b.CreatedAtUtc).IsRequired();
        builder.Property(b => b.CompletedAtUtc);

        builder.HasIndex(b => b.CreatedAtUtc);

        builder.OwnsMany(b => b.Items, items =>
        {
            items.ToTable("PlatformNewsItems");
            items.WithOwner().HasForeignKey("BroadcastId");
            items.Property<int>("Id").ValueGeneratedOnAdd();
            items.HasKey("Id");

            items.Property(i => i.Emoji).IsRequired().HasMaxLength(PlatformNewsItem.EmojiMaxLength);
            items.Property(i => i.Text).IsRequired().HasMaxLength(PlatformNewsItem.TextMaxLength);
            items.Property(i => i.SortOrder);
        });

        builder.HasMany(b => b.Recipients)
            .WithOne()
            .HasForeignKey(r => r.BroadcastId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(b => b.Recipients).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class PlatformNewsRecipientConfiguration : IEntityTypeConfiguration<PlatformNewsRecipient>
{
    public void Configure(EntityTypeBuilder<PlatformNewsRecipient> builder)
    {
        builder.ToTable("PlatformNewsRecipients");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // No FK to Clinics, like PlatformAccessEntry: the vendor's record of a send outlives the cabinet.
        builder.Property(r => r.ClinicId).IsRequired();
        builder.Property(r => r.ClinicName).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Email).IsRequired().HasMaxLength(256);
        builder.Property(r => r.FullName).HasMaxLength(200);
        builder.Property(r => r.Status).IsRequired().HasConversion<int>();
        builder.Property(r => r.Error).HasMaxLength(PlatformNewsRecipient.ErrorMaxLength);
        builder.Property(r => r.AttemptedAtUtc);

        builder.HasIndex(r => new { r.BroadcastId, r.Status });
    }
}
