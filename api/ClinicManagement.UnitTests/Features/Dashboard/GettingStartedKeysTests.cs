using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ClinicManagement.Application.Features.Dashboard;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Dashboard;

/// <summary>
/// « Premiers pas »' step keys: the server decides which steps exist and whether each is done
/// (<see cref="GettingStartedKeys"/>), <c>web/lib/getting-started.ts</c> gives each its title, video and action.
/// A key on one side alone renders as nothing (server only) or as a step that never ticks (web only), so both
/// directions are held, exactly and in order.
/// </summary>
public class GettingStartedKeysTests
{
    [Fact]
    public void The_Web_Lists_Exactly_The_Server_Steps_In_The_Same_Order()
    {
        var web = WebKeys();

        Assert.Equal(GettingStartedKeys.All, web);
    }

    [Fact]
    public void Every_Web_Step_Has_Its_Meta_Entry()
    {
        var source = File.ReadAllText(WebFilePath());
        var keys = WebKeys();

        var missing = keys
            .Where(key => !Regex.IsMatch(source, $@"(?m)^\s{{2}}{key}:\s*\{{"))
            .ToList();

        Assert.True(missing.Count == 0, "GETTING_STARTED_STEPS has no entry for: " + string.Join(", ", missing));
    }

    private static IReadOnlyList<string> WebKeys()
    {
        var source = File.ReadAllText(WebFilePath());
        var match = Regex.Match(source, @"GETTING_STARTED_KEYS\s*=\s*\[(?<body>[^\]]*)\]");
        Assert.True(match.Success, "GETTING_STARTED_KEYS was not found in web/lib/getting-started.ts");

        var keys = Regex.Matches(match.Groups["body"].Value, "\"([A-Za-z]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToList();

        // A parse failure must not read as an empty, trivially-equal list.
        Assert.NotEmpty(keys);
        return keys;
    }

    private static string WebFilePath([CallerFilePath] string thisFile = "")
    {
        var native = "web/lib/getting-started.ts".Replace('/', Path.DirectorySeparatorChar);
        for (var dir = new FileInfo(thisFile).Directory; dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, native);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not locate 'web/lib/getting-started.ts' walking up from '{thisFile}'.");
    }
}
