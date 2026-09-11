using ClinicManagement.Domain.Common;
using PhoneNumbers;

namespace ClinicManagement.Domain.ValueObjects;

public class PhoneNumber : ValueObject
{
    /// <summary>The number exactly as a human typed it. ⚠️ Never rewritten — see <see cref="E164"/>.</summary>
    public string Value { get; private set; }

    /// <summary>
    /// The stored E.164 normalisation of <see cref="Value"/>, resolved with the region the WRITER supplied.
    ///
    /// <para>⚠️ <b>Read <see cref="E164"/>, never this.</b> It is null on every row written before this column
    /// existed, and null for a number no country can parse (« 71 555 (bureau) » — a real emergency-contact
    /// value this deliberately keeps rather than refusing).</para>
    /// </summary>
    public string? PersistedE164 { get; private set; }

    /// <summary>
    /// The dialable form of this number, or <c>null</c> when no country can parse it.
    ///
    /// <para><b>Why it is stored rather than re-derived.</b> A national number cannot be resolved without
    /// knowing its country, and the country is known only at the moment of writing — it comes from the form's
    /// country selector and nothing else can recover it afterwards. Every read that re-derived E.164 from
    /// <see cref="Value"/> therefore assumed Tunisia, so a French patient saved as <c>06 12 34 56 78</c> was
    /// reachable by nothing: no WhatsApp action, no SMS reminder, and a <c>tel:</c> link that dialled a French
    /// national number from a Tunisian handset.</para>
    ///
    /// <para>⚠️ <b>The fallback is for legacy rows and must stay.</b> Nothing was backfilled — normalising
    /// existing rows needs the metadata library, which a SQL migration does not have — so a row written before
    /// the column re-derives on read, against the default region. That is exactly the behaviour those rows
    /// already had, so no stored number changes meaning.</para>
    /// </summary>
    public string? E164 => PersistedE164 ?? ToE164(Value);

    private PhoneNumber() { } // For EF Core

    /// <param name="phoneNumber">The number as typed. Stored verbatim.</param>
    /// <param name="region">
    /// The country to read <paramref name="phoneNumber"/> as when it carries no country code — the form's
    /// country selector. Omitted ⇒ <see cref="DefaultRegion"/>, which is every pre-existing caller.
    ///
    /// <para>⚠️ <b>This is the ONLY chance to record it.</b> The region is not a property of a number and is
    /// not stored; what is stored is the answer it produced. A caller that has a region and does not pass it
    /// here writes a number that no later read can resolve.</para>
    /// </param>
    public PhoneNumber(string phoneNumber, string? region = null)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new ArgumentException("Phone number cannot be empty", nameof(phoneNumber));

        Value = phoneNumber.Trim();
        PersistedE164 = ToE164(Value, region);
    }

    /// <summary>
    /// ⚠️ <b><see cref="Value"/> alone.</b> Two records of the same typed number are the same number whatever
    /// region each was written with, and folding <see cref="PersistedE164"/> in here would make a legacy row
    /// unequal to an identical new one — silently changing every equality check in the solution.
    /// </summary>
    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    /// <summary>
    /// The country a number with no country code is assumed to belong to. Tunisia, because every cabinet is
    /// Tunisian — the governorate list is 24 Tunisian names and the tax settings are Tunisian.
    /// <para>⚠️ <b>The one place that assumption is written.</b> A caller wanting a different country passes an
    /// explicit <c>+</c> prefix or a region to <see cref="ToE164(string?, string?)"/>; nothing else may restate
    /// <c>"TN"</c> or <c>+216</c> as the fallback, so a future per-clinic country setting has one place to land
    /// (international-phone-numbers AC-4).</para>
    /// </summary>
    public const string DefaultRegion = "TN";

    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    /// <summary>
    /// Every region the metadata actually knows. ⚠️ Declared AFTER <see cref="Util"/> on purpose — static field
    /// initialisers run in declaration order, so moving this above it yields an empty set and every number in
    /// the product silently falls back to <see cref="DefaultRegion"/>.
    /// </summary>
    private static readonly HashSet<string> SupportedRegions =
        new(Util.GetSupportedRegions(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The region a number with no country code is read as — <b>the one place that fallback lives</b>.
    ///
    /// <para>⚠️ <b>An unrecognised region is treated as « none given », never passed through.</b>
    /// <c>PhoneNumberUtil.Parse</c> throws on a region it does not know, and the catch turns that into « not a
    /// number » — so a junk region refused a number that was perfectly good: measured on <c>20 123 456</c> with
    /// region <c>"ZZ"</c>, a valid Tunisian number reported invalid. That matters because the region is now an
    /// <b>untrusted wire value</b> (<c>CreatePatientCommand.PhoneRegion</c>), so the refusal would arrive as
    /// « Numéro de téléphone invalide » about the number, blaming the one part of the request that was
    /// right.</para>
    ///
    /// <para>The browser's mirror has no equivalent because it needs none: <c>toE164</c>'s parameter is typed
    /// <c>CountryCode</c> there, so an unknown region cannot be spelled. This side is where a wire value
    /// lands.</para>
    ///
    /// <para>⚠️ Both readers below must go through this. The expression was written out twice before, which is
    /// how one of them could have been hardened and the other left.</para>
    /// </summary>
    private static string RegionToAssume(string? region)
    {
        var trimmed = region?.Trim();
        return !string.IsNullOrEmpty(trimmed) && SupportedRegions.Contains(trimmed)
            ? trimmed.ToUpperInvariant()
            : DefaultRegion;
    }

    /// <summary>
    /// Normalizes a phone number to E.164, or <c>null</c> when no country can parse it.
    /// <para>This is the single source of truth for « is this a usable phone number », in both directions:
    /// patient-entry validation calls it through the Application layer, and the Infrastructure reminder engine
    /// (<c>ReminderPhone.ToE164</c>) delegates to it, so entry validation and reminder dispatch can never
    /// diverge.</para>
    /// <para><b>Any country is accepted</b> (international-phone-numbers AC-2). A number carrying its own country
    /// code — <c>+33 6 12 34 56 78</c>, <c>0033612345678</c> — is parsed as that country's; a bare national
    /// number is parsed as <see cref="DefaultRegion"/>'s, so <c>20 123 456</c> is still
    /// <c>+21620123456</c>.</para>
    /// <para>⚠️ <b>Validity is per-country, and that is the point.</b> The rule this replaced accepted exactly
    /// eight digits, so a length test was the only check available; <c>201234567</c> — a Tunisian number with a
    /// ninth digit — would have to be accepted as a perfectly valid Egyptian one by any rule that only counts
    /// digits. <see cref="PhoneNumberUtil.IsValidNumber"/> knows a Tunisian national number is eight digits and
    /// refuses it (AC-3). Do not substitute <c>IsPossibleNumber</c>, which is the length test again.</para>
    /// </summary>
    /// <param name="raw">The number as a human typed it: spaces, dots, dashes and parentheses are all fine.</param>
    /// <param name="region">
    /// The country to assume when <paramref name="raw"/> carries no country code. Defaults to
    /// <see cref="DefaultRegion"/>; the country selector passes the user's own choice, which is what makes a
    /// French number typed in national form (<c>06 12 34 56 78</c>) resolvable at all.
    /// </param>
    public static string? ToE164(string? raw, string? region = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            var parsed = Util.Parse(raw, RegionToAssume(region));
            return Util.IsValidNumber(parsed) ? Util.Format(parsed, PhoneNumberFormat.E164) : null;
        }
        catch (NumberParseException)
        {
            // The library's only failure mode for unparseable input — « 71 555 (bureau) », « abc », a country
            // code that does not exist. A refusal, not an error: every caller treats null as « not a number ».
            return null;
        }
    }

    /// <summary>True when <paramref name="raw"/> is a number we can reach (see <see cref="ToE164"/>).</summary>
    public static bool IsDeliverable(string? raw, string? region = null) => ToE164(raw, region) != null;

    /// <summary>
    /// The ISO 3166-1 alpha-2 country of <paramref name="raw"/>, or <c>null</c> when it cannot be parsed.
    /// <para>Lets a stored number re-open its own country in the selector, so the control never disagrees with
    /// the field beside it (AC-11). Derived rather than stored — see the spec's Data / Schema Changes.</para>
    /// </summary>
    public static string? RegionOf(string? raw, string? region = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            var parsed = Util.Parse(raw, RegionToAssume(region));
            return Util.IsValidNumber(parsed) ? Util.GetRegionCodeForNumber(parsed) : null;
        }
        catch (NumberParseException)
        {
            return null;
        }
    }
}
