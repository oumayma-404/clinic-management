using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Billing.Queries;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Common.Csv;

/// <summary>
/// One CSV shape per exportable list (L5).
///
/// <para><b>They map DTOs, not entities, and that is the whole design.</b> Every export re-sends the query the
/// screen already sends with <b>no paging</b> — which the paging primitive models as a first-class case
/// (<c>PagedResult.Unpaged</c>) rather than as a huge page — so an export inherits the screen's filters, its
/// tenant scoping and its de-dup rules by construction. The spec's requirement that « export must honour the
/// current filters and export the whole filtered set, not the current page » is therefore not something these
/// builders have to remember: they never see a page.</para>
///
/// <para>Static and pure, so a test can assert the header row, the column order and the money format without a
/// controller, a database or a request.</para>
/// </summary>
public static class ExportTables
{
    /// <summary>
    /// Patients. Contact details are genuinely nullable, so an absent one is an <b>empty cell</b> — never « — »
    /// and never a sentinel: the four contact sentinels were retired for exactly the reason that a placeholder
    /// re-imports as data.
    /// </summary>
    public static CsvTable Patients(IEnumerable<PatientDto> patients)
    {
        var table = CsvTable.Create(
            "Nom", "Prénom", "Date de naissance", "Sexe", "Téléphone", "Email",
            "Adresse", "Ville", "Gouvernorat", "Code postal",
            "Identifiant CNAM", "Assurance", "N° police",
            "Antécédents médicaux", "Allergies",
            "Contact d'urgence", "Téléphone d'urgence", "Adressé par",
            "Archivé", "Inscrit le");

        foreach (var p in patients)
        {
            table.Row(
                CsvCell.Text(p.LastName),
                CsvCell.Text(p.FirstName),
                CsvCell.CalendarDay(p.DateOfBirth),
                // « Homme » / « Femme », not the stored `Male` / `Female`. The same rule `CsvCell.YesNo` states —
                // a French file for French readers — applied to the one column that had escaped it. The import
                // parses both spellings, so the round trip works either way (`PatientGender`).
                CsvCell.Text(PatientGender.Label(p.Gender)),
                CsvCell.Text(p.PhoneNumber),
                CsvCell.Text(p.Email),
                CsvCell.Text(p.Address?.Street),
                CsvCell.Text(p.Address?.City),
                CsvCell.Text(p.Address?.State),
                CsvCell.Text(p.Address?.ZipCode),
                CsvCell.Text(p.CnamInfo?.IdentifiantUnique),
                CsvCell.Text(p.InsuranceInfo?.Provider),
                CsvCell.Text(p.InsuranceInfo?.PolicyNumber),
                CsvCell.Text(p.MedicalHistory),
                CsvCell.Text(p.Allergies),
                CsvCell.Text(p.EmergencyContactName),
                CsvCell.Text(p.EmergencyContactPhone),
                CsvCell.Text(p.ReferredBy),
                CsvCell.YesNo(p.IsArchived),
                CsvCell.Date(p.CreatedAt));
        }

        return table;
    }

    /// <summary>
    /// Notes d'honoraires. One row per invoice, not per line: this is the accountant's view, and a per-line file
    /// would make « combien ai-je facturé ce mois » a pivot table rather than a column sum.
    /// </summary>
    public static CsvTable Invoices(IEnumerable<InvoiceDto> invoices)
    {
        var table = CsvTable.Create(
            "Numéro", "Date d'émission", "Patient", "Statut",
            "Total HT", "TVA", "Timbre", "Total TTC", "Encaissé", "Avoirs", "Reste à payer",
            "Créée le");

        foreach (var i in invoices)
        {
            table.Row(
                CsvCell.Text(i.Number),
                CsvCell.Date(i.IssueDate),
                CsvCell.Text(i.PatientName),
                CsvCell.Text(i.Status),
                CsvCell.Money(i.TotalHt),
                CsvCell.Money(i.TotalVat),
                CsvCell.Money(i.StampDutyAmount),
                CsvCell.Money(i.TotalTtc),
                CsvCell.Money(i.AmountCollected),
                CsvCell.Money(i.CreditedTotal),
                CsvCell.Money(i.Outstanding),
                CsvCell.Date(i.CreatedAt));
        }

        return table;
    }

    /// <summary>« Créances » — who owes what, and for how long.</summary>
    public static CsvTable Receivables(IEnumerable<ReceivableDto> receivables)
    {
        var table = CsvTable.Create("Patient", "Reste à payer", "Plus ancienne échéance", "Jours de retard");

        foreach (var r in receivables)
        {
            table.Row(
                CsvCell.Text(r.PatientName),
                CsvCell.Money(r.TotalOutstanding),
                CsvCell.Date(r.OldestOverdueDate),
                CsvCell.Number(r.DaysOverdue));
        }

        return table;
    }

    /// <summary>
    /// The « extrait de caisse » — every movement behind the totals.
    ///
    /// <para>⚠️ Entrée and Sortie are <b>separate columns</b>, and a voided row carries neither. That is the
    /// shape of a till statement an accountant can add up, and it keeps the file honest about the one rule the
    /// screen enforces: a voided movement is still listed (with its motif and its author) but does not move the
    /// balance. A single signed « Montant » column would have made the void indistinguishable from a zero.</para>
    /// </summary>
    public static CsvTable CaisseLedger(IEnumerable<CaisseMovementDto> movements)
    {
        var table = CsvTable.Create(
            "Date", "Libellé", "Référence", "Patient", "Mode de paiement",
            "Entrée", "Sortie", "Solde de la période",
            "Annulé", "Motif d'annulation", "Annulé par");

        foreach (var m in movements)
        {
            var isIn = string.Equals(m.Direction, "In", StringComparison.OrdinalIgnoreCase);
            var counted = !m.IsVoided;

            table.Row(
                CsvCell.Date(m.OccurredOn),
                CsvCell.Text(m.Label),
                CsvCell.Text(m.Reference),
                CsvCell.Text(m.PatientName),
                CsvCell.Text(m.Method),
                counted && isIn ? CsvCell.Money(m.Amount) : string.Empty,
                counted && !isIn ? CsvCell.Money(m.Amount) : string.Empty,
                CsvCell.Money(m.RunningBalance),
                CsvCell.YesNo(m.IsVoided),
                CsvCell.Text(m.VoidReason),
                CsvCell.Text(m.VoidedByName));
        }

        return table;
    }

    /// <summary>
    /// « Chèques à encaisser » (L8 slice B) — the list an owner takes to the bank.
    ///
    /// <para>⚠️ « Encaissable le » goes through <see cref="CsvCell.CalendarDay"/>, not <c>Date</c>: a cheque's due
    /// date is a calendar day stored with no zone conversion (exactly like an échéance's), so converting it would
    /// move a cheque due on the 1st into the previous month — the same trap the client-side
    /// <c>toISOString()</c> ban exists for. « Reçu le » is a real instant and does convert.</para>
    /// <para>« Échéance » is the bucket the screen shows, in French, so a row cannot be filed under one heading in
    /// the file and another on the page it was exported from.</para>
    /// </summary>
    public static CsvTable Cheques(IEnumerable<ChequeDto> cheques)
    {
        var table = CsvTable.Create(
            "Encaissable le", "Échéance", "N° de chèque", "Banque",
            "Montant", "Patient", "Référence", "Reçu le", "Porté en banque le");

        foreach (var c in cheques)
        {
            table.Row(
                CsvCell.CalendarDay(c.DueDate),
                CsvCell.Text(ChequeBucketLabel(c.Bucket)),
                CsvCell.Text(c.ChequeNumber),
                CsvCell.Text(c.BankName),
                CsvCell.Money(c.Amount),
                CsvCell.Text(c.PatientName),
                CsvCell.Text(c.Reference),
                CsvCell.Date(c.ReceivedOn),
                // Blank for a cheque still held — the column carries the distinction the file would otherwise
                // lose, since a « Encaissés » export and a « À encaisser » one are byte-identical without it.
                CsvCell.Date(c.BankedOn));
        }

        return table;
    }

    /// <summary>
    /// The French heading for a cheque bucket. It lives here rather than beside the query because the *screen*
    /// needs the same four words and reads them from its own component — this is the file's copy, and an
    /// unrecognised value passes through verbatim rather than becoming « Inconnu », the same tolerance every
    /// storage-key→label map in this product applies.
    /// </summary>
    private static string ChequeBucketLabel(string bucket) => bucket switch
    {
        nameof(ChequeBucket.Overdue) => "En retard",
        nameof(ChequeBucket.DueSoon) => "Bientôt",
        nameof(ChequeBucket.Later) => "Plus tard",
        nameof(ChequeBucket.Undated) => "Sans date",
        _ => bucket
    };

    /// <summary>Expenses — the money-out ledger « Caisse » already itemises on screen.</summary>
    public static CsvTable Expenses(IEnumerable<ExpenseDto> expenses)
    {
        var table = CsvTable.Create("Date", "Catégorie", "Description", "Montant", "Mode de paiement");

        foreach (var e in expenses)
        {
            table.Row(
                CsvCell.Date(e.ExpenseDate),
                CsvCell.Text(e.Category),
                CsvCell.Text(e.Description),
                CsvCell.Money(e.Amount),
                CsvCell.Text(e.Method));
        }

        return table;
    }

    /// <summary>
    /// The agenda. Duration is written as <b>minutes</b>, not as a <c>TimeSpan</c>: « 00:30:00 » is a value a
    /// spreadsheet re-interprets as a time of day.
    /// </summary>
    public static CsvTable Appointments(IEnumerable<AppointmentDto> appointments)
    {
        var table = CsvTable.Create(
            "Date et heure", "Durée (min)", "Patient", "Praticien", "Actes", "Statut",
            "Facture", "Notes");

        foreach (var a in appointments)
        {
            // The séance's acts, in the order they were booked. `Procedures` is the authority since
            // multi-act-appointments; the scalar is only its first row, so joining the list is what keeps an
            // exported « détartrage + 2 obturations » from reading as one act.
            var acts = a.Procedures.Count > 0
                ? string.Join(" + ", a.Procedures.OrderBy(p => p.SequenceNumber).Select(p => p.Name))
                : a.ProcedureTypeName;

            table.Row(
                CsvCell.Moment(a.AppointmentDateTime),
                CsvCell.Number((int)a.Duration.TotalMinutes),
                CsvCell.Text(a.PatientName),
                CsvCell.Text(a.DoctorName),
                CsvCell.Text(acts),
                CsvCell.Text(FrenchAppointmentStatus(a.Status)),
                CsvCell.Text(a.InvoiceNumber),
                CsvCell.Text(a.Notes));
        }

        return table;
    }

    /// <summary>
    /// The agenda's statut, in French. The DTO carries the enum's own <b>name</b>, so a raw write put « NoShow »
    /// into a French file — and « AwaitingClosure », an invented word, once that status existed.
    /// <para>⚠️ The three sibling exports above and below still write their raw enum names. Left alone here
    /// deliberately: each needs its own label authority (<c>InvoiceStatus</c>, <c>TreatmentPlanStatus</c>,
    /// <c>LabOrderStatus</c>) and that is a wider change than the one this belongs to.</para>
    /// </summary>
    private static string FrenchAppointmentStatus(string status) =>
        Enum.TryParse<AppointmentStatus>(status, ignoreCase: true, out var parsed)
            ? Appointment.FrenchLabel(parsed)
            : status;

    /// <summary>
    /// Devis / plans de traitement. One row per plan, with the act progress as a fraction — the figure the
    /// workspace leads with.
    /// </summary>
    public static CsvTable TreatmentPlans(IEnumerable<TreatmentPlanDto> plans)
    {
        var table = CsvTable.Create(
            "Numéro", "Patient", "Intitulé", "Statut", "Révision", "Accepté le",
            "Total prévu", "Encaissé", "Reste à payer",
            "Actes réalisés", "Actes prévus", "Prochaine séance", "Facture liée", "Créé le");

        foreach (var p in plans)
        {
            table.Row(
                CsvCell.Text(p.Number),
                CsvCell.Text(p.PatientName),
                CsvCell.Text(p.Title),
                CsvCell.Text(p.Status),
                CsvCell.Number(p.RevisionNumber),
                CsvCell.Date(p.AcceptedDate),
                CsvCell.Money(p.TotalPlanned),
                CsvCell.Money(p.AmountPaid),
                CsvCell.Money(p.Outstanding),
                CsvCell.Number(p.ItemsDone),
                CsvCell.Number(p.ItemsTotal),
                CsvCell.Moment(p.NextAppointmentAt),
                CsvCell.Text(p.LinkedInvoiceNumber),
                CsvCell.Date(p.CreatedAt));
        }

        return table;
    }

    /// <summary>
    /// Stock. Carries the earliest relevant expiry and both flags, because the reason to export stock is to
    /// order from it — and « what runs out » and « what expires » are the two questions that produce an order.
    /// </summary>
    public static CsvTable Stock(IEnumerable<StockItemDto> items)
    {
        var table = CsvTable.Create(
            "Article", "Catégorie", "Unité", "Stock actuel", "Seuil minimum", "Stock maximum",
            "Prix unitaire", "Fournisseur", "Téléphone fournisseur", "Stock bas",
            "Péremption la plus proche", "Périmé", "Expire bientôt");

        foreach (var i in items)
        {
            table.Row(
                CsvCell.Text(i.Name),
                CsvCell.Text(i.Category),
                CsvCell.Text(i.Unit),
                CsvCell.Number(i.CurrentStock),
                CsvCell.Number(i.MinimumStockLevel),
                CsvCell.Number(i.MaximumStockLevel),
                CsvCell.Money(i.UnitPrice),
                CsvCell.Text(i.SupplierName),
                // The number is what makes an exported stock list actionable: the reason to export it is to
                // order from it, and a name with no number sends the reader back into the app to find one.
                CsvCell.Text(i.SupplierPhoneE164),
                CsvCell.YesNo(i.IsLowStock),
                CsvCell.CalendarDay(i.EarliestExpiry),
                CsvCell.YesNo(i.HasExpiredStock),
                CsvCell.YesNo(i.IsExpiringSoon));
        }

        return table;
    }

    /// <summary>Bons de prothèse — what is at the lab, and since when.</summary>
    public static CsvTable LabOrders(IEnumerable<LabWorkOrderDto> orders)
    {
        var table = CsvTable.Create(
            "Patient", "Dent", "Prothésiste", "Travail", "Statut",
            "Envoyé le", "Attendu le", "Reçu le", "Coût", "Notes");

        foreach (var o in orders)
        {
            table.Row(
                CsvCell.Text(o.PatientName),
                CsvCell.Number(o.ToothNumber),
                CsvCell.Text(o.Prosthetist),
                CsvCell.Text(o.WorkDescription),
                CsvCell.Text(o.Status),
                CsvCell.Date(o.SentDate),
                CsvCell.Date(o.ExpectedDate),
                CsvCell.Date(o.ReceivedDate),
                CsvCell.Money(o.Cost),
                CsvCell.Text(o.Notes));
        }

        return table;
    }
}
