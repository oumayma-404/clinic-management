using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// <b>No DTO puts a raw enum on the wire</b>, and the consent value survives a round trip as a name.
///
/// <para><b>What this exists to stop, told straight.</b> The consent field shipped with
/// <c>PatientDto.ReminderConsent</c> typed as the enum. This API registers no <c>JsonStringEnumConverter</c>, so
/// it left as <c>0</c>/<c>1</c>/<c>2</c>: the browser compared an integer against <c>"Refused"</c>, never
/// matched, and the control showed « non renseigné » over every stored answer — while a write of
/// <c>"Refused"</c> was rejected as a 400 by the model binder, before any handler ran, so there was no French
/// message and no log line either. <c>PatientDto</c>'s own docstring three properties above had warned about
/// exactly this, for <c>Dentition</c>.</para>
///
/// <para>⚠️ <b>Neither half was visible to any gate.</b> <c>tsc</c> is happy (the client's type says
/// <c>string</c> and the server never told it otherwise), the unit suite is happy (it constructs the enum
/// directly and never serialises), and <c>check:responsive</c> looks at layout. Only a real HTTP request found
/// it — which is why this test asserts the SERIALISED form rather than the property's type alone.</para>
/// </summary>
public class ReminderConsentWireShapeTests
{
    [Fact]
    public void The_Consent_Leaves_As_Its_Name_Not_As_A_Number()
    {
        var json = JsonSerializer.Serialize(new PatientDto
        {
            ReminderConsent = nameof(PatientReminderConsent.Refused),
        });

        Assert.Contains("\"ReminderConsent\":\"Refused\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ReminderConsent\":2", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⚠️ The derived half. A per-property assertion only covers the property somebody remembered to write one
    /// for; this fails on the <i>next</i> DTO that puts an enum on the wire, which is the only version of this
    /// check that would have caught the original mistake.
    /// </summary>
    [Fact]
    public void No_Dto_Exposes_A_Raw_Enum()
    {
        var offenders = typeof(PatientDto).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsPublic: true }
                        && t.Namespace == typeof(PatientDto).Namespace
                        && t.Name.EndsWith("Dto", StringComparison.Ordinal))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => Underlying(p.PropertyType).IsEnum && SerialisesAsANumber(p))
                .Select(p => $"{t.Name}.{p.Name} ({Underlying(p.PropertyType).Name})"))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(ReviewedRawEnums.OrderBy(x => x, StringComparer.Ordinal), offenders);
    }

    /// <summary>
    /// DTO properties that are still raw enums, each reviewed. Asserted <b>equal in both directions</b>, so a
    /// new one fails here and a fixed one has to be struck off.
    ///
    /// <para>It is empty. If a genuine case ever arises, the answer is almost certainly a <c>string</c> property
    /// and a <c>*Rules.Parse</c> beside <c>DentitionRules</c> and <c>ReminderConsentRules</c>, not an entry
    /// here.</para>
    /// </summary>
    private static readonly List<string> ReviewedRawEnums = new()
    {
        // Deliberate, and the client agrees IN WRITING: `web/lib/api/push-devices.ts` declares
        // `export type DevicePlatform = 1 | 2` with the comment « Matches the backend DevicePlatform enum
        // (Android = 1, Ios = 2) ». A number on both sides is a contract; a number on one side and a name on the
        // other is the defect. Changing these would break the shell's registration payload for no gain.
        "PushDeviceDto.Platform (DevicePlatform)",
        "PushPlatformAvailabilityDto.Platform (DevicePlatform)",
    };

    /// <summary>
    /// Does this property actually reach the wire as a number? A per-property
    /// <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c> makes an enum serialise as its name, which is the
    /// whole point — so asking « is the type an enum? » would flag a property that is already correct.
    /// </summary>
    private static bool SerialisesAsANumber(PropertyInfo property) =>
        property.GetCustomAttribute<System.Text.Json.Serialization.JsonConverterAttribute>() is null;

    [Fact]
    public void The_Scan_Still_Sees_The_Dtos()
    {
        var dtos = typeof(PatientDto).Assembly
            .GetTypes()
            .Count(t => t is { IsClass: true, IsPublic: true }
                        && t.Namespace == typeof(PatientDto).Namespace
                        && t.Name.EndsWith("Dto", StringComparison.Ordinal));

        Assert.True(dtos > 30, $"Only {dtos} DTO(s) found — the reflection scan is blind.");
    }

    [Theory]
    [InlineData("Refused", PatientReminderConsent.Refused)]
    [InlineData("refused", PatientReminderConsent.Refused)]
    [InlineData("Granted", PatientReminderConsent.Granted)]
    [InlineData("NotRecorded", PatientReminderConsent.NotRecorded)]
    public void A_Known_Name_Parses(string wire, PatientReminderConsent expected)
    {
        Assert.Equal(expected, ReminderConsentRules.Parse(wire));
    }

    /// <summary>
    /// ⚠️ Unrecognised reads as « leave it alone », never as <c>NotRecorded</c>. A typo in a payload must not
    /// silently <b>erase</b> a refusal the patient actually gave — un-recording an answer has to be deliberate.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Refusé")]
    // ⚠️ A NUMBER is refused too. `Enum.TryParse` accepts "2" happily, so without this the wire would have two
    // spellings for one answer — and a client sending the integer is a client that has not been told the shape
    // changed, which should be loud rather than quietly accepted.
    [InlineData("2")]
    [InlineData("0")]
    [InlineData("99")]
    public void Anything_Else_Leaves_The_Stored_Answer_Alone(string? wire)
    {
        Assert.Null(ReminderConsentRules.Parse(wire));
    }

    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;
}
