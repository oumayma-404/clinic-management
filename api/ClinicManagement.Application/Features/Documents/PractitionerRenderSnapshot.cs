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

    /// <summary>
    /// The cabinet's email — part of the prescriber contact details a prescription must carry (R.5132-3). A
    /// reserved key like the other four: server-resolved and stripped from any client payload, because the
    /// document identifies who issued it and a caller must not be able to put another cabinet's address on it.
    /// </summary>
    public const string ClinicEmailKey = "clinicEmail";

    public string? ClinicCity { get; init; }
    public string? ClinicEmail { get; init; }
    public string? DoctorOrdreNumber { get; init; }
    public string? DoctorCachetKey { get; init; }
    public string? DoctorCachetContentType { get; init; }

    /// <summary>An all-empty snapshot — writes no values, but still strips client-supplied reserved keys.</summary>
    public static readonly PractitionerRenderSnapshot Empty = new();

    /// <summary>True when at least one value is present (worth writing onto the document).</summary>
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(ClinicCity)
        || !string.IsNullOrWhiteSpace(ClinicEmail)
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
        content.Remove(ClinicEmailKey);
        content.Remove(DoctorOrdreNumberKey);
        content.Remove(DoctorCachetKeyKey);
        content.Remove(DoctorCachetContentTypeKey);

        if (!string.IsNullOrWhiteSpace(ClinicCity))
            content[ClinicCityKey] = ClinicCity;
        if (!string.IsNullOrWhiteSpace(ClinicEmail))
            content[ClinicEmailKey] = ClinicEmail;
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
            ClinicEmail = ReadString(content, ClinicEmailKey),
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
            ClinicEmail = !string.IsNullOrWhiteSpace(ClinicEmail) ? ClinicEmail : fallback.ClinicEmail,
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
    /// Resolve the snapshot for a document's issuing practitioner + the cabinet. Null-safe throughout: a missing
    /// doctor or clinic simply yields empty fields — it never throws for absence.
    ///
    /// <para><b>Precedence: the chosen practitioner, then the caller's own doctor record, then none.</b> This is
    /// <c>PractitionerAttribution</c>'s rule one candidate shorter, and for the same reason — the caller is the
    /// <em>last</em> resort, so the practitioner named on the document is the one whose cachet it carries.</para>
    ///
    /// <para>⚠️ <b><paramref name="issuingDoctorId"/> is a selector, never a value.</b> It is validated against
    /// this clinic's roster before it is accepted, and a stale, empty or cross-clinic id <em>falls through</em> to
    /// the caller rather than resolving anything — the same guard, and the same fall-through, as
    /// <c>PractitionerAttribution.Resolve</c>. The cachet <em>key</em> itself stays untrusted from any client:
    /// <see cref="ApplyTo"/> strips the reserved keys before writing the server-resolved ones, because the
    /// unauthenticated <c>PdfGenerationJob</c> later dereferences whatever is stored there.</para>
    ///
    /// <para>This used to resolve from the caller's own record <em>only</em>, on a stated
    /// single-practitioner-per-cabinet assumption, which meant a document issued in another practitioner's name
    /// carried the caller's cachet — and a document authored by anyone with no <c>Doctor</c> record at all (a
    /// secretary, or an admin who is not a dentist) carried <b>no</b> practitioner identity, silently, on forms
    /// whose entire purpose is to carry it. The old note here said fixing that « would require a persisted
    /// DoctorId ». It did not: the <em>resolved</em> snapshot has always been persisted into the document's
    /// <c>ContentJson</c> by <see cref="ApplyTo"/> and preserved across edits by <see cref="ReadFrom"/> +
    /// <see cref="OrElse"/>. What was missing was a selector on the request — which the editor already chose and
    /// simply never sent.</para>
    /// </summary>
    public static async Task<PractitionerRenderSnapshot> ResolveAsync(
        Guid? issuingDoctorId,
        string? userId,
        Guid clinicId,
        IDoctorRepository doctorRepository,
        IClinicRepository clinicRepository,
        CancellationToken cancellationToken)
    {
        Doctor? doctor = null;

        if (issuingDoctorId is { } chosenId && chosenId != Guid.Empty)
        {
            var chosen = await doctorRepository.GetByIdAsync(chosenId, cancellationToken);

            // Tenant check before use, like every other aggregate load in the solution: accepting a foreign
            // doctor would stamp another practice's cachet and ordre onto this clinic's document.
            if (chosen != null && chosen.ClinicId == clinicId)
            {
                doctor = chosen;
            }
        }

        if (doctor == null && !string.IsNullOrEmpty(userId))
        {
            doctor = await doctorRepository.GetByUserIdAsync(userId, cancellationToken);
        }

        var clinic = await clinicRepository.GetByIdAsync(clinicId, cancellationToken);

        return new PractitionerRenderSnapshot
        {
            ClinicCity = clinic?.City,
            ClinicEmail = clinic?.Email,
            DoctorOrdreNumber = doctor?.OrdreNumberCnomdt,
            DoctorCachetKey = doctor?.CachetStorageKey,
            DoctorCachetContentType = doctor?.CachetContentType
        };
    }
}
