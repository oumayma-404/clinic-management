using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Domain.Services;

/// <summary>The 17 quick messages every cabinet starts with (staff-chat FR-4). A cabinet with no saved list uses these.</summary>
public static class StaffQuickMessageDefaults
{
    public const string PatientArrivedKey = "patient-arrived";
    public const string EmergencyWaitingKey = "emergency-waiting";
    public const string ChairHelpKey = "chair-help";

    /// <summary>Renamable, never removable, urgency fixed (AC-6.2) — the arrival action and the alerts rely on them.</summary>
    public static readonly IReadOnlySet<string> BuiltInKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        PatientArrivedKey,
        EmergencyWaitingKey,
        ChairHelpKey,
    };

    private static readonly (string Key, string Label, StaffQuickMessageAudience Audience, bool AsksForPatient, bool Urgent)[] Rows =
    {
        (PatientArrivedKey, "Patient arrivé", StaffQuickMessageAudience.Dentist, true, false),
        ("patient-late", "Patient en retard (10 min)", StaffQuickMessageAudience.Dentist, true, false),
        ("patient-cancelled", "Le patient a annulé", StaffQuickMessageAudience.Dentist, true, false),
        ("lab-on-phone", "Le labo au téléphone", StaffQuickMessageAudience.Dentist, false, false),
        ("call-waiting", "Appel en attente pour vous", StaffQuickMessageAudience.Dentist, false, false),
        (EmergencyWaitingKey, "Urgence en salle d'attente", StaffQuickMessageAudience.Dentist, false, true),
        ("next-patient", "Faites entrer le patient suivant", StaffQuickMessageAudience.Desk, false, false),
        (ChairHelpKey, "Besoin d'aide au fauteuil", StaffQuickMessageAudience.Desk, false, true),
        ("prepare-payment", "Préparez l'encaissement", StaffQuickMessageAudience.Desk, true, false),
        ("control-in-a-week", "RDV de contrôle dans 1 semaine", StaffQuickMessageAudience.Desk, true, false),
        ("call-lab", "Appelez le labo", StaffQuickMessageAudience.Desk, false, false),
        ("bring-file", "Apportez-moi le dossier / la radio", StaffQuickMessageAudience.Desk, true, false),
        ("running-late", "J'ai 10 min de retard", StaffQuickMessageAudience.Desk, false, false),
        ("reply-ok", "OK", StaffQuickMessageAudience.Reply, false, false),
        ("reply-coming", "J'arrive", StaffQuickMessageAudience.Reply, false, false),
        ("reply-two-minutes", "2 min", StaffQuickMessageAudience.Reply, false, false),
        ("reply-not-now", "Pas maintenant", StaffQuickMessageAudience.Reply, false, false),
    };

    /// <summary>A fresh copy each call — the list is owned by whoever asks.</summary>
    public static IReadOnlyList<StaffQuickMessage> Items() => Rows
        .Select((r, i) => new StaffQuickMessage(r.Key, r.Label, r.Audience, r.AsksForPatient, r.Urgent, BuiltInKeys.Contains(r.Key), i))
        .ToList();

    /// <summary>The default urgency of a built-in; null for any other key.</summary>
    public static bool? BuiltInUrgency(string key) => BuiltInKeys.Contains(key)
        ? Rows.First(r => r.Key == key).Urgent
        : null;
}
