namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Seeds a clinic's reference catalogs — CNAM nomenclature + VLC values, medications, and dental acts —
/// from the shared default set (feature cloud-security-and-tenant-isolation, #5). Every clinic starts with
/// the SAME default catalog; each clinic's admin then edits its own private copy. Idempotent per clinic: a
/// catalog that already has rows for the clinic is left untouched. Called on clinic creation and as a
/// startup backfill so existing clinics are populated automatically.
/// </summary>
public interface IClinicCatalogSeeder
{
    /// <summary>Seed the default catalogs for a single clinic (no-op for catalogs it already has).</summary>
    Task SeedForClinicAsync(Guid clinicId, CancellationToken cancellationToken = default);

    /// <summary>Backfill: seed every clinic that is missing a catalog (idempotent). Run at startup.</summary>
    Task SeedAllClinicsAsync(CancellationToken cancellationToken = default);
}
