using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Populates a clinic's per-clinic reference catalogs from the shared seed single-source-of-truth
/// (<see cref="CnamCatalogSeed"/> / <see cref="MedicationCatalogSeed"/> / <see cref="DentalActCatalogSeed"/>),
/// feature cloud-security-and-tenant-isolation, #5. Every clinic gets the SAME default; edits then diverge
/// per clinic. Uses the DbContext directly (reference-data seeding, like a migration) and runs with no clinic
/// in scope, so the tenant query filter is inactive and it can read/write any clinic's rows by explicit
/// <c>ClinicId</c>. Idempotent per catalog: a catalog that already has rows for the clinic is skipped.
/// </summary>
public class ClinicCatalogSeeder : IClinicCatalogSeeder
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ClinicCatalogSeeder> _logger;

    public ClinicCatalogSeeder(ApplicationDbContext context, ILogger<ClinicCatalogSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedAllClinicsAsync(CancellationToken cancellationToken = default)
    {
        var clinicIds = await _context.Clinics.Select(c => c.Id).ToListAsync(cancellationToken);
        foreach (var clinicId in clinicIds)
        {
            await SeedForClinicAsync(clinicId, cancellationToken);
        }
    }

    public async Task SeedForClinicAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        var seededAnything = false;

        // CNAM nomenclature entries
        if (!await _context.CnamNomenclatureEntries.IgnoreQueryFilters().AnyAsync(e => e.ClinicId == clinicId, cancellationToken))
        {
            foreach (var e in CnamCatalogSeed.Entries)
            {
                await _context.CnamNomenclatureEntries.AddAsync(new CnamNomenclatureEntry(
                    CnamCatalogSeed.DeterministicGuid($"{clinicId}:cnam-entry:{e.CodeActe}"),
                    clinicId, e.CodeActe, e.DesignationFr, e.LettreCle, e.Coefficient, e.Category), cancellationToken);
            }
            seededAnything = true;
        }

        // CNAM lettre-clé values (VLC)
        if (!await _context.CnamLetterValues.IgnoreQueryFilters().AnyAsync(v => v.ClinicId == clinicId, cancellationToken))
        {
            foreach (var v in CnamCatalogSeed.LetterValues)
            {
                await _context.CnamLetterValues.AddAsync(new CnamLetterValue(
                    CnamCatalogSeed.DeterministicGuid($"{clinicId}:cnam-vlc:{v.LettreCle}"),
                    clinicId, v.LettreCle, v.Value), cancellationToken);
            }
            seededAnything = true;
        }

        // Medication catalog (the ctor builds the active-ingredient rows from the DCIs)
        if (!await _context.Medications.IgnoreQueryFilters().AnyAsync(m => m.ClinicId == clinicId, cancellationToken))
        {
            foreach (var m in MedicationCatalogSeed.Medications)
            {
                await _context.Medications.AddAsync(new Medication(
                    MedicationCatalogSeed.DeterministicGuid($"{clinicId}:medication:{m.Id}"),
                    clinicId, m.BrandName, m.Form, m.Strength, m.Dcis), cancellationToken);
            }
            seededAnything = true;
        }

        // Dental act catalog (chapitre DCH)
        if (!await _context.DentalActCodes.IgnoreQueryFilters().AnyAsync(a => a.ClinicId == clinicId, cancellationToken))
        {
            foreach (var a in DentalActCatalogSeed.Acts)
            {
                await _context.DentalActCodes.AddAsync(new DentalActCode(
                    DentalActCatalogSeed.DeterministicGuid($"{clinicId}:dental-act:{a.CodeActe}"),
                    clinicId, a.CodeActe, a.DesignationFr, a.Category,
                    DentalActCatalogSeed.LettreCle, coefficient: null, defaultFee: null,
                    requiresAccordPrealable: a.RequiresAccordPrealable, isProvisional: true), cancellationToken);
            }
            seededAnything = true;
        }

        if (seededAnything)
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded default reference catalogs for clinic {ClinicId}.", clinicId);
        }
    }
}
