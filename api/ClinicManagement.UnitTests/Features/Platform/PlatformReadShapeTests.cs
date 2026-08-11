using System.Collections;
using System.Reflection;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Platform;
using MediatR;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// US-7's whole enforcement: <b>the vendor console cannot return anything outside a declared set of field
/// names</b> (<c>platform-console</c> AC-7.2), and that promise is checked by a build-failing test rather than
/// by a convention.
///
/// <para><b>Why this and not the tenant query filter</b> (AC-7.2a — the assumption that would be the defect).
/// The filter answers « whose rows may this request read? », and the console's honest answer is « every
/// cabinet's »: a portfolio is a cross-cabinet read by definition, so the filter is deliberately <i>lifted</i>
/// on this surface through <c>UseSystemWide</c>. Anything relying on it as the guarantee would be relying on a
/// mechanism that is switched off exactly here.</para>
///
/// <para><b>Why the set is derived from the requests, not from a list of controllers or DTOs.</b> Every console
/// read is a MediatR request in <c>Features.Platform</c> — that is how the layer works — so reflecting over that
/// namespace covers a read written next year without editing this file. Reflecting over controller signatures
/// instead would miss an action that returned <c>object</c>, and a hand-kept list of DTO types would be
/// satisfied by adding a field to a type already on it, which is precisely how a patient's name would arrive:
/// not as a new DTO, but as one more property on the row somebody was already editing.</para>
///
/// <para><b>Every property name at every depth is checked</b>, not only the scalar leaves, so a
/// <c>PatientDto Patient</c> is caught by its own name as well as by everything inside it.</para>
/// </summary>
public class PlatformReadShapeTests
{
    // ⚠️ Not `typeof(PlatformReadShape).Namespace` — that would be satisfied by a read declared one namespace
    // over. The root is the feature's own, and everything under it is in scope.
    private const string PlatformFeatureNamespace = "ClinicManagement.Application.Features.Platform";

    // [AC-7.2] The guarantee itself: nothing the console can return carries a name outside the declared set.
    [Fact]
    public void No_Console_Read_Returns_A_Field_Outside_The_Declared_Shape()
    {
        var encountered = EncounteredNames();

        var undeclared = encountered.Except(PlatformReadShape.AllowedLeafNames).OrderBy(n => n).ToList();

        Assert.True(
            undeclared.Count == 0,
            "The vendor console would return field(s) that PlatformReadShape does not declare: "
            + string.Join(", ", undeclared)
            + ". US-7 promises a cabinet that the console cannot see its patient records; adding a name to "
            + "PlatformReadShape.AllowedLeafNames is the review of that promise, and is deliberately the only "
            + "way past this test.");
    }

    // [AC-7.2] The other direction, on TenantScopeFilterTests' pattern: a name allowed but returned by nothing is
    // a hole standing open for whatever is written next, and it is also how this test would quietly become
    // vacuous if the reflection above ever stopped finding anything.
    [Fact]
    public void Every_Declared_Name_Is_Actually_Returned_By_Something()
    {
        var encountered = EncounteredNames();

        var unused = PlatformReadShape.AllowedLeafNames.Except(encountered).OrderBy(n => n).ToList();

        Assert.True(
            unused.Count == 0,
            "PlatformReadShape declares name(s) nothing returns: " + string.Join(", ", unused)
            + ". Remove them — an unused allowance is a pre-approved hole.");
    }

    // [AC-7.2] Non-vacuity, stated as data. Reflection tests fail open: a renamed namespace, a request that stops
    // implementing IRequest<>, a Result shape that changes, and this file passes for ever while checking nothing.
    [Fact]
    public void The_Reflection_Actually_Reaches_The_Consoles_Reads()
    {
        var requests = PlatformRequests();
        var encountered = EncounteredNames();

        Assert.True(requests.Count >= 4, $"Only {requests.Count} console request(s) found — the namespace scan is broken.");
        Assert.Contains("ClinicCollectedThisMonthDt", encountered);
        Assert.Contains("Writes30d", encountered);
        Assert.Contains("Token", encountered);
        // Part 3's two reads, and one name from each: the detail's trend is reached only by recursing THROUGH a
        // nested DTO inside a collection, and the ledger's by recursing into a second nested one — so naming both
        // here is what proves the recursion still descends rather than stopping at the top-level record.
        Assert.Contains("DaysMeasured", encountered);
        Assert.Contains("ActionLabel", encountered);
    }

    // [AC-7.2] The red proof. The plan's own validation step is « adding a patient name to any console DTO fails
    // this test — verify by trying it », and a guard nobody has seen fail is a guard nobody knows works. This
    // runs the real collector over a type that carries exactly that mistake.
    [Fact]
    public void A_Patient_Name_Added_To_A_Console_Response_Is_Rejected()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Collect(typeof(SmuggledPatientRow), names, new HashSet<Type>());

        var undeclared = names.Except(PlatformReadShape.AllowedLeafNames).ToList();

        Assert.Contains("PatientName", undeclared);
        Assert.Contains("LastAppointmentAt", undeclared);
        // And the innocent members of the same type still pass, so the check is discriminating rather than
        // rejecting anything it has not seen before.
        Assert.DoesNotContain("ClinicId", undeclared);
    }

    /// <summary>A console row as it would look after one careless addition. Never returned by anything.</summary>
    private sealed record SmuggledPatientRow(Guid ClinicId, string PatientName, DateTime? LastAppointmentAt);

    // ------------------------------------------------------------------ the collector

    private static IReadOnlyList<Type> PlatformRequests() =>
        typeof(PlatformReadShape).Assembly.GetTypes()
            .Where(t => t.Namespace is { } ns
                        && (ns == PlatformFeatureNamespace || ns.StartsWith(PlatformFeatureNamespace + ".", StringComparison.Ordinal)))
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IBaseRequest).IsAssignableFrom(t))
            .OrderBy(t => t.Name)
            .ToList();

    private static HashSet<string> EncounteredNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<Type>();

        foreach (var request in PlatformRequests())
        {
            foreach (var response in ResponseTypesOf(request))
            {
                Collect(response, names, visited);
            }
        }

        return names;
    }

    /// <summary>
    /// What a request answers with, unwrapped past <c>Result&lt;T&gt;</c>. The wrapper's own members
    /// (<c>IsSuccess</c>, <c>Error</c>, <c>Code</c>) are the transport and are the same on every refusal in the
    /// product, so they are not part of what this surface discloses.
    /// </summary>
    private static IEnumerable<Type> ResponseTypesOf(Type request)
    {
        foreach (var contract in request.GetInterfaces()
                     .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)))
        {
            var response = contract.GetGenericArguments()[0];

            if (response.IsGenericType && response.GetGenericTypeDefinition() == typeof(Result<>))
            {
                yield return response.GetGenericArguments()[0];
            }
            else if (response != typeof(Result))
            {
                yield return response;
            }
        }
    }

    private static void Collect(Type type, HashSet<string> names, HashSet<Type> visited)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (IsScalar(type) || !visited.Add(type))
        {
            return;
        }

        if (ElementTypeOf(type) is { } element)
        {
            Collect(element, names, visited);
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Compiler-generated on every positional record; not a field anybody returns.
            if (property.Name == "EqualityContract")
            {
                continue;
            }

            names.Add(property.Name);
            Collect(property.PropertyType, names, visited);
        }
    }

    private static Type? ElementTypeOf(Type type)
    {
        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
        {
            return null;
        }

        return type.IsArray
            ? type.GetElementType()
            : type.GetInterfaces()
                .Concat(new[] { type })
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0];
    }

    private static bool IsScalar(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(decimal)
        || type == typeof(Guid)
        || type == typeof(DateTime)
        || type == typeof(DateTimeOffset)
        || type == typeof(DateOnly)
        || type == typeof(TimeOnly)
        || type == typeof(TimeSpan)
        || type == typeof(object);
}
