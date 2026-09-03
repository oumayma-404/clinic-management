using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// Locating and reading the solution's own C# sources, for the guards that are derived by scanning them.
///
/// <para>Shared rather than copied: the <c>bin</c>/<c>obj</c> rule below is a lesson each copy would have to
/// learn again, and a guard that crashes while enumerating is a guard that never asserts.</para>
/// </summary>
internal static class SolutionSources
{
    /// <summary>
    /// The solution directory, found from this file's own compile-time path. Deliberately NOT
    /// <c>AppContext.BaseDirectory</c>: the suite is routinely built to an output directory outside the
    /// repository (the Smart App Control workaround), which would make a walk-up from the binary fail.
    /// </summary>
    public static DirectoryInfo Root([CallerFilePath] string thisFile = "")
    {
        for (var dir = new FileInfo(thisFile).Directory; dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ClinicManagement.sln")))
            {
                return dir;
            }
        }

        throw new InvalidOperationException(
            $"Could not locate ClinicManagement.sln by walking up from '{thisFile}'. Guards that read sources must "
            + "fail rather than silently pass when they cannot find them.");
    }

    /// <summary>
    /// Every C# source under <paramref name="root"/>, skipping build output.
    ///
    /// <para>⚠️ <c>bin</c>/<c>obj</c> are skipped by <b>not descending into them</b>, not by filtering the
    /// results: <c>obj/</c> holds generated copies of the sources (which would double every hit), and
    /// <c>EnumerateFiles(…, AllDirectories)</c> uses the legacy options where <c>IgnoreInaccessible</c> is
    /// <c>false</c> — so on a machine where <c>harden-permissions</c> has ACL'd a backup folder inside
    /// <c>bin/</c>, enumerating first and filtering after throws <see cref="UnauthorizedAccessException"/>
    /// before any assertion runs.</para>
    /// </summary>
    public static IEnumerable<string> CsFiles(DirectoryInfo root)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var child in directory.EnumerateDirectories())
            {
                if (child.Name is "bin" or "obj")
                {
                    continue;
                }

                pending.Push(child);
            }

            foreach (var file in directory.EnumerateFiles("*.cs"))
            {
                yield return file.FullName;
            }
        }
    }

    /// <summary>
    /// The source with its comments blanked out, for guards that match on what the code <b>does</b>.
    ///
    /// <para>⚠️ Needed because this repository documents its reasoning in prose beside the code it describes, so
    /// a member name appears in a <c>&lt;summary&gt;</c> far more often than it is called. A guard matching raw
    /// text reads « see <c>MarkItemDone</c> » in a doc comment as a call site, then demands that the *comment*
    /// load a collection — which is unfixable, so the guard gets disabled instead of the defect getting fixed.
    /// <c>CnamClosedSetContractTests</c> learned the same lesson on the frontend side.</para>
    ///
    /// <para>Replaces each comment with spaces rather than removing it, so every match index still lines up with
    /// the original text and a finding can name a real line.</para>
    /// </summary>
    public static string WithoutComments(string source) =>
        CommentPattern.Replace(source, m => new string(' ', m.Length));

    private static readonly Regex CommentPattern = new(
        @"//[^\r\n]*|/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);
}
