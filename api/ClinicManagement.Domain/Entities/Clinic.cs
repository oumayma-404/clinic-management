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

    // Working hours as a JSON array of per-day {day, enabled, from, to} (reliability-and-polish AC-7). Null =
    // no saved hours yet; the UI then falls back to the shared default. Opaque JSON here — the shape is owned
    // by WorkingHoursSerializer in the Application layer.
    public string? WorkingHoursJson { get; private set; }

    // Per-clinic Google Calendar connection (feature cloud-security-and-tenant-isolation, #4). Replaces the
    // former single token + "primary" calendar shared by ALL clinics (cross-tenant leak). Both nullable: a
    // clinic that has not connected Google has neither. It lives here (not the .local/ file store) so a
    // multi-instance Cloud deployment can resolve each clinic's own token.
    /// <summary>
    /// ⚠️ <b>Legacy plaintext, emptied as each clinic is reached and kept only until the column is dropped</b>
    /// (<c>hosted-security-hardening</c> FR-3.4). It was the last credential in this database held in the clear.
    /// Nothing writes it any more — <see cref="SetGoogleCalendarConnection"/> writes
    /// <see cref="GoogleRefreshTokenProtected"/> — and <c>verify-schema</c>'s <c>google-token-protected</c>
    /// counts the rows that still hold one. The column is dropped in a later migration, once that reads zero on
    /// the live deployment, rather than blind in the same change.
    /// </summary>
    public string? GoogleRefreshToken { get; private set; }

    /// <summary>
    /// The same token as Data-Protection ciphertext (FR-3.4). Encrypted by the caller — Domain references
    /// nothing — exactly as <c>PlatformAccount.ProtectedTotpSecret</c> is.
    /// </summary>
    public string? GoogleRefreshTokenProtected { get; private set; }
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

    /*
     * ── Unattended backup (L4a/L4d) ──────────────────────────────────────────────────────────────────────────
     *
     * Four settings, on the clinic and not in config, for the reason RecallIntervalMonths and
     * StockExpiryLeadDays are: the Application layer has no configuration dependency, and this is a thing the
     * *practice* decides (« sauvegarde à 2 h, garder une semaine »), not a thing the installer decides.
     *
     * ⚠️ Every one of them ships with a caller in the same change — `BackupJob` reads all four, and
     * `SetBackupSettings` is called by `UpdateClinicCommand`. `SetStockExpiryLeadDays` shipped with **zero**
     * production callers and its window has been permanently 30 days ever since; the spec names that failure
     * explicitly as the one not to repeat.
     */

    /// <summary>Whether the daily unattended backup runs for this clinic. On by default: the whole point of L4 is
    /// that protection must not depend on someone remembering to press a button.</summary>
    public bool BackupEnabled { get; private set; }

    /// <summary>
    /// The <b>clinic-local</b> hour the daily backup runs (0–23). Local and not UTC because 02:00 means « the
    /// middle of the night at the practice », which is 01:00 UTC — and a Tunisian clinic reading « 2 » on a
    /// settings screen must get 2 o'clock its own time.
    /// </summary>
    public int BackupHourLocal { get; private set; }

    /// <summary>
    /// How many timestamped backup folders to keep. The pruner drops the oldest beyond this count and
    /// <b>never the last surviving one</b>, whatever the setting says.
    /// </summary>
    public int BackupRetentionCount { get; private set; }

    /// <summary>
    /// After how many hours without a successful backup the admins are told. Distinct from the schedule so a
    /// clinic backing up daily is not warned by a one-off failure it already saw, but is warned by two.
    /// </summary>
    public int BackupStaleAfterHours { get; private set; }

    /// <summary>Defaults, also used by the migration's backfill so the column and the entity cannot disagree.</summary>
    public const int DefaultBackupHourLocal = 2;
    public const int DefaultBackupRetentionCount = 7;
    public const int DefaultBackupStaleAfterHours = 48;

    /// <summary>
    /// When an archive of this cabinet was last <b>delivered</b> to somebody — the copy that left the building
    /// (<c>clinic-recovery-points</c>).
    ///
    /// <para><b>Why a column at all.</b> The export is already recorded, twice, in the audit ledger
    /// (<c>ArchiveAccessLedger</c>) — but « livrée » and « NON livrée » are <i>both</i> <c>AuditAction.Update</c> and
    /// differ only in their French prose, so deriving « la dernière archive réussie » from it would mean matching a
    /// sentence. That is the <c>Contains("déjà facturée")</c> defect this repository deleted: rewording a message
    /// would silently change behaviour. One nullable column instead.</para>
    ///
    /// <para>⚠️ <b>Delivered, not requested.</b> A download that aborts at 90 % really did happen and really did not
    /// arrive; stamping the request would tell a practice it holds a copy it does not have — on the one screen whose
    /// whole job is to say whether it does.</para>
    ///
    /// <para>Null on a cabinet that has never taken one, and the staleness alert measures from
    /// <see cref="CreatedAt"/> in that case rather than from the epoch — otherwise it fires on a practice created
    /// five minutes ago, and an alert that is wrong on day one is one that gets dismissed for ever.</para>
    /// </summary>
    public DateTime? LastArchiveDownloadedAtUtc { get; private set; }

    /// <summary>
    /// When a copy of this cabinet's <b>coffre</b> last reached a second place (<c>clinic-file-vault</c>).
    ///
    /// <para>⚠️ <b>Reported by the shell, because the server cannot see the practice's disk.</b> Coffre originals
    /// were never uploaded, so no archive has ever contained one and nothing server-side can observe whether they
    /// are safe. This is the only channel through which that fact exists here.</para>
    ///
    /// <para>Null on a cabinet whose coffre has never been copied — <b>and also on every cabinet whose coffre is
    /// empty</b>, which is why the staleness alert asks whether there is anything to lose before it fires.</para>
    /// </summary>
    public DateTime? LastVaultCopyAtUtc { get; private set; }

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
        /*
         * Billing defaults for a Tunisian clinic (J11).
         *
         * ⚠️ `VatApplicable` defaults to **true**, and it used to be false — the wrong way round. Dental acts are
         * NOT TVA-exempt in Tunisia: Code de la TVA, Tableau « B » nouveau, § II « Les activités et les
         * services », n° 1 lists services performed by « les médecins, les médecins spécialistes, **les
         * dentistes**, les sages-femmes et les vétérinaires » among those **subject to VAT at the reduced rate**,
         * and Tableau « A » (the exonérations) contains no hit for médecin / dentiste / soins / santé / clinique.
         * There is no exemption to invoke. LF 2018 re-based the reduced rate to **7 %**.
         *
         * So a clinic that never opened this screen was issuing notes d'honoraires that charged no TVA and
         * carried no rate — while Code TVA art. 18 § II requires the invoice to state « les taux et les montants
         * de la taxe sur la valeur ajoutée ». The default is what almost every clinic ships with, which makes it
         * the setting that matters most.
         *
         * Both stay **editable**: a cabinet under the forfait régime is genuinely non-assujetti, and a rate can
         * move by finance law. And this is a default on a *new* clinic only — existing rows are deliberately not
         * migrated, because flipping `VatApplicable` retroactively would change what already-issued notes assert.
         * Those admins get a notice in clinic settings citing Tableau B and decide for themselves.
         *
         * `StampDutyAmount = 1.000` is correct and stays: Code des droits d'enregistrement et de timbre
         * art. 117 § I n° 6° — « Les factures … 1,000 par facture ». LF 2026's 1,5 / 2 DT tiers apply to grandes
         * surfaces (built area > 3 000 m²) only, never to a cabinet.
         */
        VatApplicable = true;
        VatRate = 7m;
        StampDutyEnabled = true;
        StampDutyAmount = 1.000m;
        RecallIntervalMonths = 6;
        StockExpiryLeadDays = DefaultStockExpiryLeadDays;
        BackupEnabled = true;
        BackupHourLocal = DefaultBackupHourLocal;
        BackupRetentionCount = DefaultBackupRetentionCount;
        BackupStaleAfterHours = DefaultBackupStaleAfterHours;
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
    /// Connect (or re-connect) this clinic's Google Calendar: store its OAuth refresh token — <b>already
    /// encrypted by the caller</b> (FR-3.4) — and the target calendar id (null = the account's primary
    /// calendar). Per-clinic so no clinic can see another's events.
    ///
    /// <para>⚠️ It also <b>clears</b> any legacy plaintext left on the row: re-connecting is the one moment a
    /// clinic's token is known in full, so leaving the old cleartext beside the new ciphertext would keep the
    /// credential readable off a stolen disk while every layer reported it protected.</para>
    /// </summary>
    public void SetGoogleCalendarConnection(string protectedRefreshToken, string? calendarId)
    {
        SetProtectedGoogleRefreshToken(protectedRefreshToken);
        GoogleCalendarId = string.IsNullOrWhiteSpace(calendarId) ? null : calendarId.Trim();
    }

    /// <summary>
    /// Stores the refresh token's ciphertext and drops any plaintext beside it. Two callers, both FR-3.4's:
    /// the startup backfill that encrypts what is already stored, and <c>reprotect-secrets</c> re-encrypting it
    /// under a newer key-ring generation. Touches the calendar id and nothing else about the connection.
    /// </summary>
    public void SetProtectedGoogleRefreshToken(string protectedRefreshToken)
    {
        if (string.IsNullOrWhiteSpace(protectedRefreshToken))
            throw new ArgumentException("Le jeton de rafraîchissement Google est obligatoire.", nameof(protectedRefreshToken));

        GoogleRefreshTokenProtected = protectedRefreshToken;
        GoogleRefreshToken = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Disconnect this clinic's Google Calendar (clears the stored refresh token + calendar id).</summary>
    public void ClearGoogleCalendarConnection()
    {
        GoogleRefreshToken = null;
        GoogleRefreshTokenProtected = null;
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
    /// Sets the approaching-expiry window in days (<b>0–365</b>). Drives which stock items are flagged as expiring
    /// soon and which generate the approaching-expiry notification (AC-P4.6).
    ///
    /// <para>⚠️ <b>Zero means « alerte désactivée », and the guard used to refuse it while both readers implemented
    /// it.</b> <c>StockExpiryJob</c> and <c>DashboardAlertsReader</c> have always treated a non-positive lead time as
    /// "off", so the only thing standing between a clinic and switching the alert off was this range check — which
    /// no caller could reach anyway, since the setter shipped with none. A clinic that stocks nothing perishable
    /// gets a daily notification it cannot silence otherwise (AC-20).</para>
    /// </summary>
    public void SetStockExpiryLeadDays(int days)
    {
        if (days < 0 || days > 365)
        {
            throw new ArgumentException("Le délai d'alerte de péremption doit être compris entre 0 et 365 jours (0 = alerte désactivée).", nameof(days));
        }

        StockExpiryLeadDays = days;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Sets the unattended-backup schedule (L4a). One mutator for the four fields rather than four, because they
    /// are one decision — « sauvegarder tous les jours à 2 h, garder 7 copies, m'avertir après 48 h » — and a
    /// per-field setter invites a settings screen that saves half of it.
    /// </summary>
    /// <param name="hourLocal">Clinic-local hour, 0–23.</param>
    /// <param name="retentionCount">Folders to keep, 1–365. One is legal: it means « garder la dernière ».</param>
    /// <param name="staleAfterHours">Hours without a success before the admins are told, 1–720.</param>
    public void SetBackupSettings(bool enabled, int hourLocal, int retentionCount, int staleAfterHours)
    {
        if (hourLocal < 0 || hourLocal > 23)
        {
            throw new ArgumentException("L'heure de sauvegarde doit être comprise entre 0 et 23.", nameof(hourLocal));
        }

        if (retentionCount < 1 || retentionCount > 365)
        {
            throw new ArgumentException(
                "Le nombre de sauvegardes à conserver doit être compris entre 1 et 365.", nameof(retentionCount));
        }

        if (staleAfterHours < 1 || staleAfterHours > 720)
        {
            throw new ArgumentException(
                "Le délai d'alerte de sauvegarde doit être compris entre 1 et 720 heures.", nameof(staleAfterHours));
        }

        BackupEnabled = enabled;
        BackupHourLocal = hourLocal;
        BackupRetentionCount = retentionCount;
        BackupStaleAfterHours = staleAfterHours;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Records that an archive of this cabinet reached somebody (<c>clinic-recovery-points</c>).
    ///
    /// <para>⚠️ <b>It never moves the moment backwards</b>, and that is not defensiveness. The delivery row is written
    /// <i>after</i> the response body completes, outside the request scope, best-effort — so two downloads started
    /// together can finish in either order, and the older one landing last must not make the cabinet look staler
    /// than it is.</para>
    ///
    /// <para>⚠️ It deliberately does <b>not</b> stamp <see cref="UpdatedAt"/>: nobody edited the cabinet, and
    /// « modifié le » is read on a settings screen as « quelqu'un a changé quelque chose ».</para>
    /// </summary>
    public void MarkArchiveDownloaded(DateTime deliveredAtUtc)
    {
        if (LastArchiveDownloadedAtUtc == null || deliveredAtUtc > LastArchiveDownloadedAtUtc)
        {
            LastArchiveDownloadedAtUtc = deliveredAtUtc;
        }
    }

    /// <summary>
    /// Records that the shell copied the coffre somewhere else. <see cref="MarkArchiveDownloaded"/>'s shape, and
    /// for its reasons: it never moves the moment backwards (two machines can report in either order and the older
    /// report must not make the cabinet look staler than it is) and it does not stamp <see cref="UpdatedAt"/>,
    /// because nobody edited the cabinet.
    /// </summary>
    public void MarkVaultCopied(DateTime copiedAtUtc)
    {
        if (LastVaultCopyAtUtc == null || copiedAtUtc > LastVaultCopyAtUtc)
        {
            LastVaultCopyAtUtc = copiedAtUtc;
        }
    }
}


