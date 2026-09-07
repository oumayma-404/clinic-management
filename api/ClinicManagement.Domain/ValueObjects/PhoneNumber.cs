using ClinicManagement.Domain.Common;
using PhoneNumbers;

namespace ClinicManagement.Domain.ValueObjects;

public class PhoneNumber : ValueObject
{
    public string Value { get; private set; }

    private PhoneNumber() { } // For EF Core

    public PhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            throw new ArgumentException("Phone number cannot be empty", nameof(phoneNumber));

        Value = phoneNumber.Trim();
    }

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
            var parsed = Util.Parse(raw, string.IsNullOrWhiteSpace(region) ? DefaultRegion : region);
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
            var parsed = Util.Parse(raw, string.IsNullOrWhiteSpace(region) ? DefaultRegion : region);
            return Util.IsValidNumber(parsed) ? Util.GetRegionCodeForNumber(parsed) : null;
        }
        catch (NumberParseException)
        {
            return null;
        }
    }
}
