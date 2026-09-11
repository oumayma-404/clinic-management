using System.Reflection;
using ClinicManagement.Domain.ValueObjects;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// <see cref="PhoneNumber.E164"/> — the stored dialable form, and the legacy fallback behind it.
///
/// <para><b>Why it is stored at all.</b> A national number cannot be resolved without knowing its country, and
/// the country is known only while the form is open: it comes from the country selector and nothing can recover
/// it afterwards. So every read that re-derived E.164 from the stored string assumed Tunisia, and a French
/// patient saved as <c>06 12 34 56 78</c> was reachable by nothing — no WhatsApp action, no SMS reminder, and a
/// <c>tel:</c> link that dialled a French national number from a Tunisian handset.</para>
///
/// <para>⚠️ These are the value object's own tests. <see cref="PhoneRuleCorpusTests"/> pins the static
/// <c>ToE164</c> rule against <c>shared/phone-e164-corpus.json</c> and is unaffected by any of this — the rule
/// was never wrong, which is exactly why the defect survived it.</para>
/// </summary>
public class PhoneNumberE164Tests
{
    [Fact]
    public void A_Number_Written_With_Its_Region_Stores_The_Dialable_Form()
    {
        var phone = new PhoneNumber("06 12 34 56 78", "FR");

        Assert.Equal("+33612345678", phone.E164);
        Assert.Equal("+33612345678", phone.PersistedE164);
    }

    /// <summary>⚠️ The raw value is never rewritten — reception reads back the number it typed.</summary>
    [Fact]
    public void The_Typed_Value_Is_Kept_Exactly()
    {
        var phone = new PhoneNumber("06 12 34 56 78", "FR");

        Assert.Equal("06 12 34 56 78", phone.Value);
        Assert.Equal("06 12 34 56 78", phone.ToString());
    }

    /// <summary>Every pre-existing caller passes no region, and nothing about those numbers changes.</summary>
    [Theory]
    [InlineData("20 123 456", "+21620123456")]
    [InlineData("+216 20 123 456", "+21620123456")]
    [InlineData("+33 6 12 34 56 78", "+33612345678")]
    public void Without_A_Region_The_Default_Still_Applies(string raw, string expected)
    {
        Assert.Equal(expected, new PhoneNumber(raw).E164);
    }

    /// <summary>
    /// ⚠️ A number no country can parse keeps its raw value and is simply not dialable. « 71 555 (bureau) » is a
    /// real emergency-contact value a relative gives, and this field deliberately accepts it rather than
    /// refusing the patient's whole record — nothing dispatches to it, a human reads it.
    /// </summary>
    [Theory]
    [InlineData("71 555 (bureau)")]
    [InlineData("à rappeler chez la voisine")]
    public void An_Unparseable_Number_Is_Kept_And_Is_Simply_Not_Dialable(string raw)
    {
        var phone = new PhoneNumber(raw);

        Assert.Equal(raw, phone.Value);
        Assert.Null(phone.E164);
        Assert.Null(phone.PersistedE164);
    }

    /// <summary>
    /// ⚠️ <b>The legacy fallback, and it must stay.</b> Nothing was backfilled — normalising existing rows needs
    /// libphonenumber's metadata, which a SQL migration does not have — so a row written before the column
    /// materialises with a null <see cref="PhoneNumber.PersistedE164"/> and re-derives on read. That is exactly
    /// the behaviour those rows already had, which is what makes the migration safe: no stored number changes
    /// meaning.
    ///
    /// <para>Built through the parameterless constructor EF itself uses, so this is the real materialisation
    /// path for a pre-migration row rather than an approximation of one.</para>
    /// </summary>
    [Theory]
    [InlineData("20 123 456", "+21620123456")]
    [InlineData("+33 6 12 34 56 78", "+33612345678")]
    public void A_Row_Written_Before_The_Column_Re_Derives_On_Read(string raw, string expected)
    {
        var legacy = LegacyRow(raw);

        Assert.Null(legacy.PersistedE164);
        Assert.Equal(expected, legacy.E164);
    }

    /// <summary>
    /// ⚠️ And a legacy FOREIGN national number still reads as unreachable — the fallback assumes Tunisia,
    /// because that is all it can do. Such a row becomes dialable when somebody next opens the patient and
    /// saves with the country selected; it is not silently repaired, and it must not be silently guessed.
    /// </summary>
    [Fact]
    public void A_Legacy_Foreign_National_Number_Is_Still_Unreachable()
    {
        Assert.Null(LegacyRow("06 12 34 56 78").E164);
    }

    /// <summary>
    /// ⚠️ Equality is the raw value alone. Folding the normalisation in would make a legacy row unequal to an
    /// identical new one, silently changing every equality check in the solution.
    /// </summary>
    [Fact]
    public void Equality_Ignores_The_Normalisation()
    {
        var legacy = LegacyRow("20 123 456");
        var written = new PhoneNumber("20 123 456");

        Assert.NotEqual(legacy.PersistedE164, written.PersistedE164);
        Assert.Equal(legacy, written);
        Assert.Equal(legacy.GetHashCode(), written.GetHashCode());
    }

    /// <summary>
    /// ⚠️ An unrecognised region is treated as « none given », never passed through. <c>Parse</c> throws on a
    /// region it does not know and the catch turns that into « not a number », so a junk region refused a
    /// perfectly good one — measured on <c>20 123 456</c> with <c>"ZZ"</c>. It matters because the region is an
    /// untrusted wire value now, and the refusal would blame the one part of the request that was right.
    /// </summary>
    [Theory]
    [InlineData("ZZ")]
    [InlineData("not-a-region")]
    [InlineData("")]
    [InlineData("   ")]
    public void An_Unrecognised_Region_Falls_Back_Rather_Than_Refusing(string region)
    {
        Assert.Equal("+21620123456", new PhoneNumber("20 123 456", region).E164);
    }

    /// <summary>A region is accepted however it is cased — a wire value, not a compile-time constant.</summary>
    [Theory]
    [InlineData("fr")]
    [InlineData("Fr")]
    [InlineData(" FR ")]
    public void A_Region_Is_Read_Case_And_Whitespace_Insensitively(string region)
    {
        Assert.Equal("+33612345678", new PhoneNumber("06 12 34 56 78", region).E164);
    }

    /// <summary>
    /// The row EF materialises for a patient stored before the column existed: the raw value set, the
    /// normalisation null.
    /// </summary>
    private static PhoneNumber LegacyRow(string raw)
    {
        var phone = (PhoneNumber)Activator.CreateInstance(typeof(PhoneNumber), nonPublic: true)!;
        typeof(PhoneNumber)
            .GetProperty(nameof(PhoneNumber.Value), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(phone, raw);
        return phone;
    }
}
