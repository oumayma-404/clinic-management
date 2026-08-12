namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// What a persisted column's name has to look like before somebody must argue about it.
///
/// <para>Deliberately broad and deliberately crude: it is a <b>prompt to make a decision</b>, not a classifier.
/// A false positive costs one line in a declared exception map; a false negative is a practice's credentials
/// sitting in the clear, which is what both guards reading this exist to prevent.</para>
///
/// <para>⚠️ <b>One definition, two guards.</b> <c>ClinicArchiveScopeTests</c> asks « is this column redacted out
/// of the archive? » and <c>SecretProtectionCoverageTests</c> asks « is this column encrypted at rest? » — two
/// different questions over the <i>same</i> candidate set. Held separately they would drift, and the drift would
/// be silent in the worst direction: a marker added to one list would leave the other guard blind to exactly the
/// columns somebody had just decided were sensitive.</para>
/// </summary>
internal static class SecretShapedNames
{
    /// <summary>The markers. Adding one widens both guards at once, which is the point.</summary>
    internal static readonly string[] Markers =
        { "Token", "Secret", "Password", "Credential", "ApiKey", "Encrypted", "Refresh" };

    internal static bool Matches(string name) =>
        Markers.Any(marker => name.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
