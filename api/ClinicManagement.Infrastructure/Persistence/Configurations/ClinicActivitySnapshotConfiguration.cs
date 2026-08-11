using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for the per-cabinet snapshot the portfolio list JOINs.
///
/// <para>⚠️ <b>Unique on <c>ClinicId</c>, and the list depends on that being true.</b> The portfolio is a
/// <c>LEFT JOIN</c> from <c>Clinics</c> onto this table; a second row for one cabinet would silently duplicate
/// that cabinet in the list, inflate <c>TotalCount</c>, and make the page boundaries wrong for every cabinet
/// after it. The index turns that from a hard-to-spot read defect into a write that cannot happen.</para>
///
/// <para>⚠️ <c>Writes30d</c> is indexed because « dormant » filters on it across the whole portfolio before the
/// page is cut — the one predicate this table exists to make cheap.</para>
/// </summary>
public class ClinicActivitySnapshotConfiguration : IEntityTypeConfiguration<ClinicActivitySnapshot>
{
    public void Configure(EntityTypeBuilder<ClinicActivitySnapshot> builder)
    {
        builder.ToTable("ClinicActivitySnapshots");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.ClinicId).IsRequired();
        builder.Property(s => s.Writes7d).IsRequired();
        builder.Property(s => s.Writes30d).IsRequired();
        builder.Property(s => s.Appointments30d).IsRequired();
        builder.Property(s => s.ActiveDays30d).IsRequired();
        builder.Property(s => s.Patients).IsRequired();
        builder.Property(s => s.Users).IsRequired();
        builder.Property(s => s.CollectedThisMonth).IsRequired();
        builder.Property(s => s.ComputedAt).IsRequired();

        builder.HasIndex(s => s.ClinicId)
            .IsUnique()
            .HasDatabaseName("IX_ClinicActivitySnapshots_ClinicId");

        builder.HasIndex(s => s.Writes30d)
            .HasDatabaseName("IX_ClinicActivitySnapshots_Writes30d");

        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(s => s.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
