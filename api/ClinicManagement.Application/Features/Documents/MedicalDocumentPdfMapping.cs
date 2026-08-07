using System.Text.Json;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;

namespace ClinicManagement.Application.Features.Documents;

/// <summary>
/// Builds the renderer's <see cref="MedicalDocumentPdfData"/> from a stored <see cref="MedicalDocumentDto"/> —
/// the "render this saved document by id" mapping.
/// <para>
/// It is a shared helper because there are now two callers that must render the <b>same</b> bytes: the
/// background <c>PdfGenerationJob</c> (which attaches the PDF to the document) and the document-email queue
/// command (which attaches it to an email). A second copy of this flattening would be a second answer to
/// "what does this ordonnance look like", and the two would drift the first time a field was added.
/// </para>
/// </summary>
public static class MedicalDocumentPdfMapping
{
    public static MedicalDocumentPdfData ToPdfData(MedicalDocumentDto document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var contentStrings = FlattenContent(document.ContentJson);

        return new MedicalDocumentPdfData
        {
            DocumentType = document.DocumentType,
            DocumentDate = document.DocumentDate,
            PatientName = document.PatientName,
            PatientAge = document.PatientAge,
            ClinicName = document.ClinicName,
            ClinicAddress = document.ClinicAddress,
            ClinicPhone = document.ClinicPhone,
            DoctorName = document.DoctorName,
            DoctorSpecialty = document.DoctorSpecialty,
            RecipientDoctorName = document.RecipientDoctorName,
            RecipientDoctorSpecialty = document.RecipientDoctorSpecialty,
            // The cachet/ordre/city snapshotted at creation, read from ContentJson so an unauthenticated caller
            // renders them without a live doctor/clinic lookup.
            ClinicCity = contentStrings.GetValueOrDefault(PractitionerRenderSnapshot.ClinicCityKey),
            ClinicEmail = contentStrings.GetValueOrDefault(PractitionerRenderSnapshot.ClinicEmailKey),
            DoctorOrdreNumber = contentStrings.GetValueOrDefault(PractitionerRenderSnapshot.DoctorOrdreNumberKey),
            DoctorCachetKey = contentStrings.GetValueOrDefault(PractitionerRenderSnapshot.DoctorCachetKeyKey),
            DoctorCachetContentType = contentStrings.GetValueOrDefault(PractitionerRenderSnapshot.DoctorCachetContentTypeKey),
            // Norm values captured on the document itself (AC-7): read from ContentJson so the unauthenticated
            // background job renders exactly what the download path does, with no live patient lookup.
            PatientSex = contentStrings.GetValueOrDefault("patientSex"),
            PatientWeightKg = contentStrings.GetValueOrDefault("patientWeightKg"),
            Content = contentStrings
        };
    }

    /// <summary>
    /// Flattens the stored <c>ContentJson</c> to string values. A non-string node (the medications / acts
    /// arrays) is re-serialized rather than dropped, because the renderer parses those back out of the string.
    /// A malformed or empty blob yields an empty dictionary — a document with unreadable content still renders
    /// its header, identity and signature rather than failing outright.
    /// </summary>
    private static Dictionary<string, string> FlattenContent(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return new Dictionary<string, string>();
        }

        Dictionary<string, JsonElement>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(contentJson);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }

        if (parsed == null)
        {
            return new Dictionary<string, string>();
        }

        return parsed.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ValueKind == JsonValueKind.String
                ? kvp.Value.GetString() ?? string.Empty
                : JsonSerializer.Serialize(kvp.Value));
    }
}
