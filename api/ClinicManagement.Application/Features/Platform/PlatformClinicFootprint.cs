using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Platform;

/// <summary>
/// What one cabinet holds, said in French — the figures the console states before and after a deletion
/// (<c>clinic-account-removal</c>).
///
/// <para><b>Shared because the preview and the outcome must describe the same thing.</b> « Sera effacé : 12
/// patients » followed by « supprimé : 12 dossiers » would read as two different operations, and the second would
/// be the one nobody could check.</para>
///
/// <para>⚠️ <b>The headline list is hand-written and the total is not.</b> These nine entities are the ones a
/// dentist or a vendor recognises; the plan covers about sixty tables, and naming all of them would bury the four
/// figures that decide whether somebody presses the button. The risk that comes with a hand-written list — a name
/// that stops matching the model, so the row silently reads 0 — is held in two ways: <c>ClinicPurgePlanTests</c>
/// asserts every key here resolves to a real entity type, and <c>RowsTotal</c> is derived from the plan, so a
/// drifted label understates a row somebody can still see in the total.</para>
/// </summary>
public static class PlatformClinicFootprint
{
    /// <summary>
    /// Entity name → the French plural the console prints. Ordered as the dialog reads them: who, then when, then
    /// what was done, then what was billed, then what is stored.
    /// </summary>
    public static readonly IReadOnlyList<(string Label, string[] Entities)> Headlines = new[]
    {
        ("patients", new[] { nameof(Patient) }),
        ("rendez-vous", new[] { nameof(Appointment) }),
        ("fiches de soins", new[] { nameof(DentalRecord) }),
        ("devis", new[] { nameof(TreatmentPlan) }),
        ("notes d'honoraires", new[] { nameof(Invoice) }),
        // ⚠️ BOTH ledgers, summed into one figure: money reaches this product on a note d'honoraires and on a
        // devis échéance, and a count naming only the first understates exactly the cabinets that sell treatment
        // plans — the same split `MoneyReadConsistencyTests` exists to keep honest.
        ("encaissements", new[] { nameof(Payment), nameof(InstallmentPayment) }),
        ("documents", new[] { nameof(MedicalDocument) }),
        ("fichiers", new[] { nameof(PatientFile) }),
        ("comptes", new[] { nameof(User) }),
    };

    /// <summary>
    /// The named figures, <b>non-zero only</b>: a list of nine zeroes hides the two numbers that matter on a
    /// cabinet holding one patient, and « 0 devis » is not a fact anybody needs before deleting.
    /// </summary>
    public static IReadOnlyList<PlatformClinicDeletionTallyDto> Tallies(ClinicPurgeCensus census) =>
        Headlines
            .Select(h => new PlatformClinicDeletionTallyDto(h.Label, h.Entities.Sum(census.RowsOf)))
            .Where(t => t.Rows > 0)
            .ToList();

    /// <summary>
    /// How many stored files the cabinet has, read off the census rather than counted again — the tile in the
    /// dialog pairs it with the bytes, because « 6 fichiers » says nothing about whether a scanner's whole output
    /// is about to go.
    /// </summary>
    public static int FileCount(ClinicPurgeCensus census) => (int)census.RowsOf(nameof(PatientFile));
}
