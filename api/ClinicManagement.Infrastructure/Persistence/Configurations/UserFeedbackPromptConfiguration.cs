using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>One row per person who has been asked the week-one question; keyed and cascaded like the dashboard preference.</summary>
public class UserFeedbackPromptConfiguration : IEntityTypeConfiguration<UserFeedbackPrompt>
{
    public void Configure(EntityTypeBuilder<UserFeedbackPrompt> builder)
    {
        builder.ToTable("UserFeedbackPrompts");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("UserId")
            .HasMaxLength(255)
            .ValueGeneratedNever();

        builder.HasOne<User>()
            .WithOne()
            .HasForeignKey<UserFeedbackPrompt>(p => p.Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(p => p.ClosedReason).HasMaxLength(20);
        builder.Property(p => p.CreatedAt).IsRequired();
    }
}
