using System.Globalization;
using System.Text.Json;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.Audit;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Relay;

/// <summary>
/// One line of French saying what a returned record is (<c>clinic-pc-copy</c> AC-5.6, AC-7.4): the patient, the act, the
/// amount, the date — read from the row as each side held it, never shown as raw JSON. Pure, so every table's wording
/// is tested; a table it does not know reads as its journal name.
/// </summary>
public static class RelayRecordSummary
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>The patients a version names, so the caller resolves their names in one read.</summary>
    public static IEnumerable<Guid> PatientIdsOf(string? json)
    {
        if (Parse(json) is { } row && Guid.TryParse(Text(row, nameof(Invoice.PatientId)), out var id))
        {
            yield return id;
        }
    }

    public static string? Describe(string table, string? json, IReadOnlyDictionary<Guid, string> patientNames)
    {
        if (Parse(json) is not { } row)
        {
            return null;
        }

        var patient = Guid.TryParse(Text(row, nameof(Invoice.PatientId)), out var pid) && patientNames.TryGetValue(pid, out var name)
            ? name
            : null;
        string With(string head) => patient is null ? head : $"{head} — {patient}";

        return table switch
        {
            nameof(Patient) => Join(" ", Text(row, nameof(Patient.FirstName)), Text(row, nameof(Patient.LastName))) ?? "Patient",
            nameof(Appointment) => With($"Rendez-vous du {Moment(row, nameof(Appointment.AppointmentDateTime))}"),
            nameof(Invoice) => With(Join(" — ", $"Note d'honoraires {Text(row, nameof(Invoice.Number)) ?? "(brouillon)"}",
                Money(row, nameof(Invoice.TotalTtc)))!),
            nameof(Payment) or nameof(InstallmentPayment) =>
                Join(" — ", "Paiement", Money(row, nameof(Payment.Amount)), Day(row, nameof(Payment.PaidOn)))!,
            nameof(Expense) => Join(" — ", Text(row, nameof(Expense.Description)) ?? "Dépense", Money(row, nameof(Expense.Amount)),
                Day(row, nameof(Expense.ExpenseDate)))!,
            nameof(TreatmentPlan) => With(Join(" — ", $"Devis {Text(row, nameof(TreatmentPlan.Number)) ?? "(non numéroté)"}",
                Money(row, nameof(TreatmentPlan.TotalPlanned)))!),
            nameof(CreditNote) => Join(" — ", $"Avoir {Text(row, nameof(CreditNote.Number))}", Money(row, nameof(CreditNote.Amount)))!,
            nameof(DentalRecord) => With($"Fiche de soins du {Day(row, nameof(DentalRecord.InterventionDate))}"),
            nameof(MedicalDocument) => With($"Document « {Text(row, nameof(MedicalDocument.DocumentType))} »"),
            _ => patient is null ? AuditLabels.Entity(table) : With(AuditLabels.Entity(table)),
        };
    }

    /// <summary>
    /// AC-7.5: a note, devis or avoir numbered on a PC whose cut was overruled may already be in the patient's hand —
    /// its number is issued again by the cloud, so the paper must be redone.
    /// </summary>
    public static string? Warning(RelayReviewKind kind, string table, string? cabinetJson)
    {
        if (kind != RelayReviewKind.ToReEnter
            || table is not (nameof(Invoice) or nameof(TreatmentPlan) or nameof(CreditNote))
            || Parse(cabinetJson) is not { } row
            || Text(row, nameof(Invoice.Number)) is not { } number)
        {
            return null;
        }

        return $"Document n° {number} remis au patient : ce numéro n'est pas valable, à refaire.";
    }

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } s
            ? s.Trim()
            : null;

    private static string? Money(JsonElement row, string property) =>
        row.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var amount)
            ? amount.ToString("N3", French) + " DT"
            : null;

    private static DateTime? Instant(JsonElement row, string property) =>
        Text(row, property) is { } text
        && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
            ? DateTime.SpecifyKind(at, DateTimeKind.Utc)
            : null;

    private static string? Day(JsonElement row, string property) =>
        Instant(row, property) is { } at ? ClinicClock.ToClinicLocal(at).ToString("dd/MM/yyyy", French) : null;

    private static string Moment(JsonElement row, string property) =>
        Instant(row, property) is { } at ? ClinicClock.ToClinicLocal(at).ToString("dd/MM/yyyy 'à' HH:mm", French) : "?";

    private static string? Join(string separator, params string?[] parts)
    {
        var present = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return present.Count == 0 ? null : string.Join(separator, present);
    }
}
