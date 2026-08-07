using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Audit;

/// <summary>
/// The French wording of the ledger, in one place.
///
/// <para><b>Why the labels are server-side.</b> Same reasoning as the « extrait de caisse »: the four kinds of
/// movement are labelled once on the server so no screen invents a fifth wording. Here it matters slightly more —
/// the entity names come from CLR type names, which a client cannot translate without a duplicate of this map, and
/// the map has to grow whenever an aggregate is added. One home, and an untranslated type degrades to its own name
/// rather than to « Inconnu », because « ProcedureTypeMaterial » at least tells an owner what was touched.</para>
/// </summary>
public static class AuditLabels
{
    /// <summary>« Création » / « Modification » / « Suppression » — the three shapes a save takes.</summary>
    public static string Action(AuditAction action) => action switch
    {
        AuditAction.Insert => "Création",
        AuditAction.Update => "Modification",
        AuditAction.Delete => "Suppression",
        _ => action.ToString()
    };

    /// <summary>
    /// The aggregate's French name. An unmapped type falls through to the CLR name **unchanged** — a new aggregate
    /// then reads as slightly technical rather than disappearing behind a placeholder, which is the right failure:
    /// this map is the one part of the ledger a person still has to maintain by hand.
    /// </summary>
    public static string Entity(string entityType) => entityType switch
    {
        nameof(Patient) => "Patient",
        nameof(Appointment) => "Rendez-vous",
        nameof(DentalRecord) => "Fiche de soins",
        nameof(Invoice) => "Note d'honoraires",
        nameof(CreditNote) => "Avoir",
        nameof(TreatmentPlan) => "Devis / plan de traitement",
        nameof(Expense) => "Dépense",
        nameof(StockItem) => "Article de stock",
        nameof(StockMovement) => "Mouvement de stock",
        nameof(MedicalDocument) => "Document médical",
        nameof(PatientFile) => "Fichier patient",
        nameof(PatientFolder) => "Dossier de fichiers",
        nameof(LabWorkOrder) => "Bon de laboratoire",
        nameof(WaitingListEntry) => "Salle d'attente",
        nameof(RecurringAppointment) => "Série de rendez-vous",
        nameof(ProcedureType) => "Type d'acte",
        nameof(User) => "Compte utilisateur",
        nameof(Doctor) => "Praticien",
        nameof(Clinic) => "Cabinet",
        nameof(ClinicReminderSettings) => "Paramètres de rappel",
        nameof(CnamNomenclatureEntry) => "Nomenclature CNAM",
        nameof(CnamLetterValue) => "Valeur de la lettre clé",
        nameof(DentalActCode) => "Acte dentaire (DCH)",
        nameof(Medication) => "Médicament",
        nameof(StaffNotification) => "Notification interne",
        nameof(DocumentEmail) => "Envoi de document",
        _ => entityType
    };

    /// <summary>
    /// Who to show. A process is named as such — « Tâche automatique (NotificationJob) » — because a row an owner
    /// cannot attribute to a colleague should say so plainly rather than leave them looking for one. A person shows
    /// their email; failing that, their raw id, which is at least traceable.
    /// </summary>
    public static string Actor(string userId, string? userEmail)
    {
        if (userId.StartsWith(AuditActor.ProcessPrefix, StringComparison.Ordinal))
        {
            var name = userId[AuditActor.ProcessPrefix.Length..];
            return name is "unknown" or ""
                ? "Tâche automatique"
                : $"Tâche automatique ({name})";
        }

        return string.IsNullOrWhiteSpace(userEmail) ? userId : userEmail;
    }
}
