using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

public class Clinic : AggregateRoot<Guid>
{
    public string Name { get; private set; }
    public string? Address { get; private set; }
    // Cabinet city (e.g. "Tunis"). Prints as the place on generated clinical documents ("{City}, le …",
    // FR-6.1) — a first-class field rather than a value parsed from the free-text Address.
    public string? City { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Code { get; private set; } // Unique code for joining clinic
    public string? LogoUrl { get; private set; } // Logo storage key in MinIO

    // Billing / note d'honoraires settings (Tunisia). Frozen onto each invoice at issue.
    public string? MatriculeFiscal { get; private set; }
    public bool VatApplicable { get; private set; }
    public decimal VatRate { get; private set; }
    public bool StampDutyEnabled { get; private set; }
    public decimal StampDutyAmount { get; private set; }

    // TTN « El Fatoora » electronic-invoicing settings (FR-8). Non-secret only: the on/off toggle + target
    // environment. Credentials/endpoint + the qualified certificate live in the per-install .local/ store,
    // never in the DB.
    public bool TtnEInvoicingEnabled { get; private set; }
    /// <summary>Target TTN environment: "Sandbox" (default, safe) or "Production".</summary>
    public string TtnEnvironment { get; private set; } = TtnEnvironmentSandbox;

    // Working hours as a JSON array of per-day {day, enabled, from, to} (reliability-and-polish AC-7). Null =
    // no saved hours yet; the UI then falls back to the shared default. Opaque JSON here — the shape is owned
    // by WorkingHoursSerializer in the Application layer.
    public string? WorkingHoursJson { get; private set; }

    // Per-clinic Google Calendar connection (feature cloud-security-and-tenant-isolation, #4). Replaces the
    // former single token + "primary" calendar shared by ALL clinics (cross-tenant leak). Both nullable: a
    // clinic that has not connected Google has neither. The refresh token is a secret — see progress.md
    // (encrypt-at-rest is a tracked follow-up); it lives here (not the .local/ file store) so a multi-instance
    // Cloud deployment can resolve each clinic's own token.
    public string? GoogleRefreshToken { get; private set; }
    /// <summary>Target Google calendar id for this clinic's sync; null falls back to the account's "primary".</summary>
    public string? GoogleCalendarId { get; private set; }

    // Patient-recall interval in months (clinical-workflow-depth): how long after a patient's last visit they
    // are considered "à relancer". Defaults to 6 months.
    public int RecallIntervalMonths { get; private set; }

    /// <summary>
    /// How many days ahead a stock lot counts as « expire bientôt » (AC-P4.6). Per-clinic and configurable,
    /// following <see cref="RecallIntervalMonths"/> — the established shape for a clinic-tunable threshold —
    /// rather than a per-install config key, because the Application layer has no configuration dependency and
    /// two clinics on one install legitimately order on different cycles.
    ///
    /// Defaults to 30: dental consumables are ordered on a monthly-ish cycle, so a month is the shortest notice
    /// that still lets a clinic use up a lot or reorder before it is wasted.
    /// </summary>
    public int StockExpiryLeadDays { get; private set; }

    public const string TtnEnvironmentSandbox = "Sandbox";
    public const string TtnEnvironmentProduction = "Production";

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Navigation properties
    private readonly List<User> _users = new();
    public IReadOnlyCollection<User> Users => _users.AsReadOnly();

    private readonly List<Patient> _patients = new();
    public IReadOnlyCollection<Patient> Patients => _patients.AsReadOnly();

    private readonly List<Appointment> _appointments = new();
    public IReadOnlyCollection<Appointment> Appointments => _appointments.AsReadOnly();

    private Clinic() { } // For EF Core

    public Clinic(
        Guid id,
        string name,
        string? address = null,
        string? phone = null,
        string? email = null,
        string? code = null,
        string? city = null)
    {
        Id = id;
        Name = name;
        Address = address;
        City = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        Phone = phone;
        Email = email;
        Code = code;
        // Billing defaults for a Tunisian clinic: VAT off by default (7 % when enabled), stamp duty on at 1,000 DT.
        VatApplicable = false;
        VatRate = 7m;
        StampDutyEnabled = true;
        StampDutyAmount = 1.000m;
        TtnEInvoicingEnabled = false;
        TtnEnvironment = TtnEnvironmentSandbox;
        RecallIntervalMonths = 6;
        StockExpiryLeadDays = DefaultStockExpiryLeadDays;
        CreatedAt = DateTime.UtcNow;
    }

    public void Update(string name, string? address = null, string? phone = null, string? email = null, string? logoUrl = null, string? city = null)
    {
        Name = name;
        Address = address;
        City = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        Phone = phone;
        Email = email;
        LogoUrl = logoUrl;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Update the clinic's billing settings used for note-d'honoraires generation. The VAT rate and stamp
    /// amount cannot be negative; the rate is only meaningful when <paramref name="vatApplicable"/> is true.
    /// </summary>
    public void SetBillingSettings(string? matriculeFiscal, bool vatApplicable, decimal vatRate, bool stampDutyEnabled, decimal stampDutyAmount)
    {
        if (vatRate < 0)
            throw new ArgumentException("Le taux de TVA ne peut pas être négatif.", nameof(vatRate));

        if (stampDutyAmount < 0)
            throw new ArgumentException("Le montant du timbre fiscal ne peut pas être négatif.", nameof(stampDutyAmount));

        MatriculeFiscal = string.IsNullOrWhiteSpace(matriculeFiscal) ? null : matriculeFiscal.Trim();
        VatApplicable = vatApplicable;
        VatRate = vatApplicable ? vatRate : 0m;
        StampDutyEnabled = stampDutyEnabled;
        StampDutyAmount = stampDutyEnabled ? stampDutyAmount : 0m;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Enable/disable TTN « El Fatoora » e-invoicing for this clinic and set the target environment (FR-8).
    /// The environment must be "Sandbox" or "Production"; anything else falls back to the safe sandbox.
    /// </summary>
    public void SetElFatooraSettings(bool enabled, string? environment)
    {
        var normalized = string.Equals(environment?.Trim(), TtnEnvironmentProduction, StringComparison.OrdinalIgnoreCase)
            ? TtnEnvironmentProduction
            : TtnEnvironmentSandbox;

        TtnEInvoicingEnabled = enabled;
        TtnEnvironment = normalized;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetCode(string code)
    {
        Code = code;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets the clinic's working-hours JSON (already validated/canonicalized by the caller). A blank value
    /// clears it (= no saved hours). reliability-and-polish AC-7.
    /// </summary>
    public void SetWorkingHours(string? workingHoursJson)
    {
        WorkingHoursJson = string.IsNullOrWhiteSpace(workingHoursJson) ? null : workingHoursJson;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Connect (or re-connect) this clinic's Google Calendar: store its OAuth refresh token and the target
    /// calendar id (null = the account's primary calendar). Per-clinic so no clinic can see another's events.
    /// </summary>
    public void SetGoogleCalendarConnection(string refreshToken, string? calendarId)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new ArgumentException("Le jeton de rafraîchissement Google est obligatoire.", nameof(refreshToken));

        GoogleRefreshToken = refreshToken.Trim();
        GoogleCalendarId = string.IsNullOrWhiteSpace(calendarId) ? null : calendarId.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Disconnect this clinic's Google Calendar (clears the stored refresh token + calendar id).</summary>
    public void ClearGoogleCalendarConnection()
    {
        GoogleRefreshToken = null;
        GoogleCalendarId = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets the patient-recall interval in months (1–60). Drives which patients appear "à relancer".
    /// </summary>
    public void SetRecallIntervalMonths(int months)
    {
        if (months < 1 || months > 60)
        {
            throw new ArgumentException("L'intervalle de relance doit être compris entre 1 et 60 mois.", nameof(months));
        }

        RecallIntervalMonths = months;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>The default approaching-expiry window, also used by the migration's backfill.</summary>
    public const int DefaultStockExpiryLeadDays = 30;

    /// <summary>
    /// Sets the approaching-expiry window in days (1–365). Drives which stock items are flagged as expiring
    /// soon and which generate the approaching-expiry notification (AC-P4.6).
    /// </summary>
    public void SetStockExpiryLeadDays(int days)
    {
        if (days < 1 || days > 365)
        {
            throw new ArgumentException("Le délai d'alerte de péremption doit être compris entre 1 et 365 jours.", nameof(days));
        }

        StockExpiryLeadDays = days;
        UpdatedAt = DateTime.UtcNow;
    }
}


