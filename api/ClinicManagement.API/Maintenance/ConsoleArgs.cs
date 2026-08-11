namespace ClinicManagement.API.Maintenance;

/// <summary>
/// How every console verb reads its <c>--flag value</c> arguments.
///
/// <para>It lived as an <c>internal</c> member of <c>ProvisionClinicCommand</c>, which was fine while that verb was
/// its only caller. The five <c>subscription-*</c> verbs then reached into it thirteen times, which made a general
/// argument reader with nothing to do with provisioning a clinic the de-facto shared utility of the folder — and
/// left a verb's parsing owned by an unrelated verb, so removing or changing that one would break four others.</para>
/// </summary>
internal static class ConsoleArgs
{
    /// <summary>
    /// The value after <paramref name="flag"/>, or null when absent. A value that itself starts with <c>--</c> reads
    /// as absent, so <c>--reason --clinic x</c> is a missing reason rather than a reason called « --clinic ».
    /// </summary>
    internal static string? ReadOption(string[] args, string flag)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = args[i + 1];
            return value.StartsWith("--", StringComparison.Ordinal) ? null : value;
        }

        return null;
    }
}
