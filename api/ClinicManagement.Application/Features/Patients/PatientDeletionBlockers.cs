using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// Turns the raw linked-data counts into the French, plural-aware list the refusal message and the confirm
/// dialog both show. Shared so the dialog can never promise a deletion the command then refuses.
/// </summary>
public static class PatientDeletionBlockers
{
    /// <summary>One kind of attached record, with the label the user reads and the tab it lives on.</summary>
    public readonly record struct Blocker(string Kind, string Label, int Count, string? Tab);

    public static IReadOnlyList<Blocker> From(PatientLinkedDataCounts counts)
    {
        var blockers = new List<Blocker>();

        Add(blockers, "appointments", counts.Appointments, "rendez-vous", "rendez-vous", "appointments");
        Add(blockers, "invoices", counts.Invoices, "facture", "factures", "factures");
        Add(blockers, "treatmentPlans", counts.TreatmentPlans, "plan de traitement", "plans de traitement", "treatment-plans");
        Add(blockers, "dentalRecords", counts.DentalRecords, "fiche de soins", "fiches de soins", "medical-records");
        Add(blockers, "toothStates", counts.ToothStates, "état dentaire", "états dentaires", "odontogram");
        Add(blockers, "medicalDocuments", counts.MedicalDocuments, "document médical", "documents médicaux", "documents");
        Add(blockers, "files", counts.Files, "fichier", "fichiers", "files");
        Add(blockers, "folders", counts.Folders, "dossier", "dossiers", "files");
        Add(blockers, "flags", counts.Flags, "signalement", "signalements", null);
        Add(blockers, "recurringAppointments", counts.RecurringAppointments, "série de rendez-vous", "séries de rendez-vous", "appointments");
        Add(blockers, "medicalHistory", counts.MedicalHistoryEntries, "antécédent médical", "antécédents médicaux", null);
        Add(blockers, "familyHistory", counts.FamilyHistoryEntries, "antécédent familial", "antécédents familiaux", null);
        Add(blockers, "labOrders", counts.LabOrders, "bon de prothèse", "bons de prothèse", null);
        Add(blockers, "waitingList", counts.WaitingListEntries, "entrée en salle d'attente", "entrées en salle d'attente", null);
        Add(blockers, "notifications", counts.Notifications, "rappel", "rappels", null);

        return blockers;
    }

    /// <summary>
    /// « 3 rendez-vous, 2 factures et 1 plan de traitement » — the enumeration the refusal reads, so the message
    /// names what actually blocks instead of listing three things of which only one can ever trigger.
    /// </summary>
    public static string Describe(PatientLinkedDataCounts counts)
    {
        var parts = From(counts).Select(b => $"{b.Count} {b.Label}").ToList();

        return parts.Count switch
        {
            0 => string.Empty,
            1 => parts[0],
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " et " + parts[^1]
        };
    }

    /// <summary>
    /// The same enumeration with its verb already agreed — « 1 signalement y est rattaché », « 3 rendez-vous et
    /// 1 facture y sont rattachés ».
    ///
    /// <para>⚠️ The verb belongs here and not at the call sites: agreement is a fact about the enumeration
    /// <see cref="Describe"/> just built, and a caller appending « y sont rattachés » to it cannot know whether the
    /// list came to one thing or several. Both refusals that quote a blocker list said « 1 signalement y sont
    /// rattachés » to a dentist before this existed.</para>
    /// </summary>
    public static string DescribeAttached(PatientLinkedDataCounts counts)
    {
        var described = Describe(counts);
        return described.Length == 0
            ? string.Empty
            : $"{described} y {(counts.Total > 1 ? "sont rattachés" : "est rattaché")}";
    }

    private static void Add(
        ICollection<Blocker> blockers,
        string kind,
        int count,
        string singular,
        string plural,
        string? tab)
    {
        if (count > 0)
        {
            blockers.Add(new Blocker(kind, count == 1 ? singular : plural, count, tab));
        }
    }
}
