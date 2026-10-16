using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class StaffQuickMessageListConfiguration : IEntityTypeConfiguration<StaffQuickMessageList>
{
    public void Configure(EntityTypeBuilder<StaffQuickMessageList> builder)
    {
        builder.ToTable("StaffQuickMessageLists");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .ValueGeneratedNever();

        builder.Property(l => l.ClinicId)
            .IsRequired();

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(l => l.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);

        // One list per cabinet; two first saves at once lose on this index, not silently.
        builder.HasIndex(l => l.ClinicId)
            .IsUnique();

        builder.Property(l => l.UpdatedAt)
            .IsRequired();

        builder.OwnsMany(l => l.Items, items =>
        {
            items.ToTable("StaffQuickMessages");
            items.WithOwner().HasForeignKey("ListId");
            // Surrogate shadow key: `Replace` swaps the whole list, so EF deletes and re-inserts (the PatientPhone precedent).
            items.Property<int>("Id").ValueGeneratedOnAdd();
            items.HasKey("Id");

            items.Property(i => i.Key)
                .IsRequired()
                .HasMaxLength(StaffQuickMessage.KeyMaxLength);

            items.Property(i => i.Label)
                .IsRequired()
                .HasMaxLength(StaffQuickMessage.LabelMaxLength);

            items.Property(i => i.Audience)
                .HasConversion<int>();

            items.Property(i => i.SortOrder);

            items.HasIndex("ListId", nameof(StaffQuickMessage.SortOrder));
        });
    }
}
