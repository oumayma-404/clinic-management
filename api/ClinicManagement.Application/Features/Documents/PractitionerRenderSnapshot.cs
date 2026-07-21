using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>An all-empty snapshot — writes no values, but still strips client-supplied reserved keys.</summary>
    public static readonly PractitionerRenderSnapshot Empty = new();

    /// <summary>True when at least one value is present (worth writing onto the document).</summary>
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(ClinicCity)
        || !string.IsNullOrWhiteSpace(DoctorOrdreNumber)
        || !string.IsNullOrWhiteSpace(DoctorCachetKey);

    /// <summary>
    /// Merge this (server-resolved) snapshot into a document's <c>ContentJson</c> (FR-3.3 / FR-6.1), shared
    /// by the create and update command handlers. The four reserved keys are authoritative server values:
    /// any client-supplied copy is <b>always stripped first</b> (so a caller cannot inject e.g. another
    /// practitioner's <c>doctorCachetKey</c>, which the unauthenticated PDF job would later dereference),
    /// then only present snapshot values are (re)written. Malformed / non-object JSON is returned unchanged.
    /// </summary>
    public string ApplyTo(string originalContentJson)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(originalContentJson);
        }
        catch (JsonException)
        {
            return originalContentJson;
        }

        if (node is not JsonObject content)
        {
            return originalContentJson;
        }

        content.Remove(ClinicCityKey);
        content.Remove(DoctorOrdreNumberKey);
        content.Remove(DoctorCachetKeyKey);
        content.Remove(DoctorCachetContentTypeKey);

        if (!string.IsNullOrWhiteSpace(ClinicCity))
            content[ClinicCityKey] = ClinicCity;
        if (!string.IsNullOrWhiteSpace(DoctorOrdreNumber))
            content[DoctorOrdreNumberKey] = DoctorOrdreNumber;
        if (!string.IsNullOrWhiteSpace(DoctorCachetKey))
        {
            content[DoctorCachetKeyKey] = DoctorCachetKey;
            content[DoctorCachetContentTypeKey] = DoctorCachetContentType;
        }

        return content.ToJsonString();
    }

    /// <summary>
    /// Reads the four reserved practitioner/clinic values already snapshotted onto a document's
    /// <c>ContentJson</c>. Used to preserve a document's issuing-practitioner identity when it is edited by
    /// a caller with no doctor record of their own (a secretary/admin) — see <see cref="OrElse"/>. Malformed
    /// / non-object JSON, or values that aren't strings, yield empty fields (never throws).
    /// </summary>
    public static PractitionerRenderSnapshot ReadFrom(string contentJson)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(contentJson);
        }
        catch (JsonException)
        {
            return Empty;
        }

        if (node is not JsonObject content)
        {
            return Empty;
        }

        return new PractitionerRenderSnapshot
        {
            ClinicCity = ReadString(content, ClinicCityKey),
            DoctorOrdreNumber = ReadString(content, DoctorOrdreNumberKey),
            DoctorCachetKey = ReadString(content, DoctorCachetKeyKey),
            DoctorCachetContentType = ReadString(content, DoctorCachetContentTypeKey)
        };
    }

    /// <summary>
    /// Returns a snapshot that keeps this instance's present values and fills each missing one from
    /// <paramref name="fallback"/> (the cachet key and its content type move together). Lets an edit prefer
    /// the caller's live doctor identity when they have one, else preserve the values already on the document.
    /// </summary>
    public PractitionerRenderSnapshot OrElse(PractitionerRenderSnapshot fallback)
    {
        var hasCachet = !string.IsNullOrWhiteSpace(DoctorCachetKey);
        return new PractitionerRenderSnapshot
        {
            ClinicCity = !string.IsNullOrWhiteSpace(ClinicCity) ? ClinicCity : fallback.ClinicCity,
            DoctorOrdreNumber = !string.IsNullOrWhiteSpace(DoctorOrdreNumber) ? DoctorOrdreNumber : fallback.DoctorOrdreNumber,
            DoctorCachetKey = hasCachet ? DoctorCachetKey : fallback.DoctorCachetKey,
            DoctorCachetContentType = hasCachet ? DoctorCachetContentType : fallback.DoctorCachetContentType
        };
    }

    private static string? ReadString(JsonObject content, string key)
        => content.TryGetPropertyValue(key, out var node) && node is JsonValue value
            && value.TryGetValue<string>(out var s)
                ? s
                : null;

    /// <summary>
    /// Resolve the snapshot for the current practitioner + clinic. Null-safe: a missing doctor/clinic (or a
    /// caller with no linked doctor record) simply yields empty fields — never throws for absence.
    /// NOTE: the cachet/ordre are resolved from the <b>caller's own</b> doctor record (by <paramref name="userId"/>),
    /// which is correct when the caller is the issuing practitioner (the single-practitioner-per-cabinet
    /// assumption this feature targets). A document has no issuing-doctor FK by design (snapshot pattern), so
    /// in a multi-doctor cabinet a document issued in another practitioner's name would carry the caller's
    /// cachet — resolving by the named issuer would require a persisted DoctorId (out of scope here).
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
