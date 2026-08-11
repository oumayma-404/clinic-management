using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for the console's daily activity history.
///
/// <para>⚠️ <b>The unique index on (cabinet, day) is what makes the pass idempotent</b>, not a tidiness measure.
/// The job re-runs — Hangfire retries it, an operator triggers it, a container restarts mid-pass — and without
/// the index a second run appends a second row for the same day, which the trend would then read as twice the
/// activity. With it, the pass loads the day and restates it, and a bug that tried to append instead fails loudly
/// at the database rather than quietly doubling a figure nobody can check.</para>
///
/// <para>⚠️ <c>Day</c> maps to PostgreSQL <c>date</c> through <see cref="DateOnly"/> — deliberately not a
/// <c>DateTime</c>: the context runs every <c>DateTime</c> property through a UTC value converter, which would
/// shift a Tunisian calendar day across the very boundary the figure is defined on.</para>
/// </summary>
public class ClinicActivityDayConfiguration : IEntityTypeConfiguration<ClinicActivityDay>
{
    public void Configure(EntityTypeBuilder<ClinicActivityDay> builder)
    {
        builder.ToTable("ClinicActivityDays");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.ClinicId).IsRequired();
        builder.Property(d => d.Day).IsRequired().HasColumnType("date");
        builder.Property(d => d.Writes).IsRequired();
        builder.Property(d => d.Appointments).IsRequired();
        builder.Property(d => d.PatientsCreated).IsRequired();
        builder.Property(d => d.ComputedAt).IsRequired();

        builder.HasIndex(d => new { d.ClinicId, d.Day })
            .IsUnique()
            .HasDatabaseName("IX_ClinicActivityDays_ClinicId_Day");

        // A closed cabinet's counters go with it: they describe that cabinet and nothing else, and an orphan row
        // would keep a deleted practice in the vendor's six-month trend for ever.
        builder.HasOne<Clinic>()
            .WithMany()
            .HasForeignKey(d => d.ClinicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
