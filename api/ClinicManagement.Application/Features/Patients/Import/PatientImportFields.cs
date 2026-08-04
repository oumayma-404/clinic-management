namespace ClinicManagement.Application.Features.Patients.Import;

/// <summary>
/// Which patient field a CSV column feeds (L5, import half).
///
/// <para><b>English tokens, French labels</b> — the repo's standing convention for a closed value set that crosses
/// the wire (<c>lib/specialties.ts</c>, <c>appointment-labels.ts</c>, the weekday keys). The client sends the token
/// in its mapping and renders <see cref="PatientImportFields.Label"/>, so the French wording can be corrected
/// without invalidating a mapping a user is halfway through building.</para>
///
/// <para>The set is deliberately <b>exactly the writable half of <c>ExportTables.Patients</c></b>: it is what makes
/// the round trip the spec asks for (« export → import → identical set ») a property of the design rather than a
/// coincidence. « Archivé » and « Inscrit le » are the two export columns absent here, both because they are the
/// product's own bookkeeping — importing an <c>IsArchived</c> would create a patient nobody can see, and importing a
/// <c>CreatedAt</c> would let a file rewrite when the practice registered someone.</para>
/// </summary>
public enum PatientImportField
{
    LastName,
    FirstName,
    DateOfBirth,
    Gender,
    PhoneNumber,
    Email,
    Street,
    City,
    State,
    ZipCode,
    CnamIdentifiant,
    InsuranceProvider,
    InsurancePolicyNumber,
    MedicalHistory,
    Allergies,
    EmergencyContactName,
    EmergencyContactPhone,
    ReferredBy,
}

/// <summary>
/// The field vocabulary an import maps onto, its French labels, and the header auto-detection.
///
/// <para><b>Why auto-detection is not a convenience.</b> The mapping UI exists because a file from Julie or LOGOSw
/// names its columns whatever it names them — but the first file most clinics will try is <i>this product's own
/// export</i>, and a mapping screen that starts blank for a file we wrote ourselves reads as the import not
/// recognising its own format. Detection is a starting point the user can always override; it is never authoritative.</para>
/// </summary>
public static class PatientImportFields
{
    /// <summary>Every field, in the order the mapping screen lists them — identity first, then the rest.</summary>
    public static readonly IReadOnlyList<PatientImportField> All = new[]
    {
        PatientImportField.LastName,
        PatientImportField.FirstName,
        PatientImportField.DateOfBirth,
        PatientImportField.Gender,
        PatientImportField.PhoneNumber,
        PatientImportField.Email,
        PatientImportField.Street,
        PatientImportField.City,
        PatientImportField.State,
        PatientImportField.ZipCode,
        PatientImportField.CnamIdentifiant,
        PatientImportField.InsuranceProvider,
        PatientImportField.InsurancePolicyNumber,
        PatientImportField.MedicalHistory,
        PatientImportField.Allergies,
        PatientImportField.EmergencyContactName,
        PatientImportField.EmergencyContactPhone,
        PatientImportField.ReferredBy,
    };

    /// <summary>
    /// The only two fields an import cannot proceed without — the same two the patient form requires, and the same
    /// two <c>Patient</c>'s constructor requires. Everything else is genuinely optional, including the phone and the
    /// email (both nullable since the four contact sentinels were retired).
    /// </summary>
    public static readonly IReadOnlyList<PatientImportField> Required = new[]
    {
        PatientImportField.LastName,
        PatientImportField.FirstName,
    };

    public static bool IsRequired(PatientImportField field) => Required.Contains(field);

    /// <summary>The French label, matching the export's own column heading wherever there is one.</summary>
    public static string Label(PatientImportField field) => field switch
    {
        PatientImportField.LastName => "Nom",
        PatientImportField.FirstName => "Prénom",
        PatientImportField.DateOfBirth => "Date de naissance",
        PatientImportField.Gender => "Sexe",
        PatientImportField.PhoneNumber => "Téléphone",
        PatientImportField.Email => "Email",
        PatientImportField.Street => "Adresse",
        PatientImportField.City => "Ville",
        PatientImportField.State => "Gouvernorat",
        PatientImportField.ZipCode => "Code postal",
        PatientImportField.CnamIdentifiant => "Identifiant CNAM",
        PatientImportField.InsuranceProvider => "Assurance",
        PatientImportField.InsurancePolicyNumber => "N° police",
        PatientImportField.MedicalHistory => "Antécédents médicaux",
        PatientImportField.Allergies => "Allergies",
        PatientImportField.EmergencyContactName => "Contact d'urgence",
        PatientImportField.EmergencyContactPhone => "Téléphone d'urgence",
        PatientImportField.ReferredBy => "Adressé par",
        _ => field.ToString(),
    };

    /// <summary>
    /// Header spellings that identify a field, matched case- and accent-insensitively through
    /// <see cref="Common.SearchTerm.Normalize"/> — the solution's existing folding authority, so the import cannot
    /// disagree with the patient search about whether « Prénom » and « prenom » are the same word.
    ///
    /// <para>Each list leads with this product's own export heading, then the spellings a French or English
    /// spreadsheet actually uses. ⚠️ « Adresse » is the <b>street</b> and « Ville » the city, matching the export;
    /// a file whose single « Adresse » column holds the whole address maps to the street and the rest stays blank,
    /// which is the honest outcome — there is no reliable way to split a free-text address, and guessing would file
    /// a governorate as a postcode.</para>
    /// </summary>
    private static readonly Dictionary<PatientImportField, string[]> Aliases = new()
    {
        [PatientImportField.LastName] = new[] { "Nom", "Nom de famille", "Last name", "Lastname", "Surname" },
        [PatientImportField.FirstName] = new[] { "Prénom", "Prenom", "First name", "Firstname", "Given name" },
        [PatientImportField.DateOfBirth] = new[]
        {
            "Date de naissance", "Naissance", "Né le", "Née le", "Date of birth", "DOB", "Birthdate",
        },
        [PatientImportField.Gender] = new[] { "Sexe", "Genre", "Gender", "Sex" },
        [PatientImportField.PhoneNumber] = new[]
        {
            "Téléphone", "Telephone", "Tél", "Tel", "Mobile", "GSM", "Portable", "Phone", "Phone number",
        },
        [PatientImportField.Email] = new[] { "Email", "E-mail", "Courriel", "Adresse email" },
        [PatientImportField.Street] = new[] { "Adresse", "Rue", "Street", "Address", "Address line 1" },
        [PatientImportField.City] = new[] { "Ville", "City", "Localité", "Commune" },
        [PatientImportField.State] = new[] { "Gouvernorat", "Région", "Region", "State", "Province" },
        [PatientImportField.ZipCode] = new[] { "Code postal", "CP", "Zip", "Zip code", "Postal code" },
        [PatientImportField.CnamIdentifiant] = new[]
        {
            "Identifiant CNAM", "CNAM", "Identifiant unique", "N° CNAM", "Numéro CNAM",
        },
        [PatientImportField.InsuranceProvider] = new[] { "Assurance", "Assureur", "Mutuelle", "Insurance", "Insurance provider" },
        [PatientImportField.InsurancePolicyNumber] = new[]
        {
            "N° police", "Numéro de police", "Police", "Policy number", "Policy",
        },
        [PatientImportField.MedicalHistory] = new[]
        {
            "Antécédents médicaux", "Antécédents", "Antecedents", "Medical history", "Historique médical",
        },
        [PatientImportField.Allergies] = new[] { "Allergies", "Allergie", "Allergy" },
        [PatientImportField.EmergencyContactName] = new[]
        {
            "Contact d'urgence", "Contact urgence", "Personne à contacter", "Emergency contact",
        },
        [PatientImportField.EmergencyContactPhone] = new[]
        {
            "Téléphone d'urgence", "Tél urgence", "Emergency phone", "Emergency contact phone",
        },
        [PatientImportField.ReferredBy] = new[] { "Adressé par", "Adresse par", "Référé par", "Referred by", "Referrer" },
    };

    /// <summary>
    /// Best-effort mapping from a file's headers to fields: <c>field → column index</c>.
    ///
    /// <para>⚠️ <b>Longest alias wins, and a column is claimed once.</b> Both matter for the same header pair:
    /// « Téléphone » and « Téléphone d'urgence » — a shortest-first or first-match-wins scan assigns the emergency
    /// column to <see cref="PatientImportField.PhoneNumber"/> if it happens to come first, which silently files a
    /// relative's number as the patient's and would then send that relative every reminder. Scanning the more
    /// specific spellings first, and never reusing a claimed column, removes the ordering dependency.</para>
    /// </summary>
    public static Dictionary<PatientImportField, int> Detect(IReadOnlyList<string> headers)
    {
        var normalizedHeaders = headers.Select(Common.SearchTerm.Normalize).ToList();
        var mapping = new Dictionary<PatientImportField, int>();
        var claimed = new HashSet<int>();

        // Every (field, alias) pair, most specific spelling first. The alias length is the specificity proxy:
        // « telephone d'urgence » is longer than « telephone », which is exactly the discrimination needed.
        var candidates = Aliases
            .SelectMany(pair => pair.Value.Select(alias => (Field: pair.Key, Normalized: Common.SearchTerm.Normalize(alias))))
            .Where(c => c.Normalized.Length > 0)
            .OrderByDescending(c => c.Normalized.Length)
            .ToList();

        foreach (var candidate in candidates)
        {
            if (mapping.ContainsKey(candidate.Field))
            {
                continue;
            }

            for (var i = 0; i < normalizedHeaders.Count; i++)
            {
                if (claimed.Contains(i) || normalizedHeaders[i] != candidate.Normalized)
                {
                    continue;
                }

                mapping[candidate.Field] = i;
                claimed.Add(i);
                break;
            }
        }

        return mapping;
    }
}
