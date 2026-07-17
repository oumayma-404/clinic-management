using ClinicManagement.Infrastructure.Services;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>Tunisian +216 E.164 phone normalization (spec AC-6). Pure function, no I/O.</summary>
public class ReminderPhoneTests
{
    [Theory]
    [InlineData("20123456", "+21620123456")]        // bare 8-digit national
    [InlineData("20 123 456", "+21620123456")]       // spaced
    [InlineData("+216 20 123 456", "+21620123456")]  // already +216, spaced
    [InlineData("+21620123456", "+21620123456")]     // already E.164
    [InlineData("0021620123456", "+21620123456")]    // 00216 international prefix
    [InlineData("216-20-123-456", "+21620123456")]   // 216 prefix, dashed
    public void Normalizes_Tunisian_Numbers(string raw, string expected)
    {
        Assert.Equal(expected, ReminderPhone.ToE164(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("123")]            // too short
    [InlineData("2012345678901")]  // too long
    [InlineData("no-digits-here")] // no digits at all
    public void Returns_Null_For_Empty_Or_Unparseable(string? raw)
    {
        Assert.Null(ReminderPhone.ToE164(raw));
    }
}
