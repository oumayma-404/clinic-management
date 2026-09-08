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
    /// <summary>
    /// The patient the request describes, or a French <see cref="Result"/> failure. Nothing here touches a
    /// repository, so a failure has nothing to roll back — which is what lets the import decide per row.
    /// </summary>
    public static Result<Patient> Build(CreatePatientCommand request, Guid clinicId)
    {
        // A provided phone must be one we can reach — any country now, not just Tunisia — else reject at entry
        // so it never silently fails at dispatch. An empty phone is allowed: the patient simply can't receive
        // reminders, and the form says so.
        if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && !PhoneNumber.IsDeliverable(request.PhoneNumber))
        {
            return Result<Patient>.Failure(PhoneRefusals.Invalid);
        }

        // Blank means blank. This used to manufacture noemail@example.com and a ten-zero phone so the NOT NULL
        // columns would accept the row — which made "we have no way to reach this patient" indistinguishable from
        // "we have their details", and put an address on file that would silently absorb any mail sent to it.
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : new Email(request.Email);
        var phoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
            ? null
            : new PhoneNumber(request.PhoneNumber);

        // Whatever address was given, however partial. The four-way `&&` this replaces required all of street,
        // city, gouvernorat and code postal — so « Sfax » alone was *silently dropped*, not refused: the address
        // was thrown away with no message, on the door most patients are created through. That is the same defect
        // the insurance block below was fixed for, in this same method, and it was left here.
        var address = Address.OfAny(
            request.Address?.Street,
            request.Address?.City,
            request.Address?.State,
            request.Address?.ZipCode,
            request.Address?.Country);

        // Blank means blank here too. This used to substitute « thirty years ago » so a NOT NULL column would take
        // the row — which stored a birthday nobody gave us and, through DentitionRules below, charted every
        // undated walk-in on adult teeth.
        var gender = string.IsNullOrWhiteSpace(request.Gender)
            ? PatientGender.Unknown
            : request.Gender;

        var patient = new Patient(
            Guid.NewGuid(),
            clinicId,
            request.FirstName,
            request.LastName,
            request.DateOfBirth,
            gender,
            email,
            phoneNumber,
            address);

        // Set medical history and allergies after creation
        if (!string.IsNullOrWhiteSpace(request.MedicalHistory) || !string.IsNullOrWhiteSpace(request.Allergies))
        {
            patient.UpdateMedicalHistory(request.MedicalHistory, request.Allergies);
        }

        // Optional CNAM identity (ToDomain returns null for an omitted/empty block).
        patient.UpdateCnamInfo(request.CnamInfo.ToDomain());

        // Optional emergency contact (finding #11): a name + a phone, in ANY shape. Deliberately not validated —
        // nothing dispatches to it, a human reads it in an emergency, and « 71 555 (bureau) » is a real value a
        // relative gives (AC-14). An empty block clears both.
        if (!string.IsNullOrWhiteSpace(request.EmergencyContactName) || !string.IsNullOrWhiteSpace(request.EmergencyContactPhone))
        {
            var emergencyPhone = string.IsNullOrWhiteSpace(request.EmergencyContactPhone)
                ? null
                : new PhoneNumber(request.EmergencyContactPhone);
            patient.UpdateEmergencyContact(
                string.IsNullOrWhiteSpace(request.EmergencyContactName) ? null : request.EmergencyContactName.Trim(),
                emergencyPhone);
        }

        // Dentition: what the form chose, else what this patient's age implies. With neither — an undated walk-in
        // created by a server-internal caller — nothing is asserted and the entity keeps its own default, which the
        // odontogram no longer trusts blindly: with no date of birth it asks (AC-18).
        if ((DentitionRules.Parse(request.Dentition) ?? DentitionRules.FromDateOfBirth(patient.DateOfBirth)) is { } dentition)
        {
            patient.SetDentition(dentition);
        }

        // Optional « adressé par » — blank/omitted leaves it null (the patient came on their own).
        patient.SetReferredBy(request.ReferredBy);

        // Optional patient-level notes — UpdateNotes normalizes blank to null, so an untouched section stores
        // nothing rather than two empty strings.
        patient.UpdateNotes(request.Notes, request.ImportantNotes);

        // Consent taken at registration, when it was taken. Omitted leaves NotRecorded, which is the honest
        // state for a patient nobody has asked — and the CSV import (L5) reaches this same line, so an imported
        // patient is never silently marked as having agreed to anything.
        var consent = ReminderConsentRules.Parse(request.ReminderConsent);
        if (consent.HasValue)
        {
            patient.SetReminderConsent(consent.Value, DateTime.UtcNow, recordedBy: null);
        }

        // « Motif de consultation » — why they came in the first place. Blank leaves it null.
        patient.SetConsultationReason(request.ConsultationReason);

        // « Tabac ». An omitted/unparseable status leaves the block null, which is « nobody has asked » — never
        // « non-fumeur ». See `TobaccoUse`.
        patient.UpdateTobaccoUse(TobaccoUseMapping.ToDomain(request.TobaccoUse));

        return Result<Patient>.Success(patient);
    }
}
