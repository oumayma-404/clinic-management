using System.Globalization;
using System.Net.Mail;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Csv;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Patients.Import;

/// <summary>
/// What one CSV row turned out to be.
/// </summary>
/// <param name="Command">
/// The row as a <see cref="CreatePatientCommand"/> — <c>null</c> only when <see cref="Errors"/> is non-empty.
/// ⚠️ It is the <b>real</b> command, not an import-shaped near-copy: the commit hands it to the same construction
/// path a hand-typed patient goes through, which is what the spec means by « reuse <c>CreatePatientCommand</c>'s
/// validation rather than a parallel path ». A parallel path is how imported rows end up bypassing rules every
/// typed row obeys.
/// </param>
/// <param name="Errors">
/// French reasons this row cannot be created. Non-empty means the row is <b>skipped</b>, never partially applied.
/// </param>
/// <param name="Warnings">
/// French notes about something the import will <b>silently drop or default</b> if the row is created as-is — a
/// partial address, an unreadable « Sexe », a missing date of birth. These are not errors, and they are surfaced for
/// exactly one reason: each of them is a place where <c>CreatePatientCommand</c> is deliberately lenient (an
/// incomplete <c>Address</c> becomes <c>null</c>, an empty date of birth becomes « 30 years ago »), and lenience
/// that nobody is told about is how 3 000 patients arrive with a birth year the practice never supplied.
/// </param>
public sealed record PatientImportRowRead(
    CreatePatientCommand? Command,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Turns a mapped CSV row into a <see cref="CreatePatientCommand"/>, with per-row French reasons (L5, import half).
///
/// <para><b>Pure and static on purpose.</b> No repository, no clinic, no DbContext — so the dry run and the commit
/// read a row through the identical code, and a test can assert « this row is refused because the phone is not
/// Tunisian » with no database at all. If the preview and the commit interpreted a row differently, the preview
/// would be a promise the commit does not keep, which is the whole value of a dry run.</para>
///
/// <para>⚠️ <b>Phone numbers are normalised to <c>+216</c> E.164 on the way in</b> (<see cref="PhoneNumber.ToE164"/>),
/// which is deliberately <i>not</i> what the hand-typed write path does — <c>PhoneNumber</c>'s constructor only
/// trims, so a number typed as « 20 123 456 » is stored with its spaces. The spec names that standing defect and
/// says the import must not replicate it: a file arrives with eight formats of the same number, and storing them
/// verbatim would make « do we already have this patient? » unanswerable for ever.</para>
/// </summary>
public static class PatientImportRowReader
{
    /// <summary>
    /// The date formats accepted, most-likely first. ⚠️ <c>MM/dd/yyyy</c> is deliberately <b>absent</b>: it is
    /// indistinguishable from <c>dd/MM/yyyy</c> for the first twelve days of every month, so accepting both would
    /// silently move a birthday for two thirds of a practice's patients. A file in US order is a file the operator
    /// must convert, and the refusal says which forms are read.
    /// </summary>
    private static readonly string[] DateFormats =
    {
        "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "yyyy/MM/dd", "dd-MM-yyyy", "d-M-yyyy", "dd.MM.yyyy", "d.M.yyyy",
    };

    /// <summary>Before this, a date of birth is a typo rather than a person.</summary>
    private static readonly DateTime EarliestPlausibleBirth = new(1900, 1, 1);

    /// <summary>The CNAM identifiant unique is ten digits (mirrors <c>web/lib/cnam.ts</c>).</summary>
    private const int CnamIdentifiantDigits = 10;

    public static PatientImportRowRead Read(
        CsvRow row,
        IReadOnlyDictionary<PatientImportField, int> mapping)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        string Value(PatientImportField field) =>
            mapping.TryGetValue(field, out var index) ? row.Cell(index).Trim() : string.Empty;

        bool Mapped(PatientImportField field) => mapping.ContainsKey(field);

        var lastName = Value(PatientImportField.LastName);
        var firstName = Value(PatientImportField.FirstName);

        if (lastName.Length == 0)
        {
            errors.Add("Le nom est vide.");
        }

        if (firstName.Length == 0)
        {
            errors.Add("Le prénom est vide.");
        }

        // ---- Date of birth ------------------------------------------------------------------------------------
        DateTime? dateOfBirth = null;
        var rawDob = Value(PatientImportField.DateOfBirth);
        if (rawDob.Length > 0)
        {
            if (TryParseDate(rawDob, out var parsed))
            {
                if (parsed > ClinicClock.ClinicToday())
                {
                    errors.Add($"Date de naissance dans le futur : « {rawDob} ».");
                }
                else if (parsed < EarliestPlausibleBirth)
                {
                    errors.Add($"Date de naissance invraisemblable : « {rawDob} ».");
                }
                else
                {
                    dateOfBirth = parsed;
                }
            }
            else
            {
                errors.Add($"Date de naissance illisible : « {rawDob} ». Formats acceptés : 31/12/1980 ou 1980-12-31.");
            }
        }
        else if (Mapped(PatientImportField.DateOfBirth))
        {
            // Not an error — the command defaults it — but the default is « 30 years ago », and the patient's
            // dentition (adulte / mixte / enfant) is derived from it, so an unnoticed default charts an adult arch
            // for a child.
            warnings.Add("Date de naissance absente : 30 ans par défaut, et la dentition en sera déduite.");
        }

        // ---- Gender ------------------------------------------------------------------------------------------
        var rawGender = Value(PatientImportField.Gender);
        var gender = PatientGender.Parse(rawGender);
        if (rawGender.Length > 0 && gender == null)
        {
            warnings.Add($"Sexe non reconnu : « {rawGender} ». Enregistré comme « Non précisé ».");
            gender = PatientGender.Unknown;
        }

        // ---- Phone -------------------------------------------------------------------------------------------
        var rawPhone = Value(PatientImportField.PhoneNumber);
        string? phone = null;
        if (rawPhone.Length > 0)
        {
            phone = PhoneNumber.ToE164(rawPhone);
            if (phone == null)
            {
                // The same refusal `CreatePatientCommand` gives, reached at the row rather than at the request, so
                // one bad number in 3 000 does not refuse the file.
                errors.Add(
                    $"Numéro de téléphone invalide : « {rawPhone} ». Utilisez un numéro tunisien à 8 chiffres (ou +216…).");
            }
        }

        // ---- Email -------------------------------------------------------------------------------------------
        var rawEmail = Value(PatientImportField.Email);
        string? email = null;
        if (rawEmail.Length > 0)
        {
            if (IsParseableEmail(rawEmail))
            {
                email = rawEmail;
            }
            else
            {
                // Checked here rather than left to `Email`'s constructor: that throws, and a throw inside the
                // commit loop would abandon every row after this one.
                errors.Add($"Adresse email invalide : « {rawEmail} ».");
            }
        }

        // ---- Address (all four parts, or none) ---------------------------------------------------------------
        var street = Value(PatientImportField.Street);
        var city = Value(PatientImportField.City);
        var state = Value(PatientImportField.State);
        var zip = Value(PatientImportField.ZipCode);
        AddressDto? address = null;
        var addressParts = new[] { street, city, state, zip };
        if (addressParts.All(p => p.Length > 0))
        {
            address = new AddressDto { Street = street, City = city, State = state, ZipCode = zip };
        }
        else if (addressParts.Any(p => p.Length > 0))
        {
            // `CreatePatientCommand` requires all four and silently stores null otherwise. Mirroring that is right
            // (one rule, one place) but staying quiet about it is not: the operator would believe they had imported
            // addresses.
            warnings.Add("Adresse incomplète (rue, ville, gouvernorat et code postal sont requis) : non importée.");
        }

        // ---- Insurance (provider + policy, or none) ----------------------------------------------------------
        var insurer = Value(PatientImportField.InsuranceProvider);
        var policy = Value(PatientImportField.InsurancePolicyNumber);
        // Either side is enough (AC-21). A one-sided row used to be dropped with a warning nobody could act on —
        // and on a 3 000-row file a silent drop is unrecoverable without re-importing the whole thing.
        InsuranceInfoDto? insurance = null;
        if (insurer.Length > 0 || policy.Length > 0)
        {
            insurance = new InsuranceInfoDto { Provider = insurer, PolicyNumber = policy };
        }

        // ---- CNAM --------------------------------------------------------------------------------------------
        var cnamId = Value(PatientImportField.CnamIdentifiant);
        CnamInfoDto? cnam = null;
        if (cnamId.Length > 0)
        {
            var digits = new string(cnamId.Where(char.IsDigit).ToArray());
            if (digits.Length != CnamIdentifiantDigits)
            {
                // A warning, not an error: it is stored as given and the BS1 editor already lists a wrong
                // identifiant among its mandatory-field checks. Refusing the patient over it would lose the
                // clinical record to fix a form field.
                warnings.Add(
                    $"Identifiant CNAM à {digits.Length} chiffre(s) au lieu de {CnamIdentifiantDigits} : importé tel quel, à corriger sur la fiche.");
            }

            cnam = new CnamInfoDto { IdentifiantUnique = cnamId };
        }

        // ---- Emergency contact -------------------------------------------------------------------------------
        var emergencyName = Value(PatientImportField.EmergencyContactName);
        var rawEmergencyPhone = Value(PatientImportField.EmergencyContactPhone);
        string? emergencyPhone = null;
        if (rawEmergencyPhone.Length > 0)
        {
            emergencyPhone = PhoneNumber.ToE164(rawEmergencyPhone);
            if (emergencyPhone == null)
            {
                // Deliberately NOT an error, unlike the patient's own number. Nothing dispatches to this one — it
                // is read by a human in an emergency — and refusing a whole patient record because a relative's
                // number is written « 71 555 (bureau) » would lose the record to protect a field nobody sends to.
                emergencyPhone = rawEmergencyPhone;
                warnings.Add($"Téléphone d'urgence non reconnu : « {rawEmergencyPhone} ». Importé tel quel.");
            }
        }

        if (errors.Count > 0)
        {
            return new PatientImportRowRead(null, errors, warnings);
        }

        var command = new CreatePatientCommand
        {
            FirstName = firstName,
            LastName = lastName,
            // Carried through as-is: a row with no date of birth stores none (AC-18, D-1).
            DateOfBirth = dateOfBirth,
            Gender = gender ?? string.Empty,
            Email = email ?? string.Empty,
            PhoneNumber = phone ?? string.Empty,
            Address = address,
            InsuranceInfo = insurance,
            CnamInfo = cnam,
            EmergencyContactName = emergencyName.Length > 0 ? emergencyName : null,
            EmergencyContactPhone = emergencyPhone,
            MedicalHistory = NullIfBlank(Value(PatientImportField.MedicalHistory)),
            Allergies = NullIfBlank(Value(PatientImportField.Allergies)),
            ReferredBy = NullIfBlank(Value(PatientImportField.ReferredBy)),
        };

        return new PatientImportRowRead(command, errors, warnings);
    }

    private static string? NullIfBlank(string value) => value.Length > 0 ? value : null;

    /// <summary>
    /// ⚠️ <see cref="DateTimeStyles.None"/> and an <b>Unspecified</b> result, matching what the JSON body of a
    /// hand-typed patient produces. A date of birth is a calendar day: giving it a zone would let
    /// <c>ApplicationDbContext</c>'s UTC treatment move it by a day for half the values, which for a date of birth
    /// is simply a wrong date — the same reason <c>CsvCell.CalendarDay</c> does no conversion on the way out.
    /// </summary>
    private static bool TryParseDate(string raw, out DateTime value) =>
        DateTime.TryParseExact(raw, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    private static bool IsParseableEmail(string raw)
    {
        // The same test `Email`'s constructor applies, run without letting it throw.
        try
        {
            var address = new MailAddress(raw);
            return address.Address == raw.Trim();
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
