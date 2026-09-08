using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.ValueObjects;

/// <summary>
/// A postal address — « adresse », « ville », « gouvernorat », « code postal ».
///
/// <para>⚠️ <b>One field is enough, and this is a reversal.</b> All four used to be mandatory, which is a rule about
/// a *form* imposed on a *record* — and it failed two different ways depending on which door you came through.
/// A patient who says only « Sfax » is the ordinary case at a desk, not an incomplete one.</para>
///
/// <list type="bullet">
///   <item>On <b>create</b>, <c>PatientFromRequest</c> guarded with a four-way <c>&amp;&amp;</c>, so a partial address
///   was <b>silently dropped</b> — no error, no message, the address simply gone. That is a *silent drop, not a
///   refusal*, exactly the defect the retired <c>InsuranceInfo</c> block was fixed for, fourteen lines down the same
///   method, and left unfixed here.</item>
///   <item>On <b>update</b>, <c>UpdatePatientCommand</c> had no guard at all and called this constructor directly,
///   so the same input threw <see cref="ArgumentException"/> into the handler's catch-all and came back as
///   « une erreur est survenue » — a lost save, naming no field.</item>
/// </list>
///
/// <para>The invariant is the one that block carried, verbatim: what is refused is a value with <b>no</b>
/// side at all, which the caller expresses as a null <see cref="Address"/> rather than an empty one. Use
/// <see cref="OfAny"/> — the constructor is private so that « an address with nothing in it » cannot be
/// constructed anywhere, and so a future call site cannot reintroduce the all-four rule by accident. That is a
/// compiler-enforced single owner rather than a guard test.</para>
/// </summary>
public class Address : ValueObject
{
    /// <summary>« Adresse » — the street line. Null when the patient gave only a town or a gouvernorat.</summary>
    public string? Street { get; private set; }

    /// <summary>« Ville ».</summary>
    public string? City { get; private set; }

    /// <summary>« Gouvernorat » — one of <c>TUNISIAN_GOVERNORATES</c> on the client, free text here.</summary>
    public string? State { get; private set; }

    /// <summary>« Code postal ».</summary>
    public string? ZipCode { get; private set; }

    public string? Country { get; private set; }

    private Address() { } // For EF Core

    private Address(string? street, string? city, string? state, string? zipCode, string? country)
    {
        Street = street;
        City = city;
        State = state;
        ZipCode = zipCode;
        Country = country;
    }

    /// <summary>
    /// The only way to build an address: <c>null</c> when every field is blank, otherwise an address holding
    /// whatever was actually given.
    /// </summary>
    /// <remarks>
    /// Returning <c>null</c> rather than throwing on an all-blank input is what lets both the create and the
    /// update path call this unconditionally — the update path's tri-state (<c>AddressSpecified</c>) still decides
    /// « leave alone » vs « clear », and this decides « clear » vs « store ».
    /// </remarks>
    public static Address? OfAny(string? street, string? city, string? state, string? zipCode, string? country = null)
    {
        var trimmedStreet = Blank(street);
        var trimmedCity = Blank(city);
        var trimmedState = Blank(state);
        var trimmedZip = Blank(zipCode);

        if (trimmedStreet is null && trimmedCity is null && trimmedState is null && trimmedZip is null)
        {
            return null;
        }

        return new Address(trimmedStreet, trimmedCity, trimmedState, trimmedZip, Blank(country));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Street ?? string.Empty;
        yield return City ?? string.Empty;
        yield return State ?? string.Empty;
        yield return ZipCode ?? string.Empty;
        yield return Country ?? string.Empty;
    }
}
