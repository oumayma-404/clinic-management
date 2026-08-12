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
        // The child tables. They never appear in « Journal d'activité » — the interceptor writes one row per
        // aggregate root — but the archive's restore report is keyed on the same CLR names and is read by a
        // cabinet owner, so « InstallmentPayment · 3 ignorés » had to stop being the sentence they meet.
        nameof(InvoiceLine) => "Ligne de facture",
        nameof(Payment) => "Paiement",
        nameof(Installment) => "Échéance",
        nameof(InstallmentPayment) => "Paiement d'échéance",
        nameof(TreatmentPlanItem) => "Acte du devis",
        nameof(AppointmentProcedure) => "Acte du rendez-vous",
        nameof(DentalRecordTooth) => "Dent d'une fiche de soins",
        nameof(DentalRecordAct) => "Acte d'une fiche de soins",
        nameof(ToothState) => "État d'une dent",
        nameof(PatientMedicalHistory) => "Antécédent médical",
        nameof(PatientFamilyHistory) => "Antécédent familial",
        nameof(PatientFlag) => "Étiquette patient",
        nameof(StockBatch) => "Lot de stock",
        nameof(ProcedureTypeMaterial) => "Consommable d'un acte",
        nameof(MedicationActiveIngredient) => "Principe actif",
        _ => entityType
    };

    /// <summary>
    /// Who to show. A process is named as such — « Tâche automatique (NotificationJob) » — because a row an owner
    /// cannot attribute to a colleague should say so plainly rather than leave them looking for one. A person shows
    /// their email; failing that, their raw id, which is at least traceable.
    ///
    /// <para>⚠️ <b>The restore decoration is unwrapped here, and without that the mark was write-only.</b>
    /// <c>AuditActor.AsRestore()</c> <i>prepends</i> to whatever identity was in scope and preserves the e-mail, so
    /// a restored row fell through to the address branch and rendered as the named admin's own — verbatim the
    /// outcome the decoration exists to prevent: three thousand <c>Insert</c> rows against a colleague, on a day
    /// they typed nothing. On the console path it is worse, since the address shown to the practice belongs to the
    /// vendor.</para>
    /// </summary>
    public static string Actor(string userId, string? userEmail)
    {
        if (userId.StartsWith(AuditActor.RestorePrefix, StringComparison.Ordinal))
        {
            var inner = Actor(userId[AuditActor.RestorePrefix.Length..], userEmail);
            return $"Restauration d'archive ({inner})";
        }

        if (userId.StartsWith(AuditActor.ConsolePrefix, StringComparison.Ordinal))
        {
            return string.IsNullOrWhiteSpace(userEmail)
                ? "Assistance éditeur"
                : $"Assistance éditeur ({userEmail})";
        }

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
