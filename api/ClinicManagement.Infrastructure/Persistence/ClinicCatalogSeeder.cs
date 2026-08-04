using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Services;
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
/// <remarks>
/// ⚠️ Seeding an <i>empty</i> catalog is no longer the whole job. A clinic seeded before a shipped default was
/// found to be wrong still holds the wrong value in its own rows, and « the catalog already has rows, skip it »
/// would leave it there forever — so <see cref="CorrectSupersededDefaultsAsync"/> runs afterwards, on every
/// clinic, every startup. It is deliberately narrow: it only ever touches a row that is <b>untouched since
/// seeding</b> and still carries the exact superseded value, because clobbering an admin's deliberate entry is
/// worse than leaving a stale default (feature <c>adoption-qa-k</c>, DEV-4/DEV-5).
/// </remarks>
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

        // Saved first, deliberately: the correction below reads from the database, so rows added just above must
        // already be there. They can never match its predicate anyway (they carry the corrected value), but a
        // correction that silently depends on what is or is not in the change tracker is one nobody can reason about.
        await CorrectSupersededDefaultsAsync(clinicId, cancellationToken);
    }

    /// <summary>
    /// Re-applies the shared seed's <b>corrected</b> defaults to a clinic that was seeded with a superseded one.
    /// Two corrections today: the CNAM valeurs de la lettre clé (the convention in force since 01/01/2021) and the
    /// Prothèse accord-préalable flag (cleared since April 2019).
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>What makes this safe is the predicate, not the value.</b> Three terms, all required:
    /// <c>UpdatedAt == null</c> (never touched since seeding), <c>IsProvisional</c> (still nobody has vouched for
    /// it), and the row still holding the <i>exact</i> superseded figure/flag. Correcting on
    /// <c>IsProvisional</c> alone — which is how the feature spec worded it — would be wrong:
    /// <c>CnamLetterValue.SetValue</c> stamps <c>UpdatedAt</c> but does <b>not</b> clear the provisional flag (only
    /// <c>Confirm()</c> does), so an admin who typed their own valeur and never pressed « Confirmer » still reads
    /// <c>IsProvisional = true</c>, and this method would overwrite the one entry it must never touch.
    /// </para>
    /// <para>
    /// Self-terminating for the same reason it is safe: both mutators stamp <c>UpdatedAt</c>, so a corrected row
    /// fails the predicate on every subsequent startup. A clinic whose admin has already fixed its own values is a
    /// no-op that writes nothing, and the divergence it leaves behind is surfaced on <c>/cnam-nomenclature</c> as a
    /// prompt the admin can accept — never applied behind their back.
    /// </para>
    /// </remarks>
    private async Task CorrectSupersededDefaultsAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        var corrections = 0;

        var letterValues = await _context.CnamLetterValues
            .IgnoreQueryFilters()
            .Where(v => v.ClinicId == clinicId)
            .ToListAsync(cancellationToken);

        foreach (var value in letterValues)
        {
            var superseded = CnamCatalogSeed.SupersededLetterValue(value.LettreCle);
            var inForce = CnamConventionTariffs.ValueFor(value.LettreCle);
            if (superseded is null || inForce is null)
            {
                continue; // nothing to correct for this lettre clé (Vd/Rd — the convention settles no value)
            }

            if (value.UpdatedAt is not null || !value.IsProvisional || value.Value != superseded.Value)
            {
                continue; // touched, vouched for, or already holding something other than the stale default
            }

            value.SetValue(inForce.Value);
            corrections++;
            _logger.LogInformation(
                "Clinic {ClinicId}: corrected the valeur de la lettre clé {Cle} from {Old} to {New} "
                + "(convention in force since {InForce:yyyy-MM-dd}).",
                clinicId, value.LettreCle, superseded.Value, inForce.Value, CnamConventionTariffs.InForceSince);
        }

        var dentalActs = await _context.DentalActCodes
            .IgnoreQueryFilters()
            .Where(a => a.ClinicId == clinicId && a.RequiresAccordPrealable)
            .ToListAsync(cancellationToken);

        foreach (var act in dentalActs)
        {
            if (act.UpdatedAt is not null || !act.IsProvisional || !DentalActCatalogSeed.SupersededAccordPrealable(act.CodeActe))
            {
                continue;
            }

            // No single-field mutator exists, so every current field is echoed back with only the flag changed.
            // Update() stamps UpdatedAt, which is what makes this run once.
            act.Update(
                act.CodeActe,
                act.DesignationFr,
                act.LettreCle,
                act.Coefficient,
                act.Category,
                act.DefaultFee,
                requiresAccordPrealable: false);
            corrections++;
            _logger.LogInformation(
                "Clinic {ClinicId}: cleared the accord-préalable flag on {CodeActe} ({Designation}) — dental "
                + "prostheses have been covered without a demande d'accord préalable since April 2019.",
                clinicId, act.CodeActe, act.DesignationFr);
        }

        if (corrections > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Corrected {Count} superseded catalog default(s) for clinic {ClinicId}.", corrections, clinicId);
        }
    }
}
