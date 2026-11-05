using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClinicManagement.Application.Features.Feedback;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Feedback;

/// <summary>
/// The feedback form's closed sets (guided-first-days E): <see cref="FeedbackRules"/> refuses anything outside them,
/// and <c>web/lib/feedback.ts</c> is what the form offers. A key on the web side alone is refused as
/// <c>feedback_invalid</c> on « Envoyer »; one on the server side alone can never be chosen. Both directions, keys
/// and French labels, in order.
/// </summary>
public class FeedbackMirrorTests
{
    [Fact]
    public void The_Form_Offers_Exactly_The_Server_Kinds_With_The_Same_Labels() =>
        Assert.Equal(
            FeedbackRules.Kinds.Select(k => (k, FeedbackRules.KindLabels[k])),
            WebPairs("FEEDBACK_KINDS"));

    [Fact]
    public void The_Form_Offers_Exactly_The_Server_Areas_With_The_Same_Labels() =>
        Assert.Equal(
            FeedbackRules.Areas.Select(a => (a, FeedbackRules.AreaLabels[a])),
            WebPairs("FEEDBACK_AREAS"));

    private static IReadOnlyList<(string Key, string Label)> WebPairs(string constant)
    {
        var source = File.ReadAllText(WebFilePath());
        var match = Regex.Match(source, constant + @"\s*=\s*\[(?<body>.*?)\]\s*as const", RegexOptions.Singleline);
        Assert.True(match.Success, $"{constant} was not found in web/lib/feedback.ts");

        var pairs = Regex.Matches(match.Groups["body"].Value, "key:\\s*\"(?<key>[^\"]+)\",\\s*label:\\s*\"(?<label>[^\"]+)\"")
            .Select(m => (m.Groups["key"].Value, m.Groups["label"].Value))
            .ToList();

        // A parse failure must not read as an empty, trivially-equal list.
        Assert.NotEmpty(pairs);
        return pairs;
    }

    private static string WebFilePath([CallerFilePath] string thisFile = "")
    {
        var native = "web/lib/feedback.ts".Replace('/', Path.DirectorySeparatorChar);
        for (var dir = new FileInfo(thisFile).Directory; dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, native);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate 'web/lib/feedback.ts' walking up from '{thisFile}'.");
    }
}
