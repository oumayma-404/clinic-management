using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Documents;

/// <summary>
/// The practitioner + cabinet values snapshotted onto a generated clinical document (Part C, FR-3.3 /
/// FR-6.1): the issuing doctor's cachet image + CNOMDT ordre number and the clinic's city. These ride in
/// the document's <c>ContentJson</c> so the unauthenticated background PDF job can render them without a
/// live doctor/clinic lookup. The <c>*Key</c> constants are the single source of truth for the JSON keys,
/// shared by the create-command writer and both render producers.
/// </summary>
public sealed class PractitionerRenderSnapshot
{
    public const string ClinicCityKey = "clinicCity";
    public const string DoctorOrdreNumberKey = "doctorOrdreNumber";
    public const string DoctorCachetKeyKey = "doctorCachetKey";
    public const string DoctorCachetContentTypeKey = "doctorCachetContentType";

    public string? ClinicCity { get; init; }
    public string? DoctorOrdreNumber { get; init; }
    public string? DoctorCachetKey { get; init; }
    public string? DoctorCachetContentType { get; init; }

    /// <summary>True when at least one value is present (worth writing onto the document).</summary>
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(ClinicCity)
        || !string.IsNullOrWhiteSpace(DoctorOrdreNumber)
        || !string.IsNullOrWhiteSpace(DoctorCachetKey);

    /// <summary>
    /// Resolve the snapshot for the current practitioner + clinic. Null-safe: a missing doctor/clinic (or a
    /// caller with no linked doctor record) simply yields empty fields — never throws for absence.
    /// </summary>
    public static async Task<PractitionerRenderSnapshot> ResolveAsync(
        string? userId,
        Guid clinicId,
        IDoctorRepository doctorRepository,
        IClinicRepository clinicRepository,
        CancellationToken cancellationToken)
    {
        Doctor? doctor = null;
        if (!string.IsNullOrEmpty(userId))
        {
            doctor = await doctorRepository.GetByUserIdAsync(userId, cancellationToken);
        }

        var clinic = await clinicRepository.GetByIdAsync(clinicId, cancellationToken);

        return new PractitionerRenderSnapshot
        {
            ClinicCity = clinic?.City,
            DoctorOrdreNumber = doctor?.OrdreNumberCnomdt,
            DoctorCachetKey = doctor?.CachetStorageKey,
            DoctorCachetContentType = doctor?.CachetContentType
        };
    }
}
