using ClinicManagement.Domain.Entities;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// [J11] A newly created clinic's default tax position — and it was <b>the wrong way round</b>.
///
/// <para>
/// Dental acts are NOT TVA-exempt in Tunisia. Code de la TVA, <b>Tableau « B » nouveau, § II « Les activités et
/// les services », n° 1</b> lists services performed by « les médecins, les médecins spécialistes, <b>les
/// dentistes</b>, les sages-femmes et les vétérinaires » among those <b>subject to VAT at the reduced rate</b>,
/// and Tableau « A » (the exonérations) contains no entry for médecin / dentiste / soins / santé / clinique at
/// all. LF 2018 re-based the reduced rate to <b>7 %</b>. Code TVA art. 18 § II then requires the invoice to carry
/// « les taux et les montants de la taxe sur la valeur ajoutée ».
/// </para>
/// <para>
/// So a clinic that never opened the billing screen was issuing notes d'honoraires charging no TVA and stating no
/// rate. The default is what almost every clinic ships with, which is what makes it the setting that matters
/// most — and why this is a domain test rather than a note in a doc.
/// </para>
/// <para>
/// ⚠️ The companion half is a <b>non</b>-change: existing rows are deliberately not migrated, because flipping
/// <c>VatApplicable</c> retroactively would alter what already-issued, numbered fiscal documents assert. Nothing
/// in the domain can express "do not migrate" — that lives in the absence of a migration and in the admin notice
/// on the settings screen — so it is recorded in <c>progress.md</c> rather than asserted here.
/// </para>
/// </summary>
public class ClinicBillingDefaultsTests
{
    private static Clinic NewClinic() => new(Guid.NewGuid(), "Cabinet Test");

    // [J11] The correction itself: TVA applies, at the reduced 7 %.
    [Fact]
    public void A_New_Clinic_Applies_Vat_At_Seven_Percent()
    {
        var clinic = NewClinic();

        Assert.True(clinic.VatApplicable);
        Assert.Equal(7m, clinic.VatRate);
    }

    // [J11] The default must be a *usable* VAT posture, not merely a flag flipped: `VatApplicable = true` with a
    // zero rate would satisfy the boolean and still print no tax. The two fields only mean something together, so
    // the pair is asserted as one claim — which is also what `SetBillingSettings` enforces in the other direction.
    [Fact]
    public void The_Default_Posture_Is_Internally_Usable()
    {
        var clinic = NewClinic();

        Assert.True(clinic.VatApplicable && clinic.VatRate > 0m);
    }

    // [J11] The timbre fiscal is unchanged and correct: Code des droits d'enregistrement et de timbre
    // art. 117 § I n° 6° — « Les factures … 1,000 par facture ». LF 2026's 1,5 / 2 DT tiers apply to grandes
    // surfaces (built area > 3 000 m²) only, never to a cabinet.
    [Fact]
    public void A_New_Clinic_Charges_The_One_Dinar_Timbre()
    {
        var clinic = NewClinic();

        Assert.True(clinic.StampDutyEnabled);
        Assert.Equal(1.000m, clinic.StampDutyAmount);
    }

    // [J11] Both stay EDITABLE. A cabinet under the forfait régime is genuinely non-assujetti, so the corrected
    // default must be a default and not a rule — turning VAT off has to keep working.
    [Fact]
    public void Vat_Can_Still_Be_Turned_Off()
    {
        var clinic = NewClinic();

        clinic.SetBillingSettings(
            matriculeFiscal: "1234567/A/M/000", vatApplicable: false, vatRate: 7m,
            stampDutyEnabled: true, stampDutyAmount: 1.000m);

        Assert.False(clinic.VatApplicable);
        // The rate is zeroed with it, so a note issued by a non-assujetti clinic cannot print a rate it does not
        // charge — the same invariant that made the old `false` default silently produce rate-less notes.
        Assert.Equal(0m, clinic.VatRate);
    }

    // [J11] …and the rate can move, because a finance law can move it.
    [Fact]
    public void The_Vat_Rate_Can_Be_Changed()
    {
        var clinic = NewClinic();

        clinic.SetBillingSettings(
            matriculeFiscal: null, vatApplicable: true, vatRate: 13m,
            stampDutyEnabled: true, stampDutyAmount: 1.000m);

        Assert.True(clinic.VatApplicable);
        Assert.Equal(13m, clinic.VatRate);
    }

    // [J11] An invoice issued by a default clinic freezes the corrected posture — which is the point of the change:
    // the default only matters because `Issue()` copies it onto the document. 100 HT → 7 TVA → 108 TTC.
    [Fact]
    public void An_Invoice_Issued_By_A_Default_Clinic_Carries_The_Vat()
    {
        var clinic = NewClinic();
        var invoice = new Invoice(Guid.NewGuid(), clinic.Id, Guid.NewGuid());
        invoice.SetLines(new[] { ("Couronne", 1, 100m) });

        invoice.Issue("2026-0001", clinic.VatApplicable, clinic.VatRate,
            clinic.StampDutyEnabled, clinic.StampDutyAmount);

        Assert.True(invoice.VatApplicable);
        Assert.Equal(7m, invoice.VatRate);
        Assert.Equal(100.000m, invoice.TotalHt);
        Assert.Equal(7.000m, invoice.TotalVat);
        Assert.Equal(108.000m, invoice.TotalTtc);
    }
}
