using ClinicManagement.Application.Common;
using System.Text.Json.Serialization;
using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Patients.Commands;

public class UpdatePatientCommand : IRequest<Result<PatientDto>>
{
    /// <summary>
    /// The <c>Version</c> the client read. Round-tripped so the save is validated against the copy the user
    /// actually edited rather than the one the handler just loaded. Omit (0) to skip the check — the seam
    /// server-internal writers use; see <c>IUnitOfWork.SetExpectedVersion</c>.
    /// </summary>
    public uint Version { get; set; }

    public Guid Id { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    /// <summary>
    /// Tri-state, same mechanism as <see cref="Address"/>: omit the key to leave the stored date alone, send an
    /// explicit <c>null</c> to clear it, send a date to set it.
    ///
    /// <para>
    /// ⚠️ It was a plain <c>DateTime?</c> read as <c>request.DateOfBirth ?? patient.DateOfBirth</c>, i.e. « clear »
    /// and « leave alone » were the same request — the exact defect <see cref="Address"/>'s own note describes. It
    /// did not bite while the form made the date of birth mandatory. It does the moment it is optional: a patient
    /// recorded with a birthday somebody guessed at can no longer have it removed, and the form would report
    /// success having changed nothing.
    /// </para>
    /// </summary>
    public DateTime? DateOfBirth
    {
        get => _dateOfBirth;
        set { _dateOfBirth = value; DateOfBirthSpecified = true; }
    }
    private DateTime? _dateOfBirth;

    [JsonIgnore]
    public bool DateOfBirthSpecified { get; private set; }

    public string? Gender { get; set; }

    /// <summary>
    /// <c>"Child"</c> or <c>"Adult"</c>. Omitted (or unrecognised) leaves the stored value alone — unlike creation
    /// there is no age fallback here, because silently re-deriving on every unrelated edit would overwrite a
    /// dentist's deliberate override the next time someone fixed a phone number.
    /// </summary>
    public string? Dentition { get; set; }
    /// <summary>
    /// Tri-state, same mechanism as <c>UpdateAppointmentCommand</c>: omit the key to leave the value alone,
    /// send an explicit <c>null</c> (or an empty string) to clear it, send a value to set it.
    /// <see cref="Address"/> carries the same mechanism, for the same reason.
    ///
    /// <para>
    /// Plain nullability is not enough. The old handler read "blank ⇒ keep the existing value", so once a
    /// patient had an e-mail on file there was no request that could remove it — making the columns nullable
    /// alone would have left clearing a silent no-op. System.Text.Json only invokes a setter for a key that is
    /// physically present in the payload, which is what makes the distinction observable.
    /// </para>
    /// </summary>
    public string? Email
    {
        get => _email;
        set { _email = value; EmailSpecified = true; }
    }
    private string? _email;

    [JsonIgnore]
    public bool EmailSpecified { get; private set; }

    /// <inheritdoc cref="Email"/>
    public string? PhoneNumber
    {
        get => _phoneNumber;
        set { _phoneNumber = value; PhoneNumberSpecified = true; }
    }
    private string? _phoneNumber;

    [JsonIgnore]
    public bool PhoneNumberSpecified { get; private set; }

    /// <summary>
    /// The postal address, tri-state like <see cref="Email"/>: omit the key to leave it alone, send an explicit
    /// <c>null</c> to clear it, send a block to set it.
    ///
    /// <para>
    /// ⚠️ It reached the same defect the contact fields did, from the other direction. The edit dialog builds the
    /// block only when at least one of the four inputs is non-blank and sent <c>undefined</c> otherwise, while this
    /// handler read a missing block as "keep the stored one" — so **emptying the address boxes silently did
    /// nothing**, exactly as emptying the e-mail box once did. Found by the L1b payload audit, not by a report:
    /// the fields that carried it clinically (allergies, antécédents) are the ones that made it worth auditing the
    /// whole literal.
    /// </para>
    /// </summary>
    public AddressDto? Address
    {
        get => _address;
        set { _address = value; AddressSpecified = true; }
    }
    private AddressDto? _address;

    [JsonIgnore]
    public bool AddressSpecified { get; private set; }

    public InsuranceInfoDto? InsuranceInfo { get; set; }
    public CnamInfoDto? CnamInfo { get; set; }
    public string? MedicalHistory { get; set; }
    public string? Allergies { get; set; }
    // Emergency contact (finding #11). null (omitted) = leave unchanged; a present value (even empty) sets/clears.
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }

    /// <summary>
    /// « Adressé par » — the referring practitioner. Same convention as the emergency contact above: null
    /// (omitted) leaves it unchanged, a present value sets it, and a present-but-blank value clears it.
    /// </summary>
    public string? ReferredBy { get; set; }

    /// <summary>
    /// Patient-level notes. Same convention as the emergency contact: a present value sets it, a present-but-blank
    /// one clears it, an omitted one leaves it unchanged. Each of the two is resolved independently, so sending only
    /// <see cref="ImportantNotes"/> cannot wipe <see cref="Notes"/>.
    /// </summary>
    public string? Notes { get; set; }

    /// <inheritdoc cref="Notes"/>
    public string? ImportantNotes { get; set; }

    // "Signaler ce patient" toggle + note. null = leave the flag state unchanged (backward-compatible with
    // callers that don't send it); true = ensure an active flag; false = clear any active flag.
    /// <summary>
    /// The patient's answer about automated SMS/WhatsApp reminders — <c>"NotRecorded"</c>, <c>"Granted"</c> or
    /// <c>"Refused"</c>. <b>Omitted means unchanged</b>, like every other key on this command; sending
    /// <c>"NotRecorded"</c> explicitly is how an answer is un-recorded.
    ///
    /// <para>⚠️ A string rather than the enum, for <c>PatientDto.Dentition</c>'s reason: with no
    /// <c>JsonStringEnumConverter</c> registered, an enum property refuses <c>"Refused"</c> with a 400 and
    /// accepts only <c>2</c>.</para>
    /// </summary>
    public string? ReminderConsent { get; set; }

    public bool? IsFlagged { get; set; }
    public string? FlagNotes { get; set; }
}

public class UpdatePatientCommandHandler : IRequestHandler<UpdatePatientCommand, Result<PatientDto>>
{
    // Description stamped on the flag created by the "Signaler ce patient" toggle.
    private const string SignaledFlagDescription = "Patient signalé";

    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClinicContext _clinicContext;

    public UpdatePatientCommandHandler(
        IPatientRepository patientRepository,
        ICurrentClinicResolver clinicResolver,
        IUnitOfWork unitOfWork,
        IClinicContext clinicContext)
    {
        _patientRepository = patientRepository;
        _clinicResolver = clinicResolver;
        _unitOfWork = unitOfWork;
        _clinicContext = clinicContext;
    }

    public async Task<Result<PatientDto>> Handle(UpdatePatientCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PatientDto>.Failure(clinicResult.Error ?? "Unable to resolve current clinic");
            }

            var patient = await _patientRepository.GetByIdAsync(request.Id, cancellationToken);
            if (patient == null)
            {
                return Result<PatientDto>.Failure("Patient introuvable.");
            }

            // Explicit tenant check (defense-in-depth alongside the global query filter): a patient
            // from another clinic reads as "not found".
            if (patient.ClinicId != clinicResult.Value)
            {
                return Result<PatientDto>.Failure("Patient introuvable.");
            }

            // AC-5: a provided phone must be a deliverable Tunisian number (same rule as the reminder engine).
            // A legacy patient whose stored number is non-conforming surfaces this error the next time it is
            // edited (the form re-submits the stored value) — the intended tightening, not a retro-invalidation.
            if (!string.IsNullOrWhiteSpace(request.PhoneNumber) && !PhoneNumber.IsDeliverable(request.PhoneNumber))
            {
                return Result<PatientDto>.Failure(
                    "Numéro de téléphone invalide. Utilisez un numéro tunisien à 8 chiffres (ou +216…).");
            }

            // Update personal info if any fields are provided. Contact is deliberately NOT in this condition
            // any more — it has its own tri-state block below, and routing it through UpdatePersonalInfo (six
            // positional parameters) would rewrite name, birth date, gender and address on every contact edit.
            if (request.FirstName != null || request.LastName != null || request.DateOfBirthSpecified ||
                request.Gender != null || request.AddressSpecified)
            {
                var firstName = request.FirstName ?? patient.FirstName;
                var lastName = request.LastName ?? patient.LastName;
                // Tri-state, per the property's note: an omitted key keeps the stored date, an explicit null clears it.
                var dateOfBirth = request.DateOfBirthSpecified ? request.DateOfBirth : patient.DateOfBirth;

                if (dateOfBirth is { } born)
                {
                    dateOfBirth = born.Kind switch
                    {
                        DateTimeKind.Unspecified => DateTime.SpecifyKind(born, DateTimeKind.Utc),
                        DateTimeKind.Local => born.ToUniversalTime(),
                        _ => born,
                    };
                }

                var gender = request.Gender ?? patient.Gender;

                // Tri-state: an unspecified block keeps the stored address, a specified `null` clears it, a
                // specified block replaces it. `request.Address != null` was the whole bug — it made "clear"
                // and "leave alone" the same request.
                Address? address;
                if (!request.AddressSpecified)
                {
                    address = patient.Address;
                }
                else if (request.Address != null)
                {
                    address = new Address(
                        request.Address.Street,
                        request.Address.City,
                        request.Address.State,
                        request.Address.ZipCode,
                        request.Address.Country);
                }
                else
                {
                    address = null;
                }

                patient.UpdatePersonalInfo(
                    firstName, lastName, dateOfBirth, gender, patient.Email, patient.PhoneNumber, address);
            }

            // Contact, tri-state. Each field is resolved independently: an unspecified one keeps whatever is
            // stored, a specified-but-blank one clears.
            if (request.EmailSpecified || request.PhoneNumberSpecified)
            {
                var email = request.EmailSpecified
                    ? (string.IsNullOrWhiteSpace(request.Email) ? null : new Email(request.Email))
                    : patient.Email;
                var phoneNumber = request.PhoneNumberSpecified
                    ? (string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : new PhoneNumber(request.PhoneNumber))
                    : patient.PhoneNumber;

                patient.UpdateContact(email, phoneNumber);
            }

            // Update insurance info. A null/omitted InsuranceInfo clears the stored insurance
            // (the edit dialog sends undefined when both insurance fields are emptied).
            if (request.InsuranceInfo != null)
            {
                // Either side is enough (AC-21) — but a block with *neither* is how the dialog says « clear it »,
                // and the value object refuses that, so it is turned into a clear here rather than a 500.
                var hasAnySide =
                    !string.IsNullOrWhiteSpace(request.InsuranceInfo.Provider) ||
                    !string.IsNullOrWhiteSpace(request.InsuranceInfo.PolicyNumber);

                patient.UpdateInsuranceInfo(hasAnySide
                    ? new InsuranceInfo(
                        request.InsuranceInfo.Provider,
                        request.InsuranceInfo.PolicyNumber,
                        request.InsuranceInfo.GroupNumber,
                        request.InsuranceInfo.ExpiryDate)
                    : null);
            }
            else
            {
                patient.UpdateInsuranceInfo(null);
            }

            // CNAM identity. Unlike insurance, a null/omitted block LEAVES it unchanged (DEV-1) — the edit
            // dialog always sends a present block, so a present-but-empty block still clears the stored value.
            if (request.CnamInfo != null)
            {
                patient.UpdateCnamInfo(request.CnamInfo.ToDomain());
            }

            // Update medical history if provided
            if (request.MedicalHistory != null || request.Allergies != null)
            {
                var medicalHistory = request.MedicalHistory ?? patient.MedicalHistory;
                var allergies = request.Allergies ?? patient.Allergies;
                patient.UpdateMedicalHistory(medicalHistory, allergies);
            }

            // Emergency contact (finding #11): a present block (either field non-null) sets or clears both;
            // an omitted block (both null) leaves the stored value unchanged.
            if (request.EmergencyContactName != null || request.EmergencyContactPhone != null)
            {
                var emergencyPhone = string.IsNullOrWhiteSpace(request.EmergencyContactPhone)
                    ? null
                    : new PhoneNumber(request.EmergencyContactPhone);
                patient.UpdateEmergencyContact(
                    string.IsNullOrWhiteSpace(request.EmergencyContactName) ? null : request.EmergencyContactName.Trim(),
                    emergencyPhone);
            }

            // Dentition: only ever changed when explicitly sent. See the property's remark on why there is no
            // age fallback on this path.
            var requestedDentition = DentitionRules.Parse(request.Dentition);
            if (requestedDentition.HasValue && requestedDentition.Value != patient.Dentition)
            {
                patient.SetDentition(requestedDentition.Value);
            }

            // « Adressé par »: a present value sets it, a present-but-blank one clears it (SetReferredBy
            // normalizes blank to null), an omitted one leaves it alone.
            if (request.ReferredBy != null)
            {
                patient.SetReferredBy(request.ReferredBy);
            }

            // Patient-level notes: a present block resolves each field independently, so clearing one leaves the
            // other alone. Passing both straight to UpdateNotes would blank whichever key the caller omitted.
            if (request.Notes != null || request.ImportantNotes != null)
            {
                patient.UpdateNotes(
                    request.Notes ?? patient.Notes,
                    request.ImportantNotes ?? patient.ImportantNotes);
            }

            // Reminder consent. Its own key and its own mutator: it must NOT ride along with the phone number,
            // or correcting a typo in the number would quietly re-enrol a patient who had refused.
            var requestedConsent = ReminderConsentRules.Parse(request.ReminderConsent);
            if (requestedConsent.HasValue)
            {
                patient.SetReminderConsent(
                    requestedConsent.Value, DateTime.UtcNow, _clinicContext.GetUserEmail());
            }

            // Patient flag ("Signaler ce patient"): a single active HighPriority flag carries the toggle
            // + note; it feeds the "Urgents" KPI and the flagged filter. A null IsFlagged leaves it unchanged.
            if (request.IsFlagged.HasValue)
            {
                var activeFlag = patient.Flags.FirstOrDefault(f => f.IsActive);
                if (request.IsFlagged.Value)
                {
                    if (activeFlag != null)
                    {
                        activeFlag.Update(activeFlag.Description, request.FlagNotes);
                    }
                    else
                    {
                        patient.AddFlag(new PatientFlag(
                            Guid.NewGuid(), patient.Id, PatientFlagType.HighPriority, SignaledFlagDescription, request.FlagNotes));
                    }
                }
                else
                {
                    foreach (var flag in patient.Flags.Where(f => f.IsActive).ToList())
                    {
                        flag.Deactivate();
                    }
                }
            }

            // Validate the save against the version the USER was editing, not the one this
            // handler just loaded — that one always matches and would detect nothing.
            _unitOfWork.SetExpectedVersion(patient, request.Version);
            await _patientRepository.UpdateAsync(patient, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            /*
             * The shared mapper, not a fifth hand-written copy.
             *
             * ⚠️ This is load-bearing for L1b, not tidiness. The client now applies **this response** to its
             * local state instead of spreading its own request over the previous patient — which is what stops
             * the UI showing a value the server rejected. That only works if the response is complete, and the
             * copy that used to live here omitted `IsArchived` / `ArchivedAt` / `ArchiveReason`: applying it
             * would have made « Ce patient est archivé » disappear from the page on every unrelated save.
             * `PatientMappingExtensions` exists for exactly this reason and says so in its own doc block.
             */
            return Result<PatientDto>.Success(patient.ToDto());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PatientDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}

