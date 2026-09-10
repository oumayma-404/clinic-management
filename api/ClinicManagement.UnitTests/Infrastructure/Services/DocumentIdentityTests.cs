using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Documents;
using ClinicManagement.Infrastructure.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// The letterhead's CNOMDT line, and the one document that does not carry it.
///
/// <para>The practice owner withdrew the numéro d'ordre from the <b>certificat médical</b>: it is a single
/// signed, stamped sentence and the cachet already identifies its author. Every other type keeps it — an
/// ordonnance is required to carry it (R.5132-3), and it is what makes a lettre de liaison traceable to a
/// registered practitioner.</para>
///
/// <para>⚠️ <b>Asserted in both directions on purpose.</b> The line is composed from two sources (the
/// practitioner snapshot, else a legacy hand-typed <c>doctorOrderNumber</c> key), and there is no user-visible
/// error either way: a certificat that keeps printing it looks correct, and an ordonnance that silently stops
/// is a legal defect nobody would see until a pharmacist refused the sheet.</para>
/// </summary>
public class DocumentIdentityTests
{
    private const string Ordre = "CNOMDT-12345";

    private static MedicalDocumentPdfData Data(string documentType, bool snapshotted = true) => new()
    {
        DocumentType = documentType,
        DoctorName = "Dr Alice Martin",
        DoctorSpecialty = "Médecin dentiste",
        DoctorOrdreNumber = snapshotted ? Ordre : null,
        // The legacy key a document written before the practitioner snapshot carries.
        Content = snapshotted
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { ["doctorOrderNumber"] = Ordre },
    };

    private static bool CarriesOrdre(MedicalDocumentPdfData data) =>
        DocumentIdentity.PrescriberLines(data).Any(line => line.Contains("CNOMDT"));

    // Every type but the certificat prints it — from the snapshot.
    [Theory]
    [InlineData(DocumentTypes.Prescription)]
    [InlineData(DocumentTypes.Examens)]
    [InlineData(DocumentTypes.Liaison)]
    [InlineData(DocumentTypes.Honoraires)]
    [InlineData(DocumentTypes.BulletinCnam)]
    public void Prescriber_Line_Carries_The_Ordre_Number(string documentType)
    {
        Assert.True(CarriesOrdre(Data(documentType)));
    }

    // …and from the legacy content key, which is the fallback that keeps an older document rendering.
    [Fact]
    public void A_Legacy_Ordonnance_Still_Prints_Its_Stored_Ordre()
    {
        Assert.True(CarriesOrdre(Data(DocumentTypes.Prescription, snapshotted: false)));
    }

    // The certificat does not, from either source. No migration: an older certificat still HOLDS the key, it
    // is simply no longer read for this type, so re-rendering one now prints a line fewer.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_Certificat_Never_Prints_The_Ordre_Number(bool snapshotted)
    {
        Assert.False(CarriesOrdre(Data(DocumentTypes.Certificat, snapshotted)));
    }

    // The practitioner is still named on a certificat — only the registration number went.
    [Fact]
    public void A_Certificat_Still_Names_Its_Practitioner()
    {
        Assert.Contains(
            DocumentIdentity.PrescriberLines(Data(DocumentTypes.Certificat)),
            line => line.Contains("Dr Alice Martin"));
    }
}
