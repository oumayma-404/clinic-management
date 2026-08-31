using System.Security.Cryptography;
using System.Text;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Canonical provisional seed for the CNAM <b>valeurs de la lettre clé</b> (VLC, FR-5.2). Single source of truth
/// shared by <c>ClinicCatalogSeeder</c> and the seed-integrity unit tests, so the two can never drift.
///
/// <para>⚠️ <b>The act entries used to live here too, and they are gone</b> (feature
/// <c>single-act-catalogue</c>). They were 26 invented codes (<c>CONS</c>, <c>OBT-2F</c>, …) carrying
/// hand-assigned coefficients, mirroring the real <c>DCH</c> catalogue in <see cref="DentalActCatalogSeed"/>
/// — which holds the 100 codes the CNAM « Liste des actes » actually publishes. One catalogue now, and it is
/// the one whose codes a caisse would recognise.</para>
/// </summary>
public static class CnamCatalogSeed
{
    // Fixed seed timestamp (migrations are deterministic; no DateTime.Now in a seed).
    public static readonly DateTime SeededAtUtc = new(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc);

    // The lettres clés a dental practice bills under, each defined by the NGAP arrêté du 1er juin 2006, art. 4.
    // ⚠️ `VD` (« visite dentaire ») was here and is deleted: it appears in neither that article nor any CNAM
    // tariff table, so it was a key nothing could ever value.
    public const string CD = "CD";   // Consultation au cabinet du médecin dentiste
    public const string CDS = "CDS"; // Consultation au cabinet du médecin dentiste spécialiste
    public const string D = "D";     // Acte réalisé par un médecin dentiste
    public const string RD = "RD";   // Acte de radiodiagnostic pratiqué par un médecin dentiste

    public sealed record LetterValueSeed(Guid Id, string LettreCle, decimal Value);

    /// <summary>
    /// What this seed shipped for each lettre clé <b>before</b> the convention values were wired in — kept, not
    /// deleted, because it is the only way a correction can tell « nobody has ever touched this row » from
    /// « an admin deliberately entered this ». See <see cref="SupersededLetterValue"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These figures were stale on the day they shipped: the convention in force since 01/01/2021 fixes Cd at
    /// 30,000 DT and D at 3,000 DT, and its own text records the *previous* values as Cd 18,000 / D 1,700 — so
    /// <c>7</c> and <c>1.200</c> predate even those. The indicative estimate is <c>coefficient × VLC × taux</c>,
    /// which understated every reimbursement figure shown to a patient by roughly 60–75 %.
    /// </para>
    /// <para>
    /// ⚠️ <b>Declared above <see cref="LetterValues"/> deliberately.</b> Static field initializers run in
    /// <i>textual</i> order, so with this below the property, <c>BuildLetterValues()</c> ran against a null array
    /// and the type initializer threw — which meant every read of the seed (and therefore application startup)
    /// failed. Do not move it down for tidiness.
    /// </para>
    /// </remarks>
    private static readonly (string Cle, decimal Value)[] LegacyLetterValues =
    {
        (CD, 7m),
        (CDS, 10m),
        (D, 1.200m),
        (RD, 2m),
    };

    public static IReadOnlyList<LetterValueSeed> LetterValues { get; } = BuildLetterValues();

    // The convention's value where it settles one, the older unverified figure otherwise (Rd — which stays
    // IsProvisional « à vérifier »). Derived rather than retyped: a second hand-written copy of 30,000 / 45,000 /
    // 3,000 here is exactly the drift CnamConventionTariffs exists to prevent.
    private static List<LetterValueSeed> BuildLetterValues()
        => LegacyLetterValues
            .Select(r => new LetterValueSeed(
                DeterministicGuid($"cnam-vlc:{r.Cle}"),
                r.Cle,
                CnamConventionTariffs.ValueFor(r.Cle) ?? r.Value))
            .ToList();

    /// <summary>
    /// The value this seed shipped for <paramref name="lettreCle"/> before the convention correction, or
    /// <c>null</c> when there is nothing to correct — either the convention settles no value for that lettre clé
    /// (<c>Rd</c>), or the seeded figure already agreed with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the third term of the startup correction's predicate (DEV-4). A correction that fires on
    /// <c>IsProvisional</c> alone would be wrong: <c>CnamLetterValue.SetValue</c> stamps <c>UpdatedAt</c> and
    /// <b>does not</b> clear the provisional flag — only <c>Confirm()</c> does — so an admin who typed their own
    /// valeur de la lettre clé and never pressed « Confirmer » still reads <c>IsProvisional = true</c>. Overwriting
    /// that is worse than leaving a stale default: it replaces a deliberate figure with one nobody asked for.
    /// </para>
    /// <para>
    /// So the correction only ever touches a row that is <b>untouched since seeding</b> (<c>UpdatedAt == null</c>),
    /// still unvouched-for, <b>and</b> still holding the exact number returned here. Everything else is left alone
    /// and surfaced to the admin as a prompt instead.
    /// </para>
    /// </remarks>
    public static decimal? SupersededLetterValue(string? lettreCle)
    {
        if (string.IsNullOrWhiteSpace(lettreCle))
        {
            return null;
        }

        foreach (var (cle, legacy) in LegacyLetterValues)
        {
            if (!string.Equals(cle, lettreCle.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var inForce = CnamConventionTariffs.ValueFor(cle);
            return inForce.HasValue && inForce.Value != legacy ? legacy : null;
        }

        return null;
    }

    /// <summary>
    /// Stable GUID derived from a key string (MD5) so the seed ids are identical on every machine and
    /// across re-generations — no <c>Guid.NewGuid()</c> churn in a committed migration.
    /// </summary>
    public static Guid DeterministicGuid(string key)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
        return new Guid(hash);
    }
}
