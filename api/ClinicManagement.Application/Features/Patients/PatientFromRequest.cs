using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// Builds a <see cref="Patient"/> from a <see cref="CreatePatientCommand"/> — <b>the single construction and
/// validation path</b> for a new patient, whoever asks for one.
///
/// <para><b>Why it was extracted (L5, import half).</b> The spec's requirement is « reuse
/// <c>CreatePatientCommand</c>'s validation rather than a parallel path, or imported rows bypass rules every
/// hand-typed row obeys ». The obvious way to honour that is for the import to <c>Send</c> the command once per row —
/// but every command goes through <c>RealtimeBroadcastBehavior</c>, so a 3 000-row file would emit 3 000 SignalR
/// broadcasts and every open client in the practice would refetch the patient list 3 000 times. Extracting the
/// construction instead gives the import the same rules with <b>one</b> broadcast for the whole import (the import
/// is itself a command, in <c>Features.Patients.Commands</c>), and it does it by <i>moving</i> the code rather than
/// copying it — so there is still exactly one answer to « what does creating a patient validate? ».</para>
///
/// <para>The parts that stay in <see cref="CreatePatientCommandHandler"/> are the ones an import does differently:
/// resolving the caller's clinic (once, not per row), persisting, and the inline medical/family-history entries,
/// which arrive from the patient form and have no CSV column.</para>
/// </summary>
public static class PatientFromRequest
{
    /// <summary>Description stamped on the flag created by the « Signaler ce patient » toggle.</summary>
    public const string SignaledFlagDescription = "Patient signalé";

    /// <summary>
    /// The patient the request describes, or a French <see cref="Result"/> failure. Nothing here touches a
    /// repository, so a failure has nothing to roll back — which is what lets the import decide per row.
    /// </summary>
    public static Result<Patient> Build(CreatePatientCommand request, Guid clinicId)
    {
        // AC-5: a provided phone must be a deliverable Tunisian number (the same rule the reminder engine uses),
        // else reject at entry so it never silently fails at dispatch. An empty phone is allowed — the patient
        // simply can't receive reminders, and the form says so.
        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && !PhoneNumber.IsDeliverable(request.PhoneNumber))
        {
            return Result<Patient>.Failure(
                "Numéro de téléphone invalide. Utilisez un numéro tunisien à 8 chiffres (ou +216…).");
        }

        // Blank means blank. This used to manufacture noemail@example.com and a ten-zero phone so the NOT NULL
        // columns would accept the row — which made "we have no way to reach this patient" indistinguishable from
        // "we have their details", and put an address on file that would silently absorb any mail sent to it.
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : new Email(request.Email);
        var phoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
            ? null
            : new PhoneNumber(request.PhoneNumber);

        // Convert AddressDto to Address value object if provided and valid
        Address? address = null;
        if (request.Address != null &&
            !string.IsNullOrWhiteSpace(request.Address.Street) &&
            !string.IsNullOrWhiteSpace(request.Address.City) &&
            !string.IsNullOrWhiteSpace(request.Address.State) &&
            !string.IsNullOrWhiteSpace(request.Address.ZipCode))
        {
            address = new Address(
                request.Address.Street,
                request.Address.City,
                request.Address.State,
                request.Address.ZipCode,
                request.Address.Country);
        }

        // Convert InsuranceInfoDto to InsuranceInfo value object if provided and valid
        InsuranceInfo? insuranceInfo = null;
        if (request.InsuranceInfo != null &&
            !string.IsNullOrWhiteSpace(request.InsuranceInfo.Provider) &&
            !string.IsNullOrWhiteSpace(request.InsuranceInfo.PolicyNumber))
        {
            insuranceInfo = new InsuranceInfo(
                request.InsuranceInfo.Provider,
                request.InsuranceInfo.PolicyNumber,
                request.InsuranceInfo.GroupNumber,
                request.InsuranceInfo.ExpiryDate);
        }

        // Provide defaults for required fields if not provided
        var dateOfBirth = request.DateOfBirth == default(DateTime)
            ? DateTime.UtcNow.AddYears(-30) // Default to 30 years ago if not provided
            : request.DateOfBirth;
        var gender = string.IsNullOrWhiteSpace(request.Gender)
            ? PatientGender.Unknown
            : request.Gender;

        var patient = new Patient(
            Guid.NewGuid(),
            clinicId,
            request.FirstName,
            request.LastName,
            dateOfBirth,
            gender,
            email,
            phoneNumber,
            address,
            insuranceInfo);

        // Set medical history and allergies after creation
        if (!string.IsNullOrWhiteSpace(request.MedicalHistory) || !string.IsNullOrWhiteSpace(request.Allergies))
        {
            patient.UpdateMedicalHistory(request.MedicalHistory, request.Allergies);
        }

        // Optional CNAM identity (ToDomain returns null for an omitted/empty block).
        patient.UpdateCnamInfo(request.CnamInfo.ToDomain());

        // Optional emergency contact (finding #11): name + a Tunisian phone. An empty block clears both.
        if (!string.IsNullOrWhiteSpace(request.EmergencyContactName) || !string.IsNullOrWhiteSpace(request.EmergencyContactPhone))
        {
            var emergencyPhone = string.IsNullOrWhiteSpace(request.EmergencyContactPhone)
                ? null
                : new PhoneNumber(request.EmergencyContactPhone);
            patient.UpdateEmergencyContact(
                string.IsNullOrWhiteSpace(request.EmergencyContactName) ? null : request.EmergencyContactName.Trim(),
                emergencyPhone);
        }

        // Dentition: what the form chose, else what this patient's age implies. Applied after construction so the
        // age fallback reads the SAME date of birth the entity was built with (defaults included) rather than the
        // raw request, which may have been empty.
        patient.SetDentition(
            DentitionRules.Parse(request.Dentition) ?? DentitionRules.FromDateOfBirth(patient.DateOfBirth));

        // Optional « adressé par » — blank/omitted leaves it null (the patient came on their own).
        patient.SetReferredBy(request.ReferredBy);

        // Optional patient-level notes — UpdateNotes normalizes blank to null, so an untouched section stores
        // nothing rather than two empty strings.
        patient.UpdateNotes(request.Notes, request.ImportantNotes);

        // Optional "Signaler ce patient" flag at creation.
        if (request.IsFlagged == true)
        {
            patient.AddFlag(new PatientFlag(
                Guid.NewGuid(), patient.Id, PatientFlagType.HighPriority, SignaledFlagDescription, request.FlagNotes));
        }

        return Result<Patient>.Success(patient);
    }
}
