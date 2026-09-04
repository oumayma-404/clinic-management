using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Infrastructure.Persistence.Configurations;

public class TreatmentPlanItemStepConfiguration : IEntityTypeConfiguration<TreatmentPlanItemStep>
{
    public void Configure(EntityTypeBuilder<TreatmentPlanItemStep> builder)
    {
        builder.ToTable("TreatmentPlanItemSteps");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .ValueGeneratedNever();

        builder.Property(s => s.TreatmentPlanItemId)
            .IsRequired();

        builder.Property(s => s.Label)
            .IsRequired()
            .HasMaxLength(TreatmentPlanItemStep.MaxLabelLength);

        // Clinical order within the act, 0-based and dense. verify-schema's `plan-step-sequence-dense` is what
        // holds the density, since nothing in the schema can express it.
        builder.Property(s => s.SequenceNumber)
            .IsRequired();

        builder.Property(s => s.DoneDate);

        // Soft reference, no FK — same treatment as TreatmentPlanItem.LinkedDentalRecordId, plus one more
        // reason: DentalRecord.SetActs rebuilds every act with a fresh id, so nothing downstream may hold a
        // key into a record's children.
        builder.Property(s => s.LinkedDentalRecordId);

        builder.Property(s => s.EstimatedDurationMinutes);

        // Waiting time, not chair time — the two are different quantities and both are nullable, so they are
        // two columns rather than one with a unit. Null is the ordinary case: most protocols state no interval.
        builder.Property(s => s.MinDaysAfterPrevious);

        // The read order every projection depends on, and the lookup the fiche→step link performs.
        builder.HasIndex(s => new { s.TreatmentPlanItemId, s.SequenceNumber });

        // « Quelle étape cette fiche a-t-elle réalisée ? » — the reverse of the evidence link, asked by the
        // un-mark correction path when a fiche is deleted. Filtered: the overwhelming majority of rows are
        // still « à venir » and carry no record.
        builder.HasIndex(s => s.LinkedDentalRecordId)
            .HasFilter("\"LinkedDentalRecordId\" IS NOT NULL");
    }
}
