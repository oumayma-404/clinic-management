using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// Turns the wire's additional-phone rows into <see cref="PatientPhone"/>s, or into the French refusal.
///
/// <para><b>The one place that conversion lives</b>, because it is the same on create and on update and the
/// two paths are different files. The rule it enforces is the primary number's, applied per row: validate
/// with <see cref="PhoneNumber.IsDeliverable"/> first, then construct — so a bad number comes back as
/// <c>PhoneRefusals.Invalid</c> and not as the <c>ArgumentException</c> the constructor throws.</para>
/// </summary>
public static class PatientPhoneMapping
{
    /// <summary>
    /// The numbers the request describes, in the order it listed them, or a French failure.
    ///
    /// <para>⚠️ <b>A blank row is dropped, not refused.</b> The form appends an empty row when « Ajouter un
    /// numéro » is pressed and a user who changes their mind leaves it there; refusing the save over it would
    /// make the button a trap. A row with something in it must be a real number.</para>
    ///
    /// <para>⚠️ <b>Duplicates of each other and of the primary are dropped too</b>, compared on E.164 so
    /// « 20 123 456 » and « +216 20 123 456 » are one number. Reception re-typing the mobile it just entered
    /// into the main box is the ordinary case, and storing it twice makes the fiche read as though the patient
    /// has two lines.</para>
    /// </summary>
    public static Result<List<PatientPhone>> Build(
        IEnumerable<PatientPhoneInputDto>? rows,
        PhoneNumber? primary)
    {
        var built = new List<PatientPhone>();

        if (rows is null)
        {
            return Result<List<PatientPhone>>.Success(built);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (primary?.E164 is { } primaryE164)
        {
            seen.Add(primaryE164);
        }

        foreach (var row in rows)
        {
            if (row is null || string.IsNullOrWhiteSpace(row.Value))
            {
                continue;
            }

            if (!PhoneNumber.IsDeliverable(row.Value, row.Region))
            {
                return Result<List<PatientPhone>>.Failure(PhoneRefusals.Invalid);
            }

            // `SortOrder` is the position among the rows we KEEP, so dropping a blank does not leave a hole.
            var phone = new PatientPhone(row.Value, row.Region, built.Count);

            if (!seen.Add(phone.E164))
            {
                continue;
            }

            built.Add(phone);
        }

        if (built.Count > Patient.MaxAdditionalPhoneNumbers)
        {
            return Result<List<PatientPhone>>.Failure(
                $"Un patient ne peut pas avoir plus de {Patient.MaxAdditionalPhoneNumbers} numéros supplémentaires.");
        }

        return Result<List<PatientPhone>>.Success(built);
    }

    /// <summary>The stored rows as a screen reads them, in their own order.</summary>
    public static List<PatientPhoneDto> ToDtos(IEnumerable<PatientPhone> phones) =>
        phones
            .OrderBy(p => p.SortOrder)
            .Select(p => new PatientPhoneDto { Value = p.Value, E164 = p.E164 })
            .ToList();
}
