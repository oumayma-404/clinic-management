using System.IO.Compression;
using System.Text;
using ClinicManagement.Application.Common.Csv;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>What one patient's dossier came out as.</summary>
/// <param name="Content">The ZIP.</param>
/// <param name="FileName">Dated with the clinic's own day, like every other export in the product.</param>
/// <param name="SectionCount">How many record sections carried at least one row — recorded in the journal.</param>
/// <param name="FilesIncluded">Uploaded files whose bytes are in the ZIP.</param>
/// <param name="FilesListedOnly">Uploaded files named in the manifest but not enclosed. See the class note.</param>
public sealed record PatientDossier(
    byte[] Content,
    string FileName,
    int SectionCount,
    int FilesIncluded,
    int FilesListedOnly);

/// <summary>
/// Assembles <b>one patient's complete record</b> into a single readable archive.
///
/// <para><b>Why this exists.</b> Every export in the product was list-scoped — the patient roster, the agenda,
/// the invoices — and the whole-clinic ZIP is the practice's own backup, not a person's file. So <b>nothing
/// assembled one patient's dossier</b>, and a cabinet asked for a copy of somebody's record had to collect it by
/// hand from about ten screens. That is the right of access under <i>loi organique 2004-63</i>, and it is also
/// the request a practice fields constantly for an ordinary reason: a patient changing dentist.</para>
///
/// <para><b>Readable, not re-importable.</b> Deliberately not <c>ClinicArchivePackager</c>'s format, which exists
/// so a cabinet can restore itself: its manifest is machine-shaped, it carries rows no patient has any business
/// receiving, and it is scoped to a clinic. This produces CSVs a person can open and PDFs they can read.</para>
///
/// <para>⚠️ <b>Files that live at the cabinet are LISTED, never silently omitted.</b> Since the coffre feature a
/// file's original may be held on the practice's own machine rather than on the server, so the ZIP cannot contain
/// it. Dropping it would make the archive quietly incomplete — the reader would have no way to know a
/// radiograph existed. It is named in the manifest with its date and its state instead, and
/// <c>LISEZ-MOI.txt</c> says so in the first paragraph, so the practice knows what it still has to attach by
/// hand.</para>
///
/// <para>⚠️ <b>The odontogram and the acts are exported as text, not as a picture.</b> A tooth chart is a
/// rendering of rows; the rows are what the patient is entitled to and what another practitioner can actually
/// use.</para>
/// </summary>
public static class PatientDossierPackager
{
    public const string ContentType = "application/zip";

    /// <summary>What the reader is told before anything else. Written for a patient, not for an operator.</summary>
    private const string ReadMeHeader =
        "DOSSIER PATIENT\r\n"
        + "===============\r\n\r\n"
        + "Cette archive contient l'ensemble des informations enregistrées à votre sujet dans le logiciel de\r\n"
        + "gestion de votre cabinet dentaire, à la date indiquée ci-dessous.\r\n\r\n"
        + "Les fichiers .csv s'ouvrent avec un tableur (Excel, LibreOffice) ou un simple éditeur de texte.\r\n"
        + "Le dossier « fichiers » contient vos radiographies, scanners et documents importés.\r\n\r\n";

    public static PatientDossier Build(
        Patient patient,
        string clinicName,
        IReadOnlyList<Appointment> appointments,
        IReadOnlyList<DentalRecord> dentalRecords,
        IReadOnlyList<ToothState> toothStates,
        IReadOnlyList<MedicalDocument> documents,
        IReadOnlyList<PatientFile> files,
        IReadOnlyList<(Guid FileId, string EntryName, byte[] Bytes)> fileContents,
        DateTime generatedAtClinicLocal)
    {
        var sections = 0;
        var enclosed = fileContents.Select(f => f.FileId).ToHashSet();

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            void AddCsv(string name, CsvTable table)
            {
                if (table.RowCount == 0)
                {
                    return;
                }

                sections++;
                Write(zip, name, table.ToBytes());
            }

            AddCsv("identite.csv", Identity(patient));
            AddCsv("antecedents-medicaux.csv", MedicalHistory(patient));
            AddCsv("antecedents-familiaux.csv", FamilyHistory(patient));
            AddCsv("rendez-vous.csv", Appointments(appointments));
            AddCsv("fiches-de-soins.csv", DentalRecords(dentalRecords));
            AddCsv("odontogramme.csv", Odontogram(toothStates));
            AddCsv("documents.csv", Documents(documents));
            AddCsv("fichiers.csv", FileManifest(files, enclosed));

            foreach (var (_, entryName, bytes) in fileContents)
            {
                Write(zip, $"fichiers/{entryName}", bytes);
            }

            Write(
                zip,
                "LISEZ-MOI.txt",
                Encoding.UTF8.GetBytes(ReadMe(
                    patient, clinicName, generatedAtClinicLocal, files.Count, enclosed.Count)));
        }

        return new PatientDossier(
            output.ToArray(),
            $"dossier-{Slug(patient.GetFullName())}-{generatedAtClinicLocal:yyyy-MM-dd}.zip",
            sections,
            enclosed.Count,
            files.Count - enclosed.Count);
    }

    private static void Write(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        stream.Write(bytes, 0, bytes.Length);
    }

    // ── the sections ───────────────────────────────────────────────────────────────────────────────────────

    private static CsvTable Identity(Patient p)
    {
        var table = CsvTable.Create("Champ", "Valeur");

        void Row(string field, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                table.Row(CsvCell.Text(field), CsvCell.Text(value));
            }
        }

        Row("Nom", p.LastName);
        Row("Prénom", p.FirstName);
        Row("Date de naissance", p.DateOfBirth.HasValue ? CsvCell.CalendarDay(p.DateOfBirth) : null);
        Row("Sexe", PatientGender.Label(p.Gender));
        Row("Téléphone", p.PhoneNumber?.Value);
        Row("Email", p.Email?.Value);
        Row("Adresse", p.Address?.Street);
        Row("Ville", p.Address?.City);
        Row("Gouvernorat", p.Address?.State);
        Row("Code postal", p.Address?.ZipCode);
        Row("Identifiant CNAM", p.CnamInfo?.IdentifiantUnique);
        Row("Régime", p.CnamInfo?.Regime);
        Row("Assurance", p.InsuranceInfo?.Provider);
        Row("N° de police", p.InsuranceInfo?.PolicyNumber);
        Row("Allergies", p.Allergies);
        Row("Antécédents médicaux (résumé)", p.MedicalHistory);
        Row("Contact d'urgence", p.EmergencyContactName);
        Row("Téléphone d'urgence", p.EmergencyContactPhone?.Value);
        Row("Adressé par", p.ReferredBy);
        Row("Notes", p.Notes);
        Row("Notes importantes", p.ImportantNotes);
        Row("Inscrit le", CsvCell.Date(p.CreatedAt));

        return table;
    }

    private static CsvTable MedicalHistory(Patient p)
    {
        var table = CsvTable.Create("Date", "Description", "Notes");

        foreach (var entry in p.MedicalHistoryEntries.OrderBy(e => e.Date))
        {
            table.Row(CsvCell.CalendarDay(entry.Date), CsvCell.Text(entry.Description), CsvCell.Text(entry.Notes));
        }

        return table;
    }

    private static CsvTable FamilyHistory(Patient p)
    {
        var table = CsvTable.Create("Lien de parenté", "Affection", "Notes");

        foreach (var entry in p.FamilyHistoryEntries)
        {
            table.Row(
                CsvCell.Text(entry.Relationship), CsvCell.Text(entry.Condition), CsvCell.Text(entry.Notes));
        }

        return table;
    }

    private static CsvTable Appointments(IReadOnlyList<Appointment> appointments)
    {
        var table = CsvTable.Create("Date et heure", "Praticien", "Actes", "Statut", "Notes");

        foreach (var a in appointments.OrderBy(a => a.AppointmentDateTime))
        {
            table.Row(
                CsvCell.Moment(a.AppointmentDateTime),
                CsvCell.Text(a.DoctorName),
                CsvCell.Text(string.Join(" + ", a.Procedures.OrderBy(p => p.SequenceNumber).Select(p => p.ProcedureName))),
                CsvCell.Text(Appointment.FrenchLabel(a.Status)),
                CsvCell.Text(a.Notes));
        }

        return table;
    }

    private static CsvTable DentalRecords(IReadOnlyList<DentalRecord> records)
    {
        var table = CsvTable.Create(
            "Date", "Acte", "Dents", "Faces", "État résultant", "Coût", "Notes");

        foreach (var record in records.OrderBy(r => r.InterventionDate))
        {
            if (record.Acts.Count == 0)
            {
                table.Row(
                    CsvCell.CalendarDay(record.InterventionDate),
                    CsvCell.Text(record.ProcedureType), CsvCell.Text(null), CsvCell.Text(null),
                    CsvCell.Text(null), CsvCell.Money(record.Cost),
                    CsvCell.Text(string.Join(" · ", record.Notes)));
                continue;
            }

            foreach (var act in record.Acts)
            {
                table.Row(
                    CsvCell.CalendarDay(record.InterventionDate),
                    CsvCell.Text(act.ProcedureName),
                    CsvCell.Text(string.Join(", ", act.ToothNumbers)),
                    CsvCell.Text(act.Surfaces),
                    CsvCell.Text(act.ResultingCondition?.ToString()),
                    CsvCell.Money(act.Cost),
                    CsvCell.Text(act.Note));
            }
        }

        return table;
    }

    private static CsvTable Odontogram(IReadOnlyList<ToothState> states)
    {
        var table = CsvTable.Create("Dent", "État", "Faces", "Date", "Origine", "Note");

        foreach (var s in states.OrderBy(s => s.ToothNumber).ThenBy(s => s.TreatmentDate))
        {
            table.Row(
                CsvCell.Number(s.ToothNumber),
                CsvCell.Text(s.Condition.ToString()),
                CsvCell.Text(s.Surfaces),
                CsvCell.CalendarDay(s.TreatmentDate),
                CsvCell.Text(s.Source.ToString()),
                CsvCell.Text(s.Note));
        }

        return table;
    }

    private static CsvTable Documents(IReadOnlyList<MedicalDocument> documents)
    {
        var table = CsvTable.Create("Date", "Type", "Praticien", "Destinataire");

        foreach (var d in documents.OrderBy(d => d.CreatedAt))
        {
            table.Row(
                CsvCell.Date(d.CreatedAt),
                CsvCell.Text(d.DocumentType),
                CsvCell.Text(d.DoctorName),
                CsvCell.Text(d.RecipientDoctorName));
        }

        return table;
    }

    /// <summary>
    /// ⚠️ <b>Every file is listed, including the ones whose bytes are not in the archive.</b> « Joint » vs
    /// « conservé au cabinet » is the difference between a complete archive and one that is quietly missing a
    /// radiograph the reader never learns existed.
    /// </summary>
    private static CsvTable FileManifest(IReadOnlyList<PatientFile> files, IReadOnlySet<Guid> enclosed)
    {
        var table = CsvTable.Create("Date", "Nom du fichier", "Type", "Taille (octets)", "Dans cette archive");

        foreach (var f in files.OrderBy(f => f.UploadedAt))
        {
            table.Row(
                CsvCell.Date(f.UploadedAt),
                CsvCell.Text(f.FileName),
                CsvCell.Text(f.FileType.ToString()),
                CsvCell.Number((int)f.FileSize),
                CsvCell.Text(enclosed.Contains(f.Id) ? "Joint" : "Non — conservé au cabinet"));
        }

        return table;
    }

    private static string ReadMe(
        Patient patient, string clinicName, DateTime generatedAt, int fileCount, int enclosedCount)
    {
        var builder = new StringBuilder(ReadMeHeader);

        builder.Append("Patient : ").Append(patient.GetFullName()).Append("\r\n");
        builder.Append("Cabinet : ").Append(clinicName).Append("\r\n");
        builder.Append("Édité le : ").Append(generatedAt.ToString("dd/MM/yyyy")).Append("\r\n\r\n");

        if (fileCount > enclosedCount)
        {
            builder.Append("IMPORTANT\r\n---------\r\n");
            builder.Append(fileCount - enclosedCount)
                   .Append(" fichier(s) sur ")
                   .Append(fileCount)
                   .Append(" ne sont pas joints à cette archive : leur original est conservé sur le poste du\r\n")
                   .Append("cabinet et non sur le serveur. Ils sont tous nommés dans « fichiers.csv », avec leur\r\n")
                   .Append("date. Demandez-les à votre cabinet, qui pourra vous les remettre séparément.\r\n\r\n");
        }

        builder.Append("CONTENU\r\n-------\r\n");
        builder.Append("identite.csv ................ vos coordonnées et informations administratives\r\n");
        builder.Append("antecedents-medicaux.csv .... vos antécédents médicaux\r\n");
        builder.Append("antecedents-familiaux.csv ... vos antécédents familiaux\r\n");
        builder.Append("rendez-vous.csv ............. l'historique de vos rendez-vous\r\n");
        builder.Append("fiches-de-soins.csv ......... les soins réalisés, acte par acte\r\n");
        builder.Append("odontogramme.csv ............ l'état relevé de chaque dent\r\n");
        builder.Append("documents.csv ............... ordonnances, certificats et bulletins établis\r\n");
        builder.Append("fichiers.csv ................ la liste de vos radiographies et documents importés\r\n");
        builder.Append("fichiers/ ................... ces fichiers eux-mêmes\r\n\r\n");
        builder.Append("Une section absente signifie qu'elle ne contenait aucune information.\r\n\r\n");
        builder.Append("Ce dossier vous appartient. Pour toute question sur son contenu, ou pour demander une\r\n");
        builder.Append("rectification, adressez-vous à votre cabinet.\r\n");

        return builder.ToString();
    }

    /// <summary>
    /// A file-name-safe form of the patient's name. Accents and spaces only — this is a *file name*, not an
    /// identifier, and it is the patient's own name in their own copy.
    /// </summary>
    private static string Slug(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var c in name.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (c is ' ' or '-' or '\'')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "patient" : slug;
    }
}
